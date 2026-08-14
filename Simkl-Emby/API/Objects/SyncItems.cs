using System.Collections.Generic;

namespace Simkl.Api.Objects
{
    /// <summary>
    /// Identifiers block understood by every Simkl write endpoint. Only non-null
    /// values are meaningful; the serializer keeps nulls out of the way for Simkl.
    /// </summary>
    public class SyncIds
    {
        public int? simkl { get; set; }
        public string imdb { get; set; }
        public int? tmdb { get; set; }
        public int? tvdb { get; set; }
        public int? tvrage { get; set; }
        public int? mal { get; set; }
        public int? anidb { get; set; }
    }

    public class SyncMovie
    {
        public string title { get; set; }
        public int? year { get; set; }
        public SyncIds ids { get; set; }

        // history
        public string watched_at { get; set; }
        // ratings
        public int? rating { get; set; }
        public string rated_at { get; set; }
        // add-to-list ("plantowatch","completed","watching","hold","dropped")
        public string to { get; set; }
    }

    public class SyncEpisode
    {
        public int? number { get; set; }
        public SyncIds ids { get; set; }
        public string watched_at { get; set; }
    }

    public class SyncSeason
    {
        public int? number { get; set; }
        public string watched_at { get; set; }
        public List<SyncEpisode> episodes { get; set; }
    }

    public class SyncShow
    {
        public string title { get; set; }
        public int? year { get; set; }
        public SyncIds ids { get; set; }
        public List<SyncSeason> seasons { get; set; }

        // ratings
        public int? rating { get; set; }
        public string rated_at { get; set; }
        // add-to-list
        public string to { get; set; }
    }

    /// <summary>
    /// Body for /sync/history, /sync/history/remove, /sync/ratings and /sync/add-to-list.
    /// </summary>
    public class SyncPayload
    {
        public List<SyncMovie> movies { get; set; }
        public List<SyncShow> shows { get; set; }
        public List<SyncEpisode> episodes { get; set; }

        public SyncPayload()
        {
            movies = new List<SyncMovie>();
            shows = new List<SyncShow>();
            episodes = new List<SyncEpisode>();
        }

        public bool IsEmpty => movies.Count == 0 && shows.Count == 0 && episodes.Count == 0;
    }
}
