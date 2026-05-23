using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SortingMachineDesktop
{
    public class ObservationStore
    {
        private readonly string _dataDir;
        private readonly string _csvPath;
        private readonly List<ObservationRow> _recent;
        private readonly object _lock = new object();
        private int _nextId;

        public ObservationStore()
        {
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            _csvPath = Path.Combine(_dataDir, "observation.csv");
            _recent = new List<ObservationRow>();
            Directory.CreateDirectory(_dataDir);
            LoadRecent(500);
        }

        public List<ObservationRow> Latest(int limit)
        {
            lock (_lock)
            {
                var start = Math.Max(0, _recent.Count - limit);
                return new List<ObservationRow>(_recent.GetRange(start, _recent.Count - start));
            }
        }

        public ObservationRow Append(ObservationRow row)
        {
            if (row == null)
            {
                return null;
            }

            lock (_lock)
            {
                row.Id = _nextId++;
                _recent.Add(row);
                if (_recent.Count > 5000)
                {
                    _recent.RemoveAt(0);
                }

                AppendCsv(row);
                return row;
            }
        }

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _recent.Count;
                }
            }
        }

        public string CsvPath
        {
            get { return _csvPath; }
        }

        private void LoadRecent(int limit)
        {
            if (!File.Exists(_csvPath))
            {
                _nextId = 1;
                return;
            }

            var lines = File.ReadAllLines(_csvPath);
            if (lines.Length <= 1)
            {
                _nextId = 1;
                return;
            }

            var start = Math.Max(1, lines.Length - limit);
            for (var i = start; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 15)
                {
                    AddLegacyRow(parts);
                }
                else
                {
                    AddRichRow(parts);
                }
            }
        }

        private void AddLegacyRow(string[] parts)
        {
            if (parts.Length < 10)
            {
                return;
            }

            int id;
            int handshake;
            double voltage;
            double ir;
            if (!int.TryParse(parts[0], out id))
            {
                return;
            }

            int? handshakeValue = null;
            if (int.TryParse(parts[3], out handshake))
            {
                handshakeValue = handshake;
            }

            double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out voltage);
            double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out ir);

            _recent.Add(new ObservationRow
            {
                Id = id,
                Timestamp = parts[1],
                Source = parts[2],
                Handshake = handshakeValue,
                SortingMode = SortingModes.Legacy,
                CellType = "21700",
                Voltage = voltage,
                Ir = ir,
                Barcode = parts[6],
                LegacyChannel = parts[7],
                Channel = parts[7],
                Result = parts[8],
                RejectReason = string.Empty,
                LearningStatus = LearningStatuses.Idle,
                AlarmSummary = parts[9]
            });
            _nextId = Math.Max(_nextId, id + 1);
        }

        private void AddRichRow(string[] parts)
        {
            int id;
            int handshake;
            double voltage;
            double ir;
            if (!int.TryParse(parts[0], out id))
            {
                return;
            }

            int? handshakeValue = null;
            if (int.TryParse(parts[3], out handshake))
            {
                handshakeValue = handshake;
            }

            double.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out voltage);
            double.TryParse(parts[7], NumberStyles.Any, CultureInfo.InvariantCulture, out ir);

            _recent.Add(new ObservationRow
            {
                Id = id,
                Timestamp = parts[1],
                Source = parts[2],
                Handshake = handshakeValue,
                SortingMode = parts[4],
                CellType = parts[5],
                Voltage = voltage,
                Ir = ir,
                Barcode = parts[8],
                LegacyChannel = parts[9],
                Channel = parts[10],
                Result = parts[11],
                RejectReason = parts[12],
                LearningStatus = parts[13],
                AlarmSummary = parts[14]
            });
            _nextId = Math.Max(_nextId, id + 1);
        }

        private void AppendCsv(ObservationRow row)
        {
            if (!File.Exists(_csvPath))
            {
                File.AppendAllText(_csvPath, "id,timestamp,source,handshake,sorting_mode,cell_type,voltage,ir,barcode,legacy_channel,target_lane,result,reject_reason,learning_status,alarms" + Environment.NewLine);
            }

            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}",
                row.Id,
                Sanitize(row.Timestamp),
                Sanitize(row.Source),
                row.Handshake.HasValue ? row.Handshake.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                Sanitize(row.SortingMode),
                Sanitize(row.CellType),
                row.Voltage,
                row.Ir,
                Sanitize(row.Barcode),
                Sanitize(row.LegacyChannel),
                Sanitize(row.Channel),
                Sanitize(row.Result),
                Sanitize(row.RejectReason),
                Sanitize(row.LearningStatus),
                Sanitize(row.AlarmSummary)
            );
            File.AppendAllText(_csvPath, line + Environment.NewLine);
        }

        private string Sanitize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace(",", ";").Replace(Environment.NewLine, " ");
        }
    }
}
