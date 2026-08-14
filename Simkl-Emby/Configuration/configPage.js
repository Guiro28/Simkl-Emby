define(["jQuery", "loading", "emby-input", "emby-button", "emby-select", "emby-checkbox"],
function ($, loading) {
    "use strict";

    var pluginId = "2ecd91d5-b14b-4b92-8eb9-52c098edfc87";
    var boolFields = ["scrobbleMovies", "scrobbleShows", "postWatchedHistory",
        "importPlaybackProgress", "syncRatings", "syncWatchlist",
        "skipUnwatchedImportFromSimkl", "extraLogging"];
    var numberFields = ["scr_pct"];

    // ---- small synchronous API helpers (plugin server routes) ----
    function syncGet(uri) {
        var request = new XMLHttpRequest();
        request.open("GET", uri, false);
        request.send();
        if (request.status === 200) {
            try { return JSON.parse(request.response); } catch (e) { return null; }
        }
        console.log("Simkl API error", request);
        return null;
    }
    function apiGetCode() { return syncGet("/Simkl/oauth/pin?api_key=" + ApiClient.accessToken()); }
    function apiCheckCode(code) { return syncGet("/Simkl/oauth/pin/" + code + "?api_key=" + ApiClient.accessToken()); }
    function apiUserSettings(secret) { return syncGet("/Simkl/users/settings/" + secret + "?api_key=" + ApiClient.accessToken()); }

    return function (view) {
        var configCache = null;
        var loginTimer = null, remainingTimer = null, finish = null, onLoginProcess = false;

        function show(id) { view.querySelector(id).style.display = ""; }
        function hide(id) { view.querySelector(id).style.display = "none"; }

        function selectedUser() { return view.querySelector("#user-selector").value; }

        function populateUsers(users) {
            var sel = view.querySelector("#user-selector");
            sel.innerHTML = "";
            users.forEach(function (u) {
                var opt = document.createElement("option");
                opt.value = u.Id; opt.textContent = u.Name;
                sel.appendChild(opt);
            });
        }

        function loadConfig(userId) {
            hide("#loginButtonContainer");
            hide("#configOptionsContainer");
            hide("#loggingIn");

            var uconfig = (configCache.userConfigs || []).filter(function (e) { return e.guid === userId; })[0];
            if (uconfig && uconfig.userToken) {
                populateOptions(uconfig);
                show("#configOptionsContainer");
            } else {
                show("#loginButtonContainer");
            }
        }

        function populateOptions(uconfig) {
            try {
                var settings = apiUserSettings(uconfig.guid);
                if (settings && settings.user) view.querySelector("#simklName").textContent = settings.user.name;
            } catch (e) { console.log(e); }

            boolFields.forEach(function (key) {
                view.querySelector("#" + key).checked = uconfig[key] !== false;
            });
            numberFields.forEach(function (key) {
                if (uconfig[key] != null) view.querySelector("#" + key).value = uconfig[key];
            });

            var excluded = uconfig.locationsExcluded || [];
            ApiClient.getVirtualFolders(uconfig.guid).then(function (folders) {
                var html = "";
                (folders || []).forEach(function (vf) {
                    (vf.Locations || []).forEach(function (loc) {
                        var checked = excluded.some(function (x) {
                            return x && x.toLowerCase() === loc.toLowerCase();
                        }) ? 'checked="checked"' : '';
                        html += '<label class="emby-checkbox-label"><input is="emby-checkbox" type="checkbox" class="chkSimklLocation" data-mini="true" value="' +
                            loc + '" ' + checked + ' /><span>' + loc + '</span></label>';
                    });
                });
                view.querySelector("#divSimklLocations").innerHTML =
                    html || "<div class='fieldDescription'>No library folders found.</div>";
            });
        }

        function saveConfig() {
            var guid = selectedUser();
            var uconfig = (configCache.userConfigs || []).filter(function (e) { return e.guid === guid; })[0];
            if (!uconfig) {
                uconfig = { guid: guid };
                if (!configCache.userConfigs) configCache.userConfigs = [];
                configCache.userConfigs.push(uconfig);
            }

            boolFields.forEach(function (key) {
                uconfig[key] = view.querySelector("#" + key).checked;
            });
            numberFields.forEach(function (key) {
                var v = parseInt(view.querySelector("#" + key).value, 10);
                if (!isNaN(v)) uconfig[key] = v;
            });
            uconfig.locationsExcluded = Array.prototype.slice
                .call(view.querySelectorAll(".chkSimklLocation:checked"))
                .map(function (c) { return c.value; });

            loading.show();
            ApiClient.updatePluginConfiguration(pluginId, configCache).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
                loading.hide();
            });
        }

        function startLogin() {
            onLoginProcess = true;
            var code = apiGetCode();
            if (!code) { Dashboard.alert("Could not contact Simkl"); return; }
            finish = new Date();
            finish.setSeconds(finish.getSeconds() + code.expires_in);

            view.querySelector("#loginText").innerHTML =
                "Please visit <a href='" + code.verification_url + "/" + code.user_code +
                "' target='_blank'>" + code.verification_url + "</a> and enter the code:";
            view.querySelector("#loginPin").textContent = code.user_code;
            hide("#loginButtonContainer");
            show("#loggingIn");

            loginTimer = window.setTimeout(function () { checkLogin(code); }, code.interval * 1000);
            remainingTimer = window.setInterval(function () {
                view.querySelector("#loginSecondsRemaining").textContent =
                    Math.max(0, Math.round((finish.getTime() - new Date().getTime()) / 1000));
            }, 1000);
        }

        function checkLogin(code) {
            var response = apiCheckCode(code.user_code);
            if (new Date() > finish) {
                Dashboard.alert("Timed out!");
                stopLogin();
            } else if (response && response.result === "KO") {
                loginTimer = window.setTimeout(function () { checkLogin(code); }, code.interval * 1000);
            } else if (response && response.result === "OK") {
                stopLogin();
                var guid = selectedUser();
                var filter = (configCache.userConfigs || []).filter(function (c) { return c.guid === guid; });
                if (filter.length > 0) {
                    filter[0].userToken = response.access_token;
                } else {
                    if (!configCache.userConfigs) configCache.userConfigs = [];
                    configCache.userConfigs.push({ guid: guid, userToken: response.access_token });
                }
                ApiClient.updatePluginConfiguration(pluginId, configCache).then(function () {
                    loadConfig(guid);
                });
            } else {
                Dashboard.alert("Error logging in");
            }
        }

        function stopLogin() {
            onLoginProcess = false;
            window.clearTimeout(loginTimer);
            window.clearInterval(remainingTimer);
            show("#loginButtonContainer");
            hide("#loggingIn");
        }

        function logOut() {
            var guid = selectedUser();
            var filter = (configCache.userConfigs || []).filter(function (c) { return c.guid === guid; });
            if (filter.length > 0) filter[0].userToken = "";
            ApiClient.updatePluginConfiguration(pluginId, configCache).then(function () {
                loadConfig(guid);
            });
        }

        // ---- wire events (once) ----
        view.querySelector("#user-selector").addEventListener("change", function () {
            if (onLoginProcess) stopLogin();
            loadConfig(selectedUser());
        });
        view.querySelector("#btnSimklLogin").addEventListener("click", startLogin);
        view.querySelector("#btnSimklCreate").addEventListener("click", function () {
            window.open("https://simkl.com/", "_blank");
        });
        view.querySelector("#btnSimklCancel").addEventListener("click", stopLogin);
        view.querySelector("#btnSimklLogout").addEventListener("click", logOut);
        view.querySelector("#SimklConfigurationForm").addEventListener("submit", function (e) {
            e.preventDefault();
            saveConfig();
            return false;
        });

        // ---- lifecycle ----
        view.addEventListener("viewshow", function () {
            loading.show();
            Promise.all([
                ApiClient.getUsers(),
                ApiClient.getPluginConfiguration(pluginId)
            ]).then(function (results) {
                populateUsers(results[0]);
                configCache = results[1];
                var current = ApiClient.getCurrentUserId();
                var sel = view.querySelector("#user-selector");
                if (current) sel.value = current;
                loadConfig(sel.value);
                loading.hide();
            });
        });

        view.addEventListener("viewhide", function () {
            if (onLoginProcess) stopLogin();
        });
    };
});
