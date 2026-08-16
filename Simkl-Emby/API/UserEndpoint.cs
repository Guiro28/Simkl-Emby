using System;
using System.Collections.Generic;
using System.Linq;

using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;

using Simkl.Api.Responses;
using Simkl.Configuration;

namespace Simkl.Api
{
    /* ---- Routes (self-service, any logged-in user) ---- */

    [Route("/Simkl/me", "GET")]
    [Authenticated]
    public class GetMe : IReturn<MeStatus> { }

    [Route("/Simkl/me/pin", "GET")]
    [Authenticated]
    public class GetMePin : IReturn<CodeResponse> { }

    [Route("/Simkl/me/pin/{user_code}", "GET")]
    [Authenticated]
    public class GetMePinStatus : IReturn<CodeStatusResponse>
    {
        [ApiMember(Name = "user_code", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string user_code { get; set; }
    }

    [Route("/Simkl/me/settings", "POST")]
    [Authenticated]
    public class UpdateMe : IReturn<MeStatus>
    {
        public bool scrobbleMovies { get; set; }
        public bool scrobbleShows { get; set; }
        public int scr_pct { get; set; }
        public bool postWatchedHistory { get; set; }
        public bool importPlaybackProgress { get; set; }
        public bool syncRatings { get; set; }
        public bool syncWatchlist { get; set; }
        public bool skipUnwatchedImportFromSimkl { get; set; }
        public bool extraLogging { get; set; }
        public string[] locationsExcluded { get; set; }
    }

    [Route("/Simkl/me/logout", "POST")]
    [Authenticated]
    public class LogoutMe : IReturn<MeStatus> { }

    [Route("/Simkl/folders", "GET")]
    [Authenticated]
    public class GetFolders : IReturn<FoldersResult> { }

    public class FoldersResult
    {
        public string[] folders { get; set; }
    }

    /// <summary>Status + options returned to the user-facing settings page.</summary>
    public class MeStatus
    {
        public bool logged_in { get; set; }
        public string name { get; set; }
        public bool scrobbleMovies { get; set; }
        public bool scrobbleShows { get; set; }
        public int scr_pct { get; set; }
        public bool postWatchedHistory { get; set; }
        public bool importPlaybackProgress { get; set; }
        public bool syncRatings { get; set; }
        public bool syncWatchlist { get; set; }
        public bool skipUnwatchedImportFromSimkl { get; set; }
        public bool extraLogging { get; set; }
        public string[] folders { get; set; }
        public string[] excluded { get; set; }
    }

    /// <summary>
    /// Per-user Simkl configuration endpoints. The caller is identified from their own
    /// Emby auth token, so a normal (non-admin) user can manage only their own account
    /// without touching the admin-only plugin configuration API.
    /// </summary>
    public class UserEndpoint : IService, IHasResultFactory
    {
        public IHttpResultFactory ResultFactory { get; set; }
        public IRequest Request { get; set; }

        private readonly ILogger _logger;
        private readonly IAuthorizationContext _auth;
        private readonly ILibraryManager _libraryManager;
        private readonly SimklApi _api;

        public UserEndpoint(IJsonSerializer json, ILogManager logManager, IHttpClient httpClient,
            IAuthorizationContext auth, ILibraryManager libraryManager)
        {
            _logger = logManager.GetLogger("Simkl");
            _auth = auth;
            _libraryManager = libraryManager;
            _api = new SimklApi(json, _logger, httpClient);
        }

        private string CurrentGuid()
        {
            var info = _auth.GetAuthorizationInfo(Request);
            return info?.User?.Id.ToString("N");
        }

        private string[] AllFolders()
        {
            var list = new List<string>();
            try
            {
                foreach (var vf in _libraryManager.GetVirtualFolders())
                    if (vf?.Locations != null) list.AddRange(vf.Locations);
            }
            catch (Exception ex) { _logger.Debug("GetVirtualFolders failed: " + ex.Message); }
            return list.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        }

        private MeStatus BuildStatus(UserConfig cfg)
        {
            var status = new MeStatus
            {
                logged_in = cfg.IsLoggedIn,
                scrobbleMovies = cfg.scrobbleMovies,
                scrobbleShows = cfg.scrobbleShows,
                scr_pct = cfg.scr_pct,
                postWatchedHistory = cfg.postWatchedHistory,
                importPlaybackProgress = cfg.importPlaybackProgress,
                syncRatings = cfg.syncRatings,
                syncWatchlist = cfg.syncWatchlist,
                skipUnwatchedImportFromSimkl = cfg.skipUnwatchedImportFromSimkl,
                extraLogging = cfg.extraLogging,
                folders = AllFolders(),
                excluded = cfg.locationsExcluded ?? new string[] { }
            };
            if (cfg.IsLoggedIn)
            {
                try { status.name = _api.getUserSettings(cfg.userToken).Result?.user?.name; }
                catch (Exception ex) { _logger.Debug("getUserSettings failed: " + ex.Message); }
            }
            return status;
        }

        public FoldersResult Get(GetFolders request)
        {
            return new FoldersResult { folders = AllFolders() };
        }

        public MeStatus Get(GetMe request)
        {
            var cfg = Plugin.Instance.Configuration.getByGuid(CurrentGuid()) ?? new UserConfig();
            return BuildStatus(cfg);
        }

        public CodeResponse Get(GetMePin request)
        {
            return _api.getCode().Result;
        }

        public CodeStatusResponse Get(GetMePinStatus request)
        {
            var resp = _api.getCodeStatus(request.user_code).Result;
            if (resp != null && resp.result == "OK" && !string.IsNullOrEmpty(resp.access_token))
            {
                var cfg = Plugin.Instance.Configuration.GetOrCreate(CurrentGuid());
                cfg.userToken = resp.access_token;
                Plugin.Instance.SaveConfiguration();
                _logger.Info("Simkl linked for user " + CurrentGuid());
            }
            return resp;
        }

        public MeStatus Post(UpdateMe request)
        {
            var cfg = Plugin.Instance.Configuration.GetOrCreate(CurrentGuid());
            cfg.scrobbleMovies = request.scrobbleMovies;
            cfg.scrobbleShows = request.scrobbleShows;
            cfg.scr_pct = request.scr_pct;
            cfg.postWatchedHistory = request.postWatchedHistory;
            cfg.importPlaybackProgress = request.importPlaybackProgress;
            cfg.syncRatings = request.syncRatings;
            cfg.syncWatchlist = request.syncWatchlist;
            cfg.skipUnwatchedImportFromSimkl = request.skipUnwatchedImportFromSimkl;
            cfg.extraLogging = request.extraLogging;
            cfg.locationsExcluded = request.locationsExcluded ?? new string[] { };
            Plugin.Instance.SaveConfiguration();
            return BuildStatus(cfg);
        }

        public MeStatus Post(LogoutMe request)
        {
            var cfg = Plugin.Instance.Configuration.getByGuid(CurrentGuid());
            if (cfg != null) { cfg.userToken = ""; Plugin.Instance.SaveConfiguration(); }
            return Get(new GetMe());
        }
    }
}
