using System;
using System.Collections.Generic;

namespace SortingMachineDesktop
{
    public class MachineConfig
    {
        public int Channels { get; set; }
        public string ComPort { get; set; }
        public int BaudRate { get; set; }
        public int SlaveId { get; set; }
        public int MeasurementRegister { get; set; }
        public int AlarmRegister { get; set; }
        public int HandshakeRegister { get; set; }
        public int StatusRegister { get; set; }
        public bool SwapWords { get; set; }
        public bool IrFirst { get; set; }
        public string ScanComPort { get; set; }
        public int ScanBaudRate { get; set; }
        public string ScanParity { get; set; }
        public bool ScanEnabled { get; set; }
        public double ScanTimeoutS { get; set; }
        public string JudgeMode { get; set; }
        public int ChannelStart { get; set; }
        public int ChannelEnd { get; set; }
        public bool NegativeVoltageToNg { get; set; }
        public string NoBarcodeValue { get; set; }
        public string CellType { get; set; }
        public string SortingMode { get; set; }
        public bool SafeMode { get; set; }
        public bool ObservationOnly { get; set; }
        public bool ShadowMode { get; set; }
        public bool UseSimulator { get; set; }

        public static MachineConfig CreateDefault()
        {
            return new MachineConfig
            {
                Channels = 11,
                ComPort = "COM1",
                BaudRate = 19200,
                SlaveId = 1,
                MeasurementRegister = 8408,
                AlarmRegister = 22808,
                HandshakeRegister = 8230,
                StatusRegister = 8231,
                SwapWords = true,
                IrFirst = true,
                ScanComPort = "COM2",
                ScanBaudRate = 115200,
                ScanParity = "Even",
                ScanEnabled = false,
                ScanTimeoutS = 3.5,
                JudgeMode = "BOTH",
                ChannelStart = 1,
                ChannelEnd = 11,
                NegativeVoltageToNg = false,
                NoBarcodeValue = "NG",
                CellType = "21700",
                SortingMode = SortingModes.IntelligentGoodNg,
                SafeMode = false,
                ObservationOnly = false,
                ShadowMode = true,
                UseSimulator = false
            };
        }
    }

    public class ChannelThreshold
    {
        public double VoltageMin { get; set; }
        public double VoltageMax { get; set; }
        public double IrMin { get; set; }
        public double IrMax { get; set; }
    }

    public class ThresholdSet
    {
        public List<ChannelThreshold> Channels { get; set; }
    }

    public class LegacyRecipe
    {
        public string CellType { get; set; }
        public string JudgeMode { get; set; }
        public int ChannelStart { get; set; }
        public int ChannelEnd { get; set; }
        public bool NegativeVoltageToNg { get; set; }
        public ThresholdSet Thresholds { get; set; }
    }

    public class LaneCapacitySetting
    {
        public string LaneId { get; set; }
        public int Capacity { get; set; }
    }

    public class IntelligentRecipe
    {
        public string CellType { get; set; }
        public int SampleSize { get; set; }
        public double MaxSigmaVoltage { get; set; }
        public double MaxSigmaIr { get; set; }
        public double AcceptanceKVoltage { get; set; }
        public double AcceptanceKIr { get; set; }
        public double MinWindowVoltage { get; set; }
        public double MinWindowIr { get; set; }
        public double MaxWindowVoltage { get; set; }
        public double MaxWindowIr { get; set; }
        public List<string> GoodLanes { get; set; }
        public string LastGoodLane { get; set; }
        public string NgLane { get; set; }
        public List<LaneCapacitySetting> LaneCapacities { get; set; }
        public int LanePreSwitchMargin { get; set; }
        public bool NegativeVoltageToNg { get; set; }
        public int LearningTimeoutCells { get; set; }
    }

    public class SamplePoint
    {
        public double Voltage { get; set; }
        public double Ir { get; set; }
        public string Timestamp { get; set; }
    }

    public class QualityIntervalSnapshot
    {
        public int Index { get; set; }
        public string LaneId { get; set; }
        public double IrMin { get; set; }
        public double IrMax { get; set; }
        public double VoltageMin { get; set; }
        public double VoltageMax { get; set; }
        public int LearningSampleCount { get; set; }
    }

    public class LotReference
    {
        public int Version { get; set; }
        public string CellType { get; set; }
        public int SampleSizeTarget { get; set; }
        public int SampleSizeValid { get; set; }
        public double MeanVoltage { get; set; }
        public double SigmaVoltage { get; set; }
        public double MeanIr { get; set; }
        public double SigmaIr { get; set; }
        public string RoutingModel { get; set; }
        public double VoltageMin { get; set; }
        public double VoltageMax { get; set; }
        public double IrMin { get; set; }
        public double IrMax { get; set; }
        public List<QualityIntervalSnapshot> QualityIntervals { get; set; }
        public string ValidatedAt { get; set; }
        public string Status { get; set; }
    }

    public class LaneState
    {
        public string LaneId { get; set; }
        public string Role { get; set; }
        public string Status { get; set; }
        public int CapacityTarget { get; set; }
        public int CapacityObserved { get; set; }
        public double CapacityConfidence { get; set; }
        public int CountAssigned { get; set; }
        public int MachineCount { get; set; }
        public bool MachineFullSignal { get; set; }
        public string LastSwitchIn { get; set; }
        public string LastSwitchOut { get; set; }
    }

    public class RoutingTicket
    {
        public long Sequence { get; set; }
        public int LotId { get; set; }
        public string CreatedAt { get; set; }
        public int? Handshake { get; set; }
        public string Decision { get; set; }
        public string IntendedLane { get; set; }
        public string EffectiveLane { get; set; }
        public string ConfirmationLane { get; set; }
        public string Status { get; set; }
        public string ConfirmedAt { get; set; }
        public double Voltage { get; set; }
        public double Ir { get; set; }
        public string RoutingModel { get; set; }
        public int? QualityInterval { get; set; }
        public double? VoltageMin { get; set; }
        public double? VoltageMax { get; set; }
        public double? IrMin { get; set; }
        public double? IrMax { get; set; }
        public string ThresholdSource { get; set; }
        public string RejectReason { get; set; }
    }

    public class RoutingLedgerState
    {
        public long NextSequence { get; set; }
        public List<RoutingTicket> Tickets { get; set; }
    }

    public class CellAuditRow
    {
        public long Sequence { get; set; }
        public int LotId { get; set; }
        public string Timestamp { get; set; }
        public int? Handshake { get; set; }
        public string SortingMode { get; set; }
        public string CellType { get; set; }
        public string OdooLotReference { get; set; }
        public string OdooLotName { get; set; }
        public double Voltage { get; set; }
        public double Ir { get; set; }
        public string RoutingModel { get; set; }
        public int? QualityInterval { get; set; }
        public double? VoltageMin { get; set; }
        public double? VoltageMax { get; set; }
        public double? IrMin { get; set; }
        public double? IrMax { get; set; }
        public string Decision { get; set; }
        public string IntendedLane { get; set; }
        public string EffectiveLane { get; set; }
        public string ConfirmationLane { get; set; }
        public string Status { get; set; }
        public string ConfirmedAt { get; set; }
        public bool Mismatch { get; set; }
        public string Result { get; set; }
        public string RejectReason { get; set; }
        public string ThresholdSource { get; set; }
        public string DataQuality { get; set; }
    }

    public class LotSession
    {
        public int Id { get; set; }
        public string StartedAt { get; set; }
        public string ClosedAt { get; set; }
        public bool IsActive { get; set; }
        public bool ShadowOnly { get; set; }
        public string SortingMode { get; set; }
        public string CellType { get; set; }
        public string OdooLotReference { get; set; }
        public string OdooLotName { get; set; }
        public string OdooProductReference { get; set; }
        public string OdooProductName { get; set; }
        public string OdooLinkedAt { get; set; }
        public string OdooNote { get; set; }
        public string OdooLinkSource { get; set; }
        public bool OdooVerified { get; set; }
        public string LearningStatus { get; set; }
        public int TotalCount { get; set; }
        public int GoodCount { get; set; }
        public int NgCount { get; set; }
        public int InvalidMeasurementCount { get; set; }
        public int LearnedCellCount { get; set; }
        public string CurrentGoodLane { get; set; }
        public string NextGoodLane { get; set; }
        public bool PauseRequested { get; set; }
        public string AlertMessage { get; set; }
        public LotReference Reference { get; set; }
        public List<SamplePoint> RecentSample { get; set; }
        public List<LaneState> Lanes { get; set; }
        public List<int> MachineCounterBaselines { get; set; }
        public int MachineNgBaseline { get; set; }
        public RoutingLedgerState RoutingLedger { get; set; }
        public List<RoutingTicket> RoutingArchive { get; set; }
    }

    public class OdooLotCandidate
    {
        public string Reference { get; set; }
        public string Name { get; set; }
        public string ProductReference { get; set; }
        public string ProductName { get; set; }
        public string Source { get; set; }
        public string LastSeen { get; set; }
        public string Quantity { get; set; }
        public bool Verified { get; set; }
        public int Score { get; set; }
    }

    public class LaneCapacityObservation
    {
        public string CellType { get; set; }
        public string LaneId { get; set; }
        public int ObservedCount { get; set; }
        public int Confidence { get; set; }
        public string LastObservedAt { get; set; }
    }

    public class CellMeasurement
    {
        public string Timestamp { get; set; }
        public int? Handshake { get; set; }
        public double Voltage { get; set; }
        public double Ir { get; set; }
        public string Barcode { get; set; }
        public bool ScannerActive { get; set; }
        public bool MachineBlocked { get; set; }
        public bool MeasurementAvailable { get; set; }
        public bool MachineLaneFullSignal { get; set; }
        public bool MachineCountersAuthoritative { get; set; }
        public List<int> ActiveAlarms { get; set; }
    }

    public class CellDecision
    {
        public string SortingMode { get; set; }
        public string Decision { get; set; }
        public string TargetLane { get; set; }
        public string RejectReason { get; set; }
        public string LearningStatus { get; set; }
        public string LegacyChannel { get; set; }
        public string ShadowChannel { get; set; }
        public double? VoltageMin { get; set; }
        public double? VoltageMax { get; set; }
        public double? IrMin { get; set; }
        public double? IrMax { get; set; }
        public string RoutingModel { get; set; }
        public int? QualityInterval { get; set; }
        public bool PauseRequested { get; set; }
        public string AlertMessage { get; set; }
    }

    public class LiveReading
    {
        public double Voltage { get; set; }
        public double Ir { get; set; }
        public string Channel { get; set; }
        public string Result { get; set; }
        public string Barcode { get; set; }
        public string ShadowChannel { get; set; }
        public string ThresholdSource { get; set; }
        public string SortingMode { get; set; }
        public string CellType { get; set; }
        public string TargetLane { get; set; }
        public string RejectReason { get; set; }
        public string LearningStatus { get; set; }
        public int CurrentLotId { get; set; }
        public string CurrentGoodLane { get; set; }
        public string NextGoodLane { get; set; }
        public string ReferenceSummary { get; set; }
    }

    public class AlarmState
    {
        public List<int> ActiveIndices { get; set; }
        public List<string> Labels { get; set; }
    }

    public class CountersState
    {
        public List<int> PerChannel { get; set; }
        public int Total { get; set; }
        public int GoodTotal { get; set; }
        public int NgTotal { get; set; }
        public string Source { get; set; }
        public List<int> ShadowPerChannel { get; set; }
        public int ShadowTotal { get; set; }
        public int ShadowGoodTotal { get; set; }
        public int ShadowNgTotal { get; set; }
    }

    public class MachineSpeedState
    {
        public int Register { get; set; }
        public int? Mode { get; set; }
        public string Label { get; set; }
        public bool Available { get; set; }
        public string Source { get; set; }
    }

    public class ProductionSnapshot
    {
        public string SortingMode { get; set; }
        public string CellType { get; set; }
        public bool LotControlEnabled { get; set; }
        public string LotStatus { get; set; }
        public int CurrentLotId { get; set; }
        public int TotalCount { get; set; }
        public string LearningStatus { get; set; }
        public int SampleCount { get; set; }
        public int SampleTarget { get; set; }
        public double MeanVoltage { get; set; }
        public double SigmaVoltage { get; set; }
        public double MeanIr { get; set; }
        public double SigmaIr { get; set; }
        public string CurrentGoodLane { get; set; }
        public string NextGoodLane { get; set; }
        public string LastGoodLane { get; set; }
        public string OdooLotReference { get; set; }
        public string OdooLotName { get; set; }
        public string OdooProductReference { get; set; }
        public string OdooProductName { get; set; }
        public string OdooLinkSource { get; set; }
        public bool OdooVerified { get; set; }
        public bool PauseRequested { get; set; }
        public string AlertMessage { get; set; }
        public int GoodCount { get; set; }
        public int NgCount { get; set; }
        public List<LaneState> Lanes { get; set; }
        public List<SamplePoint> RecentSample { get; set; }
        public List<HistoryRow> RecentNgCells { get; set; }
        public List<QualityIntervalSnapshot> QualityIntervals { get; set; }
    }

    public class MaintenanceCommandDefinition
    {
        public string Command { get; set; }
        public string Label { get; set; }
        public string Register { get; set; }
        public string Code { get; set; }
        public bool RequiresExpert { get; set; }
        public bool TerrainValidated { get; set; }
        public string Warning { get; set; }
    }

    public class MaintenanceSnapshot
    {
        public List<MaintenanceCommandDefinition> ValidatedCommands { get; set; }
        public List<MaintenanceCommandDefinition> ExpertCommands { get; set; }
    }

    public class AppState
    {
        public bool Connected { get; set; }
        public MachineConfig Config { get; set; }
        public LiveReading Live { get; set; }
        public AlarmState Alarms { get; set; }
        public CountersState Counters { get; set; }
        public MachineSpeedState Speed { get; set; }
        public ProductionSnapshot Production { get; set; }
        public DiagnosticSnapshot Diagnostic { get; set; }
        public MaintenanceSnapshot Maintenance { get; set; }
    }

    public class MachineCommandResult
    {
        public bool Ok { get; set; }
        public string Command { get; set; }
        public string Message { get; set; }
        public bool BlockedBySafety { get; set; }
        public bool Simulated { get; set; }
    }

    public class LotActionResult
    {
        public bool Ok { get; set; }
        public string Action { get; set; }
        public string Message { get; set; }
        public LotSession Lot { get; set; }
    }

    public class MaintenanceCommandResult
    {
        public bool Ok { get; set; }
        public string Command { get; set; }
        public string Message { get; set; }
        public bool RequiresExpert { get; set; }
        public bool BlockedBySafety { get; set; }
        public bool TerrainValidated { get; set; }
        public bool Simulated { get; set; }
        public string Mode { get; set; }
        public string Register { get; set; }
        public string Value { get; set; }
        public string EnableRegister { get; set; }
        public string OutputRegister { get; set; }
        public string StateRegister { get; set; }
        public string StateBefore { get; set; }
        public string StateDuring { get; set; }
        public string StateAfter { get; set; }
    }

    public class RuntimeTraceRow
    {
        public int Id { get; set; }
        public string Timestamp { get; set; }
        public string Category { get; set; }
        public string Action { get; set; }
        public string Status { get; set; }
        public string Source { get; set; }
        public string Register { get; set; }
        public string Value { get; set; }
        public string Detail { get; set; }
    }

    public class HistoryRow
    {
        public int Id { get; set; }
        public string Timestamp { get; set; }
        public string SortingMode { get; set; }
        public string CellType { get; set; }
        public int LotId { get; set; }
        public double Voltage { get; set; }
        public double Ir { get; set; }
        public string LegacyChannel { get; set; }
        public string Channel { get; set; }
        public string Result { get; set; }
        public string RejectReason { get; set; }
        public string LearningStatus { get; set; }
        public string Barcode { get; set; }
        public string ThresholdSource { get; set; }
        public string OdooLotReference { get; set; }
        public string OdooLotName { get; set; }
        public string OdooProductReference { get; set; }
        public string OdooProductName { get; set; }
    }

    public class ObservationRow
    {
        public int Id { get; set; }
        public string Timestamp { get; set; }
        public string Source { get; set; }
        public int? Handshake { get; set; }
        public string SortingMode { get; set; }
        public string CellType { get; set; }
        public double Voltage { get; set; }
        public double Ir { get; set; }
        public string Barcode { get; set; }
        public string LegacyChannel { get; set; }
        public string Channel { get; set; }
        public string Result { get; set; }
        public string RejectReason { get; set; }
        public string LearningStatus { get; set; }
        public string AlarmSummary { get; set; }
    }

    public class ThresholdDifference
    {
        public int Channel { get; set; }
        public string Field { get; set; }
        public double LocalValue { get; set; }
        public double ObservedValue { get; set; }
    }

    public class DiagnosticSnapshot
    {
        public string SourceMode { get; set; }
        public bool ObservationOnly { get; set; }
        public bool ShadowMode { get; set; }
        public int HandshakeRegister { get; set; }
        public int? HandshakeValue { get; set; }
        public string HandshakeChangedAt { get; set; }
        public int StatusRegister { get; set; }
        public int? StatusValue { get; set; }
        public List<ushort> MeasurementRegisters { get; set; }
        public List<ushort> DisplayRegisters { get; set; }
        public List<ushort> AlarmRegisters { get; set; }
        public string ScannerStatus { get; set; }
        public string ScannerParity { get; set; }
        public bool ThresholdsObserved { get; set; }
        public string ThresholdStatus { get; set; }
        public List<ThresholdDifference> ThresholdDifferences { get; set; }
        public int ObservationEventCount { get; set; }
        public List<string> Notes { get; set; }
    }

    public class ContractField
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
    }

    public class ContractDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<ContractField> Fields { get; set; }
    }

    public class ContractBundle
    {
        public List<ContractDefinition> Contracts { get; set; }
        public List<string> Constraints { get; set; }
        public List<string> Defaults { get; set; }
    }

    public class ConfigData
    {
        public MachineConfig Config { get; set; }
        public Dictionary<string, ThresholdSet> Thresholds { get; set; }
        public Dictionary<string, IntelligentRecipe> IntelligentRecipes { get; set; }
    }

    public class BusinessData
    {
        public List<LotSession> Lots { get; set; }
        public List<LaneCapacityObservation> LaneCapacityObservations { get; set; }
    }

    public static class SortingModes
    {
        public const string Legacy = "LEGACY";
        public const string IntelligentGoodNg = "INTELLIGENT_GOOD_NG";
    }

    public static class LearningStatuses
    {
        public const string Idle = "IDLE";
        public const string Learning = "LEARNING";
        public const string Stable = "STABLE";
        public const string Unstable = "UNSTABLE";
    }

    public static class LaneStatuses
    {
        public const string Available = "AVAILABLE";
        public const string NearFull = "NEAR_FULL";
        public const string Full = "FULL";
        public const string Blocked = "BLOCKED";
    }

    public static class RoutingTicketStatuses
    {
        public const string Pending = "PENDING";
        public const string Confirmed = "CONFIRMED";
    }

    public static class RejectReasons
    {
        public const string None = "";
        public const string NegativeVoltage = "NEGATIVE_VOLTAGE";
        public const string LearningInProgress = "LEARNING_IN_PROGRESS";
        public const string LearningSampleInvalid = "LEARNING_SAMPLE_INVALID";
        public const string LearningUnstable = "LEARNING_UNSTABLE";
        public const string OutOfVoltageWindow = "OUT_OF_VOLTAGE_WINDOW";
        public const string OutOfIrWindow = "OUT_OF_IR_WINDOW";
        public const string MachineBlocked = "MACHINE_BLOCKED";
        public const string NoGoodLaneAvailable = "NO_GOOD_LANE_AVAILABLE";
        public const string NoValidMeasurement = "NO_VALID_MEASUREMENT";
        public const string LegacyNoMatch = "LEGACY_NO_MATCH";
        public const string AppliedRoutingNg = "ROUTAGE_MACHINE_NG";
        public const string PhysicalCounterNg = "COMPTEUR_MACHINE_NG";
    }
}
