using System.Linq;
using MediaBrowser.Model.Plugins;

namespace Simkl.Configuration
{
    /// <summary>
    /// Class needed to create a Plugin and configure it
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public UserConfig[] userConfigs { get; set; }

        public PluginConfiguration()
        {
            userConfigs = new UserConfig[] { };
        }

        public UserConfig getByGuid(string guid)
        {
            return userConfigs?.FirstOrDefault(c => c.guid == guid);
        }

        /// <summary>Returns the config for this Emby user id, creating it if missing.</summary>
        public UserConfig GetOrCreate(string guid)
        {
            var cfg = getByGuid(guid);
            if (cfg == null)
            {
                cfg = new UserConfig { guid = guid };
                var list = (userConfigs ?? new UserConfig[] { }).ToList();
                list.Add(cfg);
                userConfigs = list.ToArray();
            }
            return cfg;
        }

        /// <summary>All users that are currently logged in to Simkl.</summary>
        public UserConfig[] LoggedInUsers()
        {
            return userConfigs?.Where(c => c != null && c.IsLoggedIn).ToArray() ?? new UserConfig[] { };
        }
    }
}
