using System;
using System.Collections.Generic;
using System.Globalization;

namespace SortingMachineDesktop
{
    public class LegacySortingEngine
    {
        public string SortToLane(double voltage, double ir, LegacyRecipe recipe)
        {
            if (recipe == null || recipe.Thresholds == null || recipe.Thresholds.Channels == null)
            {
                return "NG";
            }

            voltage = Math.Abs(voltage);
            ir = Math.Abs(ir);

            var thresholds = recipe.Thresholds.Channels;
            var start = Math.Max(1, recipe.ChannelStart);
            var end = Math.Min(thresholds.Count, recipe.ChannelEnd);
            if (end < start)
            {
                start = 1;
                end = thresholds.Count;
            }

            for (var i = start - 1; i < end; i++)
            {
                var th = thresholds[i];
                if (PassesThreshold(voltage, ir, th, recipe.JudgeMode))
                {
                    return (i + 1).ToString(CultureInfo.InvariantCulture);
                }
            }

            return "NG";
        }

        public CellDecision Evaluate(CellMeasurement measurement, LegacyRecipe recipe)
        {
            if (measurement == null || !measurement.MeasurementAvailable)
            {
                return new CellDecision
                {
                    SortingMode = SortingModes.Legacy,
                    Decision = "NG",
                    TargetLane = "NG",
                    LegacyChannel = "NG",
                    RejectReason = RejectReasons.NoValidMeasurement,
                    LearningStatus = LearningStatuses.Idle
                };
            }

            var lane = SortToLane(measurement.Voltage, measurement.Ir, recipe);
            return new CellDecision
            {
                SortingMode = SortingModes.Legacy,
                Decision = lane == "NG" ? "NG" : "GOOD",
                TargetLane = lane,
                LegacyChannel = lane,
                RejectReason = lane == "NG"
                    ? RejectReasons.LegacyNoMatch
                    : RejectReasons.None,
                LearningStatus = LearningStatuses.Idle
            };
        }

        private bool PassesThreshold(double voltage, double ir, ChannelThreshold th, string mode)
        {
            if (string.Equals(mode, "VOLTAGE", StringComparison.OrdinalIgnoreCase))
            {
                return voltage >= th.VoltageMin && voltage < th.VoltageMax;
            }

            if (string.Equals(mode, "IR", StringComparison.OrdinalIgnoreCase))
            {
                return ir >= th.IrMin && ir < th.IrMax;
            }

            return voltage >= th.VoltageMin && voltage < th.VoltageMax &&
                   ir >= th.IrMin && ir < th.IrMax;
        }
    }

    public class IntelligentSortingEngine
    {
        private const int InitialLearningBootstrapCount = QualityBandRouting.LearningSampleCount;

        public CellDecision Evaluate(
            CellMeasurement measurement,
            LotSession lot,
            IntelligentRecipe recipe,
            List<LaneCapacityObservation> observations)
        {
            if (measurement == null)
            {
                return new CellDecision
                {
                    SortingMode = SortingModes.IntelligentGoodNg,
                    Decision = "NG",
                    TargetLane = recipe == null ? "NG" : recipe.NgLane,
                    RejectReason = RejectReasons.NoValidMeasurement,
                    LearningStatus = lot == null ? LearningStatuses.Idle : lot.LearningStatus
                };
            }

            if (recipe == null || lot == null)
            {
                return new CellDecision
                {
                    SortingMode = SortingModes.IntelligentGoodNg,
                    Decision = "NG",
                    TargetLane = recipe == null ? "NG" : recipe.NgLane,
                    RejectReason = RejectReasons.NoGoodLaneAvailable,
                    LearningStatus = lot == null ? LearningStatuses.Idle : lot.LearningStatus
                };
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return EvaluateQualityIntervalRouting(measurement, lot, recipe, observations);
            }

            EnsureLotInitialized(lot, recipe, measurement.Timestamp);

            lot.TotalCount++;

            if (!measurement.MeasurementAvailable)
            {
                lot.NgCount++;
                lot.InvalidMeasurementCount++;
                return BuildDecision("NG", recipe.NgLane, RejectReasons.NoValidMeasurement, lot);
            }

            measurement.Voltage = Math.Abs(measurement.Voltage);
            measurement.Ir = Math.Abs(measurement.Ir);

            if (measurement.MachineBlocked)
            {
                lot.NgCount++;
                lot.AlertMessage = "Blocage machine détecté";
                return BuildDecision("NG", recipe.NgLane, RejectReasons.MachineBlocked, lot);
            }

            if (lot.LearningStatus == LearningStatuses.Unstable)
            {
                lot.NgCount++;
                lot.AlertMessage = "Lot instable";
                return BuildDecision("NG", recipe.NgLane, RejectReasons.LearningUnstable, lot);
            }

            if (lot.Reference == null || lot.LearningStatus == LearningStatuses.Idle || lot.LearningStatus == LearningStatuses.Learning)
            {
                var bootstrapTarget = ResolveBootstrapTarget(recipe);
                var refinementTarget = ResolveReferenceRefinementTarget(recipe);
                lot.LearningStatus = LearningStatuses.Learning;
                lot.LearnedCellCount++;
                lot.RecentSample.Add(new SamplePoint
                {
                    Voltage = measurement.Voltage,
                    Ir = measurement.Ir,
                    Timestamp = measurement.Timestamp
                });

                while (lot.RecentSample.Count > bootstrapTarget)
                {
                    lot.RecentSample.RemoveAt(0);
                }

                if (lot.RecentSample.Count >= bootstrapTarget)
                {
                    lot.Reference = BuildReference(recipe, lot.RecentSample, true);
                    if (lot.Reference.Status == LearningStatuses.Stable)
                    {
                        lot.Reference.SampleSizeTarget = refinementTarget;
                        lot.LearningStatus = LearningStatuses.Stable;
                        lot.AlertMessage = "Référence initiale validée, confirmation obligatoire ligne 1 " +
                                           lot.RecentSample.Count.ToString(CultureInfo.InvariantCulture) + "/" +
                                           refinementTarget.ToString(CultureInfo.InvariantCulture) + " GOOD.";
                    }
                    else
                    {
                        lot.LearningStatus = LearningStatuses.Learning;
                        lot.AlertMessage = "Référence trop serrée, poursuite apprentissage";
                    }
                }

                if (lot.LearnedCellCount >= recipe.LearningTimeoutCells && lot.RecentSample.Count < bootstrapTarget)
                {
                    lot.LearningStatus = LearningStatuses.Unstable;
                    lot.AlertMessage = "Apprentissage invalide";
                    lot.NgCount++;
                    return BuildDecision("NG", recipe.NgLane, RejectReasons.LearningUnstable, lot);
                }

                if (lot.LearnedCellCount >= recipe.LearningTimeoutCells &&
                    lot.RecentSample.Count >= bootstrapTarget &&
                    (lot.Reference == null || lot.Reference.Status != LearningStatuses.Stable))
                {
                    lot.LearningStatus = LearningStatuses.Unstable;
                    lot.AlertMessage = "Apprentissage trop dispersé";
                    lot.NgCount++;
                    return BuildDecision("NG", recipe.NgLane, RejectReasons.LearningUnstable, lot);
                }

                if (string.IsNullOrWhiteSpace(lot.CurrentGoodLane))
                {
                    lot.PauseRequested = true;
                    lot.AlertMessage = "Aucune ligne GOOD disponible pour l’apprentissage";
                    return new CellDecision
                    {
                        SortingMode = SortingModes.IntelligentGoodNg,
                        Decision = "PAUSE",
                        TargetLane = null,
                        RejectReason = RejectReasons.NoGoodLaneAvailable,
                        LearningStatus = lot.LearningStatus,
                        PauseRequested = true,
                        AlertMessage = lot.AlertMessage
                    };
                }

                var learningLane = FindLane(lot, lot.CurrentGoodLane);
                if (learningLane == null)
                {
                    lot.PauseRequested = true;
                    lot.AlertMessage = "État ligne incohérent";
                    return new CellDecision
                    {
                        SortingMode = SortingModes.IntelligentGoodNg,
                        Decision = "PAUSE",
                        TargetLane = null,
                        RejectReason = RejectReasons.NoGoodLaneAvailable,
                        LearningStatus = lot.LearningStatus,
                        PauseRequested = true,
                        AlertMessage = lot.AlertMessage
                    };
                }

                lot.GoodCount++;

                learningLane.CountAssigned++;
                learningLane.Status = ResolveLaneStatus(learningLane, recipe, observations, measurement.MachineLaneFullSignal);
                if (measurement.MachineLaneFullSignal || learningLane.Status == LaneStatuses.Full)
                {
                    RecordCapacityObservation(lot.CellType, learningLane, learningLane.CountAssigned, observations, measurement.Timestamp);
                }

                lot.NextGoodLane = FindNextAvailableLaneId(lot, learningLane.LaneId, recipe, observations);
                if (lot.Reference != null && lot.Reference.Status == LearningStatuses.Stable)
                {
                    lot.AlertMessage = learningLane.CountAssigned >= refinementTarget
                        ? "Confirmation obligatoire terminée : référence gelée après " + refinementTarget.ToString(CultureInfo.InvariantCulture) + " GOOD ligne 1."
                        : "Confirmation obligatoire ligne 1 " + learningLane.CountAssigned.ToString(CultureInfo.InvariantCulture) + "/" + refinementTarget.ToString(CultureInfo.InvariantCulture) + " GOOD.";
                }
                else if (lot.Reference != null && lot.Reference.Status == LearningStatuses.Unstable)
                {
                    lot.AlertMessage = "Référence trop serrée, poursuite apprentissage";
                }
                else
                {
                    lot.AlertMessage = "Apprentissage " + lot.RecentSample.Count.ToString(CultureInfo.InvariantCulture) + "/" + bootstrapTarget.ToString(CultureInfo.InvariantCulture) + " sur ligne " + learningLane.LaneId;
                }

                var assignedLearningLaneId = learningLane.LaneId;
                if (learningLane.Status == LaneStatuses.Full && !measurement.MachineCountersAuthoritative)
                {
                    AdvanceLane(lot, recipe, observations, measurement.Timestamp);
                }

                return BuildDecision("GOOD", assignedLearningLaneId, RejectReasons.None, lot);
            }

            var confirmationActive = IsReferenceConfirmationActive(lot, recipe);
            var windowVoltage = confirmationActive
                ? ResolveConfirmationGuardWindow(recipe, true)
                : BuildAdaptiveWindow(
                    lot.RecentSample,
                    recipe.AcceptanceKVoltage,
                    lot.Reference.SigmaVoltage,
                    recipe.MinWindowVoltage,
                    recipe.MaxWindowVoltage,
                    true);
            var windowIr = confirmationActive
                ? ResolveConfirmationGuardWindow(recipe, false)
                : BuildAdaptiveWindow(
                    lot.RecentSample,
                    recipe.AcceptanceKIr,
                    lot.Reference.SigmaIr,
                    recipe.MinWindowIr,
                    recipe.MaxWindowIr,
                    false);
            string confirmationRejectReason;
            if (confirmationActive && !CandidateKeepsReferenceStable(lot, recipe, measurement, out confirmationRejectReason))
            {
                lot.NgCount++;
                lot.AlertMessage = "Cellule vraiment hors cadre pendant confirmation de la reference initiale.";
                return BuildDecision("NG", recipe.NgLane, confirmationRejectReason, lot);
            }

            if (!confirmationActive &&
                (measurement.Voltage < lot.Reference.MeanVoltage - windowVoltage ||
                 measurement.Voltage > lot.Reference.MeanVoltage + windowVoltage))
            {
                lot.NgCount++;
                return BuildDecision("NG", recipe.NgLane, RejectReasons.OutOfVoltageWindow, lot, windowVoltage, windowIr);
            }

            if (!confirmationActive &&
                (measurement.Ir < lot.Reference.MeanIr - windowIr ||
                 measurement.Ir > lot.Reference.MeanIr + windowIr))
            {
                lot.NgCount++;
                return BuildDecision("NG", recipe.NgLane, RejectReasons.OutOfIrWindow, lot, windowVoltage, windowIr);
            }

            if (!measurement.MachineCountersAuthoritative)
            {
                AdvanceIfNeeded(lot, recipe, observations, measurement.MachineLaneFullSignal, measurement.Timestamp);
            }

            if (string.IsNullOrWhiteSpace(lot.CurrentGoodLane))
            {
                lot.PauseRequested = true;
                lot.AlertMessage = "Dernière ligne GOOD atteinte ou pleine";
                return new CellDecision
                {
                    SortingMode = SortingModes.IntelligentGoodNg,
                    Decision = "PAUSE",
                    TargetLane = null,
                    RejectReason = RejectReasons.NoGoodLaneAvailable,
                    LearningStatus = lot.LearningStatus,
                    PauseRequested = true,
                    AlertMessage = lot.AlertMessage
                };
            }

            var lane = FindLane(lot, lot.CurrentGoodLane);
            if (lane == null)
            {
                lot.PauseRequested = true;
                lot.AlertMessage = "État ligne incohérent";
                return new CellDecision
                {
                    SortingMode = SortingModes.IntelligentGoodNg,
                    Decision = "PAUSE",
                    TargetLane = null,
                    RejectReason = RejectReasons.NoGoodLaneAvailable,
                    LearningStatus = lot.LearningStatus,
                    PauseRequested = true,
                    AlertMessage = lot.AlertMessage
                };
            }

            lot.GoodCount++;

            lane.CountAssigned++;
            lane.Status = ResolveLaneStatus(lane, recipe, observations, measurement.MachineLaneFullSignal);
            if (measurement.MachineLaneFullSignal || lane.Status == LaneStatuses.Full)
            {
                RecordCapacityObservation(lot.CellType, lane, lane.CountAssigned, observations, measurement.Timestamp);
            }

            UpdateRollingReference(lot, recipe, measurement);
            lot.NextGoodLane = FindNextAvailableLaneId(lot, lane.LaneId, recipe, observations);
            var assignedLaneId = lane.LaneId;
            var alertBeforeLaneAdvance = lot.AlertMessage;
            if (!measurement.MachineCountersAuthoritative &&
                (lane.Status == LaneStatuses.NearFull || lane.Status == LaneStatuses.Full))
            {
                AdvanceLane(lot, recipe, observations, measurement.Timestamp);
                var firstGoodLaneId = (recipe.GoodLanes == null || recipe.GoodLanes.Count == 0) ? null : recipe.GoodLanes[0];
                if (!string.IsNullOrWhiteSpace(firstGoodLaneId) &&
                    string.Equals(assignedLaneId, firstGoodLaneId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(alertBeforeLaneAdvance))
                {
                    lot.AlertMessage = alertBeforeLaneAdvance;
                }
            }

            return BuildDecision("GOOD", assignedLaneId, RejectReasons.None, lot, windowVoltage, windowIr);
        }

        private CellDecision EvaluateQualityIntervalRouting(
            CellMeasurement measurement,
            LotSession lot,
            IntelligentRecipe recipe,
            List<LaneCapacityObservation> observations)
        {
            EnsureLotInitialized(lot, recipe, measurement.Timestamp);
            lot.TotalCount++;

            if (!measurement.MeasurementAvailable)
            {
                lot.NgCount++;
                lot.InvalidMeasurementCount++;
                return BuildDecision("NG", recipe.NgLane, RejectReasons.NoValidMeasurement, lot);
            }

            measurement.Voltage = Math.Abs(measurement.Voltage);
            measurement.Ir = Math.Abs(measurement.Ir);

            if (measurement.MachineBlocked)
            {
                lot.NgCount++;
                lot.AlertMessage = "Blocage machine détecté";
                return BuildDecision("NG", recipe.NgLane, RejectReasons.MachineBlocked, lot);
            }

            if (lot.Reference == null || lot.LearningStatus == LearningStatuses.Idle || lot.LearningStatus == LearningStatuses.Learning)
            {
                return RouteLearningCellToLane10(measurement, lot, recipe, observations);
            }

            var windows = QualityBandRouting.BuildWindows(recipe, lot.Reference, lot.RecentSample);
            string rejectReason;
            var band = QualityBandRouting.ResolveBand(measurement.Voltage, measurement.Ir, lot.Reference, windows, out rejectReason);
            if (band <= 0)
            {
                lot.NgCount++;
                lot.AlertMessage = "Cellule hors garde du modele du lot : tension trop basse/haute ou resistance hors des 9 intervalles.";
                return BuildDecision(
                    "NG",
                    recipe.NgLane,
                    rejectReason,
                    lot,
                    windows.OuterVoltage,
                    windows.OuterIr,
                    QualityBandRouting.BuildGuardThreshold(windows),
                    QualityBandRouting.RoutingModel,
                    null);
            }

            var lane = EnsureQualityIntervalLane(lot, recipe, band);

            lot.GoodCount++;
            lot.CurrentGoodLane = lane.LaneId;
            lane.CountAssigned++;
            lane.Status = LaneStatuses.Available;
            lane.MachineFullSignal = false;

            lot.NextGoodLane = null;
            var voltageWindow = QualityBandRouting.VoltageWindowForBand(windows, band);
            var irWindow = QualityBandRouting.IrWindowForBand(windows, band);
            var threshold = QualityBandRouting.BuildThresholdForBand(windows, band);
            lot.AlertMessage = "Intervalle " + band.ToString(CultureInfo.InvariantCulture) +
                " -> ligne " + lane.LaneId +
                " | " + QualityBandRouting.DescribeBand(windows, band) + ".";
            lot.PauseRequested = false;

            return BuildDecision(
                "GOOD",
                lane.LaneId,
                RejectReasons.None,
                lot,
                voltageWindow,
                irWindow,
                threshold,
                QualityBandRouting.RoutingModel,
                band);
        }

        private CellDecision RouteLearningCellToLane10(
            CellMeasurement measurement,
            LotSession lot,
            IntelligentRecipe recipe,
            List<LaneCapacityObservation> observations)
        {
            lot.LearningStatus = LearningStatuses.Learning;
            lot.LearnedCellCount++;
            if (lot.RecentSample == null)
            {
                lot.RecentSample = new List<SamplePoint>();
            }

            lot.RecentSample.Add(new SamplePoint
            {
                Voltage = measurement.Voltage,
                Ir = measurement.Ir,
                Timestamp = measurement.Timestamp
            });

            var learningLane = FindLane(lot, QualityBandRouting.LearningLaneId);
            if (learningLane == null)
            {
                learningLane = new LaneState
                {
                    LaneId = QualityBandRouting.LearningLaneId,
                    Role = "GOOD",
                    Status = LaneStatuses.Available,
                    CapacityTarget = 0,
                    CountAssigned = 0,
                    LastSwitchIn = measurement.Timestamp
                };
                lot.Lanes.Add(learningLane);
            }

            lot.GoodCount++;
            lot.CurrentGoodLane = QualityBandRouting.LearningLaneId;
            learningLane.CountAssigned++;
            learningLane.Status = LaneStatuses.Available;
            learningLane.MachineFullSignal = false;
            lot.NextGoodLane = "1";

            var targetSampleReached = lot.RecentSample.Count >= QualityBandRouting.LearningSampleCount;
            if (targetSampleReached)
            {
                lot.Reference = QualityBandRouting.BuildReference(recipe, lot.RecentSample);
                lot.LearningStatus = LearningStatuses.Stable;
                lot.Reference.SampleSizeTarget = QualityBandRouting.LearningSampleCount;
                lot.Reference.SampleSizeValid = Math.Min(lot.RecentSample.Count, QualityBandRouting.LearningSampleCount);
                learningLane.Status = LaneStatuses.Available;
                learningLane.LastSwitchOut = null;
                lot.CurrentGoodLane = null;
                lot.NextGoodLane = null;
                lot.AlertMessage = "Modele lot fige : " +
                    lot.Reference.SampleSizeValid.ToString(CultureInfo.InvariantCulture) +
                    " cellules en ligne 10, 9 intervalles resistance actifs sur lignes 1-9.";
            }
            else
            {
                lot.AlertMessage = "Apprentissage ligne 10 " +
                    lot.RecentSample.Count.ToString(CultureInfo.InvariantCulture) +
                    "/" + QualityBandRouting.LearningSampleCount.ToString(CultureInfo.InvariantCulture) +
                    " cellules. Gel obligatoire a 19 cellules.";
            }

            return BuildDecision(
                "GOOD",
                QualityBandRouting.LearningLaneId,
                RejectReasons.None,
                lot,
                null,
                null,
                null,
                QualityBandRouting.RoutingModel,
                null);
        }

        private LaneState EnsureQualityIntervalLane(LotSession lot, IntelligentRecipe recipe, int band)
        {
            var lanes = QualityBandRouting.GetBandLanes(band);
            var laneId = lanes.Length == 0
                ? band.ToString(CultureInfo.InvariantCulture)
                : lanes[0];
            var lane = FindLane(lot, laneId);
            if (lane != null)
            {
                return lane;
            }

            if (lot.Lanes == null)
            {
                lot.Lanes = new List<LaneState>();
            }

            lane = new LaneState
            {
                LaneId = laneId,
                Role = "GOOD",
                Status = LaneStatuses.Available,
                CapacityTarget = 0,
                CountAssigned = 0
            };
            lot.Lanes.Add(lane);
            return lane;
        }

        private bool CandidateKeepsReferenceStable(
            LotSession lot,
            IntelligentRecipe recipe,
            CellMeasurement measurement,
            out string rejectReason)
        {
            rejectReason = RejectReasons.None;
            if (lot == null || recipe == null || measurement == null)
            {
                rejectReason = RejectReasons.NoValidMeasurement;
                return false;
            }

            var candidateSample = new List<SamplePoint>(lot.RecentSample ?? new List<SamplePoint>());
            candidateSample.Add(new SamplePoint
            {
                Voltage = measurement.Voltage,
                Ir = measurement.Ir,
                Timestamp = measurement.Timestamp
            });

            var candidateReference = BuildReference(recipe, candidateSample, true);
            var voltageDelta = Math.Abs(measurement.Voltage - lot.Reference.MeanVoltage);
            var irDelta = Math.Abs(measurement.Ir - lot.Reference.MeanIr);
            var voltageGuard = ResolveConfirmationGuardWindow(recipe, true);
            var irGuard = ResolveConfirmationGuardWindow(recipe, false);
            if (voltageDelta > voltageGuard)
            {
                rejectReason = RejectReasons.OutOfVoltageWindow;
                return false;
            }

            if (irDelta > irGuard)
            {
                rejectReason = RejectReasons.OutOfIrWindow;
                return false;
            }

            if (candidateReference.Status == LearningStatuses.Stable)
            {
                return true;
            }

            if (voltageDelta <= voltageGuard && irDelta <= irGuard)
            {
                return true;
            }

            rejectReason = candidateReference.SigmaVoltage > recipe.MaxSigmaVoltage
                ? RejectReasons.OutOfVoltageWindow
                : RejectReasons.OutOfIrWindow;
            return false;
        }

        private double ResolveConfirmationGuardWindow(IntelligentRecipe recipe, bool forVoltage)
        {
            if (recipe == null)
            {
                return forVoltage ? 0.12 : 4.0;
            }

            var maxWindow = forVoltage ? recipe.MaxWindowVoltage : recipe.MaxWindowIr;
            var minWindow = forVoltage ? recipe.MinWindowVoltage : recipe.MinWindowIr;
            var fallback = forVoltage ? 0.12 : 4.0;
            if (maxWindow <= 0)
            {
                maxWindow = fallback;
            }

            return Math.Max(Math.Abs(maxWindow), Math.Abs(minWindow));
        }

        private bool IsReferenceConfirmationActive(LotSession lot, IntelligentRecipe recipe)
        {
            if (lot == null ||
                recipe == null ||
                lot.Reference == null ||
                lot.Reference.Status != LearningStatuses.Stable ||
                recipe.GoodLanes == null ||
                recipe.GoodLanes.Count == 0)
            {
                return false;
            }

            var firstGoodLaneId = recipe.GoodLanes[0];
            if (string.IsNullOrWhiteSpace(firstGoodLaneId) ||
                !string.Equals(lot.CurrentGoodLane, firstGoodLaneId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var firstLane = FindLane(lot, firstGoodLaneId);
            if (firstLane == null)
            {
                return false;
            }

            var bootstrapTarget = ResolveBootstrapTarget(recipe);
            var refinementTarget = ResolveReferenceRefinementTarget(recipe);
            return firstLane.CountAssigned >= bootstrapTarget &&
                firstLane.CountAssigned < refinementTarget;
        }

        private CellDecision BuildDecision(
            string decision,
            string targetLane,
            string rejectReason,
            LotSession lot,
            double? windowVoltage = null,
            double? windowIr = null,
            ChannelThreshold exactThreshold = null,
            string routingModel = null,
            int? qualityInterval = null)
        {
            var cellDecision = new CellDecision
            {
                SortingMode = SortingModes.IntelligentGoodNg,
                Decision = decision,
                TargetLane = targetLane,
                RejectReason = rejectReason,
                LearningStatus = lot.LearningStatus,
                RoutingModel = routingModel,
                QualityInterval = qualityInterval,
                PauseRequested = lot != null && lot.PauseRequested,
                AlertMessage = lot.AlertMessage
            };

            if (exactThreshold != null)
            {
                cellDecision.VoltageMin = exactThreshold.VoltageMin;
                cellDecision.VoltageMax = exactThreshold.VoltageMax;
                cellDecision.IrMin = exactThreshold.IrMin;
                cellDecision.IrMax = exactThreshold.IrMax;
            }
            else if (lot != null && lot.Reference != null && windowVoltage.HasValue && windowIr.HasValue)
            {
                cellDecision.VoltageMin = lot.Reference.MeanVoltage - windowVoltage.Value;
                cellDecision.VoltageMax = lot.Reference.MeanVoltage + windowVoltage.Value;
                cellDecision.IrMin = lot.Reference.MeanIr - windowIr.Value;
                cellDecision.IrMax = lot.Reference.MeanIr + windowIr.Value;
            }

            return cellDecision;
        }

        private void EnsureLotInitialized(LotSession lot, IntelligentRecipe recipe, string timestamp)
        {
            if (lot.RecentSample == null)
            {
                lot.RecentSample = new List<SamplePoint>();
            }

            if (lot.Lanes == null || lot.Lanes.Count == 0)
            {
                lot.Lanes = new List<LaneState>();
                foreach (var laneId in recipe.GoodLanes ?? new List<string>())
                {
                    var capacityTarget = QualityBandRouting.IsQualityIntervalRecipe(recipe)
                        ? 0
                        : GetRecipeLaneCapacity(recipe, laneId);
                    lot.Lanes.Add(new LaneState
                    {
                        LaneId = laneId,
                        Role = "GOOD",
                        Status = LaneStatuses.Available,
                        CapacityTarget = capacityTarget,
                        CountAssigned = 0,
                        LastSwitchIn = null,
                        LastSwitchOut = null
                    });
                }

                lot.Lanes.Add(new LaneState
                {
                    LaneId = recipe.NgLane,
                    Role = "NG",
                    Status = LaneStatuses.Available,
                    CapacityTarget = 0,
                    CountAssigned = 0
                });
            }

            if (string.IsNullOrWhiteSpace(lot.CurrentGoodLane))
            {
                lot.CurrentGoodLane = FindFirstAvailableLaneId(lot);
                var lane = FindLane(lot, lot.CurrentGoodLane);
                if (lane != null)
                {
                    lane.LastSwitchIn = timestamp;
                }
            }

            lot.NextGoodLane = FindNextAvailableLaneId(lot, lot.CurrentGoodLane, recipe, new List<LaneCapacityObservation>());
            if (string.IsNullOrWhiteSpace(lot.LearningStatus))
            {
                lot.LearningStatus = LearningStatuses.Idle;
            }
        }

        private LotReference BuildReference(IntelligentRecipe recipe, List<SamplePoint> sample, bool enforceStability)
        {
            double totalVoltage = 0;
            double totalIr = 0;
            var voltageValues = new List<double>();
            var irValues = new List<double>();
            foreach (var point in sample)
            {
                totalVoltage += point.Voltage;
                totalIr += point.Ir;
                voltageValues.Add(point.Voltage);
                irValues.Add(point.Ir);
            }

            var meanVoltage = sample.Count == 0 ? 0 : totalVoltage / sample.Count;
            var meanIr = sample.Count == 0 ? 0 : totalIr / sample.Count;
            var sigmaVoltage = ComputeSigma(voltageValues, meanVoltage);
            var sigmaIr = ComputeSigma(irValues, meanIr);
            var stable = !enforceStability || (sigmaVoltage <= recipe.MaxSigmaVoltage && sigmaIr <= recipe.MaxSigmaIr);

            return new LotReference
            {
                Version = 1,
                CellType = recipe.CellType,
                SampleSizeTarget = recipe.SampleSize,
                SampleSizeValid = sample.Count,
                MeanVoltage = meanVoltage,
                SigmaVoltage = sigmaVoltage,
                MeanIr = meanIr,
                SigmaIr = sigmaIr,
                ValidatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Status = stable ? LearningStatuses.Stable : LearningStatuses.Unstable
            };
        }

        private void UpdateRollingReference(LotSession lot, IntelligentRecipe recipe, CellMeasurement measurement)
        {
            if (lot == null || recipe == null || measurement == null)
            {
                return;
            }

            var firstGoodLaneId = (recipe.GoodLanes == null || recipe.GoodLanes.Count == 0) ? null : recipe.GoodLanes[0];
            if (string.IsNullOrWhiteSpace(firstGoodLaneId) ||
                !string.Equals(lot.CurrentGoodLane, firstGoodLaneId, StringComparison.OrdinalIgnoreCase))
            {
                lot.LearningStatus = LearningStatuses.Stable;
                return;
            }

            var firstLane = FindLane(lot, firstGoodLaneId);
            if (firstLane == null)
            {
                lot.LearningStatus = LearningStatuses.Stable;
                return;
            }

            var refinementTarget = ResolveReferenceRefinementTarget(recipe);
            if (refinementTarget <= 0 || firstLane.CountAssigned > refinementTarget)
            {
                lot.LearningStatus = LearningStatuses.Stable;
                return;
            }

            if (lot.RecentSample == null)
            {
                lot.RecentSample = new List<SamplePoint>();
            }

            lot.RecentSample.Add(new SamplePoint
            {
                Voltage = measurement.Voltage,
                Ir = measurement.Ir,
                Timestamp = measurement.Timestamp
            });

            while (lot.RecentSample.Count > refinementTarget)
            {
                lot.RecentSample.RemoveAt(0);
            }

            lot.Reference = BuildReference(recipe, lot.RecentSample, false);
            lot.Reference.SampleSizeTarget = refinementTarget;
            lot.Reference.SampleSizeValid = lot.RecentSample.Count;
            lot.Reference.Status = LearningStatuses.Stable;
            lot.LearningStatus = LearningStatuses.Stable;
            lot.AlertMessage = firstLane.CountAssigned >= refinementTarget
                ? "Confirmation obligatoire terminée : référence gelée après " + refinementTarget.ToString(CultureInfo.InvariantCulture) + " GOOD ligne 1."
                : "Confirmation obligatoire ligne 1 " + lot.RecentSample.Count.ToString(CultureInfo.InvariantCulture) + "/" + refinementTarget.ToString(CultureInfo.InvariantCulture) + " GOOD.";
        }

        private int ResolveBootstrapTarget(IntelligentRecipe recipe)
        {
            if (recipe == null)
            {
                return InitialLearningBootstrapCount;
            }

            return Math.Max(1, Math.Min(recipe.SampleSize <= 0 ? InitialLearningBootstrapCount : recipe.SampleSize, InitialLearningBootstrapCount));
        }

        private int ResolveReferenceRefinementTarget(IntelligentRecipe recipe)
        {
            if (recipe == null)
            {
                return InitialLearningBootstrapCount;
            }

            var firstGoodLaneId = (recipe.GoodLanes == null || recipe.GoodLanes.Count == 0) ? null : recipe.GoodLanes[0];
            var firstLaneCapacity = GetRecipeLaneCapacity(recipe, firstGoodLaneId);
            if (firstLaneCapacity > 0)
            {
                return firstLaneCapacity;
            }

            return Math.Max(recipe.SampleSize, InitialLearningBootstrapCount);
        }

        private double BuildAdaptiveWindow(
            List<SamplePoint> sample,
            double acceptanceK,
            double sigma,
            double minWindow,
            double maxWindow,
            bool forVoltage)
        {
            double robustWindow;
            if (RobustWindowCalculator.TryBuildIqrHalfWindow(sample, forVoltage, minWindow, maxWindow, out robustWindow))
            {
                return robustWindow;
            }

            var sigmaWindow = acceptanceK * sigma;
            var sampleWindow = ComputeSampleWindow(sample, forVoltage);
            const double rangeMultiplier = 1.10;
            var paddedRangeWindow = sampleWindow * rangeMultiplier;
            return Clamp(Math.Max(Math.Max(sigmaWindow, paddedRangeWindow), minWindow), minWindow, maxWindow);
        }

        private double ComputeSampleWindow(List<SamplePoint> sample, bool forVoltage)
        {
            if (sample == null || sample.Count == 0)
            {
                return 0;
            }

            double total = 0;
            var count = 0;
            foreach (var point in sample)
            {
                var value = forVoltage ? point.Voltage : point.Ir;
                total += value;
                count++;
            }

            var mean = count == 0 ? 0 : total / count;
            double maxDeviation = 0;
            foreach (var point in sample)
            {
                var value = forVoltage ? point.Voltage : point.Ir;
                maxDeviation = Math.Max(maxDeviation, Math.Abs(value - mean));
            }

            return maxDeviation;
        }

        private double ComputeSigma(List<double> values, double mean)
        {
            if (values == null || values.Count <= 1)
            {
                return 0;
            }

            double sum = 0;
            foreach (var value in values)
            {
                var diff = value - mean;
                sum += diff * diff;
            }

            return Math.Sqrt(sum / values.Count);
        }

        private void AdvanceIfNeeded(
            LotSession lot,
            IntelligentRecipe recipe,
            List<LaneCapacityObservation> observations,
            bool machineLaneFullSignal,
            string timestamp)
        {
            var current = FindLane(lot, lot.CurrentGoodLane);
            if (current == null)
            {
                lot.CurrentGoodLane = FindFirstAvailableLaneId(lot);
                return;
            }

            current.Status = ResolveLaneStatus(current, recipe, observations, machineLaneFullSignal);
            if (machineLaneFullSignal || current.Status == LaneStatuses.Full || current.Status == LaneStatuses.NearFull)
            {
                if (machineLaneFullSignal || current.CountAssigned >= ResolveCapacity(current, observations))
                {
                    RecordCapacityObservation(lot.CellType, current, current.CountAssigned, observations, timestamp);
                }

                AdvanceLane(lot, recipe, observations, timestamp);
            }
        }

        private void AdvanceLane(LotSession lot, IntelligentRecipe recipe, List<LaneCapacityObservation> observations, string timestamp)
        {
            var current = FindLane(lot, lot.CurrentGoodLane);
            if (current != null)
            {
                current.LastSwitchOut = timestamp;
            }

            var next = FindNextAvailableLaneId(lot, lot.CurrentGoodLane, recipe, observations);
            lot.CurrentGoodLane = next;
            lot.NextGoodLane = FindNextAvailableLaneId(lot, next, recipe, observations);
            if (!string.IsNullOrWhiteSpace(next))
            {
                var lane = FindLane(lot, next);
                if (lane != null)
                {
                    lane.LastSwitchIn = timestamp;
                }
                lot.PauseRequested = false;
                lot.AlertMessage = null;
            }
            else
            {
                lot.PauseRequested = true;
                lot.AlertMessage = "Toutes les lignes bonnes sont indisponibles";
            }
        }

        private LaneState FindLane(LotSession lot, string laneId)
        {
            if (lot == null || lot.Lanes == null || string.IsNullOrWhiteSpace(laneId))
            {
                return null;
            }

            foreach (var lane in lot.Lanes)
            {
                if (string.Equals(lane.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
                {
                    return lane;
                }
            }

            return null;
        }

        private string FindFirstAvailableLaneId(LotSession lot)
        {
            if (lot == null || lot.Lanes == null)
            {
                return null;
            }

            foreach (var lane in lot.Lanes)
            {
                if (lane.Role == "GOOD" && lane.Status != LaneStatuses.Full && lane.Status != LaneStatuses.Blocked)
                {
                    return lane.LaneId;
                }
            }

            return null;
        }

        private string FindNextAvailableLaneId(LotSession lot, string currentLaneId, IntelligentRecipe recipe, List<LaneCapacityObservation> observations)
        {
            if (recipe == null || recipe.GoodLanes == null || recipe.GoodLanes.Count == 0)
            {
                return null;
            }

            var currentIndex = recipe.GoodLanes.IndexOf(currentLaneId);
            var start = currentIndex < 0 ? 0 : currentIndex + 1;
            for (var i = start; i < recipe.GoodLanes.Count; i++)
            {
                var lane = FindLane(lot, recipe.GoodLanes[i]);
                if (lane == null)
                {
                    continue;
                }

                lane.Status = ResolveLaneStatus(lane, recipe, observations, lane.MachineFullSignal);
                if (lane.Status != LaneStatuses.Full && lane.Status != LaneStatuses.Blocked)
                {
                    return lane.LaneId;
                }
            }

            return null;
        }

        private string ResolveLaneStatus(LaneState lane, IntelligentRecipe recipe, List<LaneCapacityObservation> observations, bool machineLaneFullSignal)
        {
            lane.MachineFullSignal = machineLaneFullSignal;
            if (machineLaneFullSignal)
            {
                return LaneStatuses.Full;
            }

            var capacity = ResolveCapacity(lane, observations);
            if (capacity <= 0)
            {
                return LaneStatuses.Available;
            }

            if (lane.CountAssigned >= capacity)
            {
                return LaneStatuses.Full;
            }

            if (lane.CountAssigned >= Math.Max(0, capacity - recipe.LanePreSwitchMargin))
            {
                return LaneStatuses.NearFull;
            }

            return LaneStatuses.Available;
        }

        private int ResolveCapacity(LaneState lane, List<LaneCapacityObservation> observations)
        {
            if (lane.CapacityTarget > 0)
            {
                return lane.CapacityTarget;
            }

            if (lane.CapacityObserved > 0 && lane.CapacityConfidence >= 2)
            {
                return lane.CapacityObserved;
            }

            var best = FindObservation(observations, lane.LaneId, null);
            if (best != null && best.Confidence >= 2)
            {
                lane.CapacityObserved = lane.CapacityTarget > 0
                    ? Math.Min(best.ObservedCount, lane.CapacityTarget)
                    : best.ObservedCount;
                lane.CapacityConfidence = best.Confidence;
                return lane.CapacityObserved;
            }

            return lane.CapacityTarget;
        }

        private LaneCapacityObservation FindObservation(List<LaneCapacityObservation> observations, string laneId, string cellType)
        {
            if (observations == null)
            {
                return null;
            }

            foreach (var observation in observations)
            {
                if (!string.Equals(observation.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (cellType != null && !string.Equals(observation.CellType, cellType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return observation;
            }

            return null;
        }

        private void RecordCapacityObservation(string cellType, LaneState lane, int count, List<LaneCapacityObservation> observations, string timestamp)
        {
            if (observations == null || lane == null || string.IsNullOrWhiteSpace(lane.LaneId) || count <= 0)
            {
                return;
            }

            var observedCount = lane.CapacityTarget > 0
                ? Math.Min(count, lane.CapacityTarget)
                : count;

            LaneCapacityObservation existing = null;
            foreach (var observation in observations)
            {
                if (string.Equals(observation.CellType, cellType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(observation.LaneId, lane.LaneId, StringComparison.OrdinalIgnoreCase))
                {
                    existing = observation;
                    break;
                }
            }

            if (existing == null)
            {
                observations.Add(new LaneCapacityObservation
                {
                    CellType = cellType,
                    LaneId = lane.LaneId,
                    ObservedCount = observedCount,
                    Confidence = 1,
                    LastObservedAt = timestamp
                });
                return;
            }

            existing.ObservedCount = (int)Math.Round((existing.ObservedCount + observedCount) / 2.0, MidpointRounding.AwayFromZero);
            existing.Confidence = Math.Min(existing.Confidence + 1, 20);
            existing.LastObservedAt = timestamp;
        }

        private int GetRecipeLaneCapacity(IntelligentRecipe recipe, string laneId)
        {
            if (recipe == null || recipe.LaneCapacities == null)
            {
                return 0;
            }

            foreach (var lane in recipe.LaneCapacities)
            {
                if (string.Equals(lane.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
                {
                    return lane.Capacity;
                }
            }

            return 0;
        }

        private double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }
    }
}
