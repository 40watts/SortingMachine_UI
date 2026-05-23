using System;
using System.Collections.Generic;

namespace SortingMachineDesktop
{
    internal static class QualityIntervalAudit
    {
        public static void ApplyRoutingContext(
            LotSession lot,
            IntelligentRecipe recipe,
            string effectiveLane,
            string intendedLane,
            ref string routingModel,
            ref int? qualityInterval,
            ref double? voltageMin,
            ref double? voltageMax,
            ref double? irMin,
            ref double? irMax)
        {
            if (lot == null || lot.Reference == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(routingModel))
            {
                routingModel = string.IsNullOrWhiteSpace(lot.Reference.RoutingModel)
                    ? QualityBandRouting.RoutingModel
                    : lot.Reference.RoutingModel;
            }

            var intervals = ResolveIntervals(lot, recipe);
            var interval = FindIntervalForLane(intervals, effectiveLane) ??
                FindIntervalForLane(intervals, intendedLane);

            if (interval != null)
            {
                if (!qualityInterval.HasValue) qualityInterval = interval.Index;
                if (!voltageMin.HasValue) voltageMin = interval.VoltageMin;
                if (!voltageMax.HasValue) voltageMax = interval.VoltageMax;
                if (!irMin.HasValue) irMin = interval.IrMin;
                if (!irMax.HasValue) irMax = interval.IrMax;
                return;
            }

            // NG rows do not always map to one interval; keep the guard window so the CSV still proves the lot model.
            if (string.Equals(routingModel, QualityBandRouting.RoutingModel, StringComparison.OrdinalIgnoreCase))
            {
                if (!voltageMin.HasValue) voltageMin = lot.Reference.VoltageMin;
                if (!voltageMax.HasValue) voltageMax = lot.Reference.VoltageMax;
                if (!irMin.HasValue) irMin = lot.Reference.IrMin;
                if (!irMax.HasValue) irMax = lot.Reference.IrMax;
            }
        }

        public static List<QualityIntervalSnapshot> ResolveIntervals(LotSession lot, IntelligentRecipe recipe)
        {
            if (lot == null || lot.Reference == null)
            {
                return new List<QualityIntervalSnapshot>();
            }

            if (lot.Reference.QualityIntervals != null && lot.Reference.QualityIntervals.Count > 0)
            {
                return lot.Reference.QualityIntervals;
            }

            if (!QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return new List<QualityIntervalSnapshot>();
            }

            var windows = QualityBandRouting.BuildWindows(recipe, lot.Reference, lot.RecentSample);
            return QualityBandRouting.BuildIntervalSnapshots(windows, lot.Reference.SampleSizeValid);
        }

        public static QualityIntervalSnapshot FindIntervalForLane(List<QualityIntervalSnapshot> intervals, string laneId)
        {
            if (intervals == null || string.IsNullOrWhiteSpace(laneId))
            {
                return null;
            }

            foreach (var interval in intervals)
            {
                if (interval != null && string.Equals(interval.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
                {
                    return interval;
                }
            }

            return null;
        }
    }
}
