namespace Simkl.Api.Objects
{
    /// <summary>Movie block for /scrobble/{start,pause,stop}.</summary>
    public class ScrobbleMovieBody
    {
        public double progress { get; set; }
        public SyncMovie movie { get; set; }
    }

    public class ScrobbleEpisodeRef
    {
        public int? season { get; set; }
        public int? number { get; set; }
    }

    /// <summary>Episode block for /scrobble/{start,pause,stop}.</summary>
    public class ScrobbleEpisodeBody
    {
        public double progress { get; set; }
        public SyncShow show { get; set; }
        public ScrobbleEpisodeRef episode { get; set; }
    }

    /// <summary>Minimal response returned by the scrobble endpoints.</summary>
    public class ScrobbleResponse
    {
        public long? id { get; set; }
        public string action { get; set; }
        public double? progress { get; set; }
    }
}
