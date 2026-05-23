using System;
using System.Collections.Generic;

namespace SortingMachineDesktop
{
    internal static class RoutingLedgerService
    {
        private const int MaxTicketRetention = 800;
        private const int MaxArchiveRetention = 10000;

        public static RoutingLedgerState Ensure(LotSession lot)
        {
            if (lot.RoutingLedger == null)
            {
                lot.RoutingLedger = new RoutingLedgerState();
            }

            if (lot.RoutingLedger.Tickets == null)
            {
                lot.RoutingLedger.Tickets = new List<RoutingTicket>();
            }

            if (lot.RoutingLedger.NextSequence <= 0)
            {
                long max = 0;
                foreach (var ticket in lot.RoutingLedger.Tickets)
                {
                    if (ticket != null && ticket.Sequence > max)
                    {
                        max = ticket.Sequence;
                    }
                }

                lot.RoutingLedger.NextSequence = max + 1;
            }

            return lot.RoutingLedger;
        }

        public static void Reset(LotSession lot)
        {
            if (lot == null)
            {
                return;
            }

            var ledger = Ensure(lot);
            if (ledger.Tickets != null && ledger.Tickets.Count > 0)
            {
                foreach (var ticket in ledger.Tickets)
                {
                    AddToArchiveIfMissing(lot, ticket);
                }

                TrimArchive(lot);
            }

            var nextSequence = 1L;
            if (lot.RoutingArchive != null)
            {
                foreach (var archived in lot.RoutingArchive)
                {
                    if (archived != null && archived.Sequence >= nextSequence)
                    {
                        nextSequence = archived.Sequence + 1;
                    }
                }
            }

            lot.RoutingLedger = new RoutingLedgerState
            {
                NextSequence = nextSequence,
                Tickets = new List<RoutingTicket>()
            };
        }

        public static RoutingTicket Append(
            LotSession lot,
            string timestamp,
            int? handshake,
            string decision,
            string intendedLane,
            string effectiveLane,
            double voltage,
            double ir,
            string thresholdSource,
            string rejectReason,
            string routingModel,
            int? qualityInterval,
            double? voltageMin,
            double? voltageMax,
            double? irMin,
            double? irMax)
        {
            var ledger = Ensure(lot);
            var ticket = new RoutingTicket
            {
                Sequence = ledger.NextSequence++,
                LotId = lot.Id,
                CreatedAt = timestamp,
                Handshake = handshake,
                Decision = NormalizeDecision(decision),
                IntendedLane = NormalizeLane(intendedLane),
                EffectiveLane = NormalizeLane(effectiveLane),
                Status = RoutingTicketStatuses.Pending,
                Voltage = Math.Abs(voltage),
                Ir = Math.Abs(ir),
                RoutingModel = routingModel,
                QualityInterval = qualityInterval,
                VoltageMin = voltageMin,
                VoltageMax = voltageMax,
                IrMin = irMin,
                IrMax = irMax,
                ThresholdSource = thresholdSource,
                RejectReason = rejectReason
            };

            ledger.Tickets.Add(ticket);
            Trim(lot);
            return ticket;
        }

        public static int CountPendingForLane(LotSession lot, string laneId)
        {
            if (lot == null || string.IsNullOrWhiteSpace(laneId))
            {
                return 0;
            }

            var ledger = Ensure(lot);
            var count = 0;
            foreach (var ticket in ledger.Tickets)
            {
                if (ticket != null &&
                    string.Equals(ticket.Status, RoutingTicketStatuses.Pending, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ticket.EffectiveLane, laneId, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        public static RoutingTicket ConfirmOldestForLane(LotSession lot, string laneId, string timestamp)
        {
            if (lot == null || string.IsNullOrWhiteSpace(laneId))
            {
                return null;
            }

            var ledger = Ensure(lot);
            foreach (var ticket in ledger.Tickets)
            {
                if (ticket != null &&
                    string.Equals(ticket.Status, RoutingTicketStatuses.Pending, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ticket.EffectiveLane, laneId, StringComparison.OrdinalIgnoreCase))
                {
                    ticket.Status = RoutingTicketStatuses.Confirmed;
                    ticket.ConfirmationLane = laneId;
                    ticket.ConfirmedAt = timestamp;
                    Trim(lot);
                    return ticket;
                }
            }

            return null;
        }

        public static void Trim(LotSession lot)
        {
            if (lot == null || lot.RoutingLedger == null || lot.RoutingLedger.Tickets == null)
            {
                return;
            }

            var tickets = lot.RoutingLedger.Tickets;
            if (tickets.Count <= MaxTicketRetention)
            {
                return;
            }

            var kept = new List<RoutingTicket>();
            var removable = tickets.Count - MaxTicketRetention;
            foreach (var ticket in tickets)
            {
                if (ticket == null)
                {
                    continue;
                }

                if (removable > 0 &&
                    string.Equals(ticket.Status, RoutingTicketStatuses.Confirmed, StringComparison.OrdinalIgnoreCase))
                {
                    AddToArchiveIfMissing(lot, ticket);
                    removable--;
                    continue;
                }

                kept.Add(ticket);
            }

            while (kept.Count > MaxTicketRetention)
            {
                AddToArchiveIfMissing(lot, kept[0]);
                kept.RemoveAt(0);
            }

            lot.RoutingLedger.Tickets = kept;
            TrimArchive(lot);
        }

        private static void AddToArchiveIfMissing(LotSession lot, RoutingTicket ticket)
        {
            if (lot == null || ticket == null)
            {
                return;
            }

            if (lot.RoutingArchive == null)
            {
                lot.RoutingArchive = new List<RoutingTicket>();
            }

            foreach (var archived in lot.RoutingArchive)
            {
                if (archived != null && archived.Sequence == ticket.Sequence && archived.LotId == ticket.LotId)
                {
                    return;
                }
            }

            lot.RoutingArchive.Add(CopyTicket(ticket));
        }

        private static void TrimArchive(LotSession lot)
        {
            if (lot == null || lot.RoutingArchive == null)
            {
                return;
            }

            while (lot.RoutingArchive.Count > MaxArchiveRetention)
            {
                lot.RoutingArchive.RemoveAt(0);
            }
        }

        private static RoutingTicket CopyTicket(RoutingTicket ticket)
        {
            return new RoutingTicket
            {
                Sequence = ticket.Sequence,
                LotId = ticket.LotId,
                CreatedAt = ticket.CreatedAt,
                Handshake = ticket.Handshake,
                Decision = ticket.Decision,
                IntendedLane = ticket.IntendedLane,
                EffectiveLane = ticket.EffectiveLane,
                ConfirmationLane = ticket.ConfirmationLane,
                Status = ticket.Status,
                ConfirmedAt = ticket.ConfirmedAt,
                Voltage = ticket.Voltage,
                Ir = ticket.Ir,
                RoutingModel = ticket.RoutingModel,
                QualityInterval = ticket.QualityInterval,
                VoltageMin = ticket.VoltageMin,
                VoltageMax = ticket.VoltageMax,
                IrMin = ticket.IrMin,
                IrMax = ticket.IrMax,
                ThresholdSource = ticket.ThresholdSource,
                RejectReason = ticket.RejectReason
            };
        }

        private static string NormalizeDecision(string decision)
        {
            return string.IsNullOrWhiteSpace(decision) ? "NG" : decision.Trim().ToUpperInvariant();
        }

        private static string NormalizeLane(string lane)
        {
            return string.IsNullOrWhiteSpace(lane) ? "NG" : lane.Trim().ToUpperInvariant();
        }
    }
}
