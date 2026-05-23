using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SortingMachineDesktop
{
    public class BusinessStore
    {
        private readonly string _dataDir;
        private readonly string _businessPath;
        private readonly JavaScriptSerializer _serializer;

        public BusinessStore()
        {
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            _businessPath = Path.Combine(_dataDir, "business.json");
            _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public BusinessData Load()
        {
            Directory.CreateDirectory(_dataDir);
            if (!File.Exists(_businessPath))
            {
                var defaults = CreateDefault();
                Save(defaults);
                return defaults;
            }

            BusinessData data;
            try
            {
                data = LoadFromPath(_businessPath);
            }
            catch (Exception ex)
            {
                QuarantineBusinessFile(ex.GetType().Name);
                data = LoadLatestValidBackup();
                if (data == null)
                {
                    data = CreateDefault();
                }

                Normalize(data);
                Save(data);
                return data;
            }

            if (data == null)
            {
                QuarantineBusinessFile("null_business");
                data = CreateDefault();
                Save(data);
            }

            Normalize(data);

            return data;
        }

        public void Save(BusinessData data)
        {
            Directory.CreateDirectory(_dataDir);
            if (data == null)
            {
                data = CreateDefault();
            }

            Normalize(data);
            var json = _serializer.Serialize(data);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var tempPath = _businessPath + ".tmp_" + stamp;
            var backupPath = _businessPath + ".bak_" + stamp;

            File.WriteAllText(tempPath, json, Encoding.UTF8);
            try
            {
                if (File.Exists(_businessPath))
                {
                    File.Replace(tempPath, _businessPath, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, _businessPath);
                }
            }
            catch
            {
                TryCopyExistingBusiness(backupPath);
                if (File.Exists(_businessPath))
                {
                    File.Delete(_businessPath);
                }

                File.Move(tempPath, _businessPath);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private BusinessData LoadFromPath(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            if (IsInvalidJsonPayload(json))
            {
                throw new InvalidDataException("Business JSON is empty or contains invalid bytes.");
            }

            return _serializer.Deserialize<BusinessData>(json);
        }

        private BusinessData LoadLatestValidBackup()
        {
            var dir = new DirectoryInfo(_dataDir);
            if (!dir.Exists)
            {
                return null;
            }

            var candidates = new List<FileInfo>();
            candidates.AddRange(dir.GetFiles("business.json.bak*"));
            candidates.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            foreach (var candidate in candidates)
            {
                try
                {
                    var data = LoadFromPath(candidate.FullName);
                    if (data != null)
                    {
                        return data;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private void Normalize(BusinessData data)
        {
            if (data.Lots == null)
            {
                data.Lots = new List<LotSession>();
            }

            if (data.LaneCapacityObservations == null)
            {
                data.LaneCapacityObservations = new List<LaneCapacityObservation>();
            }
        }

        private bool IsInvalidJsonPayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return true;
            }

            if (json.IndexOf('\0') >= 0)
            {
                return true;
            }

            var trimmed = json.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            return !trimmed.StartsWith("{", StringComparison.Ordinal);
        }

        private void QuarantineBusinessFile(string reason)
        {
            if (!File.Exists(_businessPath))
            {
                return;
            }

            var target = _businessPath + ".corrupt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + SanitizeFileNamePart(reason);
            try
            {
                File.Move(_businessPath, target);
            }
            catch
            {
                try
                {
                    File.Copy(_businessPath, target, true);
                }
                catch
                {
                    // Preserve startup: a broken business file must not prevent the UI from opening.
                }
            }
        }

        private void TryCopyExistingBusiness(string backupPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(_businessPath))
                {
                    File.Copy(_businessPath, backupPath, true);
                }
            }
            catch
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }

        private BusinessData CreateDefault()
        {
            return new BusinessData
            {
                Lots = new List<LotSession>(),
                LaneCapacityObservations = new List<LaneCapacityObservation>()
            };
        }
    }
}
