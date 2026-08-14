namespace Simkl.Api.Objects
{
    public class PlaybackEpisodeRef
    {
        public int? season { get; set; }
        // The read endpoint uses "episode" for the number; "number" is kept as a fallback.
        public int? episode { get; set; }
        public int? number { get; set; }
        public string title { get; set; }

        public int? EpisodeNumber => episode ?? number;
    }

    /// <summary>One paused session returned by GET /sync/playback/{type}.</summary>
    public class PlaybackSession
    {
        public long id { get; set; }
        public double progress { get; set; }
        public string paused_at { get; set; }
        public string type { get; set; }   // "movie" | "episode"

        public SimklReadMovieInfo movie { get; set; }
        public SimklReadShowInfo show { get; set; }
        public PlaybackEpisodeRef episode { get; set; }
    }
}
