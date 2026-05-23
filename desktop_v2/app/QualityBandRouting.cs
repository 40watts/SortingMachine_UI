using System;
using System.Collections.Generic;
using System.Globalization;

namespace SortingMachineDesktop
{
    internal class QualityBandInterval
    {
        public int Index { get; set; }
        public string LaneId { get; set; }
        public double IrMin { get; set; }
        public double IrMax { get; set; }
    }

    internal class QualityBandWindows
    {
        public double OuterVoltage { get; set; }
        public double OuterIr { get; set; }

        public double VoltageGuardMin { get; set; }
        public double VoltageGuardMax { get; set; }
        public double IrGuardMin { get; set; }
        public double IrGuardMax { get; set; }

        public List<QualityBandInterval> Intervals { get; set; }
    }

    internal static class QualityBandRouting
    {
        public const int MinimumLearningSampleCount = 15;
        public const int LearningSampleCount = 19;
        public const int SortLaneCount = 9;
        public const string LearningLaneId = "10";
        public const string RoutingModel = "IR_9_INTERVALS";

        public static bool IsQualityIntervalRecipe(IntelligentRecipe recipe)
        {
            if (recipe == null || recipe.GoodLanes == null)
            {
                return false;
            }

            for (var lane = 1; lane <= 10; lane++)
            {
                if (!recipe.GoodLanes.Contains(lane.ToString(CultureInfo.InvariantCulture)))
                {
                    return false;
                }
            }

            return true;
        }

        public static List<string> BuildDefaultGoodLanes()
        {
            return new List<string> { "1", "2", "3", "4", "5", "6", "7", "8", "9", LearningLaneId };
        }

        public static string[] GetBandLanes(int band)
        {
            if (band < 1 || band > SortLaneCount)
            {
                return new string[0];
            }

            return new[] { band.ToString(CultureInfo.InvariantCulture) };
        }

        public static LotReference BuildReference(IntelligentRecipe recipe, List<SamplePoint> sample)
        {
            var voltage = ExtractTrimmedValues(sample, true);
            var ir = ExtractTrimmedValues(sample, false);
            var meanVoltage = Mean(voltage);
            var meanIr = Mean(ir);
            var reference = new LotReference
            {
                Version = 4,
                CellType = recipe == null ? null : recipe.CellType,
                SampleSizeTarget = LearningSampleCount,
                SampleSizeValid = sample == null ? 0 : Math.Min(sample.Count, LearningSampleCount),
                MeanVoltage = meanVoltage,
                SigmaVoltage = Sigma(voltage, meanVoltage),
                MeanIr = meanIr,
                SigmaIr = Sigma(ir, meanIr),
                RoutingModel = RoutingModel,
                ValidatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Status = LearningStatuses.Stable
            };

            var windows = BuildWindowsFromSample(recipe, reference, sample);
            reference.VoltageMin = windows.VoltageGuardMin;
            reference.VoltageMax = windows.VoltageGuardMax;
            reference.IrMin = windows.IrGuardMin;
            reference.IrMax = windows.IrGuardMax;
            reference.QualityIntervals = BuildIntervalSnapshots(windows, reference.SampleSizeValid);
            return reference;
        }

        public static QualityBandWindows BuildWindows(IntelligentRecipe recipe, LotReference reference, List<SamplePoint> sample)
        {
            var frozen = BuildWindowsFromReference(reference);
            return frozen ?? BuildWindowsFromSample(recipe, reference, sample);
        }

        public static int ResolveBand(
            double voltage,
            double ir,
            LotReference reference,
            QualityBandWindows windows,
            out string rejectReason)
        {
            rejectReason = RejectReasons.None;
            if (reference == null || windows == null || windows.Intervals == null || windows.Intervals.Count == 0)
            {
                rejectReason = RejectReasons.LearningUnstable;
                return 0;
            }

            var normalizedVoltage = Math.Abs(voltage);
            var normalizedIr = Math.Abs(ir);
            if (normalizedVoltage < windows.VoltageGuardMin || normalizedVoltage >= windows.VoltageGuardMax)
            {
                rejectReason = RejectReasons.OutOfVoltageWindow;
                return 0;
            }

            if (normalizedIr < windows.IrGuardMin || normalizedIr >= windows.IrGuardMax)
            {
                rejectReason = RejectReasons.OutOfIrWindow;
                return 0;
            }

            foreach (var interval in windows.Intervals)
            {
                if (normalizedIr >= interval.IrMin && normalizedIr < interval.IrMax)
                {
                    return interval.Index;
                }
            }

            rejectReason = RejectReasons.OutOfIrWindow;
            return 0;
        }

        public static ChannelThreshold BuildThresholdForBand(QualityBandWindows windows, int band)
        {
            var threshold = BuildGuardThreshold(windows);
            var interval = GetBandInterval(windows, band);
            if (interval != null)
            {
                threshold.IrMin = interval.IrMin;
                threshold.IrMax = interval.IrMax;
            }

            return threshold;
        }

        public static ChannelThreshold BuildGuardThreshold(QualityBandWindows windows)
        {
            if (windows == null)
            {
                return new ChannelThreshold
                {
                    VoltageMin = 99.9,
                    VoltageMax = 99.9,
                    IrMin = 999.99,
                    IrMax = 999.99
                };
            }

            return new ChannelThreshold
            {
                VoltageMin = windows.VoltageGuardMin,
                VoltageMax = windows.VoltageGuardMax,
                IrMin = windows.IrGuardMin,
                IrMax = windows.IrGuardMax
            };
        }

        public static string DescribeBand(QualityBandWindows windows, int band)
        {
            var interval = GetBandInterval(windows, band);
            if (interval == null)
            {
                return string.Empty;
            }

            return "IR " +
                interval.IrMin.ToString("0.000", CultureInfo.InvariantCulture) +
                "-" +
                interval.IrMax.ToString("0.000", CultureInfo.InvariantCulture) +
                " mOhm";
        }

        public static double VoltageWindowForBand(QualityBandWindows windows, int band)
        {
            return HalfWidth(BuildThresholdForBand(windows, band), true);
        }

        public static double IrWindowForBand(QualityBandWindows windows, int band)
        {
            return HalfWidth(BuildThresholdForBand(windows, band), false);
        }

        public static List<QualityIntervalSnapshot> BuildIntervalSnapshots(QualityBandWindows windows, int learningSampleCount)
        {
            var result = new List<QualityIntervalSnapshot>();
            if (windows == null || windows.Intervals == null)
            {
                return result;
            }

            foreach (var interval in windows.Intervals)
            {
                result.Add(new QualityIntervalSnapshot
                {
                    Index = interval.Index,
                    LaneId = interval.LaneId,
                    IrMin = interval.IrMin,
                    IrMax = interval.IrMax,
                    VoltageMin = windows.VoltageGuardMin,
                    VoltageMax = windows.VoltageGuardMax,
                    LearningSampleCount = learningSampleCount
                });
            }

            return result;
        }

        private static QualityBandWindows BuildWindowsFromReference(LotReference reference)
        {
            if (reference == null ||
                !string.Equals(reference.RoutingModel, RoutingModel, StringComparison.OrdinalIgnoreCase) ||
                reference.QualityIntervals == null ||
                reference.QualityIntervals.Count < SortLaneCount)
            {
                return null;
            }

            var intervals = new List<QualityBandInterval>();
            foreach (var snapshot in reference.QualityIntervals)
            {
                if (snapshot == null || snapshot.Index < 1 || snapshot.Index > SortLaneCount)
                {
                    continue;
                }

                intervals.Add(new QualityBandInterval
                {
                    Index = snapshot.Index,
                    LaneId = string.IsNullOrWhiteSpace(snapshot.LaneId)
                        ? snapshot.Index.ToString(CultureInfo.InvariantCulture)
                        : snapshot.LaneId,
                    IrMin = snapshot.IrMin,
                    IrMax = snapshot.IrMax
                });
            }

            intervals.Sort((a, b) => a.Index.CompareTo(b.Index));
            if (intervals.Count < SortLaneCount)
            {
                return null;
            }

            var windows = new QualityBandWindows
            {
                VoltageGuardMin = reference.VoltageMin,
                VoltageGuardMax = reference.VoltageMax,
                IrGuardMin = reference.IrMin,
                IrGuardMax = reference.IrMax,
                Intervals = intervals
            };

            if (windows.VoltageGuardMax <= windows.VoltageGuardMin)
            {
                windows.VoltageGuardMin = reference.MeanVoltage - Math.Max(0.006, reference.SigmaVoltage * 6.0);
                windows.VoltageGuardMax = reference.MeanVoltage + Math.Max(0.006, reference.SigmaVoltage * 6.0);
            }

            if (windows.IrGuardMax <= windows.IrGuardMin)
            {
                windows.IrGuardMin = intervals[0].IrMin;
                windows.IrGuardMax = intervals[intervals.Count - 1].IrMax;
            }

            PopulateSummaryWindows(windows);
            return windows;
        }

        private static QualityBandWindows BuildWindowsFromSample(IntelligentRecipe recipe, LotReference reference, List<SamplePoint> sample)
        {
            var voltageProfile = BuildProfile(ExtractValues(sample, true), reference == null ? 0 : reference.MeanVoltage);
            var irProfile = BuildProfile(ExtractValues(sample, false), reference == null ? 0 : reference.MeanIr);
            var voltageGuard = BuildVoltageGuard(recipe, voltageProfile, reference == null ? 0 : reference.MeanVoltage);
            var irGuard = BuildIrGuard(recipe, irProfile, reference == null ? 0 : reference.MeanIr);

            var windows = new QualityBandWindows
            {
                VoltageGuardMin = voltageGuard.Min,
                VoltageGuardMax = voltageGuard.Max,
                IrGuardMin = irGuard.Min,
                IrGuardMax = irGuard.Max,
                Intervals = BuildIrIntervals(irProfile, irGuard.Min, irGuard.Max)
            };

            PopulateSummaryWindows(windows);
            return windows;
        }

        private static Guard BuildVoltageGuard(IntelligentRecipe recipe, DistributionProfile profile, double referenceCenter)
        {
            var center = profile != null && profile.Count > LearningSampleCount
                ? profile.Median
                : (referenceCenter != 0 ? referenceCenter : (profile == null ? 0 : profile.Median));
            var minHalfWindow = Math.Max(0.006, recipe == null ? 0.006 : Math.Abs(recipe.MinWindowVoltage) * 6.0);
            var maxHalfWindow = recipe == null || recipe.MaxWindowVoltage <= 0
                ? 0.020
                : Math.Max(minHalfWindow, Math.Abs(recipe.MaxWindowVoltage));
            if (profile == null || profile.Count == 0)
            {
                return CreateCenteredGuard(center, minHalfWindow);
            }

            // Voltage is a charge guard, not a sorting axis. The window follows the lot but stays tighter than IR.
            var learnedHalfWindow = Math.Max(profile.Range * 2.0, Math.Max(profile.Iqr * 4.0, Sigma(profile.Values, profile.Median) * 6.0));
            var halfWindow = Clamp(Math.Max(minHalfWindow, learnedHalfWindow), minHalfWindow, maxHalfWindow);
            return CreateCenteredGuard(center, halfWindow);
        }

        private static Guard BuildIrGuard(IntelligentRecipe recipe, DistributionProfile profile, double referenceCenter)
        {
            var center = referenceCenter != 0 ? referenceCenter : (profile == null ? 0 : profile.Median);
            var floor = ResolveAxisFloor(recipe, false);
            if (profile == null || profile.Count == 0)
            {
                return CreateCenteredGuard(center, floor);
            }

            // The guard extrapolates only slightly outside the learned lot.
            // Large IQR fences made the first/last intervals much wider than the middle ones.
            var edgePadding = ResolveIrEdgePadding(recipe, profile, floor);
            var min = profile.Min - edgePadding;
            var max = profile.Max + edgePadding;
            var minimumSpan = ResolveMinimumIrGuardSpan(recipe);
            if (max - min < minimumSpan)
            {
                var guardCenter = (profile.Min + profile.Max) / 2.0;
                min = guardCenter - minimumSpan / 2.0;
                max = guardCenter + minimumSpan / 2.0;
            }

            if (min < 0)
            {
                max -= min;
                min = 0;
            }

            return new Guard
            {
                Min = min,
                Max = Math.Max(min + minimumSpan, max)
            };
        }

        private static List<QualityBandInterval> BuildIrIntervals(DistributionProfile profile, double guardMin, double guardMax)
        {
            var intervals = new List<QualityBandInterval>();
            if (guardMax <= guardMin)
            {
                guardMax = guardMin + ResolveAxisFloor(null, false) * SortLaneCount;
            }

            var boundaries = BuildRegularizedQuantileBoundaries(profile, guardMin, guardMax);
            for (var i = 0; i < SortLaneCount; i++)
            {
                var min = boundaries[i];
                var max = boundaries[i + 1];
                intervals.Add(new QualityBandInterval
                {
                    Index = i + 1,
                    LaneId = (i + 1).ToString(CultureInfo.InvariantCulture),
                    IrMin = min,
                    IrMax = max
                });
            }

            return intervals;
        }

        private static List<double> BuildRegularizedQuantileBoundaries(DistributionProfile profile, double guardMin, double guardMax)
        {
            var boundaries = new List<double> { guardMin };
            var span = Math.Max(0.001, guardMax - guardMin);
            var quantileWeight = profile != null && profile.Count >= MinimumLearningSampleCount ? 0.25 : 0.0;
            var gaussianWeight = profile != null && profile.Count >= MinimumLearningSampleCount ? 0.10 : 0.0;
            var equalWidthWeight = Math.Max(0.0, 1.0 - quantileWeight - gaussianWeight);
            var sigma = profile == null ? 0 : Sigma(profile.Values, profile.Median);
            for (var i = 1; i < SortLaneCount; i++)
            {
                var equalBoundary = guardMin + span * i / SortLaneCount;
                var boundary = equalBoundary;
                if (profile != null && profile.Count >= MinimumLearningSampleCount)
                {
                    var quantileBoundary = Percentile(profile.Values, i / (double)SortLaneCount);
                    var gaussianBoundary = profile.Median + sigma * NormalScoreForBoundary(i);
                    // The lot rule is frozen after 19 cells. With such a small sample, pure quantiles
                    // are too jumpy; this keeps regular widths while nudging the center like a gaussian.
                    boundary =
                        equalBoundary * equalWidthWeight +
                        quantileBoundary * quantileWeight +
                        gaussianBoundary * gaussianWeight;
                }

                boundaries.Add(boundary);
            }

            boundaries.Add(guardMax);
            EnsureStrictBoundaries(boundaries, guardMin, guardMax);
            return boundaries;
        }

        private static double NormalScoreForBoundary(int boundaryIndex)
        {
            switch (boundaryIndex)
            {
                case 1: return -1.22064;
                case 2: return -0.76471;
                case 3: return -0.43073;
                case 4: return -0.13971;
                case 5: return 0.13971;
                case 6: return 0.43073;
                case 7: return 0.76471;
                case 8: return 1.22064;
                default: return 0;
            }
        }

        private static void EnsureStrictBoundaries(List<double> boundaries, double guardMin, double guardMax)
        {
            var span = Math.Max(0.001, guardMax - guardMin);
            var minimumWidth = Math.Max(0.015, span / 180.0);
            boundaries[0] = guardMin;
            boundaries[boundaries.Count - 1] = guardMax;
            for (var i = 1; i < boundaries.Count - 1; i++)
            {
                boundaries[i] = Clamp(
                    boundaries[i],
                    boundaries[i - 1] + minimumWidth,
                    guardMax - minimumWidth * (boundaries.Count - 1 - i));
            }
        }

        private static double ResolveIrEdgePadding(IntelligentRecipe recipe, DistributionProfile profile, double floor)
        {
            var recipeFloor = recipe == null ? 0.25 : Math.Abs(recipe.MinWindowIr);
            var minPadding = Math.Max(0.060, recipeFloor * 0.25);
            if (profile == null || profile.Count == 0)
            {
                return Math.Min(floor, minPadding);
            }

            var maxPadding = recipe == null || recipe.MaxWindowIr <= 0
                ? Math.Max(minPadding, Math.Max(0.90, profile.Range * 0.24))
                : Math.Max(minPadding, Math.Min(Math.Abs(recipe.MaxWindowIr) * 0.30, Math.Max(0.90, profile.Range * 0.24)));
            var rangePadding = Math.Max(0, profile.Range) * 0.160;
            var iqrPadding = Math.Max(0, profile.Iqr) * 0.650;
            var sigmaPadding = Sigma(profile.Values, profile.Median) * 0.850;
            var adaptivePadding = Math.Max(minPadding, Math.Max(rangePadding, Math.Max(iqrPadding, sigmaPadding)));
            return Clamp(adaptivePadding, minPadding, maxPadding);
        }

        private static double ResolveMinimumIrGuardSpan(IntelligentRecipe recipe)
        {
            var recipeFloor = recipe == null ? 0.25 : Math.Abs(recipe.MinWindowIr);
            return Math.Max(0.90, recipeFloor * SortLaneCount * 0.5);
        }

        private static double ResolveAxisFloor(IntelligentRecipe recipe, bool forVoltage)
        {
            if (forVoltage)
            {
                var recipeFloor = recipe == null ? 0.001 : Math.Abs(recipe.MinWindowVoltage);
                return Math.Max(0.0015, recipeFloor * 1.5);
            }

            var irFloor = recipe == null ? 0.25 : Math.Abs(recipe.MinWindowIr);
            return Math.Max(0.45, irFloor * 1.5);
        }

        private static Guard CreateCenteredGuard(double center, double halfWidth)
        {
            return new Guard
            {
                Min = Math.Max(0, center - halfWidth),
                Max = Math.Max(0, center + halfWidth)
            };
        }

        private static void PopulateSummaryWindows(QualityBandWindows windows)
        {
            windows.OuterVoltage = HalfWidth(BuildGuardThreshold(windows), true);
            windows.OuterIr = HalfWidth(BuildGuardThreshold(windows), false);
        }

        private static QualityBandInterval GetBandInterval(QualityBandWindows windows, int band)
        {
            if (windows == null || windows.Intervals == null)
            {
                return null;
            }

            foreach (var interval in windows.Intervals)
            {
                if (interval.Index == band)
                {
                    return interval;
                }
            }

            return null;
        }

        private static double HalfWidth(ChannelThreshold threshold, bool forVoltage)
        {
            if (threshold == null)
            {
                return 0;
            }

            return forVoltage
                ? Math.Abs(threshold.VoltageMax - threshold.VoltageMin) / 2.0
                : Math.Abs(threshold.IrMax - threshold.IrMin) / 2.0;
        }

        private static List<double> ExtractValues(List<SamplePoint> sample, bool forVoltage)
        {
            var values = new List<double>();
            if (sample == null)
            {
                return values;
            }

            var count = 0;
            foreach (var point in sample)
            {
                if (point == null)
                {
                    continue;
                }

                var value = forVoltage ? point.Voltage : point.Ir;
                if (!double.IsNaN(value) && !double.IsInfinity(value))
                {
                    values.Add(Math.Abs(value));
                    count++;
                    if (count >= LearningSampleCount)
                    {
                        break;
                    }
                }
            }

            values.Sort();
            return values;
        }

        private static List<double> ExtractTrimmedValues(List<SamplePoint> sample, bool forVoltage)
        {
            var values = ExtractValues(sample, forVoltage);
            if (values.Count >= MinimumLearningSampleCount)
            {
                values.RemoveAt(values.Count - 1);
                values.RemoveAt(0);
            }

            return values;
        }

        private static DistributionProfile BuildProfile(List<double> values, double fallbackCenter)
        {
            if (values == null)
            {
                values = new List<double>();
            }

            values.Sort();
            if (values.Count == 0 && fallbackCenter > 0)
            {
                values.Add(Math.Abs(fallbackCenter));
            }

            if (values.Count == 0)
            {
                return new DistributionProfile
                {
                    Values = values,
                    Count = 0
                };
            }

            values = BuildRobustCoreValues(values);
            var q1 = Percentile(values, 0.25);
            var q2 = Percentile(values, 0.50);
            var q3 = Percentile(values, 0.75);
            return new DistributionProfile
            {
                Values = values,
                Count = values.Count,
                Min = values[0],
                Max = values[values.Count - 1],
                Q1 = q1,
                Median = q2,
                Q3 = q3,
                Iqr = Math.Max(0, q3 - q1),
                Range = Math.Max(0, values[values.Count - 1] - values[0])
            };
        }

        private static List<double> BuildRobustCoreValues(List<double> sortedValues)
        {
            if (sortedValues == null || sortedValues.Count < MinimumLearningSampleCount)
            {
                return sortedValues ?? new List<double>();
            }

            var q1 = Percentile(sortedValues, 0.25);
            var q3 = Percentile(sortedValues, 0.75);
            var iqr = Math.Max(0, q3 - q1);
            if (iqr <= 0)
            {
                return sortedValues;
            }

            var fence = Math.Max(0.001, iqr * 2.75);
            var min = Math.Max(0, q1 - fence);
            var max = q3 + fence;
            var core = new List<double>();
            foreach (var value in sortedValues)
            {
                if (value >= min && value <= max)
                {
                    core.Add(value);
                }
            }

            return core.Count >= MinimumLearningSampleCount ? core : sortedValues;
        }

        private static double Percentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
            {
                return 0;
            }

            if (sortedValues.Count == 1)
            {
                return sortedValues[0];
            }

            var position = (sortedValues.Count - 1) * Clamp(percentile, 0, 1);
            var lowerIndex = (int)Math.Floor(position);
            var upperIndex = (int)Math.Ceiling(position);
            if (lowerIndex == upperIndex)
            {
                return sortedValues[lowerIndex];
            }

            var ratio = position - lowerIndex;
            return sortedValues[lowerIndex] + (sortedValues[upperIndex] - sortedValues[lowerIndex]) * ratio;
        }

        private static double Mean(List<double> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            double total = 0;
            foreach (var value in values)
            {
                total += value;
            }

            return total / values.Count;
        }

        private static double Sigma(List<double> values, double mean)
        {
            if (values == null || values.Count <= 1)
            {
                return 0;
            }

            double total = 0;
            foreach (var value in values)
            {
                var delta = value - mean;
                total += delta * delta;
            }

            return Math.Sqrt(total / values.Count);
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        private class DistributionProfile
        {
            public List<double> Values { get; set; }
            public int Count { get; set; }
            public double Min { get; set; }
            public double Max { get; set; }
            public double Q1 { get; set; }
            public double Median { get; set; }
            public double Q3 { get; set; }
            public double Iqr { get; set; }
            public double Range { get; set; }
        }

        private class Guard
        {
            public double Min { get; set; }
            public double Max { get; set; }
        }
    }
}
