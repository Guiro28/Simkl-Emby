namespace Simkl
{
    /// <summary>
    /// Real-time playback status reported to Simkl through the scrobble endpoints.
    /// Mirrors the behaviour of the Trakt plugin (start / pause / stop).
    /// </summary>
    public enum MediaStatus
    {
        Watching,   // -> /scrobble/start
        Paused,     // -> /scrobble/pause  (saves a resume point)
        Stop        // -> /scrobble/stop   (marks watched when progress >= 80%)
    }
}
