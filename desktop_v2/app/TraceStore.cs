using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SortingMachineDesktop
{
    public class TraceStore
    {
        private readonly string _dataDir;
        private readonly string _csvPath;
        private readonly List<RuntimeTraceRow> _recent;
        private readonly object _lock = new object();
        private int _nextId;
        private const int MaxTraceBytesToLoad = 8 * 1024 * 1024;

        public TraceStore()
        {
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            _csvPath = Path.Combine(_dataDir, "runtime_trace.csv");
            _recent = new List<RuntimeTraceRow>();
            Directory.CreateDirectory(_dataDir);
            LoadRecent(500);
        }

        public RuntimeTraceRow Append(string category, string action, string status, string source, string register, string value, string detail)
        {
            var row = new RuntimeTraceRow
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                Category = category,
                Action = action,
                Status = status,
                Source = source,
                Register = register,
                Value = value,
                Detail = detail
            };

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

        public string CsvPath
        {
            get { return _csvPath; }
        }

        public List<RuntimeTraceRow> Latest(int limit)
        {
            lock (_lock)
            {
                var safeLimit = Math.Max(1, Math.Min(limit, _recent.Count == 0 ? 1 : _recent.Count));
                var start = Math.Max(0, _recent.Count - safeLimit);
                return new List<RuntimeTraceRow>(_recent.GetRange(start, _recent.Count - start));
            }
        }

        private void LoadRecent(int limit)
        {
            try
            {
                if (!File.Exists(_csvPath))
                {
                    _nextId = 1;
                    return;
                }

                var text = ReadRecentTraceText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    _nextId = 1;
                    return;
                }

                if (text.IndexOf('\0') >= 0)
                {
                    text = text.Replace("\0", string.Empty);
                }

                var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                if (lines.Length == 0)
                {
                    _nextId = 1;
                    return;
                }

                var firstDataLine = lines[0].StartsWith("id,", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                var start = Math.Max(firstDataLine, lines.Length - limit);
                for (var i = start; i < lines.Length; i++)
                {
                    var parts = lines[i].Split(',');
                    if (parts.Length < 9)
                    {
                        continue;
                    }

                    int id;
                    if (!int.TryParse(parts[0], out id))
                    {
                        continue;
                    }

                    _recent.Add(new RuntimeTraceRow
                    {
                        Id = id,
                        Timestamp = parts[1],
                        Category = parts[2],
                        Action = parts[3],
                        Status = parts[4],
                        Source = parts[5],
                        Register = parts[6],
                        Value = parts[7],
                        Detail = parts[8]
                    });
                    _nextId = Math.Max(_nextId, id + 1);
                }

                if (_nextId < 1)
                {
                    _nextId = 1;
                }
            }
            catch
            {
                _recent.Clear();
                _nextId = 1;
            }
        }

        private string ReadRecentTraceText()
        {
            var info = new FileInfo(_csvPath);
            if (info.Length <= MaxTraceBytesToLoad)
            {
                return File.ReadAllText(_csvPath, Encoding.UTF8);
            }

            var bytesToRead = MaxTraceBytesToLoad;
            var buffer = new byte[bytesToRead];
            using (var stream = new FileStream(_csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(-bytesToRead, SeekOrigin.End);
                var offset = 0;
                while (offset < bytesToRead)
                {
                    var read = stream.Read(buffer, offset, bytesToRead - offset);
                    if (read <= 0)
                    {
                        break;
                    }

                    offset += read;
                }
            }

            var text = Encoding.UTF8.GetString(buffer);
            var firstBreak = text.IndexOf('\n');
            return firstBreak >= 0 && firstBreak + 1 < text.Length
                ? text.Substring(firstBreak + 1)
                : text;
        }

        private void AppendCsv(RuntimeTraceRow row)
        {
            if (!File.Exists(_csvPath))
            {
                File.AppendAllText(_csvPath, "id,timestamp,category,action,status,source,register,value,detail" + Environment.NewLine);
            }

            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                row.Id,
                Sanitize(row.Timestamp),
                Sanitize(row.Category),
                Sanitize(row.Action),
                Sanitize(row.Status),
                Sanitize(row.Source),
                Sanitize(row.Register),
                Sanitize(row.Value),
                Sanitize(row.Detail)
            );
            File.AppendAllText(_csvPath, line + Environment.NewLine);
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace(",", ";").Replace(Environment.NewLine, " ");
        }
    }
}
