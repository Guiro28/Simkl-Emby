using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

using Simkl.Api.Objects;
using Simkl.Api.Responses;
using Simkl.Api.Exceptions;
using MediaBrowser.Model.Dto;

namespace Simkl.Api
{
    public class SimklApi
    {
        /* INTERFACES */
        private readonly IJsonSerializer _json;
        private readonly ILogger _logger;
        private readonly IHttpClient _httpClient;

        /* BASIC API THINGS */
        public const string BASE_URL = @"https://api.simkl.com";

        public const string REDIRECT_URI = @"https://simkl.com/apps/emby/connected/";
        public const string APIKEY = @"27dd5d6adc24aa1ad9f95ef913244cbaf6df5696036af577ed41670473dc97d0";
        public const string SECRET = @"d7b9feb9d48bbaa69dbabaca21ba4671acaa89198637e9e136a4d69ec97ab68b";

        private HttpRequestOptions GetOptions(string userToken = null)
        {
            HttpRequestOptions options = new HttpRequestOptions
            {
                RequestContentType = "application/json",
                LogRequest = true,
                LogRequestAsDebug = true,
                LogResponse = true,
                LogResponseHeaders = true,
                LogErrorResponseBody = true,
                EnableDefaultUserAgent = true,
                TimeoutMs = 60000
            };
            options.RequestHeaders.Add("simkl-api-key", APIKEY);
            if (!string.IsNullOrEmpty(userToken))
                options.RequestHeaders.Add("Authorization", "Bearer " + userToken);

            return options;
        }

        public SimklApi(IJsonSerializer json, ILogger logger, IHttpClient httpClient)
        {
            _json = json;
            _logger = logger;
            _httpClient = httpClient;
        }

        /* ---------------------------------------------------------------- */
        /*  AUTHENTICATION                                                  */
        /* ---------------------------------------------------------------- */
        public async Task<CodeResponse> getCode()
        {
            string uri = string.Format("/oauth/pin?client_id={0}&redirect={1}", APIKEY, REDIRECT_URI);
            return _json.DeserializeFromStream<CodeResponse>(await _get(uri));
        }

        public async Task<CodeStatusResponse> getCodeStatus(string user_code)
        {
            string uri = string.Format("/oauth/pin/{0}?client_id={1}", user_code, APIKEY);
            return _json.DeserializeFromStream<CodeStatusResponse>(await _get(uri));
        }

        public async Task<UserSettings> getUserSettings(string userToken)
        {
            try
            {
                return _json.DeserializeFromStream<UserSettings>(await _post("/users/settings/", userToken));
            }
            catch (MediaBrowser.Model.Net.HttpException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new UserSettings() { error = "user_token_failed" };
            }
        }

        /* ---------------------------------------------------------------- */
        /*  REAL-TIME SCROBBLING (start / pause / stop)                     */
        /* ---------------------------------------------------------------- */
        private static string ScrobbleUrl(MediaStatus status)
        {
            switch (status)
            {
                case MediaStatus.Watching: return "/scrobble/start";
                case MediaStatus.Paused: return "/scrobble/pause";
                default: return "/scrobble/stop";
            }
        }

        /// <summary>Reports movie playback status to Simkl.</summary>
        public Task<ScrobbleResponse> ScrobbleMovie(SyncMovie movie, double progress, MediaStatus status, string userToken)
        {
            var body = new ScrobbleMovieBody { progress = progress, movie = movie };
            return PostAsync<ScrobbleResponse>(ScrobbleUrl(status), userToken, body);
        }

        /// <summary>Reports episode playback status to Simkl.</summary>
        public Task<ScrobbleResponse> ScrobbleEpisode(SyncShow show, int? season, int? number, double progress, MediaStatus status, string userToken)
        {
            var body = new ScrobbleEpisodeBody
            {
                progress = progress,
                show = show,
                episode = new ScrobbleEpisodeRef { season = season, number = number }
            };
            return PostAsync<ScrobbleResponse>(ScrobbleUrl(status), userToken, body);
        }

        /* ---------------------------------------------------------------- */
        /*  HISTORY  (mark watched / unwatched)                            */
        /* ---------------------------------------------------------------- */
        public Task<SyncHistoryResponse> AddToHistory(SyncPayload payload, string userToken)
            => PostAsync<SyncHistoryResponse>("/sync/history", userToken, payload);

        public Task<SyncHistoryResponse> RemoveFromHistory(SyncPayload payload, string userToken)
            => PostAsync<SyncHistoryResponse>("/sync/history/remove", userToken, payload);

        /* ---------------------------------------------------------------- */
        /*  RATINGS                                                        */
        /* ---------------------------------------------------------------- */
        public Task<object> AddRatings(SyncPayload payload, string userToken)
            => PostAsync<object>("/sync/ratings", userToken, payload);

        /* ---------------------------------------------------------------- */
        /*  WATCHLISTS  (plan to watch, completed, ...)                    */
        /* ---------------------------------------------------------------- */
        public Task<object> AddToList(SyncPayload payload, string userToken)
            => PostAsync<object>("/sync/add-to-list", userToken, payload);

        /* ---------------------------------------------------------------- */
        /*  READ  (import from Simkl)                                      */
        /* ---------------------------------------------------------------- */
        /// <summary>type = "movies" | "shows" | "anime".</summary>
        public Task<AllItemsResponse> GetAllItems(string type, string userToken)
        {
            string uri = string.Format("/sync/all-items/{0}/?extended=full&episode_watched_at=yes", type);
            return GetAsync<AllItemsResponse>(uri, userToken);
        }

        /// <summary>type = "movies" | "episodes" (empty for both).</summary>
        public Task<List<PlaybackSession>> GetPlayback(string type, string userToken)
        {
            string uri = string.IsNullOrEmpty(type) ? "/sync/playback/" : string.Format("/sync/playback/{0}/", type);
            return GetAsync<List<PlaybackSession>>(uri, userToken);
        }

        /* ---------------------------------------------------------------- */
        /*  FILENAME FALLBACK (Simkl-specific bonus)                       */
        /* ---------------------------------------------------------------- */
        public async Task<SearchFileResponse> getFromFile(string filename)
        {
            SimklFile f = new SimklFile { file = filename };
            _logger.Info("Posting: " + _json.SerializeToString(f));
            StreamReader r = new StreamReader(await _post("/search/file/", null, f));
            string t = r.ReadToEnd();
            _logger.Debug("Response: " + t);
            return _json.DeserializeFromString<SearchFileResponse>(t);
        }

        /* ---------------------------------------------------------------- */
        /*  LOW LEVEL                                                      */
        /* ---------------------------------------------------------------- */
        private async Task<T> PostAsync<T>(string url, string userToken, object data)
        {
            try
            {
                _logger.Debug("POST {0} : {1}", url, _json.SerializeToString(data));
                return Deserialize<T>(await ReadAll(await _post(url, userToken, data)));
            }
            catch (MediaBrowser.Model.Net.HttpException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.Error("Invalid user token, deleting");
                Plugin.Instance.deleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token");
            }
        }

        private async Task<T> GetAsync<T>(string url, string userToken)
        {
            try
            {
                return Deserialize<T>(await ReadAll(await _get(url, userToken)));
            }
            catch (MediaBrowser.Model.Net.HttpException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.Error("Invalid user token, deleting");
                Plugin.Instance.deleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token");
            }
        }

        private static async Task<string> ReadAll(Stream stream)
        {
            if (stream == null) return null;
            using (var reader = new StreamReader(stream))
                return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Deserializes a response, tolerating empty bodies and the literal "null"
        /// that Simkl returns for empty lists (which would otherwise throw).
        /// </summary>
        private T Deserialize<T>(string body)
        {
            if (string.IsNullOrWhiteSpace(body) || body.Trim() == "null")
                return default(T);
            return _json.DeserializeFromString<T>(body);
        }

        private async Task<Stream> _get(string url, string userToken = null)
        {
            HttpRequestOptions options = GetOptions(userToken);
            options.Url = BASE_URL + url;
            return await _httpClient.Get(options).ConfigureAwait(false);
        }

        private async Task<Stream> _post(string url, string userToken = null, object data = null)
        {
            HttpRequestOptions options = GetOptions(userToken);
            options.Url = BASE_URL + url;
            if (data != null) options.RequestContent = _json.SerializeToString(data).AsMemory();

            return (await _httpClient.Post(options).ConfigureAwait(false)).Content;
        }
    }
}
