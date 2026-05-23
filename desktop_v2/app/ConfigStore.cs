using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Web.Script.Serialization;

namespace SortingMachineDesktop
{
    public class ConfigStore
    {
        private readonly string _dataDir;
        private readonly string _configPath;
        private readonly JavaScriptSerializer _serializer;

        public ConfigStore()
        {
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            _configPath = Path.Combine(_dataDir, "config.json");
            _serializer = new JavaScriptSerializer();
        }

        public ConfigData Load()
        {
            Directory.CreateDirectory(_dataDir);
            if (!File.Exists(_configPath))
            {
                var defaults = CreateDefault();
                Save(defaults);
                return defaults;
            }

            ConfigData data;
            try
            {
                var json = File.ReadAllText(_configPath, Encoding.UTF8);
                if (IsInvalidJsonPayload(json))
                {
                    throw new InvalidDataException("Config JSON is empty or contains invalid bytes.");
                }

                data = _serializer.Deserialize<ConfigData>(json);
            }
            catch (Exception ex)
            {
                QuarantineConfigFile(ex.GetType().Name);
                data = CreateDefault();
                Save(data);
                return data;
            }

            if (data == null)
            {
                QuarantineConfigFile("null_config");
                data = CreateDefault();
                Save(data);
                return data;
            }

            if (data.Config == null)
            {
                data.Config = MachineConfig.CreateDefault();
            }

            if (data.Thresholds == null)
            {
                data.Thresholds = new Dictionary<string, ThresholdSet>
                {
                    { "21700", CreateDefaultThresholds(data.Config.Channels) },
                    { "18650", CreateDefaultThresholds(data.Config.Channels) }
                };
            }

            if (data.IntelligentRecipes == null)
            {
                data.IntelligentRecipes = new Dictionary<string, IntelligentRecipe>
                {
                    { "21700", CreateDefaultIntelligentRecipe("21700") },
                    { "18650", CreateDefaultIntelligentRecipe("18650") }
                };
            }

            return data;
        }

        public void Save(ConfigData data)
        {
            Directory.CreateDirectory(_dataDir);
            if (data == null)
            {
                data = CreateDefault();
            }

            var json = _serializer.Serialize(data);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var tempPath = _configPath + ".tmp_" + stamp;
            var backupPath = _configPath + ".bak_" + stamp;

            File.WriteAllText(tempPath, json, Encoding.UTF8);
            try
            {
                if (File.Exists(_configPath))
                {
                    File.Replace(tempPath, _configPath, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, _configPath);
                }
            }
            catch
            {
                TryCopyExistingConfig(backupPath);
                if (File.Exists(_configPath))
                {
                    File.Delete(_configPath);
                }

                File.Move(tempPath, _configPath);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private ConfigData CreateDefault()
        {
            var cfg = MachineConfig.CreateDefault();
            cfg.ScanComPort = GuessScanCom(cfg.ComPort);
            return new ConfigData
            {
                Config = cfg,
                Thresholds = new Dictionary<string, ThresholdSet>
                {
                    { "21700", CreateDefaultThresholds(cfg.Channels) },
                    { "18650", CreateDefaultThresholds(cfg.Channels) }
                },
                IntelligentRecipes = new Dictionary<string, IntelligentRecipe>
                {
                    { "21700", CreateDefaultIntelligentRecipe("21700") },
                    { "18650", CreateDefaultIntelligentRecipe("18650") }
                }
            };
        }

        private ThresholdSet CreateDefaultThresholds(int channels)
        {
            var list = new List<ChannelThreshold>();
            for (var i = 0; i < channels; i++)
            {
                list.Add(new ChannelThreshold
                {
                    VoltageMin = 0.0,
                    VoltageMax = 4.5,
                    IrMin = 0.0,
                    IrMax = 999.99
                });
            }

            return new ThresholdSet { Channels = list };
        }

        private IntelligentRecipe CreateDefaultIntelligentRecipe(string cellType)
        {
            var capacities = new List<LaneCapacitySetting>();
            for (var i = 1; i <= 10; i++)
            {
                capacities.Add(new LaneCapacitySetting
                {
                    LaneId = i.ToString(),
                    Capacity = i == 10 ? QualityBandRouting.LearningSampleCount : 20
                });
            }

            return new IntelligentRecipe
            {
                CellType = cellType,
                SampleSize = QualityBandRouting.LearningSampleCount,
                MaxSigmaVoltage = 0.010,
                MaxSigmaIr = 2.000,
                AcceptanceKVoltage = 2.0,
                AcceptanceKIr = 2.0,
                MinWindowVoltage = 0.001,
                MinWindowIr = 0.250,
                MaxWindowVoltage = 0.020,
                MaxWindowIr = 4.000,
                GoodLanes = QualityBandRouting.BuildDefaultGoodLanes(),
                LastGoodLane = QualityBandRouting.LearningLaneId,
                NgLane = "NG",
                LaneCapacities = capacities,
                LanePreSwitchMargin = 0,
                NegativeVoltageToNg = false,
                LearningTimeoutCells = QualityBandRouting.LearningSampleCount + 1
            };
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

        private void QuarantineConfigFile(string reason)
        {
            if (!File.Exists(_configPath))
            {
                return;
            }

            var safeReason = SanitizeFileNamePart(reason);
            var target = _configPath + ".corrupt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + safeReason;
            try
            {
                File.Move(_configPath, target);
            }
            catch
            {
                try
                {
                    File.Copy(_configPath, target, true);
                }
                catch
                {
                    // Startup must continue even if Windows refuses the quarantine copy.
                }
            }
        }

        private void TryCopyExistingConfig(string backupPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(_configPath))
                {
                    File.Copy(_configPath, backupPath, true);
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

        private string GuessScanCom(string plcCom)
        {
            try
            {
                var ports = SerialPort.GetPortNames();
                if (ports == null || ports.Length == 0)
                {
                    return "COM2";
                }

                foreach (var port in ports)
                {
                    if (!string.Equals(port, plcCom, StringComparison.OrdinalIgnoreCase))
                    {
                        return port;
                    }
                }

                return ports[0];
            }
            catch
            {
                return "COM2";
            }
        }
    }
}
