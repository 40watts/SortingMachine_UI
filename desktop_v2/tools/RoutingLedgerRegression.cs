using System;

namespace SortingMachineDesktop
{
    internal static class RoutingLedgerRegression
    {
        private static int Main()
        {
            try
            {
                return Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("RoutingLedgerRegression FAILED: " + ex.Message);
                return 1;
            }
        }

        private static int Run()
        {
            var lot = new LotSession { Id = 42 };
            for (var i = 0; i < 5; i++)
            {
                RoutingLedgerService.Append(
                    lot,
                    "2026-05-13 10:00:0" + i.ToString(),
                    i,
                    "GOOD",
                    "1",
                    "1",
                    -3.58,
                    12.1,
                    "ROUTING_LEDGER",
                    string.Empty,
                    QualityBandRouting.RoutingModel,
                    1,
                    3.57,
                    3.59,
                    12.0,
                    13.0);
            }

            AssertEqual("pending initial", 5, RoutingLedgerService.CountPendingForLane(lot, "1"));

            for (var i = 0; i < 3; i++)
            {
                var confirmed = RoutingLedgerService.ConfirmOldestForLane(lot, "1", "2026-05-13 10:01:0" + i.ToString());
                if (confirmed == null || confirmed.Sequence != i + 1)
                {
                    throw new InvalidOperationException("FIFO invalide pour la confirmation physique.");
                }
            }

            AssertEqual("pending apres confirmation", 2, RoutingLedgerService.CountPendingForLane(lot, "1"));

            RoutingLedgerService.Append(
                lot,
                "2026-05-13 10:02:00",
                99,
                "NG",
                "NG",
                "NG",
                3.60,
                12.0,
                "ROUTING_LEDGER",
                "OUT_OF_VOLTAGE_WINDOW",
                QualityBandRouting.RoutingModel,
                null,
                3.57,
                3.59,
                12.0,
                13.0);

            AssertEqual("pending good ignore NG", 2, RoutingLedgerService.CountPendingForLane(lot, "1"));

            RoutingLedgerService.Reset(lot);
            AssertEqual("pending reset", 0, RoutingLedgerService.CountPendingForLane(lot, "1"));
            Console.WriteLine("RoutingLedgerRegression OK");
            return 0;
        }

        private static void AssertEqual(string label, int expected, int actual)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(label + ": attendu " + expected.ToString() + ", obtenu " + actual.ToString() + ".");
            }
        }
    }
}
