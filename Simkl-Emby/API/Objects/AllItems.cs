using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    /// <summary>
    /// Identifiers as returned by the read endpoints (/sync/all-items, /sync/playback).
    /// Note that tmdb/tvdb come back as strings here, unlike the write side.
    /// </summary>
    public class SimklReadIds
    {
        public int? simkl { get; set; }
        public string imdb { get; set; }
        public string tmdb { get; set; }
        public string tvdb { get; set; }
        public string slug { get; set; }
        public string mal { get; set; }
        public string anidb { get; set; }
        public string zap2it { get; set; }
    }

    public class SimklReadMovieInfo
    {
        public string title { get; set; }
        public int? year { get; set; }
        public SimklReadIds ids { get; set; }
    }

    public class SimklReadShowInfo
    {
        public string title { get; set; }
        public int? year { get; set; }
        public SimklReadIds ids { get; set; }
    }

    public class WatchedEpisode
    {
        public int? number { get; set; }
        public string watched_at { get; set; }
    }

    public class WatchedSeason
    {
        public int? number { get; set; }
        public List<WatchedEpisode> episodes { get; set; }
    }

    public class AllItemsMovie
    {
        public string last_watched_at { get; set; }
        public string added_to_watchlist_at { get; set; }
        public string user_rated_at { get; set; }
        public int? user_rating { get; set; }
        public string status { get; set; }   // completed, plantowatch, ...
        public SimklReadMovieInfo movie { get; set; }
    }

    public class AllItemsShow
    {
        public string last_watched_at { get; set; }
        public string added_to_watchlist_at { get; set; }
        public string user_rated_at { get; set; }
        public int? user_rating { get; set; }
        public string status { get; set; }
        public int? watched_episodes_count { get; set; }
        public SimklReadShowInfo show { get; set; }
        public List<WatchedSeason> seasons { get; set; }   // present with extended=full
    }

    /// <summary>Response of GET /sync/all-items/{type}.</summary>
    public class AllItemsResponse
    {
        public List<AllItemsMovie> movies { get; set; }
        public List<AllItemsShow> shows { get; set; }
        public List<AllItemsShow> anime { get; set; }
    }
}
