using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using AiDataGateway.LocalLogViewer.Models;

namespace AiDataGateway.LocalLogViewer.Services
{
    public sealed class ConfigurationStore
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        public ConfigurationStore(string configurationDirectory = null)
        {
            ConfigurationDirectory = string.IsNullOrWhiteSpace(configurationDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config")
                : Path.GetFullPath(configurationDirectory);
            Directory.CreateDirectory(ConfigurationDirectory);
            RestrictDirectoryToCurrentUser(ConfigurationDirectory);
        }

        public string ConfigurationDirectory { get; }
        public string LogProfilesPath => Path.Combine(ConfigurationDirectory, "log-profiles.json");
        public string DatabaseProfilesPath => Path.Combine(ConfigurationDirectory, "database-profiles.json");

        public IList<LogProfile> LoadLogProfiles() => Load<List<LogProfile>>(LogProfilesPath) ?? new List<LogProfile>();
        public IList<DatabaseProfile> LoadDatabaseProfiles() => Load<List<DatabaseProfile>>(DatabaseProfilesPath) ?? new List<DatabaseProfile>();
        public void SaveLogProfiles(IEnumerable<LogProfile> profiles) => SaveAtomic(LogProfilesPath, new List<LogProfile>(profiles));
        public void SaveDatabaseProfiles(IEnumerable<DatabaseProfile> profiles) => SaveAtomic(DatabaseProfilesPath, new List<DatabaseProfile>(profiles));

        private T Load<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path, Encoding.UTF8);
            return string.IsNullOrWhiteSpace(json) ? null : _serializer.Deserialize<T>(json);
        }

        private void SaveAtomic(string path, object value)
        {
            var temporary = path + ".tmp";
            var backup = path + ".bak";
            File.WriteAllText(temporary, _serializer.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temporary, path, backup, true);
            }
            else
            {
                File.Move(temporary, path);
            }
        }

        private static void RestrictDirectoryToCurrentUser(string path)
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                if (identity.User == null) return;
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);
                security.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                Directory.SetAccessControl(path, security);
            }
            catch
            {
                // DPAPI still protects credentials when ACL hardening is unavailable (for example FAT/exFAT media).
            }
        }
    }
}
