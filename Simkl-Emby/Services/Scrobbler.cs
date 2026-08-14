using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

using Simkl.Api;
using Simkl.Api.Exceptions;
using Simkl.Api.Objects;
using Simkl.Configuration;
using Simkl.Helpers;

namespace Simkl.Services
{
    /// <summary>
    /// Central hub between the Emby server events and Simkl. Handles real-time
    /// scrobbling (start / pause / stop) and manual "played" toggles, exactly like
    /// the Trakt plugin's ServerMediator.
    /// </summary>
    public class ServerMediator : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly IJsonSerializer _json;
        private SimklApi _api;

        public static ServerMediator Instance { get; private set; }

        public ServerMediator(IJsonSerializer json, ISessionManager sessionManager, IUserDataManager userDataManager,
            ILibraryManager libraryManager, ILogManager logManager, IHttpClient httpClient, IFileSystem fileSystem)
        {
            Instance = this;
            _json = json;
            _sessionManager = sessionManager;
            _userDataManager = userDataManager;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _logger = logManager.GetLogger("Simkl");
            _api = new SimklApi(json, _logger, httpClient);
        }

        public void Run()
        {
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            _userDataManager.UserDataSaved += OnUserDataSaved;
        }

        public void Dispose()
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            _api = null;
        }

        /* ------------------------------------------------------------ */
        /*  Manual "played" toggle -> mark watched / unwatched          */
        /* ------------------------------------------------------------ */
        private async void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            try
            {
                if (e.SaveReason != UserDataSaveReason.TogglePlayed) return;
                if (!(e.Item is BaseItem item)) return;

                var user = e.User;
                var config = UserHelper.GetSimklUser(user);
                if (config == null || !SyncHelper.CanSync(item, config, _fileSystem)) return;
                if (!config.postWatchedHistory) return;

                var played = e.UserData != null && e.UserData.Played;
                var payload = BuildHistoryPayload(item, played ? DateTimeOffset.UtcNow : (DateTimeOffset?)null, config);
                if (payload == null || payload.IsEmpty) return;

                if (played)
                {
                    _logger.Info("Marking watched on Simkl: {0}", item.Name);
                    await _api.AddToHistory(payload, config.userToken).ConfigureAwait(false);
                }
                else
                {
                    _logger.Info("Marking UNwatched on Simkl: {0}", item.Name);
                    await _api.RemoveFromHistory(payload, config.userToken).ConfigureAwait(false);
                }
            }
            catch (InvalidTokenException) { _logger.Info("User token was invalid, removed"); }
            catch (Exception ex) { _logger.ErrorException("Error handling played toggle", ex); }
        }

        /* ------------------------------------------------------------ */
        /*  Playback started -> scrobble "watching"                     */
        /* ------------------------------------------------------------ */
        private async void OnPlaybackStart(object sender, PlaybackProgressEventArgs e)
        {
            await SendStatus(e.Users?.FirstOrDefault(), e.Item, MediaStatus.Watching, Percent(e.Item, e.PlaybackPositionTicks));
        }

        /* ------------------------------------------------------------ */
        /*  Playback stopped -> stop (if finished) or pause (resume)    */
        /* ------------------------------------------------------------ */
        private async void OnPlaybackStopped(object sender, PlaybackStopEventArgs e)
        {
            var status = e.PlayedToCompletion ? MediaStatus.Stop : MediaStatus.Paused;
            var progress = e.PlayedToCompletion ? 100f : Percent(e.Item, e.PlaybackPositionTicks);
            await SendStatus(e.Users?.FirstOrDefault(), e.Item, status, progress);
        }

        /* ------------------------------------------------------------ */
        /*  Shared scrobble path                                        */
        /* ------------------------------------------------------------ */
        private async Task SendStatus(MediaBrowser.Controller.Entities.User user, BaseItem item, MediaStatus status, double progress)
        {
            try
            {
                if (user == null || item == null) return;

                var config = UserHelper.GetSimklUser(user);
                if (config == null || !SyncHelper.CanSync(item, config, _fileSystem)) return;

                if (item is Movie movie)
                {
                    if (!config.scrobbleMovies) return;
                    var syncMovie = new SyncMovie
                    {
                        title = movie.Name,
                        year = movie.ProductionYear,
                        ids = SyncHelper.MovieIds(movie)
                    };
                    _logger.Info("Scrobble {0} ({1}%) : {2}", status, (int)progress, movie.Name);
                    await _api.ScrobbleMovie(syncMovie, progress, status, config.userToken).ConfigureAwait(false);
                }
                else if (item is Episode episode && episode.Series != null)
                {
                    if (!config.scrobbleShows) return;
                    var show = new SyncShow
                    {
                        title = episode.Series.Name,
                        year = episode.Series.ProductionYear,
                        ids = SyncHelper.ShowIds(episode.Series)
                    };
                    _logger.Info("Scrobble {0} ({1}%) : {2} S{3}E{4}", status, (int)progress,
                        episode.Series.Name, SyncHelper.GetSeasonNumber(episode), episode.IndexNumber);
                    await _api.ScrobbleEpisode(show, SyncHelper.GetSeasonNumber(episode), episode.IndexNumber,
                        progress, status, config.userToken).ConfigureAwait(false);
                }
            }
            catch (InvalidTokenException) { _logger.Info("User token was invalid, removed"); }
            catch (Exception ex) { _logger.ErrorException("Error sending scrobble status", ex); }
        }

        /* ------------------------------------------------------------ */
        /*  Helpers                                                     */
        /* ------------------------------------------------------------ */
        private static float Percent(BaseItem item, long? positionTicks)
        {
            if (!(item is Video video) || !video.RunTimeTicks.HasValue || video.RunTimeTicks.Value == 0)
                return 0f;
            return (float)(positionTicks ?? 0) / video.RunTimeTicks.Value * 100f;
        }

        private SyncPayload BuildHistoryPayload(BaseItem item, DateTimeOffset? watchedAt, UserConfig config)
        {
            var payload = new SyncPayload();
            var iso = SyncHelper.ToIso(watchedAt);

            if (item is Movie movie)
            {
                payload.movies.Add(new SyncMovie
                {
                    title = movie.Name,
                    year = movie.ProductionYear,
                    ids = SyncHelper.MovieIds(movie),
                    watched_at = iso
                });
            }
            else if (item is Episode episode && episode.Series != null)
            {
                payload.shows.Add(new SyncShow
                {
                    title = episode.Series.Name,
                    year = episode.Series.ProductionYear,
                    ids = SyncHelper.ShowIds(episode.Series),
                    seasons = new System.Collections.Generic.List<SyncSeason>
                    {
                        new SyncSeason
                        {
                            number = SyncHelper.GetSeasonNumber(episode),
                            episodes = new System.Collections.Generic.List<SyncEpisode>
                            {
                                new SyncEpisode { number = episode.IndexNumber, watched_at = iso }
                            }
                        }
                    }
                });
            }
            else
            {
                return null;
            }
            return payload;
        }
    }
}
