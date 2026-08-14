using System;
using System.Linq;
using MediaBrowser.Controller.Entities;
using Simkl.Configuration;

namespace Simkl.Helpers
{
    /// <summary>Maps Emby users to their (logged-in) Simkl configuration.</summary>
    internal static class UserHelper
    {
        public static UserConfig GetSimklUser(User user)
        {
            if (user == null) return null;
            return GetSimklUser(user.Id);
        }

        public static UserConfig GetSimklUser(Guid userGuid)
        {
            return GetSimklUser(userGuid.ToString("N"));
        }

        public static UserConfig GetSimklUser(string userId)
        {
            var config = Plugin.Instance?.PluginConfiguration;
            if (config?.userConfigs == null || string.IsNullOrEmpty(userId))
                return null;

            // Emby ids may be formatted with or without dashes depending on the caller.
            var normalized = Normalize(userId);
            return config.userConfigs.FirstOrDefault(c =>
                c != null && c.IsLoggedIn && Normalize(c.guid) == normalized);
        }

        private static string Normalize(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            return Guid.TryParse(id, out var g) ? g.ToString("N") : id.Replace("-", "").ToLowerInvariant();
        }
    }
}
