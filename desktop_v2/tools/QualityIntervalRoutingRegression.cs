using System;
using System.Collections.Generic;

namespace SortingMachineDesktop
{
    internal static class QualityIntervalRoutingRegression
    {
        private static int Main()
        {
            try
            {
                return Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("QualityIntervalRoutingRegression FAILED: " + ex.Message);
                return 1;
            }
        }

        private static int Run()
        {
            var recipe = BuildRecipe();
            var sample = BuildLearningSample(QualityBandRouting.LearningSampleCount);
            var reference = QualityBandRouting.BuildReference(recipe, sample);
            var windows = QualityBandRouting.BuildWindows(recipe, reference, sample);

            AssertEqual("modele routage", QualityBandRouting.RoutingModel, reference.RoutingModel);
            AssertEqual("taille echantillon cible", QualityBandRouting.LearningSampleCount, reference.SampleSizeTarget);
            AssertEqual("taille echantillon valide", QualityBandRouting.LearningSampleCount, reference.SampleSizeValid);
            AssertEqual("nombre intervalles reference", QualityBandRouting.SortLaneCount, reference.QualityIntervals.Count);
            AssertEqual("nombre intervalles fenetre", QualityBandRouting.SortLaneCount, windows.Intervals.Count);
            AssertBalancedIntervals("intervalles gaussiens", windows, 6.0);

            AssertBand("borne basse incluse", 1, reference, windows, reference.VoltageMin, windows.Intervals[0].IrMin);
            AssertBand("frontiere interne bascule", 2, reference, windows, MidVoltage(reference), windows.Intervals[0].IrMax);
            AssertReject("borne haute tension exclusive", RejectReasons.OutOfVoltageWindow, reference, windows, reference.VoltageMax, MidIr(windows.Intervals[4]));
            AssertReject("borne haute IR exclusive", RejectReasons.OutOfIrWindow, reference, windows, MidVoltage(reference), windows.Intervals[QualityBandRouting.SortLaneCount - 1].IrMax);
            AssertBand("tension negative normalisee", 5, reference, windows, -MidVoltage(reference), MidIr(windows.Intervals[4]));

            AssertPhysicalNgLaneDoesNotOverlapGoodWindows(reference, windows);
            AssertAuditContextForGoodLane(recipe, sample, reference);
            AssertAuditContextForNgGuard(recipe, sample, reference);
            AssertFrozenIntervalsIgnorePostLearningCells(recipe, sample, reference);
            AssertUnevenSampleStillBuildsRegularIntervals(recipe);
            AssertRecentLotTailDoesNotBecomeMassNg(recipe);
            AssertStableRoutingIgnoresFullLaneState(recipe, sample, reference);
            AssertLearningRequiresAll19CellsDespiteFullSignal(recipe);
            AssertGaussianLotFillsLanesEvenly(recipe);

            Console.WriteLine("QualityIntervalRoutingRegression OK");
            return 0;
        }

        private static IntelligentRecipe BuildRecipe()
        {
            return new IntelligentRecipe
            {
                CellType = "21700",
                SampleSize = QualityBandRouting.LearningSampleCount,
                MinWindowVoltage = 0.001,
                MaxWindowVoltage = 0.020,
                MinWindowIr = 0.250,
                MaxWindowIr = 4.000,
                AcceptanceKVoltage = 2.0,
                AcceptanceKIr = 2.0,
                GoodLanes = QualityBandRouting.BuildDefaultGoodLanes(),
                NgLane = "NG",
                LaneCapacities = BuildDefaultLaneCapacities(),
                LearningTimeoutCells = QualityBandRouting.LearningSampleCount * 2
            };
        }

        private static List<LaneCapacitySetting> BuildDefaultLaneCapacities()
        {
            var capacities = new List<LaneCapacitySetting>();
            for (var lane = 1; lane <= 10; lane++)
            {
                capacities.Add(new LaneCapacitySetting
                {
                    LaneId = lane.ToString(),
                    Capacity = lane == 10 ? QualityBandRouting.LearningSampleCount : 20
                });
            }

            return capacities;
        }

        private static List<SamplePoint> BuildLearningSample(int count)
        {
            var sample = new List<SamplePoint>();
            for (var i = 0; i < count; i++)
            {
                sample.Add(new SamplePoint
                {
                    Voltage = 3.580 + ((i % 3) - 1) * 0.0002,
                    Ir = 14.000 + i * 0.180,
                    Timestamp = "2026-05-15 10:00:" + i.ToString("00")
                });
            }

            return sample;
        }

        private static void AssertPhysicalNgLaneDoesNotOverlapGoodWindows(LotReference reference, QualityBandWindows windows)
        {
            var thresholds = new ThresholdSet { Channels = new List<ChannelThreshold>() };
            for (var i = 0; i < 11; i++)
            {
                thresholds.Channels.Add(new ChannelThreshold
                {
                    VoltageMin = 99.9,
                    VoltageMax = 99.9,
                    IrMin = 999.99,
                    IrMax = 999.99
                });
            }

            for (var band = 1; band <= QualityBandRouting.SortLaneCount; band++)
            {
                thresholds.Channels[band - 1] = QualityBandRouting.BuildThresholdForBand(windows, band);
            }

            thresholds.Channels[10] = new ChannelThreshold
            {
                VoltageMin = 99.9,
                VoltageMax = 99.9,
                IrMin = 999.99,
                IrMax = 999.99
            };

            var recipe = new LegacyRecipe
            {
                JudgeMode = "BOTH",
                ChannelStart = 1,
                ChannelEnd = 11,
                Thresholds = thresholds
            };
            var engine = new LegacySortingEngine();

            AssertEqual("voie NG hors seuils: une cellule GOOD garde sa ligne", "5", engine.SortToLane(MidVoltage(reference), MidIr(windows.Intervals[4]), recipe));
            AssertEqual("voie NG hors seuils: hors IR revient au rejet par defaut", "NG", engine.SortToLane(MidVoltage(reference), windows.IrGuardMax + 0.500, recipe));
            AssertEqual("voie NG hors seuils: hors tension revient au rejet par defaut", "NG", engine.SortToLane(windows.VoltageGuardMax + 0.050, MidIr(windows.Intervals[4]), recipe));
        }

        private static void AssertUnevenSampleStillBuildsRegularIntervals(IntelligentRecipe recipe)
        {
            var sample = new List<SamplePoint>
            {
                Point(3.5850, 14.42, 0),
                Point(3.5851, 14.79, 1),
                Point(3.5849, 14.95, 2),
                Point(3.5850, 15.03, 3),
                Point(3.5851, 15.14, 4),
                Point(3.5850, 15.28, 5),
                Point(3.5848, 15.54, 6),
                Point(3.5852, 15.79, 7),
                Point(3.5849, 15.83, 8),
                Point(3.5850, 16.03, 9),
                Point(3.5851, 16.16, 10),
                Point(3.5849, 16.27, 11),
                Point(3.5850, 16.55, 12),
                Point(3.5852, 16.94, 13),
                Point(3.5848, 15.52, 14)
            };

            var reference = QualityBandRouting.BuildReference(recipe, sample);
            var windows = QualityBandRouting.BuildWindows(recipe, reference, sample);
            AssertBalancedIntervals("echantillon irregulier", windows, 6.0);
            AssertProductionDistribution("echantillon irregulier production", reference, windows);
        }

        private static void AssertRecentLotTailDoesNotBecomeMassNg(IntelligentRecipe recipe)
        {
            var sample = new List<SamplePoint>
            {
                Point(3.5840, 17.521, 0),
                Point(3.5849, 15.638, 1),
                Point(3.5844, 17.496, 2),
                Point(3.5845, 17.537, 3),
                Point(3.5844, 16.143, 4),
                Point(3.5848, 15.648, 5),
                Point(3.5845, 15.702, 6),
                Point(3.5846, 16.939, 7),
                Point(3.5846, 15.465, 8),
                Point(3.5845, 14.240, 9),
                Point(3.5846, 14.773, 10),
                Point(3.5848, 17.753, 11),
                Point(3.5844, 16.787, 12),
                Point(3.5845, 16.327, 13),
                Point(3.5846, 17.516, 14),
                Point(3.5840, 17.336, 15),
                Point(3.5844, 15.433, 16),
                Point(3.5851, 15.961, 17),
                Point(3.5841, 17.210, 18)
            };

            var reference = QualityBandRouting.BuildReference(recipe, sample);
            var windows = QualityBandRouting.BuildWindows(recipe, reference, sample);
            // Garde-fou anti-degenerescence seulement: avec des coupures gaussiennes, les voies
            // de bord absorbent la marge de garde et sont volontairement plus larges. L'equilibre
            // metier est verifie sur les comptages ci-dessous, pas sur les largeurs.
            AssertBalancedIntervals("lot recent intervalles reguliers", windows, 12.0);

            var productionIr = new[]
            {
                16.771, 18.601, 17.092, 15.199, 18.968,
                17.948, 15.307, 17.550, 19.136, 18.072,
                16.450, 14.957, 17.887, 14.919, 16.579,
                16.762, 17.495, 15.563, 15.453, 16.488,
                18.516, 14.295, 14.171, 16.161, 13.749,
                18.489, 14.723, 15.601, 15.107, 15.382
            };

            var rejects = 0;
            var counts = new int[QualityBandRouting.SortLaneCount];
            foreach (var ir in productionIr)
            {
                string reason;
                var band = QualityBandRouting.ResolveBand(MidVoltage(reference), ir, reference, windows, out reason);
                if (band <= 0)
                {
                    rejects++;
                }
                else
                {
                    counts[band - 1]++;
                }
            }

            var nonEmpty = 0;
            var max = 0;
            foreach (var count in counts)
            {
                if (count > 0)
                {
                    nonEmpty++;
                }

                max = Math.Max(max, count);
            }

            AssertTrue("lot recent: rejets naturels limites", rejects <= 2);
            AssertTrue("lot recent: repartition sur plusieurs intervalles", nonEmpty >= 6);
            AssertTrue("lot recent: aucun intervalle dominant", max <= 8);
        }

        private static void AssertStableRoutingIgnoresFullLaneState(IntelligentRecipe recipe, List<SamplePoint> sample, LotReference reference)
        {
            var lot = BuildLotWithLanes(recipe);
            lot.Reference = reference;
            lot.RecentSample = new List<SamplePoint>(sample);
            lot.LearningStatus = LearningStatuses.Stable;
            foreach (var lane in lot.Lanes)
            {
                if (lane.Role == "GOOD" && lane.LaneId != QualityBandRouting.LearningLaneId)
                {
                    lane.CountAssigned = 20;
                    lane.Status = LaneStatuses.Full;
                }
            }

            var windows = QualityBandRouting.BuildWindows(recipe, reference, sample);
            var engine = new IntelligentSortingEngine();
            var decision = engine.Evaluate(
                new CellMeasurement
                {
                    Timestamp = "2026-05-15 12:00:00",
                    MeasurementAvailable = true,
                    Voltage = MidVoltage(reference),
                    Ir = MidIr(windows.Intervals[4])
                },
                lot,
                recipe,
                new List<LaneCapacityObservation>());

            AssertEqual("ligne pleine conserve decision GOOD", "GOOD", decision.Decision);
            AssertEqual("ligne pleine conserve intervalle", "5", decision.TargetLane);
            AssertEqual("ligne pleine conserve raison", RejectReasons.None, decision.RejectReason);
            AssertEqual("ligne pleine pas NG", 0, lot.NgCount);
            AssertEqual("ligne pleine compte GOOD", 1, lot.GoodCount);
            AssertTrue("ligne pleine ignoree sans pause", !decision.PauseRequested && !lot.PauseRequested);
            AssertEqual("ligne pleine ignoree statut", LaneStatuses.Available, lot.Lanes[4].Status);
        }

        private static void AssertLearningRequiresAll19CellsDespiteFullSignal(IntelligentRecipe recipe)
        {
            var lot = BuildLotWithLanes(recipe);
            var engine = new IntelligentSortingEngine();
            for (var i = 0; i < QualityBandRouting.LearningSampleCount - 1; i++)
            {
                var decision = engine.Evaluate(
                    new CellMeasurement
                    {
                        Timestamp = "2026-05-15 12:01:" + i.ToString("00"),
                        MeasurementAvailable = true,
                        MachineLaneFullSignal = true,
                        Voltage = 3.585 + (i % 2) * 0.0001,
                        Ir = 14.0 + i * 0.2
                    },
                    lot,
                    recipe,
                    new List<LaneCapacityObservation>());

                AssertEqual("apprentissage avant 19 decision", "GOOD", decision.Decision);
                AssertEqual("apprentissage avant 19 ligne", QualityBandRouting.LearningLaneId, decision.TargetLane);
                AssertTrue("apprentissage avant 19 ignore signal plein", !decision.PauseRequested && !lot.PauseRequested);
            }

            AssertTrue("apprentissage avant 19 sans reference", lot.Reference == null);
            AssertEqual("apprentissage avant 19 statut", LearningStatuses.Learning, lot.LearningStatus);
            AssertEqual("apprentissage avant 19 echantillon", QualityBandRouting.LearningSampleCount - 1, lot.RecentSample.Count);

            var finalDecision = engine.Evaluate(
                new CellMeasurement
                {
                    Timestamp = "2026-05-15 12:01:19",
                    MeasurementAvailable = true,
                    MachineLaneFullSignal = true,
                    Voltage = 3.585,
                    Ir = 17.8
                },
                lot,
                recipe,
                new List<LaneCapacityObservation>());

            AssertEqual("apprentissage 19 decision", "GOOD", finalDecision.Decision);
            AssertEqual("apprentissage 19 ligne", QualityBandRouting.LearningLaneId, finalDecision.TargetLane);
            AssertTrue("apprentissage 19 ignore signal plein", !finalDecision.PauseRequested && !lot.PauseRequested);
            AssertTrue("apprentissage 19 reference", lot.Reference != null);
            AssertEqual("apprentissage 19 taille", QualityBandRouting.LearningSampleCount, lot.Reference.SampleSizeValid);
            AssertEqual("apprentissage 19 statut", LearningStatuses.Stable, lot.LearningStatus);
            AssertEqual("apprentissage 19 statut ligne", LaneStatuses.Available, lot.Lanes[9].Status);
        }

        private static void AssertFrozenIntervalsIgnorePostLearningCells(IntelligentRecipe recipe, List<SamplePoint> sample, LotReference reference)
        {
            var extendedSample = new List<SamplePoint>(sample);
            for (var i = 0; i < 80; i++)
            {
                extendedSample.Add(Point(3.5850, i % 2 == 0 ? 12.0 + i * 0.02 : 22.0 - i * 0.015, 100 + i));
            }

            var windows = QualityBandRouting.BuildWindows(recipe, reference, extendedSample);
            AssertEqual("gel intervalles nombre", reference.QualityIntervals.Count, windows.Intervals.Count);
            for (var i = 0; i < reference.QualityIntervals.Count; i++)
            {
                AssertTrue("gel intervalle " + (i + 1).ToString() + " min", NearlyEqual(reference.QualityIntervals[i].IrMin, windows.Intervals[i].IrMin));
                AssertTrue("gel intervalle " + (i + 1).ToString() + " max", NearlyEqual(reference.QualityIntervals[i].IrMax, windows.Intervals[i].IrMax));
            }
        }

        private static LotSession BuildLotWithLanes(IntelligentRecipe recipe)
        {
            var lot = new LotSession
            {
                Id = 99,
                CellType = recipe.CellType,
                LearningStatus = LearningStatuses.Idle,
                RecentSample = new List<SamplePoint>(),
                Lanes = new List<LaneState>()
            };

            foreach (var laneId in recipe.GoodLanes)
            {
                lot.Lanes.Add(new LaneState
                {
                    LaneId = laneId,
                    Role = "GOOD",
                    Status = LaneStatuses.Available,
                    CapacityTarget = laneId == QualityBandRouting.LearningLaneId ? QualityBandRouting.LearningSampleCount : 20
                });
            }

            lot.Lanes.Add(new LaneState
            {
                LaneId = recipe.NgLane,
                Role = "NG",
                Status = LaneStatuses.Available
            });

            return lot;
        }

        private static SamplePoint Point(double voltage, double ir, int index)
        {
            return new SamplePoint
            {
                Voltage = voltage,
                Ir = ir,
                Timestamp = "2026-05-15 11:00:" + index.ToString("00")
            };
        }

        private static void AssertAuditContextForGoodLane(IntelligentRecipe recipe, List<SamplePoint> sample, LotReference reference)
        {
            var lot = new LotSession
            {
                Id = 1,
                CellType = recipe.CellType,
                Reference = reference,
                RecentSample = sample
            };

            string routingModel = null;
            int? interval = null;
            double? voltageMin = null;
            double? voltageMax = null;
            double? irMin = null;
            double? irMax = null;

            QualityIntervalAudit.ApplyRoutingContext(
                lot,
                recipe,
                "5",
                "5",
                ref routingModel,
                ref interval,
                ref voltageMin,
                ref voltageMax,
                ref irMin,
                ref irMax);

            AssertEqual("audit modele GOOD", QualityBandRouting.RoutingModel, routingModel);
            AssertEqual("audit intervalle GOOD", 5, interval.Value);
            AssertTrue("audit tension min GOOD", voltageMin.HasValue && NearlyEqual(voltageMin.Value, reference.QualityIntervals[4].VoltageMin));
            AssertTrue("audit IR max GOOD", irMax.HasValue && NearlyEqual(irMax.Value, reference.QualityIntervals[4].IrMax));
        }

        private static void AssertAuditContextForNgGuard(IntelligentRecipe recipe, List<SamplePoint> sample, LotReference reference)
        {
            var lot = new LotSession
            {
                Id = 1,
                CellType = recipe.CellType,
                Reference = reference,
                RecentSample = sample
            };

            string routingModel = null;
            int? interval = null;
            double? voltageMin = null;
            double? voltageMax = null;
            double? irMin = null;
            double? irMax = null;

            QualityIntervalAudit.ApplyRoutingContext(
                lot,
                recipe,
                "NG",
                "NG",
                ref routingModel,
                ref interval,
                ref voltageMin,
                ref voltageMax,
                ref irMin,
                ref irMax);

            AssertEqual("audit modele NG", QualityBandRouting.RoutingModel, routingModel);
            AssertTrue("audit intervalle NG vide", !interval.HasValue);
            AssertTrue("audit garde tension NG", voltageMin.HasValue && voltageMax.HasValue);
            AssertTrue("audit garde IR NG", irMin.HasValue && irMax.HasValue);
        }

        private static void AssertBand(string label, int expected, LotReference reference, QualityBandWindows windows, double voltage, double ir)
        {
            string reason;
            var actual = QualityBandRouting.ResolveBand(voltage, ir, reference, windows, out reason);
            AssertEqual(label + " band", expected, actual);
            AssertEqual(label + " reason", RejectReasons.None, reason);
        }

        private static void AssertReject(string label, string expectedReason, LotReference reference, QualityBandWindows windows, double voltage, double ir)
        {
            string reason;
            var actual = QualityBandRouting.ResolveBand(voltage, ir, reference, windows, out reason);
            AssertEqual(label + " band", 0, actual);
            AssertEqual(label + " reason", expectedReason, reason);
        }

        private static double MidVoltage(LotReference reference)
        {
            return (reference.VoltageMin + reference.VoltageMax) / 2.0;
        }

        private static double MidIr(QualityBandInterval interval)
        {
            return (interval.IrMin + interval.IrMax) / 2.0;
        }

        private static bool NearlyEqual(double left, double right)
        {
            return Math.Abs(left - right) < 0.000001;
        }

        private static void AssertGaussianLotFillsLanesEvenly(IntelligentRecipe recipe)
        {
            // Objectif metier: sur un lot gaussien, chaque voie GOOD recoit ~1/9 des cellules
            // pour que les bacs operateur se remplissent au meme rythme.
            const double mean = 15.0;
            const double sigma = 0.8;
            var zScores = new[]
            {
                -1.83, -1.40, -1.13, -0.92, -0.74, -0.59, -0.45, -0.31, -0.18,
                0.0, 0.18, 0.31, 0.45, 0.59, 0.74, 0.92, 1.13, 1.40, 1.83
            };
            var sample = new List<SamplePoint>();
            for (var i = 0; i < zScores.Length; i++)
            {
                sample.Add(new SamplePoint
                {
                    Voltage = 3.600 + ((i % 3) - 1) * 0.0002,
                    Ir = mean + sigma * zScores[i],
                    Timestamp = "2026-06-10 15:00:" + i.ToString("00")
                });
            }

            var reference = QualityBandRouting.BuildReference(recipe, sample);
            var windows = QualityBandRouting.BuildWindows(recipe, reference, sample);

            var random = new Random(20260610);
            var counts = new int[QualityBandRouting.SortLaneCount];
            var routed = 0;
            var rejected = 0;
            const int population = 900;
            for (var i = 0; i < population; i++)
            {
                var u1 = 1.0 - random.NextDouble();
                var u2 = random.NextDouble();
                var gaussian = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                var ir = mean + sigma * gaussian;
                string reason;
                var band = QualityBandRouting.ResolveBand(3.600, ir, reference, windows, out reason);
                if (band > 0)
                {
                    counts[band - 1]++;
                    routed++;
                }
                else
                {
                    rejected++;
                }
            }

            AssertTrue("lot gaussien: rejets limites (" + rejected + "/" + population + ")", rejected <= population / 10);
            for (var lane = 0; lane < counts.Length; lane++)
            {
                var share = counts[lane] / (double)routed;
                AssertTrue("lot gaussien: voie " + (lane + 1) + " pas affamee (" + counts[lane] + "/" + routed + ")", share >= 0.05);
                AssertTrue("lot gaussien: voie " + (lane + 1) + " ne deborde pas (" + counts[lane] + "/" + routed + ")", share <= 0.20);
            }
        }

        private static void AssertBalancedIntervals(string label, QualityBandWindows windows, double maxRatio)
        {
            var minWidth = double.MaxValue;
            var maxWidth = 0.0;
            foreach (var interval in windows.Intervals)
            {
                var width = interval.IrMax - interval.IrMin;
                minWidth = Math.Min(minWidth, width);
                maxWidth = Math.Max(maxWidth, width);
            }

            AssertTrue(label + ": largeur positive", minWidth > 0);
            AssertTrue(label + ": ecart largeur", maxWidth / minWidth <= maxRatio);
        }

        private static void AssertProductionDistribution(string label, LotReference reference, QualityBandWindows windows)
        {
            var productionIr = new[]
            {
                13.71399974822998, 13.81499958038330, 14.03899955749512, 14.10099983215332,
                14.18299961090088, 14.27199935913086, 14.80599975585938, 15.03299999237061,
                15.11900043487549, 15.33800029754639, 15.43900012969971, 15.60200023651123,
                15.72199916839600, 15.83700084686279, 15.97999954223633, 16.17600059509277,
                16.25200080871582, 16.48600006103516, 16.64599990844727, 16.82900047302246,
                17.04500007629395, 17.13500022888184, 17.32400131225586, 17.48899841308594,
                17.69899940490723, 17.72299957275391, 17.78000068664551, 17.95299911499023,
                18.29599952697754, 18.30299949645996
            };

            var counts = new int[QualityBandRouting.SortLaneCount];
            foreach (var ir in productionIr)
            {
                string reason;
                var band = QualityBandRouting.ResolveBand(MidVoltage(reference), ir, reference, windows, out reason);
                if (band > 0)
                {
                    counts[band - 1]++;
                }
            }

            var max = 0;
            var nonEmpty = 0;
            foreach (var count in counts)
            {
                max = Math.Max(max, count);
                if (count > 0)
                {
                    nonEmpty++;
                }
            }

            AssertTrue(label + ": repartition sur plusieurs intervalles", nonEmpty >= 6);
            AssertTrue(label + ": aucun intervalle ne capte tout", max <= 8);
        }

        private static void AssertEqual(string label, int expected, int actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(label + ": attendu " + expected.ToString() + ", obtenu " + actual.ToString() + ".");
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
