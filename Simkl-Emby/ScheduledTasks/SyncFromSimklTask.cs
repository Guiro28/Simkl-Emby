using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

using Simkl.Api;
using Simkl.Api.Objects;
using Simkl.Configuration;
using Simkl.Helpers;

using User = MediaBrowser.Controller.Entities.User;

namespace Simkl.ScheduledTasks
{
    /// <summary>
    /// Imports playstates from Simkl into Emby: watched/unwatched status, play count,
    /// last played date, per-user ratings and — crucially — resume points so you can
    /// continue where you left off. Equivalent to the Trakt "Import playstates" task.
    /// </summary>
    public class SyncFromSimklTask : IScheduledTask
    {
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly SimklApi _api;

        public SyncFromSimklTask(ILogManager logManager, IJsonSerializer json, IUserManager userManager,
            IUserDataManager userDataManager, IHttpClient httpClient, ILibraryManager libraryManager, IFileSystem fileSystem)
        {
            _userManager = userManager;
            _userDataManager = userDataManager;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _logger = logManager.GetLogger("Simkl");
            _api = new SimklApi(json, _logger, httpClient);
        }

        public string Key => "SimklSyncFromSimklTask";
        public string Name => "Import playstates from Simkl";
        public string Description => "Sync watched status, ratings and resume points from Simkl for each Emby user linked to a Simkl account";
        public string Category => "Simkl";
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new List<TaskTriggerInfo>();

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var configs = Plugin.Instance.PluginConfiguration.LoggedInUsers();
            if (configs.Length == 0) { _logger.Info("No logged-in Simkl users"); return; }

            double perUser = 100.0 / configs.Length;
            double baseProgress = 0;

            foreach (var config in configs)
            {
                var user = FindUser(config.guid);
                if (user == null) { _logger.Error("No Emby user for {0}", config.guid); continue; }

                try
                {
                    await SyncUser(user, config, baseProgress, perUser, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error importing Simkl data for " + user.Name, ex);
                }

                baseProgress += perUser;
                progress.Report(baseProgress);
            }
        }

        private async Task SyncUser(User user, UserConfig config, double baseProgress, double perUser,
            IProgress<double> progress, CancellationToken cancellationToken)
        {
            var token = config.userToken;

            var watchedMovies = (await _api.GetAllItems("movies", token).ConfigureAwait(false))?.movies
                                ?? new List<AllItemsMovie>();

            var showsResp = await _api.GetAllItems("shows", token).ConfigureAwait(false);
            var animeResp = await _api.GetAllItems("anime", token).ConfigureAwait(false);
            var watchedShows = new List<AllItemsShow>();
            if (showsResp?.shows != null) watchedShows.AddRange(showsResp.shows);
            if (animeResp?.anime != null) watchedShows.AddRange(animeResp.anime);

            List<PlaybackSession> playbackMovies = new List<PlaybackSession>();
            List<PlaybackSession> playbackEpisodes = new List<PlaybackSession>();
            if (config.importPlaybackProgress)
            {
                playbackMovies = await _api.GetPlayback("movies", token).ConfigureAwait(false) ?? playbackMovies;
                playbackEpisodes = await _api.GetPlayback("episodes", token).ConfigureAwait(false) ?? playbackEpisodes;
            }

            _logger.Info("Simkl import for {0}: {1} watched movies, {2} watched shows, {3}+{4} paused",
                user.Name, watchedMovies.Count, watchedShows.Count, playbackMovies.Count, playbackEpisodes.Count);

            var items = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { typeof(Movie).Name, typeof(Episode).Name },
                IsVirtualItem = false
            }).Where(i => SyncHelper.CanSync(i, config, _fileSystem)).ToList();

            double perItem = items.Count > 0 ? perUser / items.Count : 0;
            double current = baseProgress;

            foreach (var movie in items.OfType<Movie>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImportMovie(movie, user, config, watchedMovies, playbackMovies, cancellationToken);
                current += perItem;
                progress.Report(current);
            }

            foreach (var episode in items.OfType<Episode>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImportEpisode(episode, user, config, watchedShows, playbackEpisodes, cancellationToken);
                current += perItem;
                progress.Report(current);
            }
        }

        private void ImportMovie(Movie movie, User user, UserConfig config, List<AllItemsMovie> watchedMovies,
            List<PlaybackSession> playbackMovies, CancellationToken cancellationToken)
        {
            var matched = Match.FindMovie(movie, watchedMovies);
            var userData = _userDataManager.GetUserData(user, movie);
            bool changed = false;

            bool watchedOnSimkl = matched != null &&
                (string.Equals(matched.status, "completed", StringComparison.OrdinalIgnoreCase)
                 || !string.IsNullOrEmpty(matched.last_watched_at));

            if (watchedOnSimkl)
            {
                if (!userData.Played) { userData.Played = true; changed = true; }
                if (userData.PlayCount < 1) { userData.PlayCount = 1; changed = true; }
                var lastPlayed = ParseDate(matched.last_watched_at);
                if (lastPlayed.HasValue &&
                    (!userData.LastPlayedDate.HasValue || lastPlayed.Value > userData.LastPlayedDate.Value))
                {
                    userData.LastPlayedDate = lastPlayed;
                    changed = true;
                }
            }
            else if (!config.skipUnwatchedImportFromSimkl && userData.Played)
            {
                userData.Played = false;
                userData.PlayCount = 0;
                userData.LastPlayedDate = null;
                changed = true;
            }

            if (config.syncRatings && matched?.user_rating != null)
                changed |= ApplyRating(userData, matched.user_rating.Value);

            if (changed)
                _userDataManager.SaveUserData(user.InternalId, movie, userData, UserDataSaveReason.Import, cancellationToken);

            if (config.importPlaybackProgress)
            {
                var session = Match.FindMoviePlayback(movie, playbackMovies);
                UpdateResume(movie, user, session?.progress ?? 0, ParseDate(session?.paused_at));
            }
        }

        private void ImportEpisode(Episode episode, User user, UserConfig config, List<AllItemsShow> watchedShows,
            List<PlaybackSession> playbackEpisodes, CancellationToken cancellationToken)
        {
            var matchedShow = episode.Series != null ? Match.FindShow(episode.Series, watchedShows) : null;
            var userData = _userDataManager.GetUserData(user, episode);
            bool changed = false;

            bool watchedOnSimkl = false;
            if (matchedShow?.seasons != null)
            {
                var season = matchedShow.seasons.FirstOrDefault(s => s.number == SyncHelper.GetSeasonNumber(episode));
                watchedOnSimkl = season?.episodes != null && season.episodes.Any(ep => ep.number == episode.IndexNumber);
            }

            if (watchedOnSimkl)
            {
                if (!userData.Played) { userData.Played = true; changed = true; }
                if (userData.PlayCount < 1) { userData.PlayCount = 1; changed = true; }
                var lastPlayed = ParseDate(matchedShow.last_watched_at);
                if (lastPlayed.HasValue &&
                    (!userData.LastPlayedDate.HasValue || lastPlayed.Value > userData.LastPlayedDate.Value))
                {
                    userData.LastPlayedDate = lastPlayed;
                    changed = true;
                }
            }
            else if (!config.skipUnwatchedImportFromSimkl && userData.Played)
            {
                userData.Played = false;
                userData.PlayCount = 0;
                userData.LastPlayedDate = null;
                changed = true;
            }

            if (changed)
                _userDataManager.SaveUserData(user.InternalId, episode, userData, UserDataSaveReason.Import, cancellationToken);

            if (config.importPlaybackProgress)
            {
                var session = Match.FindEpisodePlayback(episode, playbackEpisodes);
                UpdateResume(episode, user, session?.progress ?? 0, ParseDate(session?.paused_at));
            }
        }

        /// <summary>Writes a resume point (playback position) back into Emby.</summary>
        private void UpdateResume(BaseItem item, User user, double progressPercent, DateTimeOffset? pausedAt)
        {
            if (!item.RunTimeTicks.HasValue) return;

            var userData = _userDataManager.GetUserData(user, item);
            long positionTicks = 0;
            if (progressPercent > 0)
            {
                positionTicks = Convert.ToInt64(item.RunTimeTicks.Value * (progressPercent / 100.0));
                if (pausedAt.HasValue) userData.LastPlayedDate = pausedAt;
            }

            if (userData.PlaybackPositionTicks != positionTicks)
            {
                _userDataManager.UpdatePlayState(item, userData, positionTicks);
                _userDataManager.SaveUserData(user, item, userData, UserDataSaveReason.PlaybackProgress, CancellationToken.None);
            }
        }

        private static bool ApplyRating(UserItemData userData, int rating)
        {
            double value = rating;
            if (userData.Rating.HasValue && Math.Abs(userData.Rating.Value - value) < 0.01) return false;
            userData.Rating = value;
            return true;
        }

        private static DateTimeOffset? ParseDate(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal, out var d) ? d : (DateTimeOffset?)null;
        }

        private User FindUser(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            Guid.TryParse(guid, out var parsed);
            return _userManager.Users.FirstOrDefault(u => u.Id == parsed || u.Id.ToString("N") == guid.Replace("-", ""));
        }
    }
}
