define(["loading", "emby-input", "emby-button", "emby-checkbox"], function (loading) {
    "use strict";

    var boolFields = ["scrobbleMovies", "scrobbleShows", "postWatchedHistory",
        "importPlaybackProgress", "syncRatings", "syncWatchlist",
        "skipUnwatchedImportFromSimkl", "extraLogging"];
    var numberFields = ["scr_pct"];

    return function (view) {
        var loginTimer = null, remainingTimer = null, finish = null;

        function show(id) { view.querySelector(id).style.display = ""; }
        function hide(id) { view.querySelector(id).style.display = "none"; }

        function render(status) {
            hide("#suLoginContainer");
            hide("#suLoggingIn");
            hide("#SimklUserForm");

            if (status && status.logged_in) {
                view.querySelector("#suName").textContent = status.name || "";
                boolFields.forEach(function (k) { view.querySelector("#" + k).checked = !!status[k]; });
                numberFields.forEach(function (k) {
                    if (status[k] != null) view.querySelector("#" + k).value = status[k];
                });
                renderFolders(status.folders || [], status.excluded || []);
                show("#SimklUserForm");
            } else {
                show("#suLoginContainer");
            }
        }

        function loadStatus() {
            loading.show();
            ApiClient.getJSON(ApiClient.getUrl("Simkl/me")).then(function (status) {
                render(status);
                loading.hide();
            }, function () {
                loading.hide();
                render(null);
            });
        }

        function renderFolders(folders, excluded) {
            var html = "";
            folders.forEach(function (loc) {
                var checked = excluded.some(function (x) {
                    return x && x.toLowerCase() === loc.toLowerCase();
                }) ? 'checked="checked"' : '';
                html += '<label class="emby-checkbox-label"><input is="emby-checkbox" type="checkbox" class="suLoc" data-mini="true" value="' +
                    loc + '" ' + checked + ' /><span>' + loc + '</span></label>';
            });
            view.querySelector("#suLocations").innerHTML =
                html || "<div class='fieldDescription'>No library folders found.</div>";
        }

        function save() {
            var payload = {};
            boolFields.forEach(function (k) { payload[k] = view.querySelector("#" + k).checked; });
            numberFields.forEach(function (k) {
                var v = parseInt(view.querySelector("#" + k).value, 10);
                payload[k] = isNaN(v) ? 0 : v;
            });
            payload.locationsExcluded = Array.prototype.slice
                .call(view.querySelectorAll(".suLoc:checked"))
                .map(function (c) { return c.value; });
            loading.show();
            ApiClient.ajax({
                type: "POST",
                url: ApiClient.getUrl("Simkl/me/settings"),
                data: JSON.stringify(payload),
                contentType: "application/json",
                dataType: "json"
            }).then(function (status) {
                render(status);
                loading.hide();
                Dashboard.alert("Settings saved.");
            }, function () {
                loading.hide();
                Dashboard.alert("Could not save settings.");
            });
        }

        function startLogin() {
            ApiClient.getJSON(ApiClient.getUrl("Simkl/me/pin")).then(function (code) {
                if (!code || !code.user_code) { Dashboard.alert("Could not contact Simkl."); return; }
                finish = new Date();
                finish.setSeconds(finish.getSeconds() + (code.expires_in || 900));

                view.querySelector("#suLoginText").innerHTML =
                    "Please visit <a href='" + code.verification_url + "/" + code.user_code +
                    "' target='_blank'>" + code.verification_url + "</a> and enter the code:";
                view.querySelector("#suLoginPin").textContent = code.user_code;
                hide("#suLoginContainer");
                show("#suLoggingIn");

                var interval = (code.interval || 5) * 1000;
                loginTimer = window.setTimeout(function () { checkLogin(code); }, interval);
                remainingTimer = window.setInterval(function () {
                    view.querySelector("#suSeconds").textContent =
                        Math.max(0, Math.round((finish.getTime() - new Date().getTime()) / 1000));
                }, 1000);
            });
        }

        function checkLogin(code) {
            ApiClient.getJSON(ApiClient.getUrl("Simkl/me/pin/" + code.user_code)).then(function (resp) {
                if (new Date() > finish) { stopLogin(); Dashboard.alert("Timed out!"); return; }
                if (resp && resp.result === "OK") {
                    stopLogin();
                    loadStatus();
                } else {
                    loginTimer = window.setTimeout(function () { checkLogin(code); }, (code.interval || 5) * 1000);
                }
            }, function () {
                loginTimer = window.setTimeout(function () { checkLogin(code); }, (code.interval || 5) * 1000);
            });
        }

        function stopLogin() {
            window.clearTimeout(loginTimer);
            window.clearInterval(remainingTimer);
            hide("#suLoggingIn");
            show("#suLoginContainer");
        }

        function logout() {
            loading.show();
            ApiClient.ajax({ type: "POST", url: ApiClient.getUrl("Simkl/me/logout"), dataType: "json" })
                .then(function (status) { render(status); loading.hide(); },
                      function () { loading.hide(); loadStatus(); });
        }

        view.querySelector("#suLogin").addEventListener("click", startLogin);
        view.querySelector("#suCreate").addEventListener("click", function () { window.open("https://simkl.com/", "_blank"); });
        view.querySelector("#suCancel").addEventListener("click", stopLogin);
        view.querySelector("#suLogout").addEventListener("click", logout);
        view.querySelector("#SimklUserForm").addEventListener("submit", function (e) {
            e.preventDefault(); save(); return false;
        });

        view.addEventListener("viewshow", loadStatus);
        view.addEventListener("viewhide", stopLogin);
    };
});
