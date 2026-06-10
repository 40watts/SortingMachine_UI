using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace SortingMachineDesktop
{
    internal static class NgSweepSimulatorRegression
    {
        private static int Main()
        {
            try
            {
                return Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("NgSweepSimulatorRegression FAILED: " + ex.Message);
                return 1;
            }
        }

        private static int Run()
        {
            AssertCycleCommandCodesMatchConstructorButtons();

            var state = new MachineState();
            state.SetUseSimulator(true);
            state.SetSortingMode(SortingModes.IntelligentGoodNg);

            var lot = state.StartNewLot();
            AssertTrue("nouveau lot", lot.Ok && lot.Lot != null);

            var start = state.ExecuteCycleCommand("START");
            AssertTrue("START simulateur", start.Ok && start.Simulated);

            var expectedTops = QualityBandRouting.LearningSampleCount + 5;
            for (var i = 0; i < expectedTops; i++)
            {
                state.Tick();
                Thread.Sleep(270);
            }

            var trace = state.GetRuntimeTrace(300);
            var sweeps = FindRows(trace, "SAFETY", "NG_SWEEP", null);
            AssertEqual("aucun balayage NG PC en production", 0, sweeps.Count);

            var directPushers = FindRows(trace, "ROUTING", "PUSHER_DIRECT", null);
            AssertEqual("aucun piston GOOD direct en production", 0, directPushers.Count);

            var conveyorAdvances = FindRows(trace, "ROUTING", "CONVEYOR_ADVANCE", null);
            AssertEqual("aucune avance convoyeur PC en production", 0, conveyorAdvances.Count);

            var decisions = FindRows(trace, "DECISION", SortingModes.IntelligentGoodNg, null);
            AssertTrue("decision routage PLC + NG catch-all", HasDetail(decisions, "routing_control=PLC_THRESHOLDS_NG_CATCHALL"));
            AssertTrue("decision trace NG PLC", HasDetail(decisions, "pusher_mode=PLC_NG_CATCHALL"));
            AssertTrue("decision apprentissage ligne 10", HasDetail(decisions, "target=10"));
            AssertTrue("decision production lignes GOOD", HasAnyGoodBandTarget(decisions));

            var currentLot = state.GetCurrentLot();
            AssertTrue("lot courant stable", currentLot != null);
            AssertEqual("statut apprentissage stable", LearningStatuses.Stable, currentLot.LearningStatus);
            AssertTrue("reference lot stable", currentLot.Reference != null);
            AssertEqual("taille reference", QualityBandRouting.LearningSampleCount, currentLot.Reference.SampleSizeValid);
            AssertEqual("intervalles reference", QualityBandRouting.SortLaneCount, currentLot.Reference.QualityIntervals.Count);

            var diagnostic = state.GetDiagnostic();
            AssertTrue("diagnostic routage", diagnostic != null && diagnostic.PhysicalRouting != null);
            AssertTrue("diagnostic pulse NG", diagnostic.PhysicalRouting.LastNgPulse != null);
            AssertEqual("aucun pulse NG automatique PC", "NONE", diagnostic.PhysicalRouting.LastNgPulse.Status);
            AssertEqual("mode routage physique", "PLC_THRESHOLDS_NG_CATCHALL", diagnostic.PhysicalRouting.PhysicalRoutingMode);
            AssertQualityBandThresholds(diagnostic.PhysicalRouting.ProgrammedThresholds);
            AssertNoPcNgSweepAfterStop(state);
            AssertPistonTestsBlockOnAnyRunState();
            AssertConveyorMaintenanceBlocksOnAnyRunState();
            AssertNgAutoReleaseBlocksOnAnyRunState();
            AssertLatchedY11OnBlockedInReal();

            Console.WriteLine("NgSweepSimulatorRegression OK");
            return 0;
        }

        private static void AssertNoPcNgSweepAfterStop(MachineState state)
        {
            var stop = state.ExecuteCycleCommand("STOP");
            AssertTrue("STOP simulateur", stop.Ok && stop.Simulated);
            TickSeveral(state, 3);

            var restart = state.ExecuteCycleCommand("START");
            AssertTrue("START apres STOP simulateur", restart.Ok && restart.Simulated);
            TickSeveral(state, 1);

            var pause = state.ExecuteCycleCommand("PAUSE");
            AssertTrue("PAUSE simulateur", pause.Ok && pause.Simulated);
            TickSeveral(state, 3);

            var rows = state.GetRuntimeTrace(2000);
            AssertEqual("aucun balayage NG PC, y compris autour de STOP/START/PAUSE", 0, CountRows(rows, "SAFETY", "NG_SWEEP", null));
        }

        private static void AssertPistonTestsBlockOnAnyRunState()
        {
            var state = new MachineState();
            state.SetUseSimulator(false);

            SetPrivateBool(state, "_operatorStartArmed", false);
            SetPrivateBool(state, "_lotControlEnabled", false);
            AssertTrue("test piston autorise machine arretee", !InvokePistonRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", true);
            SetPrivateBool(state, "_lotControlEnabled", false);
            AssertTrue("test piston bloque si START arme seul", InvokePistonRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", false);
            SetPrivateBool(state, "_lotControlEnabled", true);
            AssertTrue("test piston bloque si lotControl seul", InvokePistonRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", true);
            SetPrivateBool(state, "_lotControlEnabled", true);
            AssertTrue("test piston bloque si cycle complet", InvokePistonRunBlock(state));

            state.SetUseSimulator(true);
            AssertTrue("simulateur ne bloque pas test piston", !InvokePistonRunBlock(state));
        }

        private static void AssertConveyorMaintenanceBlocksOnAnyRunState()
        {
            var state = new MachineState();
            state.SetUseSimulator(false);

            SetPrivateBool(state, "_operatorStartArmed", false);
            SetPrivateBool(state, "_lotControlEnabled", false);
            AssertTrue("convoyeur maintenance autorise machine arretee", !InvokeConveyorRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", true);
            SetPrivateBool(state, "_lotControlEnabled", false);
            AssertTrue("convoyeur maintenance bloque si START arme seul", InvokeConveyorRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", false);
            SetPrivateBool(state, "_lotControlEnabled", true);
            AssertTrue("convoyeur maintenance bloque si lotControl seul", InvokeConveyorRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", true);
            SetPrivateBool(state, "_lotControlEnabled", true);
            AssertTrue("convoyeur maintenance bloque si cycle complet", InvokeConveyorRunBlock(state));

            state.SetUseSimulator(true);
            AssertTrue("simulateur ne bloque pas convoyeur maintenance", !InvokeConveyorRunBlock(state));
        }

        private static void AssertNgAutoReleaseBlocksOnAnyRunState()
        {
            var state = new MachineState();
            state.SetUseSimulator(false);

            SetPrivateBool(state, "_operatorStartArmed", false);
            SetPrivateBool(state, "_lotControlEnabled", false);
            AssertTrue("auto release NG autorisee machine arretee", !InvokeNgAutoReleaseRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", true);
            SetPrivateBool(state, "_lotControlEnabled", false);
            AssertTrue("auto release NG bloquee si START arme seul", InvokeNgAutoReleaseRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", false);
            SetPrivateBool(state, "_lotControlEnabled", true);
            AssertTrue("auto release NG bloquee si lotControl seul", InvokeNgAutoReleaseRunBlock(state));

            SetPrivateBool(state, "_operatorStartArmed", true);
            SetPrivateBool(state, "_lotControlEnabled", true);
            AssertTrue("auto release NG bloquee si cycle complet", InvokeNgAutoReleaseRunBlock(state));

            state.SetUseSimulator(true);
            AssertTrue("simulateur ne bloque pas auto release NG", !InvokeNgAutoReleaseRunBlock(state));
        }

        private static void AssertLatchedY11OnBlockedInReal()
        {
            var state = new MachineState();
            state.SetUseSimulator(false);
            var result = InvokeY11OutputBit(state, true);

            AssertTrue("Y11 ON maintenu bloque en reel", !result.Ok);
            AssertTrue("Y11 ON maintenu bloque securite", result.BlockedBySafety);
            AssertTrue("Y11 ON maintenu reste expert", result.RequiresExpert);
            AssertEqual("Y11 ON maintenu non valide terrain", "False", result.TerrainValidated.ToString(CultureInfo.InvariantCulture));

            var trace = state.GetRuntimeTrace(50);
            AssertTrue("Y11 ON maintenu trace bloque", HasTrace(trace, "MAINTENANCE", "Y11_OUTPUT_ON", "BLOCKED_LATCHED_ON"));
        }

        private static MaintenanceCommandResult InvokeY11OutputBit(MachineState state, bool active)
        {
            var method = typeof(MachineState).GetMethod(
                "ExecuteY11OutputBitNoLock",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertTrue("helper Y11 brut visible", method != null);
            return (MaintenanceCommandResult)method.Invoke(state, new object[] { active ? "Y11_OUTPUT_ON" : "Y11_OUTPUT_OFF", active });
        }

        private static void AssertCycleCommandCodesMatchConstructorButtons()
        {
            var state = new MachineState();
            state.SetUseSimulator(true);
            var before = state.GetRuntimeTrace(1000);
            var baselineId = MaxTraceId(before);

            AssertCycleCommandCode(state, "START", "31");
            var afterStart = NewRowsSince(state.GetRuntimeTrace(1000), baselineId);
            AssertEqual("START n'envoie pas RESET=26", 0, CountRows(afterStart, "COMMAND", "RESET", "SIMULATED"));
            AssertEqual("START sans reset cache", 0, CountCommandValue(afterStart, "RESET", "26"));

            AssertCycleCommandCode(state, "PAUSE", "32");
            AssertCycleCommandCode(state, "STOP", "29");
            AssertCycleCommandCode(state, "RESET", "26");
        }

        private static void AssertCycleCommandCode(MachineState state, string command, string expectedCode)
        {
            var result = state.ExecuteCycleCommand(command);
            AssertTrue(command + " simulateur", result.Ok && result.Simulated);

            var rows = state.GetRuntimeTrace(200);
            var simulated = FindRows(rows, "COMMAND", command, "SIMULATED");
            AssertTrue(command + " trace simulateur", simulated.Count > 0);

            var last = simulated[simulated.Count - 1];
            AssertEqual(command + " registre cycle constructeur", "5978", last.Register);
            AssertEqual(command + " code constructeur", expectedCode, last.Value);
        }

        private static int CountCommandValue(List<RuntimeTraceRow> rows, string action, string value)
        {
            var count = 0;
            if (rows == null)
            {
                return count;
            }

            foreach (var row in rows)
            {
                if (row != null &&
                    string.Equals(row.Category, "COMMAND", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Action, action, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<RuntimeTraceRow> NewRowsSince(List<RuntimeTraceRow> rows, int baselineId)
        {
            var result = new List<RuntimeTraceRow>();
            if (rows == null)
            {
                return result;
            }

            foreach (var row in rows)
            {
                if (row != null && row.Id > baselineId)
                {
                    result.Add(row);
                }
            }

            return result;
        }

        private static int MaxTraceId(List<RuntimeTraceRow> rows)
        {
            var max = 0;
            if (rows == null)
            {
                return max;
            }

            foreach (var row in rows)
            {
                if (row != null && row.Id > max)
                {
                    max = row.Id;
                }
            }

            return max;
        }

        private static bool InvokePistonRunBlock(MachineState state)
        {
            var method = typeof(MachineState).GetMethod(
                "IsPistonMaintenanceBlockedByRunStateNoLock",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertTrue("helper blocage test piston visible", method != null);
            return (bool)method.Invoke(state, new object[0]);
        }

        private static bool InvokeConveyorRunBlock(MachineState state)
        {
            var method = typeof(MachineState).GetMethod(
                "IsMaintenanceConveyorBlockedByRunStateNoLock",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertTrue("helper blocage convoyeur maintenance visible", method != null);
            return (bool)method.Invoke(state, new object[0]);
        }

        private static bool InvokeNgAutoReleaseRunBlock(MachineState state)
        {
            var method = typeof(MachineState).GetMethod(
                "IsNgAutoReleaseBlockedByRunStateNoLock",
                BindingFlags.NonPublic | BindingFlags.Instance);
            AssertTrue("helper blocage auto release NG visible", method != null);
            return (bool)method.Invoke(state, new object[0]);
        }

        private static void SetPrivateBool(MachineState state, string fieldName, bool value)
        {
            var field = typeof(MachineState).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            AssertTrue("champ " + fieldName + " visible", field != null);
            field.SetValue(state, value);
        }

        private static void TickSeveral(MachineState state, int count)
        {
            for (var i = 0; i < count; i++)
            {
                state.Tick();
                Thread.Sleep(270);
            }
        }

        private static List<RuntimeTraceRow> FindRows(List<RuntimeTraceRow> rows, string category, string action, string status)
        {
            var result = new List<RuntimeTraceRow>();
            if (rows == null)
            {
                return result;
            }

            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                if (!string.Equals(row.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(row.Action, action, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(row);
            }

            return result;
        }

        private static bool HasDetail(List<RuntimeTraceRow> rows, string expected)
        {
            foreach (var row in rows)
            {
                if (row != null &&
                    !string.IsNullOrWhiteSpace(row.Detail) &&
                    row.Detail.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTrace(List<RuntimeTraceRow> rows, string category, string action, string status)
        {
            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                if (string.Equals(row.Category, category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Action, action, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.Status, status, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountRows(List<RuntimeTraceRow> rows, string category, string action, string status)
        {
            return FindRows(rows, category, action, status).Count;
        }

        private static bool HasAnyGoodBandTarget(List<RuntimeTraceRow> rows)
        {
            for (var lane = 1; lane <= QualityBandRouting.SortLaneCount; lane++)
            {
                if (HasDetail(rows, "target=" + lane.ToString(CultureInfo.InvariantCulture)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertQualityBandThresholds(ThresholdSet thresholds)
        {
            AssertTrue("seuils programmes presents", thresholds != null && thresholds.Channels != null);
            AssertTrue("seuils 11 voies", thresholds.Channels.Count >= 11);

            for (var lane = 1; lane <= QualityBandRouting.SortLaneCount; lane++)
            {
                AssertTrue("voie GOOD " + lane.ToString(CultureInfo.InvariantCulture) + " active", !IsDisabled(thresholds.Channels[lane - 1]));
            }

            AssertTrue("ligne 10 apprentissage retiree apres stabilisation", IsDisabled(thresholds.Channels[9]));
            AssertTrue("voie NG physique en catch-all constructeur", IsNgCatchAll(thresholds.Channels[10]));
        }

        private static bool IsDisabled(ChannelThreshold threshold)
        {
            return threshold != null &&
                threshold.VoltageMin >= 90.0 &&
                threshold.VoltageMax >= 90.0 &&
                threshold.IrMin >= 900.0 &&
                threshold.IrMax >= 900.0;
        }

        private static bool IsNgCatchAll(ChannelThreshold threshold)
        {
            return threshold != null &&
                threshold.VoltageMin <= 0.0 &&
                threshold.VoltageMax >= 90.0 &&
                threshold.IrMin <= 0.0 &&
                threshold.IrMax >= 900.0;
        }

        private static void AssertEqual(string label, int expected, int actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(label + ": attendu " + expected.ToString(CultureInfo.InvariantCulture) + ", obtenu " + actual.ToString(CultureInfo.InvariantCulture) + ".");
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
