using System;
using System.Collections.Generic;

namespace SortingMachineDesktop
{
    internal static class RobustWindowCalculator
    {
        private const int MinimumRobustSampleCount = 10;
        private const double IqrFenceMultiplier = 2.0;
        private const double SampleEnvelopePadding = 1.10;

        public static bool TryBuildIqrHalfWindow(
            List<SamplePoint> sample,
            bool forVoltage,
            double minWindow,
            double maxWindow,
            out double window)
        {
            window = 0;
            if (sample == null || sample.Count < MinimumRobustSampleCount)
            {
                return false;
            }

            var values = ExtractSortedValues(sample, forVoltage);
            if (values.Count < MinimumRobustSampleCount)
            {
                return false;
            }

            var q1 = Quantile(values, 0.25);
            var q3 = Quantile(values, 0.75);
            var iqr = q3 - q1;
            if (iqr < 0)
            {
                return false;
            }

            var lower = q1 - (IqrFenceMultiplier * iqr);
            var upper = q3 + (IqrFenceMultiplier * iqr);
            var center = Mean(values);
            var lowerWindow = Math.Abs(center - lower);
            var upperWindow = Math.Abs(upper - center);
            var observedWindow = ComputeObservedWindow(values, center) * SampleEnvelopePadding;
            var robustWindow = Math.Max(lowerWindow, upperWindow);
            window = Clamp(Math.Max(Math.Max(robustWindow, observedWindow), Math.Abs(minWindow)), Math.Abs(minWindow), Math.Max(Math.Abs(minWindow), Math.Abs(maxWindow)));
            return true;
        }

        private static List<double> ExtractSortedValues(List<SamplePoint> sample, bool forVoltage)
        {
            var values = new List<double>();
            foreach (var point in sample)
            {
                if (point == null)
                {
                    continue;
                }

                var value = forVoltage ? point.Voltage : point.Ir;
                if (!double.IsNaN(value) && !double.IsInfinity(value))
                {
                    values.Add(value);
                }
            }

            values.Sort();
            return values;
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

        private static double ComputeObservedWindow(List<double> values, double center)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            double maxDeviation = 0;
            foreach (var value in values)
            {
                maxDeviation = Math.Max(maxDeviation, Math.Abs(value - center));
            }

            return maxDeviation;
        }

        private static double Quantile(List<double> sortedValues, double quantile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
            {
                return 0;
            }

            if (sortedValues.Count == 1)
            {
                return sortedValues[0];
            }

            var index = (sortedValues.Count - 1) * quantile;
            var lowerIndex = (int)Math.Floor(index);
            var upperIndex = (int)Math.Ceiling(index);
            if (lowerIndex == upperIndex)
            {
                return sortedValues[lowerIndex];
            }

            var ratio = index - lowerIndex;
            return sortedValues[lowerIndex] + ((sortedValues[upperIndex] - sortedValues[lowerIndex]) * ratio);
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Min(Math.Max(value, min), max);
        }
    }
}
