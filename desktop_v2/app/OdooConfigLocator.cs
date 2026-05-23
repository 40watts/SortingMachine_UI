using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SortingMachineDesktop
{
    internal sealed class OdooConnectionSettings
    {
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public string Source { get; set; }
    }

    internal static class OdooConfigLocator
    {
        private const string DefaultUrl = "https://40-watts-cycles.odoo.com";

        public static OdooConnectionSettings Load()
        {
            var settings = new OdooConnectionSettings
            {
                Url = NormalizeText(Environment.GetEnvironmentVariable("ODOO_URL")) ?? DefaultUrl,
                ApiKey = NormalizeText(Environment.GetEnvironmentVariable("ODOO_API_KEY")),
                Source = "environnement"
            };

            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                return settings;
            }

            foreach (var path in GetConfigPaths())
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var data = ReadConfigFile(path);
                    if (data == null || data.Count == 0)
                    {
                        continue;
                    }

                    var apiKey = ReadConfigString(data, "api_key", "apiKey", "ODOO_API_KEY", "odoo_api_key", "CLE_API", "cle_api");
                    if (string.IsNullOrWhiteSpace(apiKey))
                    {
                        continue;
                    }

                    settings.ApiKey = apiKey;
                    settings.Url = ReadConfigString(data, "url", "base_url", "ODOO_URL", "odoo_url", "ADRESSE", "adresse") ?? settings.Url;
                    settings.Source = path;
                    return settings;
                }
                catch
                {
                    // Invalid local config must not block the machine UI.
                }
            }

            return settings;
        }

        public static List<string> GetConfigPaths()
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return new List<string>
            {
                Path.Combine(roaming, "TriCellPilot", "ODOO.txt"),
                Path.Combine(roaming, "TriCellPilot", "odoo_config.json"),
                Path.Combine(local, "TriCellPilot", "ODOO.txt"),
                Path.Combine(local, "TriCellPilot", "odoo_config.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "odoo_config.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ODOO.txt"),
                Path.Combine(common, "TriCellPilot", "odoo_config.json"),
                Path.Combine(common, "SortingMachineDesktop", "odoo_config.json")
            };
        }

        private static Dictionary<string, object> ReadConfigFile(string path)
        {
            var raw = File.ReadAllText(path, Encoding.UTF8);
            if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                return new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
            }

            var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var trimmed = line.Trim();
                if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var splitIndex = trimmed.IndexOf(':');
                if (splitIndex < 0)
                {
                    splitIndex = trimmed.IndexOf('=');
                }

                if (splitIndex <= 0 || splitIndex >= trimmed.Length - 1)
                {
                    continue;
                }

                var key = trimmed.Substring(0, splitIndex).Trim();
                var value = trimmed.Substring(splitIndex + 1).Trim();
                data[key] = value;
            }

            return data;
        }

        private static string ReadConfigString(Dictionary<string, object> data, params string[] keys)
        {
            if (data == null || keys == null)
            {
                return null;
            }

            foreach (var key in keys)
            {
                foreach (var pair in data)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return NormalizeText(Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
                    }
                }
            }

            return null;
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
