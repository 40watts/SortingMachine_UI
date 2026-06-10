using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace SortingMachineDesktop
{
    internal static class ScannerHandshakeRegression
    {
        private static int Main()
        {
            try
            {
                return Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ScannerHandshakeRegression FAILED: " + ex.Message);
                return 1;
            }
        }

        private static int Run()
        {
            AssertSimulatorScanOkResponds8230Zero();
            AssertScannerFallbackHelpers();
            AssertScannerResponseHelperUses8230ZeroOrTwo();
            AssertConFallbackDoesNotFeedLearning();

            Console.WriteLine("ScannerHandshakeRegression OK");
            return 0;
        }

        private static void AssertSimulatorScanOkResponds8230Zero()
        {
            var state = new MachineState();
            state.ApplySettings(
                "COM_SIM",
                19200,
                1,
                MachineConfig.DefaultMeasurementRegister,
                22808,
                8230,
                8231,
                true,
                true,
                true,
                true,
                "COM_SCAN_SIM",
                115200,
                "Even",
                3.5,
                "BOTH",
                1,
                11,
                false,
                "NG");
            state.SetSortingMode(SortingModes.IntelligentGoodNg);
            state.StartNewLot();

            var start = state.ExecuteCycleCommand("START");
            AssertTrue("START simulateur scan", start.Ok && start.Simulated);

            for (var i = 0; i < 3; i++)
            {
                state.Tick();
                Thread.Sleep(270);
            }

            var trace = state.GetRuntimeTrace(200);
            AssertTraceRow(
                "reponse scan OK 8230=0",
                trace,
                "HANDSHAKE",
                "SCAN_RESPONSE",
                "SIMULATED",
                "8230",
                "0",
                "8230=0");
            AssertDetail(trace, "scanner_response=SIMULATED_0");
            AssertTrue("trois reponses scan OK", CountRows(trace, "HANDSHAKE", "SCAN_RESPONSE", "SIMULATED", "0") >= 3);
            AssertEqual("aucun skip scanner simulateur", "0", CountRows(trace, "HANDSHAKE", "SCAN_RESPONSE", "SKIPPED", null).ToString(CultureInfo.InvariantCulture));
        }

        private static void AssertScannerFallbackHelpers()
        {
            var cfgNg = MachineConfig.CreateDefault();
            cfgNg.UseSimulator = true;
            cfgNg.ScanEnabled = true;
            cfgNg.NoBarcodeValue = "NG";

            var fallbackNg = InvokePrivateStaticString("ResolveScannerFallbackResultNoLock", cfgNg, null);
            AssertEqual("fallback null -> NG", "NG", fallbackNg);
            AssertEqual("barcode valide ignore fallback", null, InvokePrivateStaticString("ResolveScannerFallbackResultNoLock", cfgNg, "ABC123"));

            var cfgCon = MachineConfig.CreateDefault();
            cfgCon.UseSimulator = true;
            cfgCon.ScanEnabled = true;
            cfgCon.NoBarcodeValue = "CON";

            var fallbackCon = InvokePrivateStaticString("ResolveScannerFallbackResultNoLock", cfgCon, "CON");
            AssertEqual("fallback CON", "CON", fallbackCon);

            var buildDecision = typeof(MachineState).GetMethod(
                "BuildScannerFallbackDecisionNoLock",
                BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue("BuildScannerFallbackDecisionNoLock visible", buildDecision != null);

            var decision = (CellDecision)buildDecision.Invoke(null, new object[] { cfgCon, null, "CON" });
            AssertEqual("decision CON", "CON", decision.Decision);
            AssertEqual("target CON vers NG physique", "NG", decision.TargetLane);
            AssertEqual("reject CON", RejectReasons.ScannerNoBarcodeCon, decision.RejectReason);
        }

        private static void AssertScannerResponseHelperUses8230ZeroOrTwo()
        {
            var state = new MachineState();
            var cfg = MachineConfig.CreateDefault();
            cfg.UseSimulator = true;
            cfg.ScanEnabled = true;
            cfg.HandshakeRegister = 8230;

            var method = typeof(MachineState).GetMethod(
                "SendScannerHandshakeResponseNoLock",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertTrue("SendScannerHandshakeResponseNoLock visible", method != null);

            var ok = (string)method.Invoke(state, new object[] { cfg, (int?)1, "ABC123", false, "TEST" });
            AssertEqual("barcode OK repond 0", "SIMULATED_0", ok);

            var fallback = (string)method.Invoke(state, new object[] { cfg, (int?)1, "NG", true, "TEST" });
            AssertEqual("barcode absent repond 2", "SIMULATED_2", fallback);

            var trace = state.GetRuntimeTrace(100);
            AssertTraceRow("helper trace 8230=0", trace, "HANDSHAKE", "SCAN_RESPONSE", "SIMULATED", "8230", "0", "8230=0");
            AssertTraceRow("helper trace 8230=2", trace, "HANDSHAKE", "SCAN_RESPONSE", "SIMULATED", "8230", "2", "8230=2");
        }

        private static void AssertConFallbackDoesNotFeedLearning()
        {
            var state = new MachineState();
            state.ApplySettings(
                "COM_SIM",
                19200,
                1,
                MachineConfig.DefaultMeasurementRegister,
                22808,
                8230,
                8231,
                true,
                true,
                true,
                true,
                "COM_SCAN_SIM",
                115200,
                "Even",
                3.5,
                "BOTH",
                1,
                11,
                false,
                "CON");
            state.SetSortingMode(SortingModes.IntelligentGoodNg);
            state.StartNewLot();
            var start = state.ExecuteCycleCommand("START");
            AssertTrue("START simulateur CON", start.Ok && start.Simulated);

            InvokeHandleMeasurement(
                state,
                state.GetConfigCopy(),
                state.GetThresholds("21700"),
                "CON");

            var history = state.GetHistory(5);
            AssertTrue("historique CON present", history.Count > 0);
            var row = history[history.Count - 1];
            AssertEqual("resultat historique CON", "CON", row.Result);
            AssertEqual("canal physique CON", "NG", row.Channel);
            AssertEqual("rejet CON", RejectReasons.ScannerNoBarcodeCon, row.RejectReason);
            AssertEqual("source seuil CON", "SCANNER_FALLBACK", row.ThresholdSource);

            var audit = state.GetCellAuditHistory(5);
            AssertTrue("audit CON present", audit.Count > 0);
            var auditRow = audit[audit.Count - 1];
            AssertEqual("decision audit CON", "CON", auditRow.Decision);
            AssertEqual("resultat audit CON conserve", "CON", auditRow.Result);
            AssertEqual("canal audit CON physique", "NG", auditRow.EffectiveLane);
            AssertEqual("rejet audit CON", RejectReasons.ScannerNoBarcodeCon, auditRow.RejectReason);
            AssertEqual("source audit CON", "SCANNER_FALLBACK", auditRow.ThresholdSource);

            var lot = state.GetCurrentLot();
            AssertTrue("lot CON present", lot != null);
            AssertEqual("CON compte une cellule lot", "1", lot.TotalCount.ToString(CultureInfo.InvariantCulture));
            AssertEqual("CON ne compte pas GOOD", "0", lot.GoodCount.ToString(CultureInfo.InvariantCulture));
            AssertEqual("CON sort physiquement NG", "1", lot.NgCount.ToString(CultureInfo.InvariantCulture));
            AssertEqual("CON ne nourrit pas apprentissage", "0", lot.LearnedCellCount.ToString(CultureInfo.InvariantCulture));
            AssertTrue("CON ne cree pas reference", lot.Reference == null);

            var trace = state.GetRuntimeTrace(200);
            AssertTraceRow("CON repond 8230=2", trace, "HANDSHAKE", "SCAN_RESPONSE", "SIMULATED", "8230", "2", "8230=2");
            AssertTraceRow("CON fallback trace", trace, "SCANNER", "NO_BARCODE_FALLBACK", "CON", "HS=1", "CON", "apprentissage qualite");
        }

        private static void InvokeHandleMeasurement(MachineState state, MachineConfig cfg, ThresholdSet thresholds, string barcode)
        {
            var method = typeof(MachineState).GetMethod("HandleMeasurement", BindingFlags.NonPublic | BindingFlags.Instance);
            AssertTrue("HandleMeasurement visible", method != null);
            method.Invoke(
                state,
                new object[]
                {
                    cfg,
                    thresholds,
                    null,
                    false,
                    new List<string> { "Regression scanner CON." },
                    (int?)1,
                    true,
                    (int?)1,
                    new ushort[0],
                    new ushort[0],
                    new ushort[0],
                    3.580,
                    15.000,
                    true,
                    barcode,
                    new List<int>(),
                    "SIMULATEUR"
                });
        }

        private static string InvokePrivateStaticString(string methodName, MachineConfig cfg, string barcode)
        {
            var method = typeof(MachineState).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            AssertTrue(methodName + " visible", method != null);
            return (string)method.Invoke(null, new object[] { cfg, barcode });
        }

        private static void AssertTraceRow(
            string label,
            List<RuntimeTraceRow> rows,
            string category,
            string action,
            string status,
            string register,
            string value,
            string detail)
        {
            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                if (string.Equals(row.Category, category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Action, action, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Register, register, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Value, value, StringComparison.OrdinalIgnoreCase) &&
                    row.Detail != null &&
                    row.Detail.IndexOf(detail, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }
            }

            throw new InvalidOperationException(label + ": ligne trace absente.");
        }

        private static void AssertDetail(List<RuntimeTraceRow> rows, string expected)
        {
            foreach (var row in rows)
            {
                if (row != null &&
                    row.Detail != null &&
                    row.Detail.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return;
                }
            }

            throw new InvalidOperationException("Detail trace absent: " + expected);
        }

        private static int CountRows(List<RuntimeTraceRow> rows, string category, string action, string status, string value)
        {
            var count = 0;
            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                if (string.Equals(row.Category, category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Action, action, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase) &&
                    (value == null || string.Equals(row.Value, value, StringComparison.OrdinalIgnoreCase)))
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertEqual(string label, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    label + ": attendu " + (expected ?? "<null>") + ", obtenu " + (actual ?? "<null>") + ".");
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
