using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SortingMachineDesktop
{
    public class HistoryStore
    {
        private readonly string _dataDir;
        private readonly string _csvPath;
        private readonly string _compactCsvPath;
        private readonly List<HistoryRow> _recent;
        private readonly object _lock = new object();
        private int _nextId;

        public HistoryStore()
        {
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            _csvPath = Path.Combine(_dataDir, "history.csv");
            _compactCsvPath = Path.Combine(_dataDir, "odoo_cell_tests.csv");
            _recent = new List<HistoryRow>();
            Directory.CreateDirectory(_dataDir);
            EnsureCompactCsvHeader();
            LoadRecent(200);
        }

        public List<HistoryRow> Latest(int limit)
        {
            lock (_lock)
            {
                var start = Math.Max(0, _recent.Count - limit);
                return new List<HistoryRow>(_recent.GetRange(start, _recent.Count - start));
            }
        }

        public HistoryRow Append(HistoryRow row)
        {
            if (row == null)
            {
                return null;
            }

            lock (_lock)
            {
                row.Id = _nextId++;
                if (string.IsNullOrWhiteSpace(row.Timestamp))
                {
                    row.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }

                _recent.Add(row);
                if (_recent.Count > 5000)
                {
                    _recent.RemoveAt(0);
                }

                AppendCsv(row);
                return row;
            }
        }

        public string CsvPath
        {
            get { return _csvPath; }
        }

        public string CompactCsvPath
        {
            get { return _compactCsvPath; }
        }

        public List<HistoryRow> LatestNg(int limit)
        {
            lock (_lock)
            {
                var result = new List<HistoryRow>();
                for (var i = _recent.Count - 1; i >= 0 && result.Count < limit; i--)
                {
                    var row = _recent[i];
                    if (IsNgRow(row))
                    {
                        result.Add(row);
                    }
                }

                result.Reverse();
                return result;
            }
        }

        private void LoadRecent(int limit)
        {
            if (!File.Exists(_csvPath))
            {
                _nextId = 1;
                return;
            }

            var lines = File.ReadAllLines(_csvPath);
            if (lines.Length == 0)
            {
                _nextId = 1;
                return;
            }

            var start = lines[0].StartsWith("id,", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            start = Math.Max(start, lines.Length - limit);
            for (var i = start; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length >= 14)
                {
                    AddRichRow(parts);
                }
                else if (parts.Length >= 6)
                {
                    AddLegacyRow(parts);
                }
            }
        }

        private void AddLegacyRow(string[] parts)
        {
            int id;
            double voltage;
            double ir;
            if (!int.TryParse(parts[0], out id))
            {
                return;
            }

            double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out voltage);
            double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out ir);
            _recent.Add(new HistoryRow
            {
                Id = id,
                Timestamp = parts[5],
                SortingMode = SortingModes.Legacy,
                CellType = "21700",
                LotId = 0,
                Voltage = voltage,
                Ir = ir,
                LegacyChannel = parts[3],
                Channel = parts[3],
                Result = parts[4],
                RejectReason = string.Empty,
                LearningStatus = LearningStatuses.Idle,
                Barcode = string.Empty,
                ThresholdSource = "LOCAL"
            });
            _nextId = Math.Max(_nextId, id + 1);
        }

        private void AddRichRow(string[] parts)
        {
            int id;
            int lotId;
            double voltage;
            double ir;
            if (!int.TryParse(parts[0], out id))
            {
                return;
            }

            int.TryParse(parts[4], out lotId);
            double.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out voltage);
            double.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out ir);

            _recent.Add(new HistoryRow
            {
                Id = id,
                Timestamp = parts[1],
                SortingMode = parts[2],
                CellType = parts[3],
                LotId = lotId,
                Voltage = voltage,
                Ir = ir,
                LegacyChannel = parts[7],
                Channel = parts[8],
                Result = parts[9],
                RejectReason = parts[10],
                LearningStatus = parts[11],
                Barcode = parts[12],
                ThresholdSource = parts[13]
            });
            _nextId = Math.Max(_nextId, id + 1);
        }

        private void AppendCsv(HistoryRow row)
        {
            if (!File.Exists(_csvPath))
            {
                File.AppendAllText(_csvPath, "id,timestamp,sorting_mode,cell_type,lot_id,voltage,ir,legacy_channel,target_lane,result,reject_reason,learning_status,barcode,threshold_source" + Environment.NewLine);
            }

            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13}",
                row.Id,
                Sanitize(row.Timestamp),
                Sanitize(row.SortingMode),
                Sanitize(row.CellType),
                row.LotId,
                row.Voltage,
                row.Ir,
                Sanitize(row.LegacyChannel),
                Sanitize(row.Channel),
                Sanitize(row.Result),
                Sanitize(row.RejectReason),
                Sanitize(row.LearningStatus),
                Sanitize(row.Barcode),
                Sanitize(row.ThresholdSource)
            );
            File.AppendAllText(_csvPath, line + Environment.NewLine);
            AppendCompactCsv(row);
        }

        private void AppendCompactCsv(HistoryRow row)
        {
            EnsureCompactCsvHeader();

            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                Sanitize(row.Timestamp),
                Sanitize(row.OdooLotReference),
                Sanitize(row.OdooLotName),
                Sanitize(string.IsNullOrWhiteSpace(row.OdooProductReference) ? row.OdooProductName : row.OdooProductReference),
                Sanitize(row.CellType),
                row.LotId,
                Sanitize(row.Result),
                Sanitize(row.Channel),
                row.Voltage,
                row.Ir,
                Sanitize(row.RejectReason),
                Sanitize(row.Barcode)
            );
            File.AppendAllText(_compactCsvPath, line + Environment.NewLine);
        }

        private void EnsureCompactCsvHeader()
        {
            if (!File.Exists(_compactCsvPath))
            {
                File.AppendAllText(_compactCsvPath, "timestamp,odoo_lot,odoo_lot_name,odoo_product,cell_type,lot_id,result,target_lane,voltage,ir,reject_reason,barcode" + Environment.NewLine);
            }
        }

        private bool IsNgRow(HistoryRow row)
        {
            if (row == null)
            {
                return false;
            }

            if (string.Equals(row.Result, "NG", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(row.Channel, "NG", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(row.RejectReason) &&
                !string.Equals(row.RejectReason, RejectReasons.None, StringComparison.OrdinalIgnoreCase);
        }

        private string Sanitize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace(",", ";").Replace(Environment.NewLine, " ");
        }
    }
}
