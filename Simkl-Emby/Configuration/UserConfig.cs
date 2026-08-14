using System;

namespace Simkl.Configuration
{
    /// <summary>
    /// Per-Emby-user Simkl configuration. One instance per linked user.
    /// Field names are kept camelCase because the config page binds inputs by matching
    /// the HTML element id to the property name.
    /// </summary>
    public class UserConfig
    {
        /* ---- account ---- */
        public string guid { get; set; }        // Emby user id
        public string userToken { get; set; }    // Simkl OAuth access token ("" when logged out)
        public string userName { get; set; }     // Simkl display name (informational)

        /* ---- real-time scrobbling ---- */
        public bool scrobbleMovies { get; set; }  // send movie playback status
        public bool scrobbleShows { get; set; }   // send episode playback status
        public int scr_pct { get; set; }          // minimum % to consider "watched"
        public int scr_w_pct { get; set; }        // % from which "now watching" is sent
        public int min_length { get; set; }       // minimum runtime (minutes) to scrobble
        public int scrobbleTimeout { get; set; }  // seconds between scrobble attempts

        /* ---- two-way library sync (scheduled tasks) ---- */
        // Export local -> Simkl : push watched/unwatched history for played items.
        public bool postWatchedHistory { get; set; }
        // Import Simkl -> Emby : when false, items watched only on Simkl are NOT marked
        // unwatched locally (safer default, same meaning as Trakt's SkipUnwatchedImport).
        public bool skipUnwatchedImportFromSimkl { get; set; }
        // Import resume points (paused sessions) so you can continue where you left off.
        public bool importPlaybackProgress { get; set; }
        // Two-way ratings sync (movies & shows only; Simkl has no episode/season ratings).
        public bool syncRatings { get; set; }
        // Push items present in the library but unseen to the Simkl "Plan to watch" list.
        public bool syncWatchlist { get; set; }

        /* ---- misc ---- */
        public bool extraLogging { get; set; }
        public string[] locationsExcluded { get; set; }  // library paths to ignore

        public UserConfig()
        {
            userToken = "";
            userName = "";

            scrobbleMovies = true;
            scrobbleShows = true;
            scr_pct = 70;
            scr_w_pct = 5;
            min_length = 5;
            scrobbleTimeout = 30;

            postWatchedHistory = true;
            skipUnwatchedImportFromSimkl = true;
            importPlaybackProgress = true;
            syncRatings = true;
            syncWatchlist = false;

            extraLogging = false;
            locationsExcluded = new string[] { };
        }

        public bool IsLoggedIn => !string.IsNullOrEmpty(userToken);
    }
}
