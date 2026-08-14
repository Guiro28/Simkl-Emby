using System;
using System.Collections.Generic;
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
    /// Exports the local library playstates to Simkl: marks locally-watched movies and
    /// episodes as watched, pushes ratings and (optionally) adds unseen library items to
    /// the Simkl "Plan to watch" list. Equivalent to the Trakt "Sync library" task.
    ///
    /// Note: unlike the Trakt plugin this task never *removes* history from Simkl, to
    /// avoid accidental data loss. Use the Simkl website if you need to unmark items.
    /// </summary>
    public class SyncToSimklTask : IScheduledTask
    {
        private const int BATCH = 50;

        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly SimklApi _api;

        public SyncToSimklTask(ILogManager logManager, IJsonSerializer json, IUserManager userManager,
            IUserDataManager userDataManager, IHttpClient httpClient, ILibraryManager libraryManager, IFileSystem fileSystem)
        {
            _userManager = userManager;
            _userDataManager = userDataManager;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _logger = logManager.GetLogger("Simkl");
            _api = new SimklApi(json, _logger, httpClient);
        }

        public string Key => "SimklSyncToSimklTask";
        public string Name => "Sync library to Simkl";
        public string Description => "Marks locally watched movies/episodes as watched on Simkl and syncs ratings for each linked user";
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
                    await SyncUser(user, config, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error exporting to Simkl for " + user.Name, ex);
                }

                baseProgress += perUser;
                progress.Report(baseProgress);
            }
        }

        private async Task SyncUser(User user, UserConfig config, CancellationToken cancellationToken)
        {
            if (!config.postWatchedHistory && !config.syncRatings && !config.syncWatchlist)
            {
                _logger.Info("Nothing enabled to export for {0}", user.Name);
                return;
            }

            var token = config.userToken;
            var watchedMovies = (await _api.GetAllItems("movies", token).ConfigureAwait(false))?.movies ?? new List<AllItemsMovie>();
            var showsResp = await _api.GetAllItems("shows", token).ConfigureAwait(false);
            var animeResp = await _api.GetAllItems("anime", token).ConfigureAwait(false);
            var watchedShows = new List<AllItemsShow>();
            if (showsResp?.shows != null) watchedShows.AddRange(showsResp.shows);
            if (animeResp?.anime != null) watchedShows.AddRange(animeResp.anime);

            var movies = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { typeof(Movie).Name },
                IsVirtualItem = false
            }).OfType<Movie>().Where(m => SyncHelper.CanSync(m, config, _fileSystem)).ToList();

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { typeof(Episode).Name },
                IsVirtualItem = false
            }).OfType<Episode>().Where(e => SyncHelper.CanSync(e, config, _fileSystem)).ToList();

            // ---- Movies : mark watched + ratings + watchlist --------------------
            var watchedPayloadMovies = new List<SyncMovie>();
            var ratingMovies = new List<SyncMovie>();
            var watchlistMovies = new List<SyncMovie>();

            foreach (var movie in movies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var userData = _userDataManager.GetUserData(user, movie);
                var matched = Match.FindMovie(movie, watchedMovies);
                bool watchedOnSimkl = matched != null &&
                    (string.Equals(matched.status, "completed", StringComparison.OrdinalIgnoreCase)
                     || !string.IsNullOrEmpty(matched.last_watched_at));

                if (config.postWatchedHistory && userData.Played && !watchedOnSimkl)
                {
                    watchedPayloadMovies.Add(new SyncMovie
                    {
                        title = movie.Name,
                        year = movie.ProductionYear,
                        ids = SyncHelper.MovieIds(movie),
                        watched_at = SyncHelper.ToIso(userData.LastPlayedDate)
                    });
                }

                if (config.syncRatings && userData.Rating.HasValue && (matched?.user_rating == null))
                {
                    ratingMovies.Add(new SyncMovie
                    {
                        title = movie.Name,
                        year = movie.ProductionYear,
                        ids = SyncHelper.MovieIds(movie),
                        rating = (int)Math.Round(userData.Rating.Value)
                    });
                }

                if (config.syncWatchlist && !userData.Played && matched == null)
                {
                    watchlistMovies.Add(new SyncMovie
                    {
                        title = movie.Name,
                        year = movie.ProductionYear,
                        ids = SyncHelper.MovieIds(movie),
                        to = "plantowatch"
                    });
                }
            }

            // ---- Episodes : mark watched (grouped by show) ----------------------
            var showsById = new Dictionary<Guid, SyncShow>();
            foreach (var episode in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (episode.Series == null || !config.postWatchedHistory) break;

                var userData = _userDataManager.GetUserData(user, episode);
                if (!userData.Played) continue;

                var matchedShow = Match.FindShow(episode.Series, watchedShows);
                if (EpisodeWatchedOnSimkl(episode, matchedShow)) continue;

                if (!showsById.TryGetValue(episode.Series.Id, out var syncShow))
                {
                    syncShow = new SyncShow
                    {
                        title = episode.Series.Name,
                        year = episode.Series.ProductionYear,
                        ids = SyncHelper.ShowIds(episode.Series),
                        seasons = new List<SyncSeason>()
                    };
                    showsById[episode.Series.Id] = syncShow;
                }

                int seasonNo = SyncHelper.GetSeasonNumber(episode);
                var season = syncShow.seasons.FirstOrDefault(s => s.number == seasonNo);
                if (season == null)
                {
                    season = new SyncSeason { number = seasonNo, episodes = new List<SyncEpisode>() };
                    syncShow.seasons.Add(season);
                }
                season.episodes.Add(new SyncEpisode
                {
                    number = episode.IndexNumber,
                    watched_at = SyncHelper.ToIso(userData.LastPlayedDate)
                });
            }

            // ---- Series ratings -------------------------------------------------
            var ratingShows = new List<SyncShow>();
            if (config.syncRatings)
            {
                var series = _libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { typeof(Series).Name },
                    IsVirtualItem = false
                }).OfType<Series>().Where(s => SyncHelper.CanSync(s, config, _fileSystem)).ToList();

                foreach (var show in series)
                {
                    var userData = _userDataManager.GetUserData(user, show);
                    if (!userData.Rating.HasValue) continue;
                    var matched = Match.FindShow(show, watchedShows);
                    if (matched?.user_rating != null) continue;
                    ratingShows.Add(new SyncShow
                    {
                        title = show.Name,
                        year = show.ProductionYear,
                        ids = SyncHelper.ShowIds(show),
                        rating = (int)Math.Round(userData.Rating.Value)
                    });
                }
            }

            // ---- Send -----------------------------------------------------------
            await SendHistory(watchedPayloadMovies, showsById.Values.ToList(), token).ConfigureAwait(false);
            await SendRatings(ratingMovies, ratingShows, token).ConfigureAwait(false);
            if (config.syncWatchlist)
                await SendWatchlist(watchlistMovies, token).ConfigureAwait(false);
        }

        private async Task SendHistory(List<SyncMovie> movies, List<SyncShow> shows, string token)
        {
            _logger.Info("Export to Simkl history: {0} movies, {1} shows", movies.Count, shows.Count);
            foreach (var chunk in SyncHelper.Chunk(movies, BATCH))
                await _api.AddToHistory(new SyncPayload { movies = chunk }, token).ConfigureAwait(false);
            foreach (var chunk in SyncHelper.Chunk(shows, BATCH))
                await _api.AddToHistory(new SyncPayload { shows = chunk }, token).ConfigureAwait(false);
        }

        private async Task SendRatings(List<SyncMovie> movies, List<SyncShow> shows, string token)
        {
            if (movies.Count == 0 && shows.Count == 0) return;
            _logger.Info("Export to Simkl ratings: {0} movies, {1} shows", movies.Count, shows.Count);
            foreach (var chunk in SyncHelper.Chunk(movies, BATCH))
                await _api.AddRatings(new SyncPayload { movies = chunk }, token).ConfigureAwait(false);
            foreach (var chunk in SyncHelper.Chunk(shows, BATCH))
                await _api.AddRatings(new SyncPayload { shows = chunk }, token).ConfigureAwait(false);
        }

        private async Task SendWatchlist(List<SyncMovie> movies, string token)
        {
            if (movies.Count == 0) return;
            _logger.Info("Export to Simkl watchlist: {0} movies", movies.Count);
            foreach (var chunk in SyncHelper.Chunk(movies, BATCH))
                await _api.AddToList(new SyncPayload { movies = chunk }, token).ConfigureAwait(false);
        }

        private static bool EpisodeWatchedOnSimkl(Episode episode, AllItemsShow matchedShow)
        {
            if (matchedShow?.seasons == null) return false;
            var season = matchedShow.seasons.FirstOrDefault(s => s.number == SyncHelper.GetSeasonNumber(episode));
            return season?.episodes != null && season.episodes.Any(ep => ep.number == episode.IndexNumber);
        }

        private User FindUser(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            Guid.TryParse(guid, out var parsed);
            return _userManager.Users.FirstOrDefault(u => u.Id == parsed || u.Id.ToString("N") == guid.Replace("-", ""));
        }
    }
}
