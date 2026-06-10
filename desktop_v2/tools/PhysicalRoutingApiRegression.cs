using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SortingMachineDesktop
{
    internal static class PhysicalRoutingApiRegression
    {
        private static int Main(string[] args)
        {
            try
            {
                var webRoot = args != null && args.Length > 0 ? args[0] : "app\\web";
                return Run(webRoot);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PhysicalRoutingApiRegression FAILED: " + ex.Message);
                return 1;
            }
        }

        private static int Run(string webRoot)
        {
            AssertErroneousMeasurementRegisterMigratesToConstructorDefault();
            PrepareFieldValidationDataDir();
            WriteFieldValidationReport(
                "field_validation_codex_complete.md",
                CompleteFieldValidationReport(999),
                DateTime.Now.AddSeconds(1));

            var state = new MachineState();
            state.SetUseSimulator(true);
            var lot = state.StartNewLot();
            state.Tick();
            var currentLotId = lot.Lot == null ? 1 : lot.Lot.Id;

            var port = FindFreePort();
            var server = new ApiServer(state, port, webRoot);
            try
            {
                server.Start();
                var statePayload = GetJson(server.BaseUrl + "api/state");
                var diagnosticPayload = GetJson(server.BaseUrl + "api/diagnostic");
                var physicalPayload = GetJson(server.BaseUrl + "api/diagnostic/physical-routing");
                var readinessPayload = GetJson(server.BaseUrl + "api/diagnostic/start-readiness");
                var fieldValidationPayload = GetJson(server.BaseUrl + "api/diagnostic/field-validation");
                var maintenancePayload = GetJson(server.BaseUrl + "api/maintenance");

                var stateDiagnostic = Dict(statePayload, "Diagnostic");
                var statePhysical = Dict(stateDiagnostic, "PhysicalRouting");
                AssertPhysicalRouting("state", statePhysical);
                AssertStartReadiness("state", Dict(stateDiagnostic, "StartReadiness"));
                AssertFieldValidation("state", Dict(stateDiagnostic, "FieldValidation"));
                AssertFieldValidationStatus("smoke ignored", Dict(stateDiagnostic, "FieldValidation"), "NO_REPORT", false, false);

                var diagnostic = Dict(diagnosticPayload, "diagnostic");
                AssertPhysicalRouting("diagnostic", Dict(diagnostic, "PhysicalRouting"));
                AssertStartReadiness("diagnostic", Dict(diagnostic, "StartReadiness"));
                AssertFieldValidation("diagnostic", Dict(diagnostic, "FieldValidation"));
                AssertFieldValidationStatus("diagnostic smoke ignored", Dict(diagnostic, "FieldValidation"), "NO_REPORT", false, false);

                var physical = Dict(physicalPayload, "physicalRouting");
                AssertPhysicalRouting("physical endpoint", physical);
                AssertStartReadiness("readiness endpoint", Dict(readinessPayload, "startReadiness"));
                AssertFieldValidation("field validation endpoint", Dict(fieldValidationPayload, "fieldValidation"));
                AssertFieldValidationStatus("field endpoint smoke ignored", Dict(fieldValidationPayload, "fieldValidation"), "NO_REPORT", false, false);
                AssertMaintenanceCatalog("maintenance endpoint", Dict(maintenancePayload, "maintenance"));

                WriteFieldValidationReport(
                    "field_validation_incomplete.md",
                    IncompleteFieldValidationReport(),
                    DateTime.Now.AddSeconds(2));
                RefreshSimulatorDiagnostic(state);
                var incompletePayload = GetJson(server.BaseUrl + "api/diagnostic/field-validation");
                var incomplete = Dict(incompletePayload, "fieldValidation");
                AssertFieldValidationStatus("incomplete report", incomplete, "INCOMPLETE", true, false);
                AssertEqual("incomplete trace verdict", "OK", Value(incomplete, "TraceVerdict"));
                AssertEqual("incomplete counter verdict", "INCOMPLET", Value(incomplete, "CounterVerdict"));

                WriteFieldValidationReport(
                    "field_validation_wrong_lot_complete.md",
                    CompleteFieldValidationReport(currentLotId + 100),
                    DateTime.Now.AddSeconds(3));
                RefreshSimulatorDiagnostic(state);
                var wrongLotPayload = GetJson(server.BaseUrl + "api/diagnostic/field-validation");
                var wrongLot = Dict(wrongLotPayload, "fieldValidation");
                AssertFieldValidationStatus("wrong lot report", wrongLot, "INCOMPLETE", true, false);
                AssertEqual("wrong lot matches", "False", BoolValue(wrongLot, "MatchesCurrentLot").ToString(CultureInfo.InvariantCulture));

                WriteFieldValidationReport(
                    "field_validation_forged_coverage_complete.md",
                    ForgedCoverageFieldValidationReport(currentLotId),
                    DateTime.Now.AddSeconds(4));
                RefreshSimulatorDiagnostic(state);
                var forgedCoveragePayload = GetJson(server.BaseUrl + "api/diagnostic/field-validation");
                var forgedCoverage = Dict(forgedCoveragePayload, "fieldValidation");
                AssertFieldValidationStatus("forged coverage report", forgedCoverage, "INCOMPLETE", true, false);
                AssertEqual("forged coverage verdict", "OK", Value(forgedCoverage, "LaneCoverageVerdict"));

                WriteFieldValidationReport(
                    "field_validation_complete.md",
                    CompleteFieldValidationReport(currentLotId),
                    DateTime.Now.AddSeconds(5));
                RefreshSimulatorDiagnostic(state);
                var completePayload = GetJson(server.BaseUrl + "api/diagnostic/field-validation");
                var complete = Dict(completePayload, "fieldValidation");
                AssertFieldValidationStatus("complete report", complete, "COMPLETE", true, true);
                AssertEqual("complete report lot", currentLotId.ToString(CultureInfo.InvariantCulture), Value(complete, "ReportLotId"));
                AssertEqual("complete current lot", currentLotId.ToString(CultureInfo.InvariantCulture), Value(complete, "CurrentLotId"));
                AssertEqual("complete matches", "True", BoolValue(complete, "MatchesCurrentLot").ToString(CultureInfo.InvariantCulture));
                AssertEqual("complete trace verdict", "OK", Value(complete, "TraceVerdict"));
                AssertEqual("complete counter verdict", "OK", Value(complete, "CounterVerdict"));
                AssertEqual("complete observation verdict", "OK", Value(complete, "PhysicalObservationVerdict"));
                AssertEqual("complete lane coverage verdict", "OK", Value(complete, "LaneCoverageVerdict"));

                Console.WriteLine("PhysicalRoutingApiRegression OK");
                return 0;
            }
            finally
            {
                server.Stop();
            }
        }

        private static void AssertPhysicalRouting(string label, Dictionary<string, object> physical)
        {
            AssertEqual(label + " mode", "PLC_THRESHOLDS_NG_CATCHALL", Value(physical, "PhysicalRoutingMode"));
            AssertTrue(label + " GOOD direct blocked", BoolValue(physical, "GoodPusherDirectControlBlocked"));
            AssertTrue(label + " expected lane", !string.IsNullOrWhiteSpace(Value(physical, "ExpectedLane")));
            AssertTrue(label + " applied lane", !string.IsNullOrWhiteSpace(Value(physical, "AppliedLane")));
            AssertEqual(label + " handshake register", "8230", Value(physical, "HandshakeRegister"));
            AssertEqual(label + " status register", "8231", Value(physical, "StatusRegister"));
            AssertTrue(label + " programmed thresholds", Dict(physical, "ProgrammedThresholds").ContainsKey("Channels"));
            AssertTrue(label + " alarms", physical.ContainsKey("AlarmRegisters"));

            var pulse = Dict(physical, "LastNgPulse");
            AssertEqual(label + " Y11 maintenance output path", "Y11_4X_3144_BIT_10", Value(pulse, "OutputPath"));
            AssertEqual(label + " Y11 maintenance output image", "3144", Value(pulse, "OutputImageRegister"));
            AssertEqual(label + " Y11 maintenance output bit", "10", Value(pulse, "OutputBit"));
            AssertTrue(label + " Y11 maintenance status", !string.IsNullOrWhiteSpace(Value(pulse, "Status")));
        }

        private static void AssertStartReadiness(string label, Dictionary<string, object> readiness)
        {
            AssertTrue(label + " ready field", readiness.ContainsKey("ReadyToStart"));
            AssertTrue(label + " connected field", readiness.ContainsKey("Connected"));
            AssertTrue(label + " handshake ready field", readiness.ContainsKey("HandshakeReady"));
            AssertEqual(label + " handshake register", "8230", Value(readiness, "HandshakeRegister"));
            AssertTrue(label + " blocking reasons", readiness.ContainsKey("BlockingReasons"));
            AssertTrue(label + " warnings", readiness.ContainsKey("Warnings"));
            AssertTrue(label + " Odoo non bloquant", !ListContains(readiness, "BlockingReasons", "Odoo"));
            AssertTrue(label + " Odoo avertissement", ListContains(readiness, "Warnings", "Odoo"));
            AssertTrue(label + " apprentissage non bloquant", !ListContains(readiness, "BlockingReasons", "apprentissage"));
            AssertTrue(label + " ligne 10 avertissement", ListContains(readiness, "Warnings", "ligne 10"));
            AssertTrue(label + " seuils precharges non bloquants", !ListContains(readiness, "BlockingReasons", "Seuils"));
            AssertTrue(label + " START_PRELOAD avertissement", ListContains(readiness, "Warnings", "START_PRELOAD"));
            AssertTrue(label + " operator checks", readiness.ContainsKey("OperatorChecks"));
            AssertTrue(label + " expected lane", readiness.ContainsKey("ExpectedLane"));
            AssertTrue(label + " applied lane", readiness.ContainsKey("AppliedLane"));
        }

        private static void AssertFieldValidation(string label, Dictionary<string, object> fieldValidation)
        {
            AssertTrue(label + " has report", fieldValidation.ContainsKey("HasReport"));
            AssertTrue(label + " verified", fieldValidation.ContainsKey("Verified"));
            AssertTrue(label + " status", !string.IsNullOrWhiteSpace(Value(fieldValidation, "Status")));
            AssertTrue(label + " report lot", fieldValidation.ContainsKey("ReportLotId"));
            AssertTrue(label + " current lot", fieldValidation.ContainsKey("CurrentLotId"));
            AssertTrue(label + " matches current lot", fieldValidation.ContainsKey("MatchesCurrentLot"));
            AssertTrue(label + " trace verdict", fieldValidation.ContainsKey("TraceVerdict"));
            AssertTrue(label + " counter verdict", fieldValidation.ContainsKey("CounterVerdict"));
            AssertTrue(label + " physical verdict", fieldValidation.ContainsKey("PhysicalObservationVerdict"));
            AssertTrue(label + " lane coverage verdict", fieldValidation.ContainsKey("LaneCoverageVerdict"));
            AssertTrue(label + " validation command", !string.IsNullOrWhiteSpace(Value(fieldValidation, "ValidationCommand")));
            AssertTrue(label + " check command", !string.IsNullOrWhiteSpace(Value(fieldValidation, "CheckCommand")));
        }

        private static void AssertFieldValidationStatus(string label, Dictionary<string, object> fieldValidation, string status, bool hasReport, bool verified)
        {
            AssertEqual(label + " status", status, Value(fieldValidation, "Status"));
            AssertEqual(label + " has report", hasReport.ToString(CultureInfo.InvariantCulture), BoolValue(fieldValidation, "HasReport").ToString(CultureInfo.InvariantCulture));
            AssertEqual(label + " verified", verified.ToString(CultureInfo.InvariantCulture), BoolValue(fieldValidation, "Verified").ToString(CultureInfo.InvariantCulture));
        }

        private static void AssertMaintenanceCatalog(string label, Dictionary<string, object> maintenance)
        {
            AssertTrue(label + " START standard valide", HasCommand(maintenance, "ValidatedCommands", "START"));
            AssertTrue(label + " START_RAW pas valide", !HasCommand(maintenance, "ValidatedCommands", "START_RAW"));
            AssertTrue(label + " START_RAW expert", HasCommand(maintenance, "ExpertCommands", "START_RAW"));
            AssertTrue(label + " Y11 brut expert", HasCommand(maintenance, "ExpertCommands", "Y11_OUTPUT_ON"));
        }

        private static void RefreshSimulatorDiagnostic(MachineState state)
        {
            Thread.Sleep(270);
            state.Tick();
        }

        private static void AssertErroneousMeasurementRegisterMigratesToConstructorDefault()
        {
            var dataDir = FieldValidationDataDir();
            Directory.CreateDirectory(dataDir);
            var configPath = Path.Combine(dataDir, "config.json");
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }

            var cfg = MachineConfig.CreateDefault();
            cfg.MeasurementRegister = MachineConfig.KnownErroneousMeasurementRegister;
            var data = new ConfigData
            {
                Config = cfg,
                Thresholds = new Dictionary<string, ThresholdSet>
                {
                    { "21700", new ThresholdSet { Channels = new List<ChannelThreshold>() } },
                    { "18650", new ThresholdSet { Channels = new List<ChannelThreshold>() } }
                },
                IntelligentRecipes = new Dictionary<string, IntelligentRecipe>
                {
                    { "21700", new IntelligentRecipe { CellType = "21700" } },
                    { "18650", new IntelligentRecipe { CellType = "18650" } }
                }
            };

            var serializer = new JavaScriptSerializer();
            File.WriteAllText(configPath, serializer.Serialize(data), Encoding.UTF8);

            var loaded = new ConfigStore().Load();
            AssertEqual(
                "migration registre mesure 8402 vers 8408",
                MachineConfig.DefaultMeasurementRegister.ToString(CultureInfo.InvariantCulture),
                loaded.Config.MeasurementRegister.ToString(CultureInfo.InvariantCulture));

            var persisted = File.ReadAllText(configPath, Encoding.UTF8);
            AssertTrue("config migree ne conserve pas 8402", persisted.IndexOf("\"MeasurementRegister\":8402", StringComparison.OrdinalIgnoreCase) < 0);
            AssertTrue("config migree persiste 8408", persisted.IndexOf("\"MeasurementRegister\":8408", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FieldValidationDataDir()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        }

        private static void PrepareFieldValidationDataDir()
        {
            var dataDir = FieldValidationDataDir();
            Directory.CreateDirectory(dataDir);
            foreach (var file in Directory.GetFiles(dataDir, "field_validation*.md"))
            {
                File.Delete(file);
            }
        }

        private static void WriteFieldValidationReport(string fileName, string content, DateTime timestamp)
        {
            var path = Path.Combine(FieldValidationDataDir(), fileName);
            File.WriteAllText(path, content);
            File.SetLastWriteTime(path, timestamp);
        }

        private static string IncompleteFieldValidationReport()
        {
            return "# TriCell Pilot - rapport surveillance terrain" + Environment.NewLine +
                "- Lot: #1 PAUSED" + Environment.NewLine +
                "## Verdict structure" + Environment.NewLine +
                "- VERDICT_TRACE_LOGICIEL: OK" + Environment.NewLine +
                "- VERDICT_COMPTEURS_MACHINE: INCOMPLET" + Environment.NewLine +
                "- VERDICT_OBSERVATION_PHYSIQUE: OK" + Environment.NewLine +
                "- VERDICT_COUVERTURE_VOIES_GOOD: INCOMPLET" + Environment.NewLine +
                "## Conclusion automatique" + Environment.NewLine +
                "Preuve trace logiciel OK, mais aucun delta compteur suffisant.";
        }

        private static string CompleteFieldValidationReport(int lotId)
        {
            return "# TriCell Pilot - rapport surveillance terrain" + Environment.NewLine +
                "- Lot: #" + lotId.ToString(CultureInfo.InvariantCulture) + " PAUSED" + Environment.NewLine +
                "## Verdict structure" + Environment.NewLine +
                "- VERDICT_TRACE_LOGICIEL: OK" + Environment.NewLine +
                "- VERDICT_COMPTEURS_MACHINE: OK" + Environment.NewLine +
                "- VERDICT_OBSERVATION_PHYSIQUE: OK" + Environment.NewLine +
                "- VERDICT_COUVERTURE_VOIES_GOOD: OK" + Environment.NewLine +
                "- Minimums effectifs: tops=9, compteurs=9, observations=9, voies GOOD=1,2,3,4,5,6,7,8,9" + Environment.NewLine +
                "- Tops 8230 acceptes: True lignes=9" + Environment.NewLine +
                "- Couverture voies GOOD requise: 1,2,3,4,5,6,7,8,9" + Environment.NewLine +
                "- Couverture voies GOOD observee: 1,2,3,4,5,6,7,8,9" + Environment.NewLine +
                "- Couverture voies GOOD manquante: aucune" + Environment.NewLine +
                "## Conclusion automatique" + Environment.NewLine +
                "Preuve terrain complete: traces logiciel, compteurs machine et observations physiques sont coherents.";
        }

        private static string ForgedCoverageFieldValidationReport(int lotId)
        {
            return "# TriCell Pilot - rapport surveillance terrain" + Environment.NewLine +
                "- Lot: #" + lotId.ToString(CultureInfo.InvariantCulture) + " PAUSED" + Environment.NewLine +
                "## Verdict structure" + Environment.NewLine +
                "- VERDICT_TRACE_LOGICIEL: OK" + Environment.NewLine +
                "- VERDICT_COMPTEURS_MACHINE: OK" + Environment.NewLine +
                "- VERDICT_OBSERVATION_PHYSIQUE: OK" + Environment.NewLine +
                "- VERDICT_COUVERTURE_VOIES_GOOD: OK" + Environment.NewLine +
                "## Conclusion automatique" + Environment.NewLine +
                "Preuve terrain complete: traces logiciel, compteurs machine et observations physiques sont coherents.";
        }

        private static Dictionary<string, object> GetJson(string url)
        {
            using (var client = new WebClient())
            {
                var json = client.DownloadString(url);
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<Dictionary<string, object>>(json);
            }
        }

        private static Dictionary<string, object> Dict(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                throw new InvalidOperationException("Champ absent: " + key);
            }

            var dict = value as Dictionary<string, object>;
            if (dict == null)
            {
                throw new InvalidOperationException("Champ non objet: " + key);
            }

            return dict;
        }

        private static string Value(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool BoolValue(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) && parsed;
        }

        private static bool ListContains(Dictionary<string, object> source, string key, string expected)
        {
            object value;
            if (source == null ||
                !source.TryGetValue(key, out value) ||
                value == null ||
                string.IsNullOrWhiteSpace(expected))
            {
                return false;
            }

            var enumerable = value as System.Collections.IEnumerable;
            if (enumerable != null && !(value is string))
            {
                foreach (var item in enumerable)
                {
                    if (Convert.ToString(item, CultureInfo.InvariantCulture).IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture).IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasCommand(Dictionary<string, object> source, string key, string command)
        {
            object value;
            if (source == null ||
                !source.TryGetValue(key, out value) ||
                value == null ||
                string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            var enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null || value is string)
            {
                return false;
            }

            foreach (var item in enumerable)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null)
                {
                    continue;
                }

                if (string.Equals(Value(dict, "Command"), command, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static void AssertEqual(string label, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(label + ": attendu " + expected + ", obtenu " + actual + ".");
            }
        }

        private static void AssertTrue(string label, bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException(label + ": condition fausse.");
            }
        }
    }
}
