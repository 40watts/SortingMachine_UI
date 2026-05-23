using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SortingMachineDesktop
{
    public class ApiServer
    {
        private readonly MachineState _state;
        private readonly int _port;
        private readonly JavaScriptSerializer _serializer;
        private readonly string _webRoot;
        private HttpListener _listener;
        private Thread _thread;
        private bool _running;

        public ApiServer(MachineState state, int port, string webRoot)
        {
            _state = state;
            _port = port;
            _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            _webRoot = webRoot;
        }

        public string BaseUrl
        {
            get { return "http://127.0.0.1:" + _port + "/"; }
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _listener = new HttpListener();
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _running = true;

            _thread = new Thread(ListenLoop);
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                }
            }
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(HandleRequest, ctx);
                }
                catch
                {
                    if (!_running)
                    {
                        break;
                    }
                }
            }
        }

        private void HandleRequest(object state)
        {
            var ctx = (HttpListenerContext)state;
            var rawPath = ctx.Request.Url.AbsolutePath;
            var path = rawPath.ToLowerInvariant();

            try
            {
                if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && path != "/api")
                {
                    WriteStatic(ctx, rawPath);
                    return;
                }

                if (path == "/api/state")
                {
                    WriteJson(ctx, _state.Snapshot());
                    return;
                }

                if (path == "/api/config")
                {
                    WriteJson(ctx, _state.GetConfigCopy());
                    return;
                }

                if (path == "/api/contracts")
                {
                    WriteJson(ctx, _state.GetContracts());
                    return;
                }

                if (path == "/api/config/cell-type" && ctx.Request.HttpMethod == "POST")
                {
                    var payload = ReadDictionary(ctx.Request);
                    var cellType = payload.ContainsKey("cell_type") ? Convert.ToString(payload["cell_type"]) : null;
                    if (cellType == "21700" || cellType == "18650")
                    {
                        _state.SetCellType(cellType);
                        WriteJson(ctx, new { cell_type = cellType });
                        return;
                    }

                    WriteError(ctx, 400, "cell_type must be 21700 or 18650");
                    return;
                }

                if (path == "/api/config/sorting-mode" && ctx.Request.HttpMethod == "POST")
                {
                    var payload = ReadDictionary(ctx.Request);
                    var sortingMode = payload.ContainsKey("sorting_mode") ? Convert.ToString(payload["sorting_mode"]) : null;
                    if (sortingMode == SortingModes.Legacy || sortingMode == SortingModes.IntelligentGoodNg)
                    {
                        _state.SetSortingMode(sortingMode);
                        WriteJson(ctx, new { sorting_mode = sortingMode });
                        return;
                    }

                    WriteError(ctx, 400, "sorting_mode must be LEGACY or INTELLIGENT_GOOD_NG");
                    return;
                }

                if (path == "/api/config/legacy-options" && ctx.Request.HttpMethod == "POST")
                {
                    var payload = ReadDictionary(ctx.Request);
                    var judgeMode = payload.ContainsKey("judge_mode") ? Convert.ToString(payload["judge_mode"]) : null;
                    var channelStart = payload.ContainsKey("channel_start") ? Convert.ToInt32(payload["channel_start"]) : 0;
                    var channelEnd = payload.ContainsKey("channel_end") ? Convert.ToInt32(payload["channel_end"]) : 0;
                    _state.UpdateLegacyOptions(judgeMode, channelStart, channelEnd);
                    WriteJson(ctx, new { ok = true });
                    return;
                }

                if (path == "/api/thresholds" || path == "/api/recipes/legacy")
                {
                    var response = new
                    {
                        active_cell_type = _state.GetConfigCopy().CellType,
                        thresholds = _state.GetAllThresholds()
                    };
                    WriteJson(ctx, response);
                    return;
                }

                if (path.StartsWith("/api/thresholds/") && ctx.Request.HttpMethod == "POST")
                {
                    var cellType = path.Substring("/api/thresholds/".Length);
                    HandleLegacyRecipeUpdate(ctx, cellType);
                    return;
                }

                if (path.StartsWith("/api/recipes/legacy/"))
                {
                    var cellType = path.Substring("/api/recipes/legacy/".Length);
                    if (ctx.Request.HttpMethod == "GET")
                    {
                        HandleLegacyRecipeGet(ctx, cellType);
                        return;
                    }
                    if (ctx.Request.HttpMethod == "POST")
                    {
                        HandleLegacyRecipeUpdate(ctx, cellType);
                        return;
                    }
                }

                if (path == "/api/recipes/intelligent")
                {
                    WriteJson(ctx, new
                    {
                        active_cell_type = _state.GetConfigCopy().CellType,
                        recipes = _state.GetAllIntelligentRecipes()
                    });
                    return;
                }

                if (path.StartsWith("/api/recipes/intelligent/"))
                {
                    var cellType = path.Substring("/api/recipes/intelligent/".Length);
                    if (cellType != "21700" && cellType != "18650")
                    {
                        WriteError(ctx, 400, "cell_type must be 21700 or 18650");
                        return;
                    }

                    if (ctx.Request.HttpMethod == "GET")
                    {
                        WriteJson(ctx, _state.GetIntelligentRecipe(cellType));
                        return;
                    }

                    if (ctx.Request.HttpMethod == "POST")
                    {
                        var body = ReadBody(ctx.Request);
                        var payload = _serializer.Deserialize<IntelligentRecipe>(body);
                        if (payload == null)
                        {
                            WriteError(ctx, 400, "invalid intelligent recipe payload");
                            return;
                        }

                        payload.CellType = cellType;
                        _state.UpdateIntelligentRecipe(cellType, payload);
                        WriteJson(ctx, new { ok = true });
                        return;
                    }
                }

                if (path == "/api/live")
                {
                    WriteJson(ctx, new { live = _state.GetLive() });
                    return;
                }

                if (path == "/api/learning")
                {
                    WriteJson(ctx, new { learning = _state.GetCurrentLot() });
                    return;
                }

                if (path == "/api/lanes")
                {
                    WriteJson(ctx, new { lanes = _state.GetLaneStates() });
                    return;
                }

                if (path == "/api/lots/current")
                {
                    WriteJson(ctx, new { lot = _state.GetCurrentLot() });
                    return;
                }

                if (path == "/api/lots/history")
                {
                    var limit = ParseLimit(ctx.Request.QueryString["limit"], 20, 1, 500);
                    WriteJson(ctx, new { lots = _state.GetLotHistory(limit) });
                    return;
                }

                if (path == "/api/odoo/lots")
                {
                    var limit = ParseLimit(ctx.Request.QueryString["limit"], 10, 1, 50);
                    var query = ctx.Request.QueryString["q"];
                    WriteJson(ctx, new
                    {
                        lots = _state.SearchOdooLots(query, limit),
                        query = query ?? string.Empty,
                        live_configured = _state.IsOdooLiveSearchConfigured()
                    });
                    return;
                }

                if (path == "/api/odoo/cells")
                {
                    var limit = ParseLimit(ctx.Request.QueryString["limit"], 10, 1, 50);
                    var query = ctx.Request.QueryString["q"];
                    WriteJson(ctx, new
                    {
                        cells = _state.SearchOdooCells(query, limit),
                        query = query ?? string.Empty,
                        live_configured = _state.IsOdooLiveSearchConfigured()
                    });
                    return;
                }

                if (path == "/api/lots/odoo-link" && ctx.Request.HttpMethod == "POST")
                {
                    var payload = ReadDictionary(ctx.Request);
                    var result = _state.LinkOdooLot(
                        ValueAsString(payload, "odoo_lot_reference"),
                        ValueAsString(payload, "odoo_lot_name"),
                        ValueAsString(payload, "odoo_product_reference"),
                        ValueAsString(payload, "odoo_product_name"),
                        ValueAsString(payload, "note")
                    );
                    if (!result.Ok)
                    {
                        WriteError(ctx, 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                if (path == "/api/lots/new" && ctx.Request.HttpMethod == "POST")
                {
                    var result = _state.StartNewLot();
                    if (!result.Ok)
                    {
                        WriteError(ctx, 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                if (path == "/api/lots/continue" && ctx.Request.HttpMethod == "POST")
                {
                    var result = _state.ContinueLot();
                    if (!result.Ok)
                    {
                        WriteError(ctx, 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                if (path == "/api/lots/close" && ctx.Request.HttpMethod == "POST")
                {
                    var result = _state.CloseCurrentLot();
                    if (!result.Ok)
                    {
                        WriteError(ctx, 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                if (path == "/api/lots/reset-lines" && ctx.Request.HttpMethod == "POST")
                {
                    var result = _state.ResetCurrentLotLines();
                    if (!result.Ok)
                    {
                        WriteError(ctx, 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                if (path == "/api/lots/confirm-empty-lines" && ctx.Request.HttpMethod == "POST")
                {
                    var result = _state.ConfirmCurrentLotLinesEmptied();
                    if (!result.Ok)
                    {
                        WriteError(ctx, 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                if (path == "/api/diagnostic")
                {
                    WriteJson(ctx, new { diagnostic = _state.GetDiagnostic() });
                    return;
                }

                if (path == "/api/alarms")
                {
                    WriteJson(ctx, new { alarms = _state.GetAlarms() });
                    return;
                }

                if (path == "/api/counters")
                {
                    WriteJson(ctx, new { counters = _state.GetCounters() });
                    return;
                }

                if (path == "/api/cells" || path == "/api/history")
                {
                    var limit = ParseLimit(ctx.Request.QueryString["limit"], 50, 1, 1000);
                    WriteJson(ctx, new { cells = _state.GetHistory(limit) });
                    return;
                }

                if (path == "/api/cells/audit")
                {
                    var limit = ParseLimit(ctx.Request.QueryString["limit"], 500, 1, 10000);
                    WriteJson(ctx, new { cells = _state.GetCellAuditHistory(limit) });
                    return;
                }

                if (path == "/api/cells/ng")
                {
                    var limit = ParseLimit(ctx.Request.QueryString["limit"], 20, 1, 500);
                    WriteJson(ctx, new { cells = _state.GetRecentNgCells(limit) });
                    return;
                }

                if (path == "/api/observation-events")
                {
                    var limit = ParseLimit(ctx.Request.QueryString["limit"], 100, 1, 1000);
                    WriteJson(ctx, new { events = _state.GetObservationEvents(limit) });
                    return;
                }

                if (path == "/api/runtime-trace")
                {
                    var limit = ParseLimit(ctx.Request.QueryString["limit"], 200, 1, 5000);
                    WriteJson(ctx, new { rows = _state.GetRuntimeTrace(limit) });
                    return;
                }

                if (path == "/api/maintenance")
                {
                    WriteJson(ctx, new { maintenance = _state.GetMaintenanceSnapshot() });
                    return;
                }

                if (path == "/api/maintenance/command" && ctx.Request.HttpMethod == "POST")
                {
                    var payload = ReadDictionary(ctx.Request);
                    object commandValue;
                    payload.TryGetValue("command", out commandValue);
                    var result = _state.ExecuteMaintenanceCommand(commandValue == null ? null : commandValue.ToString());
                    if (!result.Ok)
                    {
                        WriteError(ctx, result.RequiresExpert || result.BlockedBySafety ? 403 : 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                if (path == "/api/maintenance/piston-test" && ctx.Request.HttpMethod == "POST")
                {
                    var payload = ReadDictionary(ctx.Request);
                    object laneValue;
                    payload.TryGetValue("lane", out laneValue);
                    var result = _state.ExecutePistonTest(laneValue == null ? null : laneValue.ToString());
                    if (!result.Ok)
                    {
                        WriteError(ctx, result.RequiresExpert || result.BlockedBySafety ? 403 : 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                if (path == "/api/export/csv")
                {
                    WriteText(ctx, _state.GetCellAuditCsv(), "text/csv; charset=utf-8");
                    return;
                }

                if (path == "/api/export/cells-audit-csv")
                {
                    WriteText(ctx, _state.GetCellAuditCsv(), "text/csv; charset=utf-8");
                    return;
                }

                if (path == "/api/export/history-legacy-csv")
                {
                    WriteFile(ctx, _state.GetHistoryCsvPath(), "text/csv");
                    return;
                }

                if (path == "/api/export/odoo-cell-tests")
                {
                    WriteFile(ctx, _state.GetHistoryCompactCsvPath(), "text/csv");
                    return;
                }

                if (path == "/api/export/observation/csv")
                {
                    WriteFile(ctx, _state.GetObservationCsvPath(), "text/csv");
                    return;
                }

                if (path == "/api/export/diagnostic/json")
                {
                    WriteJson(ctx, _state.GetDiagnostic());
                    return;
                }

                if (path == "/api/export/thresholds/json")
                {
                    WriteJson(ctx, new
                    {
                        active_cell_type = _state.GetConfigCopy().CellType,
                        thresholds = _state.GetAllThresholds()
                    });
                    return;
                }

                if (path == "/api/export/recipes/json")
                {
                    WriteJson(ctx, new
                    {
                        legacy = _state.GetAllThresholds(),
                        intelligent = _state.GetAllIntelligentRecipes()
                    });
                    return;
                }

                if (path == "/api/export/runtime-trace")
                {
                    WriteFile(ctx, _state.GetTraceCsvPath(), "text/csv");
                    return;
                }

                if (path == "/api/command" && ctx.Request.HttpMethod == "POST")
                {
                    var payload = ReadDictionary(ctx.Request);
                    object commandValue;
                    payload.TryGetValue("command", out commandValue);
                    var result = _state.ExecuteCycleCommand(commandValue == null ? null : commandValue.ToString());
                    if (!result.Ok)
                    {
                        WriteError(ctx, result.BlockedBySafety ? 403 : 400, result.Message);
                        return;
                    }

                    WriteJson(ctx, result);
                    return;
                }

                WriteError(ctx, 404, "Not found");
            }
            catch (Exception ex)
            {
                WriteError(ctx, 500, ex.Message);
            }
        }

        private void WriteStatic(HttpListenerContext ctx, string requestPath)
        {
            if (string.IsNullOrWhiteSpace(_webRoot) || !Directory.Exists(_webRoot))
            {
                WriteError(ctx, 404, "web root not found");
                return;
            }

            var relative = requestPath.TrimStart('/');
            if (string.IsNullOrWhiteSpace(relative))
            {
                relative = "index.html";
            }

            relative = relative.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_webRoot, relative));
            var rootPath = Path.GetFullPath(_webRoot);

            if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                WriteError(ctx, 403, "forbidden");
                return;
            }

            if (!File.Exists(fullPath))
            {
                fullPath = Path.Combine(_webRoot, "index.html");
            }

            WriteFile(ctx, fullPath, GetContentType(fullPath));
        }

        private void HandleLegacyRecipeGet(HttpListenerContext ctx, string cellType)
        {
            if (cellType != "21700" && cellType != "18650")
            {
                WriteError(ctx, 400, "cell_type must be 21700 or 18650");
                return;
            }

            WriteJson(ctx, _state.GetThresholds(cellType));
        }

        private void HandleLegacyRecipeUpdate(HttpListenerContext ctx, string cellType)
        {
            if (cellType != "21700" && cellType != "18650")
            {
                WriteError(ctx, 400, "cell_type must be 21700 or 18650");
                return;
            }

            var body = ReadBody(ctx.Request);
            var payload = _serializer.Deserialize<ThresholdSet>(body);
            if (payload == null || payload.Channels == null)
            {
                WriteError(ctx, 400, "invalid thresholds payload");
                return;
            }

            _state.UpdateThresholds(cellType, payload);
            WriteJson(ctx, new { ok = true });
        }

        private Dictionary<string, object> ReadDictionary(HttpListenerRequest request)
        {
            var body = ReadBody(request);
            var payload = _serializer.Deserialize<Dictionary<string, object>>(body);
            return payload ?? new Dictionary<string, object>();
        }

        private string ValueAsString(Dictionary<string, object> payload, string key)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return Convert.ToString(value);
        }

        private int ParseLimit(string raw, int defaultValue, int min, int max)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            int parsed;
            if (!int.TryParse(raw, out parsed))
            {
                return defaultValue;
            }

            return Math.Max(min, Math.Min(max, parsed));
        }

        private string ReadBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        private void WriteFile(HttpListenerContext ctx, string path, string contentType)
        {
            if (!File.Exists(path))
            {
                WriteError(ctx, 404, "file not found");
                return;
            }

            var bytes = File.ReadAllBytes(path);
            ctx.Response.ContentType = contentType;
            ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            ctx.Response.Headers["Pragma"] = "no-cache";
            ctx.Response.Headers["Expires"] = "0";
            ctx.Response.StatusCode = 200;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private void WriteText(HttpListenerContext ctx, string text, string contentType)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            ctx.Response.ContentType = contentType;
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            ctx.Response.Headers["Pragma"] = "no-cache";
            ctx.Response.Headers["Expires"] = "0";
            ctx.Response.StatusCode = 200;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private string GetContentType(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            switch (extension)
            {
                case ".html":
                    return "text/html; charset=utf-8";
                case ".css":
                    return "text/css; charset=utf-8";
                case ".js":
                    return "application/javascript; charset=utf-8";
                case ".json":
                    return "application/json; charset=utf-8";
                case ".svg":
                    return "image/svg+xml";
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".ico":
                    return "image/x-icon";
                default:
                    return "application/octet-stream";
            }
        }

        private void WriteJson(HttpListenerContext ctx, object payload)
        {
            var json = _serializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.StatusCode = 200;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }

        private void WriteError(HttpListenerContext ctx, int status, string message)
        {
            var json = _serializer.Serialize(new { error = message });
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentEncoding = Encoding.UTF8;
            ctx.Response.StatusCode = status;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
    }
}
