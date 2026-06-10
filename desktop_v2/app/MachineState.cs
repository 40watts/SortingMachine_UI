using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SortingMachineDesktop
{
    public class MachineState
    {
        private class MeasurementDecodeCandidate
        {
            public double Ir { get; set; }
            public double Voltage { get; set; }
            public string Label { get; set; }
            public bool SwapWords { get; set; }
            public bool IsPlausible { get; set; }
            public string Detail { get; set; }
        }

        private class LaneOccupancySnapshot
        {
            public int Count { get; set; }
            public bool Full { get; set; }
        }

        private class NgPusherReleaseResult
        {
            public bool CommandReleased { get; set; }
            public bool FeedbackReleased { get; set; }
            public bool WriteSent { get; set; }
            public bool WriteOk { get; set; }
            public bool HasResetBefore { get; set; }
            public ushort ResetBefore { get; set; }
            public bool HasResetAfter { get; set; }
            public ushort ResetAfter { get; set; }
            public bool HasFeedbackBefore { get; set; }
            public ushort FeedbackBefore { get; set; }
            public bool HasFeedbackAfter { get; set; }
            public ushort FeedbackAfter { get; set; }
            public bool HasEnable { get; set; }
            public ushort Enable { get; set; }
            public bool HasOutput { get; set; }
            public ushort Output { get; set; }
            public string Message { get; set; }
        }

        private static readonly List<string> AlarmLabels = BuildAlarmLabels();
        private static readonly int[] ThresholdBaseRegisters = { 1188, 1236, 1284, 1332 };
        private const int LiveIrRegister = 8402;
        private const int LiveVoltageRegister = 8404;
        private const int CycleCommandRegister = 5978;
        private const int SpeedModeRegister = 23341;
        private const ushort MachineSwitchesRegister = 640;
        private const ushort StartCycleCode = 31;
        private const ushort StopCycleCode = 29;
        private const ushort PauseCycleCode = 32;
        private const ushort ResetCycleCode = 26;
        private const ushort SaveChannelCode = 59;
        private const ushort ConnectInitCode = 57;
        private const int ResetRequiredStatusCode = 7;
        private const int RunningStatusCode = 1;
        private const ushort PusherResetCommandBaseRegister = 28295;
        private const ushort PusherCylinderStateBaseRegister = 28158;
        private const ushort PusherCylinderEnableBaseRegister = 28414;
        private const ushort PusherCylinderReadbackBaseRegister = 28679;
        private const ushort PusherCylinderOutputBaseRegister = 28926;
        private const ushort PusherActiveValue = 1;
        private const ushort PusherInactiveValue = 0;
        private const ushort PusherResetReleasedValue = 1;
        private const int PusherPreReleaseSettleMs = 90;
        private const int PusherEnableSettleMs = 80;
        private const int PusherPulseMs = 1000;
        private const int MaintenancePusherPulseMs = 1000;
        private const int NgPusherDiagnosticPulseMs = 1500;
        private const int NgPusherReleaseCheckIntervalMs = 2000;
        private const ushort ConveyorForwardRegister = 5981;
        private const ushort ConveyorForwardCode = 1;
        private const int ConveyorForwardPulseMs = 1000;
        private const int ConveyorFineForwardPulseMs = 200;
        private const ushort Y11OutputImageRegister = 3144;
        private const int Y11OutputImageBit = 10;
        private const ushort Y11OutputImageMask = 0x0400;
        private const ushort Y11OutputImageClearMask = 0xFBFF;

        private readonly object _lock = new object();
        private readonly ConfigStore _configStore;
        private readonly BusinessStore _businessStore;
        private readonly HistoryStore _history;
        private readonly ObservationStore _observations;
        private readonly TraceStore _trace;
        private readonly Random _random;
        private readonly ModbusRtuClient _modbus;
        private readonly ScannerClient _scanner;
        private readonly LegacySortingEngine _legacyEngine;
        private readonly IntelligentSortingEngine _intelligentEngine;
        private int _pusherWorkGeneration;
        private bool _operatorStartArmed;
        private DateTime _lastNgPusherReleaseCheck = DateTime.MinValue;
        private DateTime _lastNgAutoReleaseSkippedTrace = DateTime.MinValue;
        private const int CounterBaseRegister = 900;
        private const int TotalCounterRegister = 948;
        private const int NgCounterIndex = 10;
        private const int NgPhysicalPusherLane = NgCounterIndex + 1;
        private const ushort NgPusherResetRegister = PusherResetCommandBaseRegister + NgCounterIndex;
        private const ushort NgPusherEnableRegister = PusherCylinderEnableBaseRegister + NgCounterIndex;
        private const ushort NgPusherReadbackRegister = PusherCylinderReadbackBaseRegister + NgCounterIndex;
        private const ushort NgPusherOutputRegister = PusherCylinderOutputBaseRegister + NgCounterIndex;
        private const int PlcPollIntervalMs = 60;
        private const int SimulatorPollIntervalMs = 250;

        private MachineConfig _config;
        private Dictionary<string, ThresholdSet> _thresholds;
        private Dictionary<string, IntelligentRecipe> _intelligentRecipes;
        private List<LotSession> _lots;
        private List<LaneCapacityObservation> _laneCapacityObservations;
        private LiveReading _live;
        private List<int> _counters;
        private int _total;
        private int _good;
        private int _ng;
        private List<int> _machineCounters;
        private int _machineTotal;
        private int _machineGood;
        private int _machineNg;
        private bool _machineCountersAvailable;
        private bool _lotControlEnabled;
        private List<int> _alarmsActive;
        private bool _connected;
        private DateTime _lastUpdate;
        private DateTime _lastThresholdRead;
        private DateTime _lastCounterRead;
        private DateTime _lastSpeedRead;
        private DateTime _lastSwitchesRead;
        private int? _machineSwitchesValue;
        private bool _constructorConnectInitSent;
        private DateTime _lastEnablesRead;
        private ushort[] _pusherEnablesSnapshot;
        private DateTime _lastAlarmRead;
        private ushort[] _lastAlarmRegisters;
        private int? _lastHandshakeValue;
        private int? _lastRecordedHandshake;
        private DateTime _lastHandshakeChange;
        private ThresholdSet _observedThresholds;
        private DiagnosticSnapshot _diagnostic;
        private int _nextLotId;
        private int _lastLoggedMachineTotal;
        private bool? _lastConnectedLoggedState;
        private string _lastAlarmSignature;
        private string _lastThresholdProgramSignature;
        private string _lastThresholdControlSignature;
        private bool _forceThresholdSync;
        private string _programmedRoutingLaneId;
        private NgPulseDiagnostic _lastNgPulseDiagnostic;
        private bool? _pendingSwapWordsCandidate;
        private int _pendingSwapWordsConfirmations;
        private DateTime _suspendThresholdSyncUntil;
        private bool _lastLaneFullSignalState;
        private DateTime _lastSafetyStopSentAt;
        private int? _machineSpeedMode;
        private bool _machineSpeedAvailable;
        private int? _lastLoggedMachineSpeedMode;

        public MachineState()
        {
            _configStore = new ConfigStore();
            _businessStore = new BusinessStore();
            _history = new HistoryStore();
            _observations = new ObservationStore();
            _trace = new TraceStore();
            _random = new Random();
            _modbus = new ModbusRtuClient();
            _scanner = new ScannerClient();
            _legacyEngine = new LegacySortingEngine();
            _intelligentEngine = new IntelligentSortingEngine();

            var data = _configStore.Load();
            var business = _businessStore.Load();
            _config = data.Config;
            _thresholds = data.Thresholds;
            _intelligentRecipes = data.IntelligentRecipes;
            _lots = business.Lots ?? new List<LotSession>();
            _laneCapacityObservations = business.LaneCapacityObservations ?? new List<LaneCapacityObservation>();

            NormalizeConfig();
            NormalizeRecipes();
            NormalizeLaneCapacityObservations();
            NormalizeLotLaneMetadata();
            PersistConfig();
            PersistBusiness();
            ResetCounters();
            _alarmsActive = new List<int>();
            _diagnostic = CreateEmptyDiagnostic(_config);
            _lastUpdate = DateTime.MinValue;
            _lastThresholdRead = DateTime.MinValue;
            _lastCounterRead = DateTime.MinValue;
            _lastSpeedRead = DateTime.MinValue;
            _lastSwitchesRead = DateTime.MinValue;
            _lastAlarmRead = DateTime.MinValue;
            _lastAlarmRegisters = new ushort[0];
            _lotControlEnabled = false;
            _operatorStartArmed = false;
            MarkRecoveredActiveLotAsWaitingNoLock();
            _lastLoggedMachineTotal = -1;
            _lastConnectedLoggedState = null;
            _lastAlarmSignature = null;
            _lastThresholdProgramSignature = null;
            _lastThresholdControlSignature = null;
            _forceThresholdSync = true;
            _programmedRoutingLaneId = null;
            _lastNgPulseDiagnostic = CreateEmptyNgPulseDiagnostic();
            _suspendThresholdSyncUntil = DateTime.MinValue;
            _lastLaneFullSignalState = false;
            _lastSafetyStopSentAt = DateTime.MinValue;
            _nextLotId = 1;
            foreach (var lot in _lots)
            {
                if (lot.Id >= _nextLotId)
                {
                    _nextLotId = lot.Id + 1;
                }
            }

            _trace.Append(
                "APP",
                "START",
                "OK",
                "LOCAL",
                string.Empty,
                string.Empty,
                "Config=" + _config.ComPort + "/" + _config.BaudRate + "/ID" + _config.SlaveId +
                " HS=" + _config.HandshakeRegister + " MES=" + _config.MeasurementRegister +
                " Mode=" + _config.SortingMode + " Cell=" + _config.CellType + " Sim=" + _config.UseSimulator
            );
            _trace.Append(
                "ROUTING",
                "STATUS",
                "INFO",
                "LOCAL",
                string.Empty,
                string.Empty,
                "Les commandes cycle 5978 et la programmation des seuils 1188..1370 peuvent fonctionner avec un lot local; sans lot Odoo associé, la traçabilité reste locale."
            );
        }

        public MachineConfig GetConfigCopy()
        {
            lock (_lock)
            {
                return CopyConfig(_config);
            }
        }

        public ThresholdSet GetThresholds(string cellType)
        {
            lock (_lock)
            {
                return CopyThresholds(_thresholds[cellType]);
            }
        }

        public Dictionary<string, ThresholdSet> GetAllThresholds()
        {
            lock (_lock)
            {
                return new Dictionary<string, ThresholdSet>
                {
                    { "21700", CopyThresholds(_thresholds["21700"]) },
                    { "18650", CopyThresholds(_thresholds["18650"]) }
                };
            }
        }

        public IntelligentRecipe GetIntelligentRecipe(string cellType)
        {
            lock (_lock)
            {
                return CopyIntelligentRecipe(_intelligentRecipes[cellType]);
            }
        }

        public Dictionary<string, IntelligentRecipe> GetAllIntelligentRecipes()
        {
            lock (_lock)
            {
                return new Dictionary<string, IntelligentRecipe>
                {
                    { "21700", CopyIntelligentRecipe(_intelligentRecipes["21700"]) },
                    { "18650", CopyIntelligentRecipe(_intelligentRecipes["18650"]) }
                };
            }
        }

        public string GetTraceCsvPath()
        {
            return _trace.CsvPath;
        }

        public void SetCellType(string cellType)
        {
            lock (_lock)
            {
                if (_config.CellType == cellType)
                {
                    return;
                }

                CloseActiveLot("Changement type cellule");
                _config.CellType = cellType;
                PersistConfig();
                PersistBusiness();
                _forceThresholdSync = true;
            }
        }

        public void SetSortingMode(string sortingMode)
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(sortingMode))
                {
                    return;
                }

                if (_config.SortingMode == sortingMode)
                {
                    return;
                }

                CloseActiveLot("Changement mode tri");
                _config.SortingMode = sortingMode;
                PersistConfig();
                PersistBusiness();
                _forceThresholdSync = true;
            }
        }

        public void SetUseSimulator(bool value)
        {
            lock (_lock)
            {
                _config.UseSimulator = value;
                PersistConfig();
                _forceThresholdSync = true;
            }
        }

        public void SetScanEnabled(bool value)
        {
            lock (_lock)
            {
                _config.ScanEnabled = value;
                PersistConfig();
            }
        }

        public void ApplySettings(
            string plcCom,
            int plcBaud,
            int slaveId,
            int measurementReg,
            int alarmReg,
            int handshakeReg,
            int statusReg,
            bool swapWords,
            bool irFirst,
            bool useSimulator,
            bool scanEnabled,
            string scanCom,
            int scanBaud,
            string scanParity,
            double scanTimeout,
            string judgeMode,
            int channelStart,
            int channelEnd,
            bool negativeToNg,
            string noBarcodeValue
        )
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(plcCom)) _config.ComPort = plcCom;
                if (plcBaud > 0) _config.BaudRate = plcBaud;
                if (slaveId > 0) _config.SlaveId = slaveId;
                if (measurementReg > 0) _config.MeasurementRegister = measurementReg;
                if (alarmReg > 0) _config.AlarmRegister = alarmReg;
                if (handshakeReg > 0) _config.HandshakeRegister = handshakeReg;
                if (statusReg > 0) _config.StatusRegister = statusReg;
                _config.SwapWords = swapWords;
                _config.IrFirst = irFirst;
                _config.UseSimulator = useSimulator;

                if (!string.IsNullOrWhiteSpace(scanCom)) _config.ScanComPort = scanCom;
                if (scanBaud > 0) _config.ScanBaudRate = scanBaud;
                if (!string.IsNullOrWhiteSpace(scanParity)) _config.ScanParity = scanParity;
                if (scanTimeout > 0) _config.ScanTimeoutS = scanTimeout;
                _config.ScanEnabled = scanEnabled;

                if (!string.IsNullOrWhiteSpace(judgeMode)) _config.JudgeMode = judgeMode;
                if (channelStart > 0) _config.ChannelStart = channelStart;
                if (channelEnd > 0) _config.ChannelEnd = channelEnd;

                _config.NegativeVoltageToNg = false;
                if (!string.IsNullOrWhiteSpace(noBarcodeValue)) _config.NoBarcodeValue = noBarcodeValue;
                _config.SafeMode = false;
                _config.ObservationOnly = false;

                PersistConfig();
                _forceThresholdSync = true;
            }
        }

        public MachineCommandResult ExecuteCycleCommand(string commandName)
        {
            lock (_lock)
            {
                var result = new MachineCommandResult
                {
                    Command = string.IsNullOrWhiteSpace(commandName) ? string.Empty : commandName.Trim().ToUpperInvariant()
                };

                ushort code;
                switch (result.Command)
                {
                    case "RESET":
                        code = ResetCycleCode;
                        break;
                    case "START":
                        code = StartCycleCode;
                        break;
                    case "STOP":
                        code = StopCycleCode;
                        break;
                    case "PAUSE":
                        code = PauseCycleCode;
                        break;
                    default:
                        result.Message = "Commande cycle inconnue.";
                        _trace.Append("COMMAND", "CYCLE", "ERROR", "LOCAL", "5978", string.Empty, result.Message);
                        return result;
                }

                _trace.Append("COMMAND", result.Command, "ATTEMPT", _config.UseSimulator ? "SIMULATEUR" : "UI", "5978", code.ToString(CultureInfo.InvariantCulture), "Demande opérateur");

                if (_config.UseSimulator)
                {
                    ApplyCycleCommandNoLock(result.Command);
                    result.Ok = true;
                    result.Simulated = true;
                    result.Message = "Simulateur actif : commande cycle traitée localement.";
                    _trace.Append("COMMAND", result.Command, "SIMULATED", "SIMULATEUR", "5978", code.ToString(CultureInfo.InvariantCulture), result.Message);
                    return result;
                }

                if (result.Command == "START")
                {
                    var ngRelease = ReleaseNgPusherResetNoLock(_config, "CYCLE_" + result.Command, "UI", true);
                    if (!ngRelease.CommandReleased)
                    {
                        result.BlockedBySafety = true;
                        result.Message = "DÉMARRER bloqué : impossible de libérer la commande reset du vérin NG (28305=1). " + ngRelease.Message;
                        _trace.Append("COMMAND", "START", "BLOCKED", "PLC", NgPusherResetRegister.ToString(CultureInfo.InvariantCulture), ngRelease.HasResetAfter ? ngRelease.ResetAfter.ToString(CultureInfo.InvariantCulture) : string.Empty, result.Message);
                        return result;
                    }
                }

                if (result.Command == "START")
                {
                    var activeLot = GetActiveLotNoLock();
                    if (_config.SortingMode == SortingModes.IntelligentGoodNg)
                    {
                        activeLot = EnsureActiveLotNoLock(_config);
                        activeLot.ShadowOnly = _config.ShadowMode;
                    }

                    if (MachineRequiresResetBeforeStartNoLock(_config))
                    {
                        result.BlockedBySafety = true;
                        result.Message = BuildMachineRearmBlockedMessageNoLock();
                        _trace.Append("COMMAND", "START", "BLOCKED", "PLC", _config.StatusRegister.ToString(CultureInfo.InvariantCulture), ResetRequiredStatusCode.ToString(CultureInfo.InvariantCulture), result.Message);
                        return result;
                    }

                    string handshakeGateMessage;
                    if (!TryPrimeHandshakeGateBeforeStartNoLock(_config, out handshakeGateMessage))
                    {
                        result.BlockedBySafety = true;
                        result.Message = handshakeGateMessage;
                        _trace.Append("COMMAND", "START", "BLOCKED", "PLC", _config.HandshakeRegister.ToString(CultureInfo.InvariantCulture), string.Empty, result.Message);
                        return result;
                    }

                    if (!TryPreloadThresholdsBeforeStartNoLock(_config, activeLot))
                    {
                        result.Message = "DÉMARRER bloqué : échec de programmation des seuils machine avant START.";
                        _trace.Append("COMMAND", "START", "BLOCKED", "LOCAL", "1188..1370", string.Empty, result.Message);
                        return result;
                    }

                    ArmPusherStationsNoLock(_config, "START");
                }

                if (result.Command == "RESET")
                {
                    EnsureStoppedBeforeResetNoLock(_config);
                }

                var success = SendCycleCommandNoLock(_config, code, result.Command);

                if (!success)
                {
                    result.Message = "Échec d’écriture Modbus sur le registre 5978.";
                    _trace.Append("COMMAND", result.Command, "ERROR", "UI", "5978", code.ToString(CultureInfo.InvariantCulture), result.Message);
                    return result;
                }

                _suspendThresholdSyncUntil = DateTime.Now.AddSeconds(result.Command == "RESET" ? 4 : 2);
                ApplyCycleCommandNoLock(result.Command);
                result.Ok = true;
                result.Message = result.Command == "RESET"
                    ? "Réarmement automate envoyé au registre 5978=26. Relancer DÉMARRER si le statut est revenu prêt."
                    : "Commande " + result.Command + " envoyée au registre 5978.";
                _trace.Append("COMMAND", result.Command, "SENT", "UI", "5978", code.ToString(CultureInfo.InvariantCulture), result.Message);
                return result;
            }
        }

        public void UpdateThresholds(string cellType, ThresholdSet set)
        {
            lock (_lock)
            {
                _thresholds[cellType] = CopyThresholds(set);
                PersistConfig();
                _forceThresholdSync = true;
            }
        }

        public void UpdateIntelligentRecipe(string cellType, IntelligentRecipe recipe)
        {
            lock (_lock)
            {
                _intelligentRecipes[cellType] = CopyIntelligentRecipe(recipe);
                NormalizeIntelligentRecipe(_intelligentRecipes[cellType]);
                NormalizeLaneCapacityObservations();
                NormalizeLotLaneMetadata();
                PersistConfig();
                PersistBusiness();
                _forceThresholdSync = true;
            }
        }

        public void UpdateLegacyOptions(string judgeMode, int channelStart, int channelEnd)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(judgeMode))
                {
                    _config.JudgeMode = judgeMode.Trim().ToUpperInvariant();
                }

                if (channelStart > 0)
                {
                    _config.ChannelStart = channelStart;
                }

                if (channelEnd > 0)
                {
                    _config.ChannelEnd = channelEnd;
                }

                if (_config.ChannelStart <= 0)
                {
                    _config.ChannelStart = 1;
                }

                if (_config.ChannelEnd < _config.ChannelStart)
                {
                    _config.ChannelEnd = _config.ChannelStart;
                }

                if (_config.ChannelEnd > _config.Channels)
                {
                    _config.ChannelEnd = _config.Channels;
                }

                PersistConfig();
                _forceThresholdSync = true;
            }
        }

        public void SetLotControlEnabled(bool enabled)
        {
            lock (_lock)
            {
                _lotControlEnabled = enabled;
                if (!enabled)
                {
                    CloseActiveLot("Arrêt lot demandé");
                    PersistBusiness();
                }
            }
        }

        public void Tick()
        {
            MachineConfig cfg;
            ThresholdSet thresholds;
            lock (_lock)
            {
                cfg = CopyConfig(_config);
                thresholds = CopyThresholds(_thresholds[cfg.CellType]);
            }

            var pollIntervalMs = cfg.UseSimulator ? SimulatorPollIntervalMs : PlcPollIntervalMs;
            if ((DateTime.Now - _lastUpdate).TotalMilliseconds < pollIntervalMs)
            {
                return;
            }

            _lastUpdate = DateTime.Now;

            if (cfg.UseSimulator)
            {
                Simulate(cfg, thresholds);
            }
            else
            {
                ReadFromPlc(cfg, thresholds);
            }
        }

        public AppState Snapshot()
        {
            lock (_lock)
            {
                return new AppState
                {
                    Connected = _connected,
                    Config = CopyConfig(_config),
                    Live = _live == null ? null : CopyLive(_live),
                    Alarms = BuildAlarmState(),
                    Counters = BuildCounters(GetDisplayLotNoLock()),
                    Speed = BuildMachineSpeedStateNoLock(),
                    Production = BuildProductionSnapshot(GetDisplayLotNoLock()),
                    Diagnostic = CopyDiagnostic(_diagnostic),
                    Maintenance = BuildMaintenanceSnapshotNoLock()
                };
            }
        }

        public AlarmState GetAlarms()
        {
            lock (_lock)
            {
                return BuildAlarmState();
            }
        }

        public CountersState GetCounters()
        {
            lock (_lock)
            {
                return BuildCounters(GetDisplayLotNoLock());
            }
        }

        public LiveReading GetLive()
        {
            lock (_lock)
            {
                return _live == null ? null : CopyLive(_live);
            }
        }

        public DiagnosticSnapshot GetDiagnostic()
        {
            lock (_lock)
            {
                return CopyDiagnostic(_diagnostic);
            }
        }

        public LotSession GetCurrentLot()
        {
            lock (_lock)
            {
                return CopyLot(GetActiveLotNoLock());
            }
        }

        public List<LotSession> GetLotHistory(int limit)
        {
            lock (_lock)
            {
                var result = new List<LotSession>();
                var start = Math.Max(0, _lots.Count - limit);
                for (var i = start; i < _lots.Count; i++)
                {
                    result.Add(CopyLot(_lots[i]));
                }

                return result;
            }
        }

        public List<OdooLotCandidate> SearchOdooLots(string query, int limit)
        {
            query = NormalizeSearch(query);
            limit = Math.Max(1, Math.Min(50, limit));

            var map = new Dictionary<string, OdooLotCandidate>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                for (var i = _lots.Count - 1; i >= 0; i--)
                {
                    var lot = _lots[i];
                    if (lot == null || !lot.OdooVerified)
                    {
                        continue;
                    }

                    AddOdooCandidate(
                        map,
                        new OdooLotCandidate
                        {
                            Reference = lot.OdooLotReference,
                            Name = lot.OdooLotName,
                            ProductReference = lot.OdooProductReference,
                            ProductName = lot.OdooProductName,
                            LastSeen = string.IsNullOrWhiteSpace(lot.OdooLinkedAt) ? lot.StartedAt : lot.OdooLinkedAt,
                            Source = string.IsNullOrWhiteSpace(lot.OdooLinkSource) ? "Historique Odoo vérifié" : lot.OdooLinkSource,
                            Verified = true
                        },
                        query,
                        1000);
                }

                AddOdooCandidatesFromLocalCsv(map, query);
            }

            AddOdooLiveStockLotCandidates(map, query, limit);
            return SortAndLimitOdooCandidates(map, limit);
        }

        public List<OdooLotCandidate> SearchOdooCells(string query, int limit)
        {
            query = NormalizeSearch(query);
            limit = Math.Max(1, Math.Min(50, limit));

            var map = new Dictionary<string, OdooLotCandidate>(StringComparer.OrdinalIgnoreCase);
            lock (_lock)
            {
                for (var i = _lots.Count - 1; i >= 0; i--)
                {
                    var lot = _lots[i];
                    if (lot == null)
                    {
                        continue;
                    }

                    AddOdooCandidate(
                        map,
                        new OdooLotCandidate
                        {
                            Reference = lot.OdooProductReference,
                            Name = lot.OdooProductName,
                            ProductReference = lot.OdooProductReference,
                            ProductName = lot.OdooProductName,
                            LastSeen = string.IsNullOrWhiteSpace(lot.OdooLinkedAt) ? lot.StartedAt : lot.OdooLinkedAt,
                            Source = "Lot suivi déjà associé"
                        },
                        query,
                        1200);
                }

                AddOdooCellCandidatesFromLocalFiles(map, query);
            }

            AddOdooLiveCellCandidates(map, query, limit);
            return SortAndLimitOdooCandidates(map, limit);
        }

        public bool IsOdooLiveSearchConfigured()
        {
            return !string.IsNullOrWhiteSpace(GetOdooSettings().ApiKey);
        }

        public List<LaneState> GetLaneStates()
        {
            lock (_lock)
            {
                var lot = GetActiveLotNoLock();
                if (lot == null || lot.Lanes == null)
                {
                    return new List<LaneState>();
                }

                var copy = new List<LaneState>();
                foreach (var lane in lot.Lanes)
                {
                    copy.Add(CopyLane(lane));
                }
                return copy;
            }
        }

        public List<HistoryRow> GetHistory(int limit)
        {
            return _history.Latest(limit);
        }

        public List<CellAuditRow> GetCellAuditHistory(int limit)
        {
            lock (_lock)
            {
                var rows = new List<CellAuditRow>();
                if (_lots != null)
                {
                    foreach (var lot in _lots)
                    {
                        AddCellAuditRowsNoLock(lot, rows);
                    }
                }

                rows.Sort(CompareCellAuditRows);
                var safeLimit = Math.Max(1, Math.Min(10000, limit));
                var start = Math.Max(0, rows.Count - safeLimit);
                return rows.GetRange(start, rows.Count - start);
            }
        }

        public string GetCellAuditCsv()
        {
            var rows = GetCellAuditHistory(10000);
            var sb = new StringBuilder();
            sb.AppendLine("sequence,timestamp,confirmed_at,lot_id,odoo_lot,cell_type,handshake,voltage,ir,routing_model,quality_interval,voltage_min,voltage_max,ir_min,ir_max,decision,intended_lane,effective_lane,confirmed_lane,status,data_quality,mismatch,reject_reason,threshold_source");
            foreach (var row in rows)
            {
                sb.Append(row.Sequence.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(EscapeCsv(row.Timestamp)).Append(",");
                sb.Append(EscapeCsv(row.ConfirmedAt)).Append(",");
                sb.Append(row.LotId.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(EscapeCsv(row.OdooLotReference)).Append(",");
                sb.Append(EscapeCsv(row.CellType)).Append(",");
                sb.Append(row.Handshake.HasValue ? row.Handshake.Value.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(",");
                sb.Append(row.Voltage.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(row.Ir.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append(EscapeCsv(row.RoutingModel)).Append(",");
                sb.Append(row.QualityInterval.HasValue ? row.QualityInterval.Value.ToString(CultureInfo.InvariantCulture) : string.Empty).Append(",");
                sb.Append(FormatNullableInvariant(row.VoltageMin)).Append(",");
                sb.Append(FormatNullableInvariant(row.VoltageMax)).Append(",");
                sb.Append(FormatNullableInvariant(row.IrMin)).Append(",");
                sb.Append(FormatNullableInvariant(row.IrMax)).Append(",");
                sb.Append(EscapeCsv(row.Decision)).Append(",");
                sb.Append(EscapeCsv(row.IntendedLane)).Append(",");
                sb.Append(EscapeCsv(row.EffectiveLane)).Append(",");
                sb.Append(EscapeCsv(row.ConfirmationLane)).Append(",");
                sb.Append(EscapeCsv(row.Status)).Append(",");
                sb.Append(EscapeCsv(row.DataQuality)).Append(",");
                sb.Append(row.Mismatch ? "1" : "0").Append(",");
                sb.Append(EscapeCsv(row.RejectReason)).Append(",");
                sb.Append(EscapeCsv(row.ThresholdSource)).AppendLine();
            }

            return sb.ToString();
        }

        public List<HistoryRow> GetRecentNgCells(int limit)
        {
            return _history.LatestNg(limit);
        }

        public List<ObservationRow> GetObservationEvents(int limit)
        {
            return _observations.Latest(limit);
        }

        public string GetHistoryCsvPath()
        {
            return _history.CsvPath;
        }

        public string GetHistoryCompactCsvPath()
        {
            return _history.CompactCsvPath;
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool IsSameOdooLot(LotSession lot, string lotReference, string lotName, string productReference, string productName)
        {
            if (lot == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(lotReference) && !string.IsNullOrWhiteSpace(lot.OdooLotReference))
            {
                return string.Equals(lot.OdooLotReference, lotReference, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(lotName) && !string.IsNullOrWhiteSpace(lot.OdooLotName))
            {
                return string.Equals(lot.OdooLotName, lotName, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(productReference) && !string.IsNullOrWhiteSpace(lot.OdooProductReference))
            {
                return string.Equals(lot.OdooProductReference, productReference, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(productName) && !string.IsNullOrWhiteSpace(lot.OdooProductName))
            {
                return string.Equals(lot.OdooProductName, productName, StringComparison.OrdinalIgnoreCase);
            }

            return string.IsNullOrWhiteSpace(lot.OdooLotReference) &&
                string.IsNullOrWhiteSpace(lot.OdooLotName) &&
                string.IsNullOrWhiteSpace(lot.OdooProductReference) &&
                string.IsNullOrWhiteSpace(lot.OdooProductName);
        }

        private static bool HasOdooLotAssociation(LotSession lot)
        {
            return lot != null &&
                lot.OdooVerified &&
                (!string.IsNullOrWhiteSpace(lot.OdooLotReference) ||
                 !string.IsNullOrWhiteSpace(lot.OdooLotName));
        }

        private OdooLotCandidate ResolveVerifiedOdooLotCandidate(string lotReference, string lotName)
        {
            var query = NormalizeText(lotReference) ?? NormalizeText(lotName);
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            var candidates = SearchOdooLots(query, 25);
            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.Verified)
                {
                    continue;
                }

                if (OdooLotMatchesSelection(candidate, lotReference, lotName))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool OdooLotMatchesSelection(OdooLotCandidate candidate, string lotReference, string lotName)
        {
            if (candidate == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(lotReference) &&
                (string.Equals(candidate.Reference, lotReference, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.Name, lotReference, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(lotName) &&
                (string.Equals(candidate.Reference, lotName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(candidate.Name, lotName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private void AddOdooCandidatesFromLocalCsv(Dictionary<string, OdooLotCandidate> map, string query)
        {
            var dataDir = Path.GetDirectoryName(_history.CompactCsvPath);
            if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
            {
                return;
            }

            var files = new List<string>();
            if (File.Exists(_history.CompactCsvPath))
            {
                files.Add(_history.CompactCsvPath);
            }

            foreach (var file in Directory.GetFiles(dataDir, "*.csv"))
            {
                var name = Path.GetFileName(file).ToLowerInvariant();
                if ((name.Contains("odoo") || name.Contains("lot")) && !files.Contains(file))
                {
                    files.Add(file);
                }
            }

            foreach (var file in files)
            {
                AddOdooCandidatesFromCsv(map, file, query);
            }
        }

        private static void AddOdooCandidatesFromCsv(Dictionary<string, OdooLotCandidate> map, string path, string query)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > 25 * 1024 * 1024)
                {
                    return;
                }

                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    var headerLine = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(headerLine))
                    {
                        return;
                    }

                    var headers = SplitCsvLine(headerLine);
                    var rowCount = 0;
                    string line;
                    while ((line = reader.ReadLine()) != null && rowCount < 10000)
                    {
                        rowCount++;
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var values = SplitCsvLine(line);
                        var reference = FirstCsvValue(headers, values, "odoo_lot", "lot_reference", "lot_ref", "reference", "ref", "of", "ordre", "production", "name", "lot");
                        var name = FirstCsvValue(headers, values, "odoo_lot_name", "lot_name", "display_name", "nom", "libelle", "libellé", "designation", "désignation");
                        var product = FirstCsvValue(headers, values, "odoo_product", "product_reference", "product_ref", "product", "produit", "article", "cellule", "product_name");
                        var date = FirstCsvValue(headers, values, "timestamp", "date", "write_date", "last_seen", "odoo_linked_at");

                        AddOdooCandidate(
                            map,
                            new OdooLotCandidate
                            {
                                Reference = reference,
                                Name = name,
                                ProductReference = product,
                                ProductName = product,
                                LastSeen = date,
                                Source = Path.GetFileName(path),
                                Verified = true
                            },
                            query,
                            path.EndsWith("odoo_cell_tests.csv", StringComparison.OrdinalIgnoreCase) ? 700 : 850);
                    }
                }
            }
            catch
            {
                // A bad operator export must not break machine supervision.
            }
        }

        private void AddOdooCellCandidatesFromLocalFiles(Dictionary<string, OdooLotCandidate> map, string query)
        {
            var dataDir = Path.GetDirectoryName(_history.CompactCsvPath);
            if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(dataDir, "*.csv"))
            {
                var name = Path.GetFileName(file).ToLowerInvariant();
                if (name.Contains("odoo") || name.Contains("cell") || name.Contains("cellule") || name.Contains("product") || name.Contains("produit"))
                {
                    AddOdooCellCandidatesFromCsv(map, file, query);
                }
            }
        }

        private static void AddOdooCellCandidatesFromCsv(Dictionary<string, OdooLotCandidate> map, string path, string query)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > 25 * 1024 * 1024)
                {
                    return;
                }

                using (var reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    var headerLine = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(headerLine))
                    {
                        return;
                    }

                    var headers = SplitCsvLine(headerLine);
                    var rowCount = 0;
                    string line;
                    while ((line = reader.ReadLine()) != null && rowCount < 10000)
                    {
                        rowCount++;
                        var values = SplitCsvLine(line);
                        var id = FirstCsvValue(headers, values, "id", "odoo_id", "product_id", "template_id");
                        var reference = FirstCsvValue(headers, values, "default_code", "code", "reference", "ref", "sku", "odoo_product", "product_reference", "product_ref");
                        var name = FirstCsvValue(headers, values, "display_name", "name", "product", "product_name", "produit", "cellule", "cell", "article");
                        var date = FirstCsvValue(headers, values, "timestamp", "date", "write_date", "last_seen", "odoo_linked_at");

                        if (string.IsNullOrWhiteSpace(reference) && !string.IsNullOrWhiteSpace(id))
                        {
                            reference = "Odoo #" + id;
                        }

                        AddOdooCandidate(
                            map,
                            new OdooLotCandidate
                            {
                                Reference = reference,
                                Name = name,
                                ProductReference = reference,
                                ProductName = name,
                                LastSeen = date,
                                Source = Path.GetFileName(path)
                            },
                            query,
                            path.EndsWith("odoo_cell_tests.csv", StringComparison.OrdinalIgnoreCase) ? 800 : 950);
                    }
                }
            }
            catch
            {
                // A malformed cache should not block production.
            }
        }

        private static List<OdooLotCandidate> SortAndLimitOdooCandidates(Dictionary<string, OdooLotCandidate> map, int limit)
        {
            var result = new List<OdooLotCandidate>(map.Values);
            result.Sort(delegate(OdooLotCandidate a, OdooLotCandidate b)
            {
                var score = b.Score.CompareTo(a.Score);
                if (score != 0)
                {
                    return score;
                }

                return string.Compare(b.LastSeen, a.LastSeen, StringComparison.OrdinalIgnoreCase);
            });

            if (result.Count > limit)
            {
                result.RemoveRange(limit, result.Count - limit);
            }

            return result;
        }

        private static void AddOdooLiveStockLotCandidates(Dictionary<string, OdooLotCandidate> map, string query, int limit)
        {
            var settings = GetOdooSettings();
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                return;
            }

            ConfigureOdooHttp();
            var baseUrl = NormalizeText(settings.Url) ?? "https://40-watts-cycles.odoo.com";
            var terms = new List<string>();
            if (!string.IsNullOrWhiteSpace(query))
            {
                terms.Add(query);
            }
            else
            {
                terms.Add("21700");
                terms.Add("18650");
                terms.Add("cellule");
            }

            foreach (var term in terms)
            {
                AddOdooLiveStockLotCandidatesForTerm(map, baseUrl, settings.ApiKey, "stock.lot", term, query, limit);
                AddOdooLiveStockLotCandidatesForTerm(map, baseUrl, settings.ApiKey, "stock.production.lot", term, query, limit);
                AddOdooLiveBomComponentLotCandidatesForTerm(map, baseUrl, settings.ApiKey, term, query, limit);
            }
        }

        private static void AddOdooLiveStockLotCandidatesForTerm(Dictionary<string, OdooLotCandidate> map, string baseUrl, string apiKey, string model, string term, string query, int limit)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var url = baseUrl.TrimEnd('/') + "/json/2/" + model + "/search_read";
                var payload = new Dictionary<string, object>
                {
                    {
                        "domain",
                        new object[]
                        {
                            "|",
                            new object[] { "name", "ilike", term },
                            new object[] { "product_id", "ilike", term }
                        }
                    },
                    { "fields", new object[] { "id", "name", "product_id", "product_qty", "write_date" } },
                    { "limit", Math.Max(1, Math.Min(30, limit)) }
                };

                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.Authorization] = "bearer " + apiKey;
                    client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    client.Headers[HttpRequestHeader.UserAgent] = "SortingMachineDesktop/2.0";
                    var response = client.UploadString(url, "POST", serializer.Serialize(payload));
                    var rows = serializer.DeserializeObject(response) as object[];
                    if (rows == null)
                    {
                        return;
                    }

                    foreach (var item in rows)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null)
                        {
                            continue;
                        }

                        var id = ReadOdooValue(row, "id");
                        var lotName = ReadOdooValue(row, "name");
                        var productName = ReadOdooMany2OneDisplay(row, "product_id");
                        var quantity = ReadOdooValue(row, "product_qty");
                        if (IsZeroOrNegativeOdooQuantity(quantity))
                        {
                            continue;
                        }

                        var writeDate = ReadOdooValue(row, "write_date");
                        AddOdooCandidate(
                            map,
                            new OdooLotCandidate
                            {
                                Reference = string.IsNullOrWhiteSpace(lotName) ? "Odoo lot #" + id : lotName,
                                Name = string.IsNullOrWhiteSpace(lotName) ? "Lot Odoo #" + id : lotName,
                                ProductReference = productName,
                                ProductName = productName,
                                Source = "Odoo " + model,
                                LastSeen = string.IsNullOrWhiteSpace(writeDate) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : writeDate,
                                Quantity = quantity,
                                Verified = true
                            },
                            query,
                            2600);
                    }
                }
            }
            catch
            {
                // Live Odoo search is optional; local operation must remain available.
            }
        }

        private static void AddOdooLiveBomComponentLotCandidatesForTerm(Dictionary<string, OdooLotCandidate> map, string baseUrl, string apiKey, string term, string query, int limit)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var url = baseUrl.TrimEnd('/') + "/json/2/mrp.bom.line/search_read";
                var payload = new Dictionary<string, object>
                {
                    { "domain", new object[] { new object[] { "product_id", "ilike", term } } },
                    { "fields", new object[] { "product_id" } },
                    { "limit", 40 }
                };

                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.Authorization] = "bearer " + apiKey;
                    client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    client.Headers[HttpRequestHeader.UserAgent] = "SortingMachineDesktop/2.0";
                    var response = client.UploadString(url, "POST", serializer.Serialize(payload));
                    var rows = serializer.DeserializeObject(response) as object[];
                    if (rows == null)
                    {
                        return;
                    }

                    var seenProducts = new HashSet<int>();
                    foreach (var item in rows)
                    {
                        var row = item as Dictionary<string, object>;
                        var productId = ReadOdooMany2OneId(row, "product_id");
                        if (!productId.HasValue || !seenProducts.Add(productId.Value))
                        {
                            continue;
                        }

                        AddOdooLiveStockLotCandidatesForProduct(map, baseUrl, apiKey, productId.Value, query, limit);
                    }
                }
            }
            catch
            {
                // If BoM access is not allowed, direct stock.lot search remains the source.
            }
        }

        private static void AddOdooLiveStockLotCandidatesForProduct(Dictionary<string, OdooLotCandidate> map, string baseUrl, string apiKey, int productId, string query, int limit)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var url = baseUrl.TrimEnd('/') + "/json/2/stock.lot/search_read";
                var payload = new Dictionary<string, object>
                {
                    { "domain", new object[] { new object[] { "product_id", "=", productId } } },
                    { "fields", new object[] { "id", "name", "product_id", "product_qty", "write_date" } },
                    { "limit", Math.Max(1, Math.Min(30, limit)) }
                };

                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.Authorization] = "bearer " + apiKey;
                    client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    client.Headers[HttpRequestHeader.UserAgent] = "SortingMachineDesktop/2.0";
                    var response = client.UploadString(url, "POST", serializer.Serialize(payload));
                    var rows = serializer.DeserializeObject(response) as object[];
                    if (rows == null)
                    {
                        return;
                    }

                    foreach (var item in rows)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null)
                        {
                            continue;
                        }

                        var id = ReadOdooValue(row, "id");
                        var lotName = ReadOdooValue(row, "name");
                        var productName = ReadOdooMany2OneDisplay(row, "product_id");
                        var quantity = ReadOdooValue(row, "product_qty");
                        if (IsZeroOrNegativeOdooQuantity(quantity))
                        {
                            continue;
                        }

                        var writeDate = ReadOdooValue(row, "write_date");
                        AddOdooCandidate(
                            map,
                            new OdooLotCandidate
                            {
                                Reference = string.IsNullOrWhiteSpace(lotName) ? "Odoo lot #" + id : lotName,
                                Name = string.IsNullOrWhiteSpace(lotName) ? "Lot Odoo #" + id : lotName,
                                ProductReference = productName,
                                ProductName = productName,
                                Source = "Odoo stock.lot via nomenclature",
                                LastSeen = string.IsNullOrWhiteSpace(writeDate) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : writeDate,
                                Quantity = quantity,
                                Verified = true
                            },
                            query,
                            2500);
                    }
                }
            }
            catch
            {
                // Product-derived lot lookup is a bonus path, not a hard dependency.
            }
        }

        private static void AddOdooLiveCellCandidates(Dictionary<string, OdooLotCandidate> map, string query, int limit)
        {
            var settings = GetOdooSettings();
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                return;
            }

            ConfigureOdooHttp();
            var baseUrl = NormalizeText(settings.Url) ?? "https://40-watts-cycles.odoo.com";
            var terms = new List<string>();
            if (!string.IsNullOrWhiteSpace(query))
            {
                terms.Add(query);
            }
            else
            {
                terms.Add("cellule");
                terms.Add("21700");
                terms.Add("18650");
            }

            foreach (var term in terms)
            {
                AddOdooLiveCellCandidatesForTerm(map, baseUrl, settings.ApiKey, "product.product", term, query, limit);
                AddOdooLiveCellCandidatesForTerm(map, baseUrl, settings.ApiKey, "product.template", term, query, limit);
            }
        }

        private static void AddOdooLiveCellCandidatesForTerm(Dictionary<string, OdooLotCandidate> map, string baseUrl, string apiKey, string model, string term, string query, int limit)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var url = baseUrl.TrimEnd('/') + "/json/2/" + model + "/search_read";
                var payload = new Dictionary<string, object>
                {
                    { "domain", new object[] { new object[] { "name", "ilike", term } } },
                    { "fields", new object[] { "id", "display_name", "name", "default_code" } },
                    { "limit", Math.Max(1, Math.Min(20, limit)) }
                };

                using (var client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.Authorization] = "bearer " + apiKey;
                    client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                    client.Headers[HttpRequestHeader.UserAgent] = "SortingMachineDesktop/2.0";
                    var response = client.UploadString(url, "POST", serializer.Serialize(payload));
                    var rows = serializer.DeserializeObject(response) as object[];
                    if (rows == null)
                    {
                        return;
                    }

                    foreach (var item in rows)
                    {
                        var row = item as Dictionary<string, object>;
                        if (row == null)
                        {
                            continue;
                        }

                        var id = ReadOdooValue(row, "id");
                        var code = ReadOdooValue(row, "default_code");
                        var display = ReadOdooValue(row, "display_name");
                        var name = ReadOdooValue(row, "name");
                        var reference = string.IsNullOrWhiteSpace(code) ? "Odoo #" + id : code;
                        AddOdooCandidate(
                            map,
                            new OdooLotCandidate
                            {
                                Reference = reference,
                                Name = string.IsNullOrWhiteSpace(display) ? name : display,
                                ProductReference = reference,
                                ProductName = string.IsNullOrWhiteSpace(display) ? name : display,
                                Source = "Odoo " + model,
                                LastSeen = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                            },
                            query,
                            2000);
                    }
                }
            }
            catch
            {
                // Live Odoo search is optional; local operation must remain available.
            }
        }

        private static string ReadOdooValue(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static void ConfigureOdooHttp()
        {
            try
            {
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
                ServicePointManager.Expect100Continue = false;
            }
            catch
            {
                // Older runtimes may not expose the same TLS flags; keep best-effort.
            }
        }

        private static bool IsZeroOrNegativeOdooQuantity(string quantity)
        {
            if (string.IsNullOrWhiteSpace(quantity))
            {
                return false;
            }

            double value;
            return double.TryParse(quantity, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value <= 0.0;
        }

        private static string ReadOdooMany2OneDisplay(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            var items = value as object[];
            if (items != null && items.Length >= 2)
            {
                return Convert.ToString(items[1], CultureInfo.InvariantCulture);
            }

            var list = value as List<object>;
            if (list != null && list.Count >= 2)
            {
                return Convert.ToString(list[1], CultureInfo.InvariantCulture);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int? ReadOdooMany2OneId(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            var items = value as object[];
            if (items != null && items.Length > 0)
            {
                int id;
                if (int.TryParse(Convert.ToString(items[0], CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                {
                    return id;
                }
            }

            int directId;
            if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out directId))
            {
                return directId;
            }

            return null;
        }

        private static OdooConnectionSettings GetOdooSettings()
        {
            return OdooConfigLocator.Load();
        }

        private static void AddOdooCandidate(Dictionary<string, OdooLotCandidate> map, OdooLotCandidate candidate, string query, int baseScore)
        {
            if (candidate == null)
            {
                return;
            }

            candidate.Reference = NormalizeText(candidate.Reference);
            candidate.Name = NormalizeText(candidate.Name);
            candidate.ProductReference = NormalizeText(candidate.ProductReference);
            candidate.ProductName = NormalizeText(candidate.ProductName);
            candidate.LastSeen = NormalizeText(candidate.LastSeen);
            candidate.Quantity = NormalizeText(candidate.Quantity);
            candidate.Source = NormalizeText(candidate.Source) ?? "Local";
            if (!candidate.Verified)
            {
                candidate.Verified =
                    StartsWithIgnoreCase(candidate.Source, "Odoo") ||
                    ContainsIgnoreCase(candidate.Source, "cache") ||
                    ContainsIgnoreCase(candidate.Source, "lot") ||
                    ContainsIgnoreCase(candidate.Source, "historique");
            }

            if (string.IsNullOrWhiteSpace(candidate.Reference) && string.IsNullOrWhiteSpace(candidate.Name))
            {
                return;
            }

            if (!MatchesOdooCandidate(candidate, query))
            {
                return;
            }

            candidate.Score = baseScore + ScoreOdooCandidate(candidate, query);
            var key = BuildOdooCandidateKey(candidate);
            OdooLotCandidate existing;
            if (map.TryGetValue(key, out existing))
            {
                if (candidate.Score > existing.Score)
                {
                    map[key] = candidate;
                }
                else if (string.IsNullOrWhiteSpace(existing.LastSeen) && !string.IsNullOrWhiteSpace(candidate.LastSeen))
                {
                    existing.LastSeen = candidate.LastSeen;
                }

                existing.Verified = existing.Verified || candidate.Verified;
                if (string.IsNullOrWhiteSpace(existing.Quantity) && !string.IsNullOrWhiteSpace(candidate.Quantity))
                {
                    existing.Quantity = candidate.Quantity;
                }

                return;
            }

            map[key] = candidate;
        }

        private static string BuildOdooCandidateKey(OdooLotCandidate candidate)
        {
            var main = string.IsNullOrWhiteSpace(candidate.Reference) ? candidate.Name : candidate.Reference;
            return (main ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool MatchesOdooCandidate(OdooLotCandidate candidate, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return ContainsIgnoreCase(candidate.Reference, query) ||
                ContainsIgnoreCase(candidate.Name, query) ||
                ContainsIgnoreCase(candidate.ProductReference, query) ||
                ContainsIgnoreCase(candidate.ProductName, query);
        }

        private static int ScoreOdooCandidate(OdooLotCandidate candidate, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return 0;
            }

            if (StartsWithIgnoreCase(candidate.Reference, query))
            {
                return 300;
            }

            if (StartsWithIgnoreCase(candidate.Name, query))
            {
                return 240;
            }

            if (ContainsIgnoreCase(candidate.Reference, query))
            {
                return 180;
            }

            if (ContainsIgnoreCase(candidate.Name, query))
            {
                return 150;
            }

            if (ContainsIgnoreCase(candidate.ProductReference, query) || ContainsIgnoreCase(candidate.ProductName, query))
            {
                return 90;
            }

            return 0;
        }

        private static bool ContainsIgnoreCase(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool StartsWithIgnoreCase(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                value.StartsWith(query, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSearch(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string FirstCsvValue(List<string> headers, List<string> values, params string[] names)
        {
            for (var i = 0; i < headers.Count && i < values.Count; i++)
            {
                var header = NormalizeHeader(headers[i]);
                for (var j = 0; j < names.Length; j++)
                {
                    if (header == NormalizeHeader(names[j]))
                    {
                        return NormalizeText(values[i]);
                    }
                }
            }

            return null;
        }

        private static string NormalizeHeader(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "_");
        }

        private static List<string> SplitCsvLine(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }

            values.Add(current.ToString());
            return values;
        }

        public string GetObservationCsvPath()
        {
            return _observations.CsvPath;
        }

        private void AddCellAuditRowsNoLock(LotSession lot, List<CellAuditRow> rows)
        {
            if (lot == null || rows == null)
            {
                return;
            }

            if (lot.RoutingArchive != null)
            {
                foreach (var ticket in lot.RoutingArchive)
                {
                    UpsertCellAuditRowNoLock(lot, ticket, rows);
                }
            }

            if (lot.RoutingLedger != null && lot.RoutingLedger.Tickets != null)
            {
                foreach (var ticket in lot.RoutingLedger.Tickets)
                {
                    UpsertCellAuditRowNoLock(lot, ticket, rows);
                }
            }
        }

        private void UpsertCellAuditRowNoLock(LotSession lot, RoutingTicket ticket, List<CellAuditRow> rows)
        {
            if (lot == null || ticket == null || rows == null)
            {
                return;
            }

            var row = BuildCellAuditRowNoLock(lot, ticket);
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].LotId == row.LotId && rows[i].Sequence == row.Sequence)
                {
                    rows[i] = row;
                    return;
                }
            }

            rows.Add(row);
        }

        private CellAuditRow BuildCellAuditRowNoLock(LotSession lot, RoutingTicket ticket)
        {
            var confirmed = string.Equals(ticket.Status, RoutingTicketStatuses.Confirmed, StringComparison.OrdinalIgnoreCase);
            var effectiveLane = ResolveAppliedRoutingChannelNoLock(ticket.EffectiveLane);
            var confirmationLane = string.IsNullOrWhiteSpace(ticket.ConfirmationLane)
                ? null
                : ResolveAppliedRoutingChannelNoLock(ticket.ConfirmationLane);
            var mismatch = confirmed &&
                !string.IsNullOrWhiteSpace(confirmationLane) &&
                !string.Equals(effectiveLane, confirmationLane, StringComparison.OrdinalIgnoreCase);

            var decision = string.IsNullOrWhiteSpace(ticket.Decision) ? "NG" : ticket.Decision;
            var dataQuality = mismatch ? "MISMATCH" : confirmed ? "CONFIRMED" : "PENDING";
            var routingModel = ticket.RoutingModel;
            var qualityInterval = ticket.QualityInterval;
            var voltageMin = ticket.VoltageMin;
            var voltageMax = ticket.VoltageMax;
            var irMin = ticket.IrMin;
            var irMax = ticket.IrMax;
            var auditRecipe = lot != null &&
                _intelligentRecipes != null &&
                !string.IsNullOrWhiteSpace(lot.CellType) &&
                _intelligentRecipes.ContainsKey(lot.CellType)
                    ? _intelligentRecipes[lot.CellType]
                    : null;
            QualityIntervalAudit.ApplyRoutingContext(
                lot,
                auditRecipe,
                effectiveLane,
                ticket.IntendedLane,
                ref routingModel,
                ref qualityInterval,
                ref voltageMin,
                ref voltageMax,
                ref irMin,
                ref irMax);

            return new CellAuditRow
            {
                Sequence = ticket.Sequence,
                LotId = lot.Id,
                Timestamp = ticket.CreatedAt,
                Handshake = ticket.Handshake,
                SortingMode = lot.SortingMode,
                CellType = lot.CellType,
                OdooLotReference = lot.OdooLotReference,
                OdooLotName = lot.OdooLotName,
                Voltage = Math.Abs(ticket.Voltage),
                Ir = Math.Abs(ticket.Ir),
                RoutingModel = routingModel,
                QualityInterval = qualityInterval,
                VoltageMin = voltageMin,
                VoltageMax = voltageMax,
                IrMin = irMin,
                IrMax = irMax,
                Decision = decision,
                IntendedLane = ResolveAppliedRoutingChannelNoLock(ticket.IntendedLane),
                EffectiveLane = effectiveLane,
                ConfirmationLane = confirmationLane,
                Status = string.IsNullOrWhiteSpace(ticket.Status) ? RoutingTicketStatuses.Pending : ticket.Status,
                ConfirmedAt = ticket.ConfirmedAt,
                Mismatch = mismatch,
                Result = string.Equals(decision, "CON", StringComparison.OrdinalIgnoreCase)
                    ? "CON"
                    : IsGoodRoutingChannelNoLock(effectiveLane) ? "GOOD" : "NG",
                RejectReason = ticket.RejectReason,
                ThresholdSource = ticket.ThresholdSource,
                DataQuality = dataQuality
            };
        }

        private static int CompareCellAuditRows(CellAuditRow left, CellAuditRow right)
        {
            if (left == null && right == null) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            var timestamp = string.Compare(left.Timestamp, right.Timestamp, StringComparison.Ordinal);
            if (timestamp != 0)
            {
                return timestamp;
            }

            var lot = left.LotId.CompareTo(right.LotId);
            if (lot != 0)
            {
                return lot;
            }

            return left.Sequence.CompareTo(right.Sequence);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var escaped = value.Replace("\"", "\"\"");
            if (escaped.IndexOf(',') >= 0 || escaped.IndexOf('\n') >= 0 || escaped.IndexOf('\r') >= 0 || escaped.IndexOf('"') >= 0)
            {
                return "\"" + escaped + "\"";
            }

            return escaped;
        }

        private static string FormatNullableInvariant(double? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        public List<RuntimeTraceRow> GetRuntimeTrace(int limit)
        {
            return _trace.Latest(limit);
        }

        public MaintenanceSnapshot GetMaintenanceSnapshot()
        {
            lock (_lock)
            {
                return BuildMaintenanceSnapshotNoLock();
            }
        }

        public LotActionResult StartNewLot()
        {
            lock (_lock)
            {
                var previousLot = GetActiveLotNoLock() ?? GetDisplayLotNoLock();
                var previousRecipe = TryGetIntelligentRecipeNoLock(_config);
                var preservedOccupancy = CaptureGoodLaneOccupancyNoLock(previousLot, previousRecipe);
                CloseActiveLot("Nouveau lot");
                _lotControlEnabled = true;
                var lot = EnsureActiveLotNoLock(_config);
                lot.ShadowOnly = _config.ShadowMode;
                if (preservedOccupancy.Count > 0)
                {
                    ApplyPreservedLaneOccupancyNoLock(lot, previousRecipe, preservedOccupancy, CurrentTimestamp());
                    lot.AlertMessage = "Nouveau lot créé. Occupation physique conservée: " + DescribePreservedLaneOccupancyNoLock(preservedOccupancy, previousRecipe) + ". Utiliser Bacs vidés seulement après vidage réel.";
                }
                else
                {
                    lot.AlertMessage = "Lot créé. Utiliser DÉMARRER pour lancer le tri.";
                }
                PersistBusiness();
                _forceThresholdSync = true;
                _trace.Append("LOT", "NEW", "OK", "UI", string.Empty, lot.Id.ToString(CultureInfo.InvariantCulture), "Mode=" + lot.SortingMode + " Cell=" + lot.CellType);
                return new LotActionResult
                {
                    Ok = true,
                    Action = "NEW",
                    Message = lot.AlertMessage,
                    Lot = CopyLot(lot)
                };
            }
        }

        public LotActionResult LinkOdooLot(string lotReference, string lotName, string productReference, string productName, string note)
        {
            lotReference = NormalizeText(lotReference);
            lotName = NormalizeText(lotName);
            productReference = NormalizeText(productReference);
            productName = NormalizeText(productName);
            note = NormalizeText(note);

            if (string.IsNullOrWhiteSpace(lotReference) && string.IsNullOrWhiteSpace(lotName))
            {
                return new LotActionResult
                {
                    Ok = false,
                    Action = "ODOO_LINK",
                    Message = "Sélectionner un lot de cellules suivi provenant d’Odoo avant d’associer.",
                    Lot = null
                };
            }

            var selectedLot = ResolveVerifiedOdooLotCandidate(lotReference, lotName);
            if (selectedLot == null)
            {
                _trace.Append("ODOO", "LOT_LINK", "BLOCKED", "UI", string.Empty, lotReference ?? lotName, "Lot introuvable dans Odoo/cache local ; association refusée.");
                return new LotActionResult
                {
                    Ok = false,
                    Action = "ODOO_LINK",
                    Message = "Lot non trouvé dans Odoo ou dans le cache local. Configure la connexion Odoo ou sélectionne un lot proposé.",
                    Lot = null
                };
            }

            lotReference = NormalizeText(selectedLot.Reference) ?? lotReference;
            lotName = NormalizeText(selectedLot.Name) ?? lotName;
            productReference = NormalizeText(selectedLot.ProductReference) ?? productReference;
            productName = NormalizeText(selectedLot.ProductName) ?? productName;

            lock (_lock)
            {
                var active = GetActiveLotNoLock();
                if (active != null &&
                    active.TotalCount > 0 &&
                    !IsSameOdooLot(active, lotReference, lotName, productReference, productName))
                {
                    CloseActiveLot("Changement association Odoo");
                    active = null;
                }

                if (active == null)
                {
                    _lotControlEnabled = true;
                    active = EnsureActiveLotNoLock(_config);
                }

                active.OdooLotReference = lotReference;
                active.OdooLotName = lotName;
                active.OdooProductReference = productReference;
                active.OdooProductName = productName;
                active.OdooNote = note;
                active.OdooLinkSource = selectedLot.Source;
                active.OdooVerified = true;
                active.OdooLinkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                active.ShadowOnly = _config.ShadowMode;
                active.AlertMessage = "Lot de cellules Odoo associé. Utiliser DÉMARRER pour lancer le tri.";
                active.IsActive = true;
                active.ClosedAt = null;

                PersistBusiness();
                _forceThresholdSync = true;
                _trace.Append(
                    "ODOO",
                    "LOT_LINK",
                    "OK",
                    "UI",
                    string.Empty,
                    lotReference,
                    "Lot local #" + active.Id.ToString(CultureInfo.InvariantCulture) + " associé au lot Odoo suivi depuis " + selectedLot.Source + "."
                );

                return new LotActionResult
                {
                    Ok = true,
                    Action = "ODOO_LINK",
                    Message = "Lot de cellules Odoo associé au lot local #" + active.Id.ToString(CultureInfo.InvariantCulture) + ".",
                    Lot = CopyLot(active)
                };
            }
        }

        public LotActionResult ContinueLot()
        {
            lock (_lock)
            {
                _lotControlEnabled = true;
                var active = GetActiveLotNoLock();
                if (active != null)
                {
                    active.AlertMessage = "Lot chargé. Utiliser DÉMARRER pour lancer le tri.";
                    PersistBusiness();
                    _trace.Append("LOT", "CONTINUE", "OK", "UI", string.Empty, active.Id.ToString(CultureInfo.InvariantCulture), "Lot déjà actif");
                    return new LotActionResult
                    {
                        Ok = true,
                        Action = "CONTINUE",
                        Message = "Lot chargé. Utiliser DÉMARRER pour lancer le tri.",
                        Lot = CopyLot(active)
                    };
                }

                for (var i = _lots.Count - 1; i >= 0; i--)
                {
                    var candidate = _lots[i];
                    if (string.Equals(candidate.CellType, _config.CellType, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(candidate.SortingMode, _config.SortingMode, StringComparison.OrdinalIgnoreCase))
                    {
                        candidate.IsActive = true;
                        candidate.ClosedAt = null;
                        candidate.AlertMessage = "Lot chargé. Utiliser DÉMARRER pour lancer le tri.";
                        candidate.ShadowOnly = _config.ShadowMode;
                        PersistBusiness();
                        _forceThresholdSync = true;
                        _trace.Append("LOT", "CONTINUE", "OK", "UI", string.Empty, candidate.Id.ToString(CultureInfo.InvariantCulture), "Lot historique repris");
                        return new LotActionResult
                        {
                            Ok = true,
                            Action = "CONTINUE",
                            Message = "Dernier lot compatible chargé. Utiliser DÉMARRER pour lancer le tri.",
                            Lot = CopyLot(candidate)
                        };
                    }
                }

                _trace.Append("LOT", "CONTINUE", "ERROR", "UI", string.Empty, string.Empty, "Aucun lot compatible à reprendre");
                return new LotActionResult
                {
                    Ok = false,
                    Action = "CONTINUE",
                    Message = "Aucun lot compatible à reprendre.",
                    Lot = null
                };
            }
        }

        public LotActionResult CloseCurrentLot()
        {
            lock (_lock)
            {
                var active = GetActiveLotNoLock();
                if (active == null)
                {
                    return new LotActionResult
                    {
                        Ok = false,
                        Action = "CLOSE",
                        Message = "Aucun lot actif à clôturer.",
                        Lot = null
                    };
                }

                CloseActiveLot("Lot clôturé opérateur");
                _lotControlEnabled = false;
                PersistBusiness();
                _trace.Append("LOT", "CLOSE", "OK", "UI", string.Empty, active.Id.ToString(CultureInfo.InvariantCulture), "Lot clôturé opérateur");
                return new LotActionResult
                {
                    Ok = true,
                    Action = "CLOSE",
                    Message = "Lot clôturé.",
                    Lot = CopyLot(active)
                };
            }
        }

        public LotActionResult ResetCurrentLotLines()
        {
            lock (_lock)
            {
                var lot = GetActiveLotNoLock() ?? GetDisplayLotNoLock();
                if (lot == null)
                {
                    _lotControlEnabled = true;
                    lot = EnsureActiveLotNoLock(_config);
                }

                ResetLotLinesNoLock(lot, "Apprentissage du lot remis à zéro. Association Odoo conservée.", false);
                _lotControlEnabled = true;
                PersistBusiness();
                _forceThresholdSync = true;
                _lastThresholdProgramSignature = null;
                _lastThresholdControlSignature = null;
                _trace.Append(
                    "LOT",
                    "RESET_LINES",
                    "OK",
                    "UI",
                    string.Empty,
                    lot.Id.ToString(CultureInfo.InvariantCulture),
                    "Lignes et apprentissage réinitialisés sans action machine; occupation physique conservée si présente."
                );

                return new LotActionResult
                {
                    Ok = true,
                    Action = "RESET_LINES",
                    Message = lot.AlertMessage,
                    Lot = CopyLot(lot)
                };
            }
        }

        public LotActionResult ConfirmCurrentLotLinesEmptied()
        {
            lock (_lock)
            {
                var lot = GetActiveLotNoLock() ?? GetDisplayLotNoLock();
                if (lot == null)
                {
                    _lotControlEnabled = true;
                    lot = EnsureActiveLotNoLock(_config);
                }

                ResetLotLinesNoLock(lot, "Bacs vidés confirmés. Lignes remises à zéro logiciel.", true);
                _lotControlEnabled = true;
                PersistBusiness();
                _forceThresholdSync = true;
                _lastThresholdProgramSignature = null;
                _lastThresholdControlSignature = null;
                _trace.Append(
                    "LOT",
                    "CONFIRM_EMPTY_LINES",
                    "OK",
                    "UI",
                    string.Empty,
                    lot.Id.ToString(CultureInfo.InvariantCulture),
                    "Occupation physique effacée après confirmation opérateur."
                );

                return new LotActionResult
                {
                    Ok = true,
                    Action = "CONFIRM_EMPTY_LINES",
                    Message = "Bacs vidés confirmés. Les lignes repartent à zéro.",
                    Lot = CopyLot(lot)
                };
            }
        }

        public MaintenanceCommandResult ExecuteMaintenanceCommand(string commandName)
        {
            lock (_lock)
            {
                var normalized = string.IsNullOrWhiteSpace(commandName)
                    ? string.Empty
                    : commandName.Trim().ToUpperInvariant();

                if (normalized == "START_RAW")
                {
                    _trace.Append("MAINTENANCE", normalized, "ATTEMPT", _config.UseSimulator ? "SIMULATEUR" : "UI", "5978", StartCycleCode.ToString(CultureInfo.InvariantCulture), "Démarrage constructeur brut : écriture 5978=31 uniquement, sans seuils, sans lot, sans reset, sans piston.");
                    if (_config.UseSimulator)
                    {
                        return new MaintenanceCommandResult
                        {
                            Ok = true,
                            Command = normalized,
                            Message = "Simulateur actif : démarrage constructeur brut tracé seulement.",
                            RequiresExpert = true,
                            TerrainValidated = false,
                            Simulated = true
                        };
                    }

                    var rawStartMessage = "Démarrage brut constructeur bloqué en réel : utiliser DÉMARRER standard pour lire 8230, précharger 1188..1370, puis envoyer 5978=31.";
                    _trace.Append("MAINTENANCE", normalized, "BLOCKED", "LOCAL", "5978", StartCycleCode.ToString(CultureInfo.InvariantCulture), rawStartMessage);
                    return new MaintenanceCommandResult
                    {
                        Ok = false,
                        Command = normalized,
                        Message = rawStartMessage,
                        RequiresExpert = true,
                        BlockedBySafety = true,
                        TerrainValidated = false,
                        Simulated = false
                    };
                }

                if (normalized == "START" || normalized == "STOP" || normalized == "PAUSE" || normalized == "RESET")
                {
                    var cycleResult = ExecuteCycleCommand(normalized);
                    return new MaintenanceCommandResult
                    {
                        Ok = cycleResult.Ok,
                        Command = cycleResult.Command,
                        Message = cycleResult.Message,
                        RequiresExpert = false,
                        TerrainValidated = true,
                        Simulated = cycleResult.Simulated
                    };
                }

                if (normalized == "RELEASE_NG_PISTON" || normalized == "RETRACT_NG" || normalized == "LIBERER_NG")
                {
                    return ExecuteNgPusherReleaseNoLock(normalized);
                }

                if (normalized == "PUSHER_STATIONS_ENABLE")
                {
                    return ExecutePusherStationsEnableNoLock(normalized);
                }

                if (normalized == "NG_STATION_ENABLE")
                {
                    return ExecuteNgStationEnableNoLock(normalized, true);
                }

                if (normalized == "NG_STATION_DISABLE")
                {
                    return ExecuteNgStationEnableNoLock(normalized, false);
                }

                if (normalized == "DIAG_PULSE_NG" || normalized == "PULSE_NG_DIAGNOSTIC")
                {
                    return ExecuteNgPusherDiagnosticPulseNoLock(normalized);
                }

                if (normalized == "Y11_OUTPUT_OFF" || normalized == "Y11_OFF" || normalized == "DIAG_Y11_OFF")
                {
                    return ExecuteY11OutputBitNoLock(normalized, false);
                }

                if (normalized == "Y11_OUTPUT_ON" || normalized == "Y11_ON" || normalized == "DIAG_Y11_ON")
                {
                    return ExecuteY11OutputBitNoLock(normalized, true);
                }


                if (normalized == "RESTORE_PISTONS_AUTO")
                {
                    _trace.Append("MAINTENANCE", normalized, "BLOCKED", "LOCAL", "28414..28424/28926..28936", string.Empty, "Commande supprimée : TriCell Pilot ne force plus les sorties piston à 0 ou à 1.");
                    return new MaintenanceCommandResult
                    {
                        Ok = false,
                        Command = normalized,
                        Message = "Commande supprimée : aucun forçage global piston n'est envoyé. Le routage production passe par les seuils machine et l'automate.",
                        RequiresExpert = false,
                        TerrainValidated = false,
                        Simulated = _config.UseSimulator
                    };
                }

                if (normalized == "CONVEYOR_FINE_FORWARD")
                {
                    return ExecuteConveyorOnlyForwardNoLock(
                        normalized,
                        ConveyorFineForwardPulseMs,
                        "Micro-ajustement tapis : impulsion courte du coil convoyeur constructeur.");
                }

                if (normalized == "CONVEYOR_ONLY_FORWARD" || normalized == "FORWARD")
                {
                    return ExecuteConveyorOnlyForwardNoLock(
                        normalized,
                        ConveyorForwardPulseMs,
                        "Dégagement convoyeur : impulsion du coil convoyeur constructeur.");
                }

                if (normalized == "BACKWARD" || normalized == "STEP" || normalized == "CLEAN" || normalized == "MANUAL_TEST")
                {
                    _trace.Append("MAINTENANCE", normalized, "BLOCKED", "LOCAL", string.Empty, string.Empty, "Commande brute désactivée : utiliser uniquement Avancer convoyeur.");
                    return new MaintenanceCommandResult
                    {
                        Ok = false,
                        Command = normalized,
                        Message = "Commande brute désactivée. Utiliser Avancer convoyeur (coil constructeur 1X 5981).",
                        RequiresExpert = false,
                        TerrainValidated = false,
                        Simulated = _config.UseSimulator
                    };
                }

                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = normalized,
                    Message = "Commande maintenance inconnue.",
                    RequiresExpert = false,
                    TerrainValidated = false,
                    Simulated = _config.UseSimulator
                };
            }
        }

        private MaintenanceCommandResult ExecutePusherStationsEnableNoLock(string commandName)
        {
            // Etat production constructeur: chaque station piston a un enable (28414+i) qui doit
            // etre a 1 pour que le PLC puisse tirer le piston. Les nettoyages logiciels de mai
            // les ont remis a 0 (constate par lecture directe le 10 juin 2026: seules les voies
            // armees tirent). On arme les enables 1..10 + NG, un par un, sans toucher les sorties.
            if (_config.UseSimulator)
            {
                _trace.Append("MAINTENANCE", commandName, "SIMULATED", "SIMULATEUR", "28414..28424", "1", "Simulateur actif : armement stations pistons tracé seulement.");
                return new MaintenanceCommandResult
                {
                    Ok = true,
                    Command = commandName,
                    Message = "Simulateur actif : armement stations pistons tracé seulement.",
                    RequiresExpert = false,
                    TerrainValidated = false,
                    Simulated = true,
                    Mode = "PUSHER_STATIONS_ENABLE",
                    Register = "28414..28424",
                    Value = "1"
                };
            }

            if (IsPistonMaintenanceBlockedByRunStateNoLock())
            {
                var runBlock = "Armement stations pistons bloqué pendant le cycle. Envoyer STOP ou PAUSE d'abord.";
                _trace.Append("MAINTENANCE", commandName, "BLOCKED_RUN", "LOCAL", "28414..28424", string.Empty, runBlock);
                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = commandName,
                    Message = runBlock,
                    RequiresExpert = false,
                    BlockedBySafety = true,
                    TerrainValidated = false,
                    Simulated = false,
                    Mode = "PUSHER_STATIONS_ENABLE",
                    Register = "28414..28424"
                };
            }

            var armed = new List<string>();
            var failed = new List<string>();
            for (var index = 0; index <= NgCounterIndex; index++)
            {
                var enableRegister = (ushort)(PusherCylinderEnableBaseRegister + index);
                var laneLabel = index == NgCounterIndex ? "NG" : (index + 1).ToString(CultureInfo.InvariantCulture);
                var writeOk = WritePistonIoMaintenanceSingleNoLock(_config, enableRegister, PusherActiveValue);
                Thread.Sleep(60);
                ushort after;
                var hasAfter = TryReadHoldingSingleNoLock(_config, enableRegister, out after);
                if (writeOk && hasAfter && after != 0)
                {
                    armed.Add(laneLabel);
                }
                else
                {
                    failed.Add(laneLabel + "(" + (hasAfter ? after.ToString(CultureInfo.InvariantCulture) : "?") + ")");
                }
            }

            var ok = failed.Count == 0;
            var detail = "Stations armées (enable=1, maintenu, sorties jamais écrites): " +
                         (armed.Count > 0 ? string.Join(",", armed.ToArray()) : "aucune") +
                         (failed.Count > 0 ? " ; échecs: " + string.Join(",", failed.ToArray()) : "") + ".";
            _trace.Append("MAINTENANCE", commandName, ok ? "SENT" : "ERROR", "UI", "28414..28424", "1", detail);

            return new MaintenanceCommandResult
            {
                Ok = ok,
                Command = commandName,
                Message = ok
                    ? "Toutes les stations pistons sont armées (lignes 1..10 + NG). Lancer RÉARMER puis DÉMARRER : les pistons peuvent maintenant tirer selon les seuils."
                    : "Armement incomplet. " + detail,
                RequiresExpert = false,
                TerrainValidated = false,
                Simulated = false,
                Mode = "PUSHER_STATIONS_ENABLE",
                Register = "28414..28424",
                Value = "1",
                StateAfter = detail
            };
        }

        private MaintenanceCommandResult ExecuteNgStationEnableNoLock(string commandName, bool enable)
        {
            // Hypothese constructeur: l'enable 28424 (ligne NG de la page I/O chinoise) arme la
            // station NG pour que le PLC batte le poussoir a chaque avance, meme a vide.
            // On ecrit UNIQUEMENT l'enable, jamais la sortie 28936, et on le LAISSE en place.
            var targetLabel = enable ? "1" : "0";
            if (_config.UseSimulator)
            {
                _trace.Append("MAINTENANCE", commandName, "SIMULATED", "SIMULATEUR", NgPusherEnableRegister.ToString(CultureInfo.InvariantCulture), targetLabel, "Simulateur actif : enable station NG tracé seulement.");
                return new MaintenanceCommandResult
                {
                    Ok = true,
                    Command = commandName,
                    Message = "Simulateur actif : enable station NG = " + targetLabel + " tracé seulement.",
                    RequiresExpert = false,
                    TerrainValidated = false,
                    Simulated = true,
                    Mode = "NG_STATION_ENABLE",
                    Register = NgPusherEnableRegister.ToString(CultureInfo.InvariantCulture),
                    Value = targetLabel
                };
            }

            ushort before;
            var hasBefore = TryReadHoldingSingleNoLock(_config, NgPusherEnableRegister, out before);
            var writeOk = WritePistonIoMaintenanceSingleNoLock(_config, NgPusherEnableRegister, enable ? PusherActiveValue : (ushort)0);
            Thread.Sleep(150);
            ushort after;
            var hasAfter = TryReadHoldingSingleNoLock(_config, NgPusherEnableRegister, out after);
            var ok = writeOk && hasAfter && (after != 0) == enable;

            var detail = "Enable station NG 28424: avant=" + (hasBefore ? before.ToString(CultureInfo.InvariantCulture) : "--") +
                         " cible=" + targetLabel +
                         " relu=" + (hasAfter ? after.ToString(CultureInfo.InvariantCulture) : "--") +
                         ". La sortie 28936 n'est pas écrite; l'enable reste en place (pas d'impulsion).";
            _trace.Append("MAINTENANCE", commandName, ok ? "SENT" : "ERROR", "UI", NgPusherEnableRegister.ToString(CultureInfo.InvariantCulture), targetLabel, detail);

            return new MaintenanceCommandResult
            {
                Ok = ok,
                Command = commandName,
                Message = ok
                    ? (enable
                        ? "Station NG armée (enable 28424=1, maintenu). Lancer RÉARMER puis DÉMARRER et observer le battement du poussoir NG à chaque avance."
                        : "Station NG désarmée (enable 28424=0).")
                    : "Échec écriture enable station NG. " + detail,
                RequiresExpert = false,
                TerrainValidated = false,
                Simulated = false,
                Mode = "NG_STATION_ENABLE",
                Register = NgPusherEnableRegister.ToString(CultureInfo.InvariantCulture),
                Value = targetLabel,
                StateBefore = hasBefore ? before.ToString(CultureInfo.InvariantCulture) : null,
                StateAfter = hasAfter ? after.ToString(CultureInfo.InvariantCulture) : null
            };
        }

        private MaintenanceCommandResult ExecuteNgPusherReleaseNoLock(string commandName)
        {
            if (_config.UseSimulator)
            {
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "SIMULATED",
                    "SIMULATEUR",
                    NgPusherResetRegister.ToString(CultureInfo.InvariantCulture),
                    PusherResetReleasedValue.ToString(CultureInfo.InvariantCulture),
                    "Simulateur actif : libération reset NG tracée seulement."
                );
                return new MaintenanceCommandResult
                {
                    Ok = true,
                    Command = commandName,
                    Message = "Simulateur actif : libération reset NG tracée seulement.",
                    RequiresExpert = false,
                    TerrainValidated = true,
                    Simulated = true,
                    Mode = "NG_RESET_RELEASE",
                    Register = NgPusherResetRegister.ToString(CultureInfo.InvariantCulture),
                    Value = PusherResetReleasedValue.ToString(CultureInfo.InvariantCulture),
                    StateRegister = NgPusherReadbackRegister.ToString(CultureInfo.InvariantCulture)
                };
            }

            var release = ReleaseNgPusherResetNoLock(_config, commandName, "UI", true);
            var releaseMessage = release.Message + " Aucun enable/sortie NG 28424/28936 n'a été écrit.";
            return new MaintenanceCommandResult
            {
                Ok = release.CommandReleased,
                Command = commandName,
                Message = releaseMessage,
                RequiresExpert = false,
                TerrainValidated = release.FeedbackReleased,
                Simulated = false,
                Mode = "NG_RESET_RELEASE",
                Register = NgPusherResetRegister.ToString(CultureInfo.InvariantCulture),
                Value = "reset=" + PusherResetReleasedValue.ToString(CultureInfo.InvariantCulture),
                StateRegister = NgPusherReadbackRegister.ToString(CultureInfo.InvariantCulture),
                StateBefore = release.HasFeedbackBefore ? release.FeedbackBefore.ToString(CultureInfo.InvariantCulture) : null,
                StateAfter = release.HasFeedbackAfter ? release.FeedbackAfter.ToString(CultureInfo.InvariantCulture) : null
            };
        }

        private MaintenanceCommandResult ExecuteNgPusherDiagnosticPulseNoLock(string commandName)
        {
            if (_operatorStartArmed || _lotControlEnabled)
            {
                var runBlockMessage = "Diagnostic NG bloqué pendant le cycle. Envoyer STOP ou PAUSE, attendre l'arrêt des tops 8230, puis relancer le diagnostic NG.";
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "BLOCKED_RUN",
                    "LOCAL",
                    _config.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                    _lastRecordedHandshake.HasValue ? _lastRecordedHandshake.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    runBlockMessage
                );
                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = commandName,
                    Message = runBlockMessage,
                    RequiresExpert = false,
                    BlockedBySafety = true,
                    TerrainValidated = true,
                    Simulated = _config.UseSimulator,
                    Mode = "NG_OUTPUT_DIAGNOSTIC",
                    OutputRegister = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    StateRegister = NgPusherReadbackRegister.ToString(CultureInfo.InvariantCulture)
                };
            }

            var safetyBlock = _config.UseSimulator ? null : BuildPistonSafetyBlockMessageNoLock();
            if (!string.IsNullOrWhiteSpace(safetyBlock))
            {
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "BLOCKED",
                    "PLC",
                    _config.AlarmRegister.ToString(CultureInfo.InvariantCulture),
                    BuildAlarmSummary(_alarmsActive),
                    "Diagnostic NG non envoyé: " + safetyBlock
                );
                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = commandName,
                    Message = safetyBlock,
                    RequiresExpert = false,
                    BlockedBySafety = true,
                    TerrainValidated = true,
                    Simulated = _config.UseSimulator,
                    Mode = "NG_OUTPUT_DIAGNOSTIC",
                    OutputRegister = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    StateRegister = NgPusherReadbackRegister.ToString(CultureInfo.InvariantCulture)
                };
            }

            _trace.Append(
                "MAINTENANCE",
                commandName,
                _config.UseSimulator ? "SIMULATED" : "ATTEMPT",
                _config.UseSimulator ? "SIMULATEUR" : "UI",
                Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=1",
                "Diagnostic sortie carte Y11: 4X 3144 bit 10 ON " +
                NgPusherDiagnosticPulseMs.ToString(CultureInfo.InvariantCulture) +
                " ms puis OFF. Ce n'est pas le verin NG; aucun piston ni convoyeur n'est ecrit."
            );

            if (_config.UseSimulator)
            {
                return new MaintenanceCommandResult
                {
                    Ok = true,
                    Command = commandName,
                    Message = "Simulateur actif : diagnostic NG tracé seulement.",
                    RequiresExpert = false,
                    TerrainValidated = false,
                    Simulated = true,
                    Mode = "NG_OUTPUT_DIAGNOSTIC",
                    OutputRegister = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    StateRegister = NgPusherReadbackRegister.ToString(CultureInfo.InvariantCulture)
                };
            }

            CancelScheduledPusherWorkNoLock(commandName);

            ushort stateBefore;
            ushort stateAfter;
            var hasStateBefore = TryReadHoldingSingleNoLock(_config, NgPusherReadbackRegister, out stateBefore);
            string y11Detail;
            var ok = PulseY11OutputBitNoLock(
                _config,
                "MAINTENANCE",
                commandName,
                "UI",
                "Diagnostic NG terrain valide",
                NgPusherDiagnosticPulseMs,
                out y11Detail);
            var hasStateAfter = TryReadHoldingSingleNoLock(_config, NgPusherReadbackRegister, out stateAfter);
            var stateDetail = "Retour repos 28689: avant=" +
                (hasStateBefore ? stateBefore.ToString(CultureInfo.InvariantCulture) : "--") +
                " apres=" +
                (hasStateAfter ? stateAfter.ToString(CultureInfo.InvariantCulture) : "--") +
                ". " + y11Detail;

            _trace.Append(
                "MAINTENANCE",
                commandName,
                ok ? "SENT" : "ERROR",
                "UI",
                Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=1",
                stateDetail
            );

            return new MaintenanceCommandResult
            {
                Ok = ok,
                Command = commandName,
                Message = ok
                    ? "Diagnostic Y11 envoyé : observer la LED Y de la carte. Pour tester le vérin NG, utiliser le test piston NG."
                    : "Échec diagnostic Y11 : la commande n'a pas été acceptée.",
                RequiresExpert = false,
                TerrainValidated = ok,
                Simulated = false,
                Mode = "NG_Y11_DIAGNOSTIC",
                Register = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                Value = "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + " ON " + NgPusherDiagnosticPulseMs.ToString(CultureInfo.InvariantCulture) + " ms puis OFF",
                OutputRegister = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                StateRegister = NgPusherReadbackRegister.ToString(CultureInfo.InvariantCulture),
                StateBefore = hasStateBefore ? stateBefore.ToString(CultureInfo.InvariantCulture) : null,
                StateAfter = hasStateAfter ? stateAfter.ToString(CultureInfo.InvariantCulture) : null
            };
        }

        private MaintenanceCommandResult ExecuteY11OutputBitNoLock(string commandName, bool active)
        {
            var targetLabel = active ? "ON" : "OFF";
            var targetValueText = active ? "1" : "0";

            if (_operatorStartArmed || _lotControlEnabled)
            {
                var runBlockMessage = "Commande brute Y11 bloquée pendant le cycle. Envoyer STOP ou PAUSE, attendre l'arrêt machine, puis relancer le diagnostic Y11.";
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "BLOCKED_RUN",
                    "LOCAL",
                    Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    targetValueText,
                    runBlockMessage
                );
                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = commandName,
                    Message = runBlockMessage,
                    RequiresExpert = true,
                    TerrainValidated = false,
                    BlockedBySafety = true,
                    Simulated = _config.UseSimulator,
                    Mode = "Y11_OUTPUT_BIT",
                    Register = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    Value = "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=" + targetValueText
                };
            }

            if (active && !_config.UseSimulator)
            {
                var latchedOnBlockMessage = "Y11 ON maintenu bloqué en réel : utiliser Diagnostic sortie NG, qui pulse Y11 puis revient OFF, ou Y11 OFF pour libérer la sortie.";
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "BLOCKED_LATCHED_ON",
                    "LOCAL",
                    Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    targetValueText,
                    latchedOnBlockMessage
                );
                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = commandName,
                    Message = latchedOnBlockMessage,
                    RequiresExpert = true,
                    TerrainValidated = false,
                    BlockedBySafety = true,
                    Simulated = false,
                    Mode = "Y11_OUTPUT_BIT",
                    Register = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    Value = "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=" + targetValueText
                };
            }

            if (active)
            {
                var safetyBlock = _config.UseSimulator ? null : BuildPistonSafetyBlockMessageNoLock();
                if (!string.IsNullOrWhiteSpace(safetyBlock))
                {
                    var blockMessage = "Activation Y11 bloquée : " + safetyBlock;
                    _trace.Append(
                        "MAINTENANCE",
                        commandName,
                        "BLOCKED",
                        "PLC",
                        Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                        targetValueText,
                        blockMessage
                    );
                    return new MaintenanceCommandResult
                    {
                        Ok = false,
                        Command = commandName,
                        Message = blockMessage,
                        RequiresExpert = true,
                        TerrainValidated = false,
                        BlockedBySafety = true,
                        Simulated = _config.UseSimulator,
                        Mode = "Y11_OUTPUT_BIT",
                        Register = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                        Value = "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=" + targetValueText
                    };
                }
            }

            _trace.Append(
                "MAINTENANCE",
                commandName,
                _config.UseSimulator ? "SIMULATED" : "ATTEMPT",
                _config.UseSimulator ? "SIMULATEUR" : "UI",
                Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=" + targetValueText,
                "Diagnostic sortie Y11 " + targetLabel + " : read-modify-write 4X 3144 bit 10 uniquement."
            );

            if (_config.UseSimulator)
            {
                return new MaintenanceCommandResult
                {
                    Ok = true,
                    Command = commandName,
                    Message = "Simulateur actif : diagnostic Y11 " + targetLabel + " tracé seulement.",
                    RequiresExpert = true,
                    TerrainValidated = false,
                    Simulated = true,
                    Mode = "Y11_OUTPUT_BIT",
                    Register = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    Value = "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=" + targetValueText
                };
            }

            ushort before;
            if (!TryReadHoldingSingleNoLock(_config, Y11OutputImageRegister, out before))
            {
                var readFailMessage = "Diagnostic Y11 impossible : lecture 4X 3144 refusée. Aucune écriture n'est envoyée pour éviter de modifier les autres sorties Y.";
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "READ_ERROR",
                    "PLC",
                    Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    targetValueText,
                    readFailMessage
                );
                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = commandName,
                    Message = readFailMessage,
                    RequiresExpert = true,
                    TerrainValidated = false,
                    Simulated = false,
                    Mode = "Y11_OUTPUT_BIT",
                    Register = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                    Value = "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=" + targetValueText
                };
            }

            var targetRegisterValue = active
                ? (ushort)(before | Y11OutputImageMask)
                : (ushort)(before & Y11OutputImageClearMask);
            var writeOk = WriteHoldingSingleNoLock(_config, Y11OutputImageRegister, targetRegisterValue);
            Thread.Sleep(150);

            ushort after;
            var hasAfter = TryReadHoldingSingleNoLock(_config, Y11OutputImageRegister, out after);
            var beforeBit = IsY11OutputImageBitSet(before);
            var afterBit = hasAfter && IsY11OutputImageBitSet(after);
            var ok = writeOk && hasAfter && afterBit == active;
            var detail = "3144 " + before.ToString(CultureInfo.InvariantCulture) +
                         "->" + (hasAfter ? after.ToString(CultureInfo.InvariantCulture) : "--") +
                         " ; Y11 bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) +
                         " " + (beforeBit ? "1" : "0") +
                         "->" + (hasAfter ? (afterBit ? "1" : "0") : "--") +
                         " ; masque 0x0400.";

            _trace.Append(
                "MAINTENANCE",
                commandName,
                ok ? "SENT" : "ERROR",
                "UI",
                Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                targetRegisterValue.ToString(CultureInfo.InvariantCulture),
                "Diagnostic Y11 " + targetLabel + " : " + detail
            );

            return new MaintenanceCommandResult
            {
                Ok = ok,
                Command = commandName,
                Message = ok
                    ? "Diagnostic Y11 " + targetLabel + " envoyé : " + detail
                    : "Échec diagnostic Y11 " + targetLabel + " : " + detail,
                RequiresExpert = true,
                TerrainValidated = false,
                Simulated = false,
                Mode = "Y11_OUTPUT_BIT",
                Register = Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                Value = "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=" + targetValueText,
                StateBefore = (beforeBit ? "1" : "0"),
                StateAfter = hasAfter ? (afterBit ? "1" : "0") : null
            };
        }

        public MaintenanceCommandResult ExecutePistonTest(string laneId)
        {
            lock (_lock)
            {
                var normalizedLane = string.IsNullOrWhiteSpace(laneId)
                    ? string.Empty
                    : laneId.Trim().ToUpperInvariant();
                int laneNumber;
                if (!TryResolvePistonLane(normalizedLane, out laneNumber))
                {
                    return new MaintenanceCommandResult
                    {
                        Ok = false,
                        Command = "PISTON_TEST",
                        Message = "Ligne piston invalide. Utiliser 1 à 10 ou NG.",
                        RequiresExpert = false,
                        TerrainValidated = false,
                        Simulated = _config.UseSimulator
                    };
                }

                var safetyBlock = _config.UseSimulator ? null : BuildPistonSafetyBlockMessageNoLock();
                if (!string.IsNullOrWhiteSpace(safetyBlock))
                {
                    _trace.Append(
                        "MAINTENANCE",
                        "PISTON_TEST_" + normalizedLane,
                        "BLOCKED",
                        "PLC",
                        _config.AlarmRegister.ToString(CultureInfo.InvariantCulture),
                        BuildAlarmSummary(_alarmsActive),
                        safetyBlock
                    );
                    return new MaintenanceCommandResult
                    {
                        Ok = false,
                        Command = "PISTON_TEST_" + normalizedLane,
                        Message = safetyBlock,
                        RequiresExpert = false,
                        BlockedBySafety = true,
                        TerrainValidated = true,
                        Simulated = _config.UseSimulator
                    };
                }

                if (IsPistonMaintenanceBlockedByRunStateNoLock())
                {
                    var runBlockMessage = "Test piston manuel bloqué pendant le tri en cours. Utiliser STOP ou PAUSE, attendre l'arrêt des tops 8230, puis relancer le test vérin.";
                    _trace.Append(
                        "MAINTENANCE",
                        "PISTON_TEST_" + normalizedLane,
                        "BLOCKED_RUN",
                        "LOCAL",
                        _config.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                        _lastRecordedHandshake.HasValue ? _lastRecordedHandshake.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                        runBlockMessage
                    );
                    return new MaintenanceCommandResult
                    {
                        Ok = false,
                        Command = "PISTON_TEST_" + normalizedLane,
                        Message = runBlockMessage,
                        RequiresExpert = false,
                        BlockedBySafety = true,
                        TerrainValidated = true,
                        Simulated = false
                    };
                }

                var laneIndex = laneNumber - 1;
                var resetRegister = (ushort)(PusherResetCommandBaseRegister + laneIndex);
                var stateRegister = (ushort)(PusherCylinderStateBaseRegister + laneIndex);
                var enableRegister = (ushort)(PusherCylinderEnableBaseRegister + laneIndex);
                var outputRegister = (ushort)(PusherCylinderOutputBaseRegister + laneIndex);
                var commandName = "PISTON_TEST_" + normalizedLane;
                CancelScheduledPusherWorkNoLock(commandName);

                var detail = BuildPistonPulseDetail(
                    "Test piston ligne " + normalizedLane,
                    normalizedLane,
                    laneNumber,
                    enableRegister,
                    outputRegister);
                _trace.Append("MAINTENANCE", commandName, "ATTEMPT", _config.UseSimulator ? "SIMULATEUR" : "UI", outputRegister.ToString(CultureInfo.InvariantCulture), PusherActiveValue.ToString(CultureInfo.InvariantCulture), detail);

                if (_config.UseSimulator)
                {
                    return new MaintenanceCommandResult
                    {
                        Ok = true,
                        Command = commandName,
                        Message = "Simulateur actif : test piston " + normalizedLane + " tracé seulement.",
                        RequiresExpert = false,
                        TerrainValidated = false,
                        Simulated = true,
                        Mode = "PUSH_CYLINDER_IO",
                        Register = outputRegister.ToString(CultureInfo.InvariantCulture),
                        Value = PusherActiveValue.ToString(CultureInfo.InvariantCulture),
                        EnableRegister = enableRegister.ToString(CultureInfo.InvariantCulture),
                        OutputRegister = outputRegister.ToString(CultureInfo.InvariantCulture),
                        StateRegister = stateRegister.ToString(CultureInfo.InvariantCulture)
                    };
                }

                ushort stateBefore;
                ushort stateDuring;
                ushort stateAfter;
                var hasStateBefore = TryReadHoldingSingleNoLock(_config, stateRegister, out stateBefore);
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "COMMAND_SNAPSHOT_BEFORE",
                    "PLC",
                    resetRegister.ToString(CultureInfo.InvariantCulture) + "/" +
                    enableRegister.ToString(CultureInfo.InvariantCulture) + "/" +
                    outputRegister.ToString(CultureInfo.InvariantCulture),
                    string.Empty,
                    BuildPusherCommandSnapshotNoLock(_config, resetRegister, enableRegister, outputRegister)
                );

                var preReleaseOk = PreparePusherLaneForMaintenancePulseNoLock(
                    _config,
                    "MAINTENANCE",
                    commandName,
                    "UI",
                    "ligne=" + normalizedLane,
                    enableRegister,
                    outputRegister,
                    "Test piston");

                var enableOk = false;
                if (preReleaseOk)
                {
                    enableOk = WritePistonIoMaintenanceSingleNoLock(_config, enableRegister, PusherActiveValue);
                }
                _trace.Append("MAINTENANCE", commandName, enableOk ? "ENABLE_SENT" : "ENABLE_ERROR", "UI", enableRegister.ToString(CultureInfo.InvariantCulture), PusherActiveValue.ToString(CultureInfo.InvariantCulture), "Activation sortie piston.");

                var outputOk = false;
                var hasStateDuring = false;
                if (enableOk)
                {
                    Thread.Sleep(PusherEnableSettleMs);
                    outputOk = WritePistonIoMaintenanceSingleNoLock(_config, outputRegister, PusherActiveValue);
                    _trace.Append("MAINTENANCE", commandName, outputOk ? "OUTPUT_SENT" : "OUTPUT_ERROR", "UI", outputRegister.ToString(CultureInfo.InvariantCulture), PusherActiveValue.ToString(CultureInfo.InvariantCulture), "Impulsion sortie piston.");
                    if (outputOk)
                    {
                        Thread.Sleep(MaintenancePusherPulseMs);
                        hasStateDuring = TryReadHoldingSingleNoLock(_config, stateRegister, out stateDuring);
                    }
                    else
                    {
                        stateDuring = 0;
                    }
                }
                else
                {
                    stateDuring = 0;
                }

                var outputReleaseOk = WritePistonIoMaintenanceSingleNoLock(_config, outputRegister, PusherInactiveValue);
                _trace.Append("MAINTENANCE", commandName, outputReleaseOk ? "OUTPUT_RELEASE" : "OUTPUT_RELEASE_ERROR", "UI", outputRegister.ToString(CultureInfo.InvariantCulture), PusherInactiveValue.ToString(CultureInfo.InvariantCulture), "Relachement sortie piston.");

                var enableReleaseOk = WritePistonIoMaintenanceSingleNoLock(_config, enableRegister, PusherInactiveValue);
                _trace.Append("MAINTENANCE", commandName, enableReleaseOk ? "ENABLE_RELEASE" : "ENABLE_RELEASE_ERROR", "UI", enableRegister.ToString(CultureInfo.InvariantCulture), PusherInactiveValue.ToString(CultureInfo.InvariantCulture), "Retour mode auto apres test piston.");

                var hasStateAfter = TryReadHoldingSingleNoLock(_config, stateRegister, out stateAfter);
                var ok = preReleaseOk && enableOk && outputOk && outputReleaseOk && enableReleaseOk;
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "STATE_SNAPSHOT",
                    "UI",
                    stateRegister.ToString(CultureInfo.InvariantCulture),
                    (hasStateAfter ? stateAfter.ToString(CultureInfo.InvariantCulture) : "--"),
                    "Etat piston: avant=" + (hasStateBefore ? stateBefore.ToString(CultureInfo.InvariantCulture) : "--") +
                    " pendant=" + (hasStateDuring ? stateDuring.ToString(CultureInfo.InvariantCulture) : "--") +
                    " apres=" + (hasStateAfter ? stateAfter.ToString(CultureInfo.InvariantCulture) : "--") +
                    ". " + BuildPusherCommandSnapshotNoLock(_config, resetRegister, enableRegister, outputRegister) +
                    ". Aucun reset piston ni ecriture globale banque n'a ete envoye."
                );
                _trace.Append("MAINTENANCE", commandName, ok ? "SENT" : "ERROR", "UI", outputRegister.ToString(CultureInfo.InvariantCulture), PusherActiveValue.ToString(CultureInfo.InvariantCulture), detail);

                return new MaintenanceCommandResult
                {
                    Ok = ok,
                    Command = commandName,
                    Message = ok
                    ? "Test piston " + normalizedLane + " envoyé; impulsion maintenue " + MaintenancePusherPulseMs.ToString(CultureInfo.InvariantCulture) + " ms."
                        : "Échec d’envoi du test piston " + normalizedLane + ".",
                    RequiresExpert = false,
                    TerrainValidated = false,
                    Simulated = false,
                    Mode = "PUSH_CYLINDER_IO",
                    Register = outputRegister.ToString(CultureInfo.InvariantCulture),
                    Value = PusherActiveValue.ToString(CultureInfo.InvariantCulture) + " pendant " + MaintenancePusherPulseMs.ToString(CultureInfo.InvariantCulture) + " ms",
                    EnableRegister = enableRegister.ToString(CultureInfo.InvariantCulture),
                    OutputRegister = outputRegister.ToString(CultureInfo.InvariantCulture),
                    StateRegister = stateRegister.ToString(CultureInfo.InvariantCulture),
                    StateBefore = hasStateBefore ? stateBefore.ToString(CultureInfo.InvariantCulture) : null,
                    StateDuring = hasStateDuring ? stateDuring.ToString(CultureInfo.InvariantCulture) : null,
                    StateAfter = hasStateAfter ? stateAfter.ToString(CultureInfo.InvariantCulture) : null
                };
            }
        }

        private bool IsPistonMaintenanceBlockedByRunStateNoLock()
        {
            return _config != null &&
                   !_config.UseSimulator &&
                   (_operatorStartArmed || _lotControlEnabled);
        }

        private static bool TryResolvePistonLane(string laneId, out int laneNumber)
        {
            if (string.Equals(laneId, "NG", StringComparison.OrdinalIgnoreCase))
            {
                laneNumber = NgPhysicalPusherLane;
                return true;
            }

            int lane;
            if (int.TryParse(laneId, out lane) && lane >= 1 && lane <= 10)
            {
                laneNumber = lane;
                return true;
            }

            laneNumber = 0;
            return false;
        }

        private void SetLastNgPulseNoLock(
            int? handshakeValue,
            string status,
            ushort enableRegister,
            ushort outputRegister,
            ushort enableValue,
            ushort outputValue,
            string result,
            string source,
            string detail)
        {
            _lastNgPulseDiagnostic = new NgPulseDiagnostic
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Handshake = handshakeValue,
                Status = string.IsNullOrWhiteSpace(status) ? "UNKNOWN" : status,
                OutputPath = "Y11_4X_3144_BIT_10",
                OutputImageRegister = Y11OutputImageRegister,
                OutputBit = Y11OutputImageBit,
                EnableRegister = enableRegister,
                OutputRegister = outputRegister,
                EnableValue = enableValue,
                OutputValue = outputValue,
                Result = string.IsNullOrWhiteSpace(result) ? "UNKNOWN" : result,
                Source = string.IsNullOrWhiteSpace(source) ? "LOCAL" : source,
                Detail = string.IsNullOrWhiteSpace(detail) ? string.Empty : detail
            };
        }

        private void CancelScheduledPusherWorkNoLock(string reason)
        {
            _pusherWorkGeneration++;
            _trace.Append(
                "ROUTING",
                "PUSHER_QUEUE",
                "CANCELED",
                "LOCAL",
                "generation",
                _pusherWorkGeneration.ToString(CultureInfo.InvariantCulture),
                "Toutes les impulsions piston planifiees avant cette action sont abandonnees. reason=" +
                (string.IsNullOrWhiteSpace(reason) ? "--" : reason)
            );
        }

        private bool PreparePusherLaneForPulseNoLock(MachineConfig cfg, string traceCategory, string traceAction, string source, string laneDetail, ushort enableRegister, ushort outputRegister, string context)
        {
            var outputOffOk = WritePistonIoSingleNoLock(cfg, outputRegister, PusherInactiveValue);
            var enableOffOk = WritePistonIoSingleNoLock(cfg, enableRegister, PusherInactiveValue);

            Thread.Sleep(PusherPreReleaseSettleMs);

            var ok = outputOffOk && enableOffOk;
            _trace.Append(
                traceCategory,
                traceAction,
                ok ? "PRE_RELEASE" : "PRE_RELEASE_ERROR",
                source,
                enableRegister.ToString(CultureInfo.InvariantCulture) + "/" + outputRegister.ToString(CultureInfo.InvariantCulture),
                PusherInactiveValue.ToString(CultureInfo.InvariantCulture),
                context + " " + laneDetail + " : front montant force, sortie et enable de cette voie uniquement remis a 0 avant impulsion."
            );

            return ok;
        }

        private bool PreparePusherLaneForMaintenancePulseNoLock(MachineConfig cfg, string traceCategory, string traceAction, string source, string laneDetail, ushort enableRegister, ushort outputRegister, string context)
        {
            var outputOffOk = WritePistonIoMaintenanceSingleNoLock(cfg, outputRegister, PusherInactiveValue);
            var enableOffOk = WritePistonIoMaintenanceSingleNoLock(cfg, enableRegister, PusherInactiveValue);

            Thread.Sleep(PusherPreReleaseSettleMs);

            var ok = outputOffOk && enableOffOk;
            _trace.Append(
                traceCategory,
                traceAction,
                ok ? "PRE_RELEASE" : "PRE_RELEASE_ERROR",
                source,
                enableRegister.ToString(CultureInfo.InvariantCulture) + "/" + outputRegister.ToString(CultureInfo.InvariantCulture),
                PusherInactiveValue.ToString(CultureInfo.InvariantCulture),
                context + " " + laneDetail + " : front montant force, sortie et enable de cette voie uniquement remis a 0 avant impulsion maintenance."
            );

            return ok;
        }

        private string BuildPusherCommandSnapshotNoLock(MachineConfig cfg, ushort resetRegister, ushort enableRegister, ushort outputRegister)
        {
            ushort resetValue;
            ushort enableValue;
            ushort outputValue;
            var hasReset = TryReadHoldingSingleNoLock(cfg, resetRegister, out resetValue);
            var hasEnable = TryReadHoldingSingleNoLock(cfg, enableRegister, out enableValue);
            var hasOutput = TryReadHoldingSingleNoLock(cfg, outputRegister, out outputValue);

            return "Commandes piston lues: reset 4X " +
                   resetRegister.ToString(CultureInfo.InvariantCulture) +
                   "=" +
                   (hasReset ? resetValue.ToString(CultureInfo.InvariantCulture) : "--") +
                   ", enable 4X " +
                   enableRegister.ToString(CultureInfo.InvariantCulture) +
                   "=" +
                   (hasEnable ? enableValue.ToString(CultureInfo.InvariantCulture) : "--") +
                   ", sortie holding 4X " +
                   outputRegister.ToString(CultureInfo.InvariantCulture) +
                   "=" +
                   (hasOutput ? outputValue.ToString(CultureInfo.InvariantCulture) : "--");
        }

        private static string BuildPistonPulseDetail(string prefix, string requestedLane, int physicalLane, ushort enableRegister, ushort outputRegister)
        {
            return prefix +
                   ": ligne " + requestedLane +
                   " (voie physique " + physicalLane.ToString(CultureInfo.InvariantCulture) +
                   "), enable 4X " + enableRegister.ToString(CultureInfo.InvariantCulture) +
                   " pre-relache a 0 puis = 1, sortie " +
                   "4X " + outputRegister.ToString(CultureInfo.InvariantCulture) +
                   " = 1 puis 0, enable relache a 0. Aucun reset 28295..28305 et aucun autre piston ne sont ecrits.";
        }

        public ContractBundle GetContracts()
        {
            return ApiContractCatalog.Build();
        }

        private MaintenanceSnapshot BuildMaintenanceSnapshotNoLock()
        {
            return new MaintenanceSnapshot
            {
                ValidatedCommands = new List<MaintenanceCommandDefinition>
                {
                    new MaintenanceCommandDefinition { Command = "CONVEYOR_FINE_FORWARD", Label = "Micro-avance tapis", Register = "coil 1X 5981", Code = "coil 200 ms", RequiresExpert = false, TerrainValidated = true, Warning = "Micro-avance tapis via le coil constructeur. Zone dégagée requise." },
                    new MaintenanceCommandDefinition { Command = "CONVEYOR_ONLY_FORWARD", Label = "Avancer convoyeur", Register = "coil 1X 5981", Code = "coil 1000 ms", RequiresExpert = false, TerrainValidated = true, Warning = "Avance tapis via le coil constructeur. Zone dégagée requise." },
                    new MaintenanceCommandDefinition { Command = "PUSHER_STATIONS_ENABLE", Label = "Armer toutes les stations pistons (1..10 + NG)", Register = "4X 28414..28424", Code = "enable=1, maintenu", RequiresExpert = false, TerrainValidated = false, Warning = "Arme les enables constructeur de toutes les stations pistons (les sorties 28926..28936 ne sont jamais écrites). Sans enable=1, un piston ne tire jamais, quels que soient les seuils — c'est ce qui a réveillé le NG." },
                    new MaintenanceCommandDefinition { Command = "NG_STATION_ENABLE", Label = "Armer station NG (battement à chaque avance)", Register = "4X 28424", Code = "enable=1, maintenu", RequiresExpert = false, TerrainValidated = true, Warning = "Arme la station NG constructeur : enable 28424=1 laissé en place (la sortie 28936 n'est pas écrite). Validé terrain le 10 juin 2026 : le PLC bat le poussoir NG à chaque avance." },
                    new MaintenanceCommandDefinition { Command = "NG_STATION_DISABLE", Label = "Désarmer station NG", Register = "4X 28424", Code = "enable=0", RequiresExpert = false, TerrainValidated = false, Warning = "Coupe l'enable 28424 de la station NG." },
                    new MaintenanceCommandDefinition { Command = "RELEASE_NG_PISTON", Label = "Libérer vérin NG", Register = "4X 28305", Code = "reset NG repos (=1)", RequiresExpert = false, TerrainValidated = true, Warning = "Relâche le reset NG constructeur 28305 à 1 si le vérin NG est resté sorti. Les enables/sorties 28424/28936 ne sont pas écrits." },
                    new MaintenanceCommandDefinition { Command = "DIAG_PULSE_NG", Label = "Diagnostic sortie Y11 (carte)", Register = "4X 3144 bit 10", Code = "ON 1500 ms puis OFF", RequiresExpert = false, TerrainValidated = true, Warning = "Pulse la sortie carte Y11 machine arrêtée pour observer la LED Y. Ce n'est pas le vérin NG : pour tester le vérin NG, utiliser le test piston NG (28424/28936)." },
                    new MaintenanceCommandDefinition { Command = "START", Label = "Démarrer machine", Register = "5978", Code = "31 écrit une fois (le PLC consomme)", RequiresExpert = false, TerrainValidated = true, Warning = string.Empty },
                    new MaintenanceCommandDefinition { Command = "RESET", Label = "Réarmer automate", Register = "5978", Code = "26 écrit une fois (le PLC consomme)", RequiresExpert = false, TerrainValidated = true, Warning = "Commande opérateur volontaire. À utiliser seulement si le statut 7 empêche de redémarrer; aucun registre piston n'est écrit." },
                    new MaintenanceCommandDefinition { Command = "STOP", Label = "Arrêter machine", Register = "5978", Code = "29 écrit une fois (le PLC consomme)", RequiresExpert = false, TerrainValidated = true, Warning = string.Empty },
                    new MaintenanceCommandDefinition { Command = "PAUSE", Label = "Pause machine", Register = "5978", Code = "32 écrit une fois (le PLC consomme)", RequiresExpert = false, TerrainValidated = true, Warning = string.Empty }
                },
                ExpertCommands = new List<MaintenanceCommandDefinition>
                {
                    new MaintenanceCommandDefinition { Command = "START_RAW", Label = "Démarrage brut constructeur", Register = "5978", Code = "31 écrit une fois", RequiresExpert = true, TerrainValidated = false, Warning = "Diagnostic expert seulement : bloqué en réel pour éviter de contourner 8230, START_PRELOAD et le lot actif." },
                    new MaintenanceCommandDefinition { Command = "Y11_OUTPUT_OFF", Label = "Y11 OFF", Register = "4X 3144 bit 10", Code = "bit=0", RequiresExpert = true, TerrainValidated = false, Warning = "Diagnostic brut sortie carte : modifie uniquement le bit Y11 après lecture 3144. À lancer machine arrêtée et zone dégagée." },
                    new MaintenanceCommandDefinition { Command = "Y11_OUTPUT_ON", Label = "Y11 ON maintenu", Register = "4X 3144 bit 10", Code = "bit=1", RequiresExpert = true, TerrainValidated = false, Warning = "Bloqué en réel : utiliser Diagnostic sortie NG pour une impulsion ON/OFF validée, ou Y11 OFF pour libérer la sortie." }
                }
            };
        }

        private static bool IsY11OutputImageBitSet(ushort value)
        {
            return (value & Y11OutputImageMask) != 0;
        }

        private bool TrySetY11OutputBitNoLock(MachineConfig cfg, bool active, out string detail)
        {
            detail = string.Empty;
            if (cfg == null)
            {
                detail = "Configuration PLC indisponible : Y11 non modifie.";
                return false;
            }

            if (cfg.UseSimulator)
            {
                detail = "Simulateur : Y11 bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) +
                         "=" + (active ? "1" : "0") + ".";
                return true;
            }

            ushort before;
            if (!TryReadHoldingSingleNoLock(cfg, Y11OutputImageRegister, out before))
            {
                detail = "Lecture 4X " + Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture) +
                         " refusee : aucune ecriture Y11 envoyee.";
                return false;
            }

            var beforeBit = IsY11OutputImageBitSet(before);
            var targetRegisterValue = active
                ? (ushort)(before | Y11OutputImageMask)
                : (ushort)(before & Y11OutputImageClearMask);
            var writeOk = WriteHoldingSingleNoLock(cfg, Y11OutputImageRegister, targetRegisterValue);
            Thread.Sleep(150);

            ushort after;
            var hasAfter = TryReadHoldingSingleNoLock(cfg, Y11OutputImageRegister, out after);
            var afterBit = hasAfter && IsY11OutputImageBitSet(after);
            detail = "3144 " + before.ToString(CultureInfo.InvariantCulture) +
                     "->" + (hasAfter ? after.ToString(CultureInfo.InvariantCulture) : "--") +
                     " ; Y11 bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) +
                     " " + (beforeBit ? "1" : "0") +
                     "->" + (hasAfter ? (afterBit ? "1" : "0") : "--") +
                     " ; cible=" + (active ? "1" : "0") + ".";
            return writeOk && hasAfter && afterBit == active;
        }

        private bool PulseY11OutputBitNoLock(MachineConfig cfg, string traceCategory, string traceAction, string source, string context, int pulseMs, out string pulseDetail)
        {
            string preDetail;
            var preReleaseOk = TrySetY11OutputBitNoLock(cfg, false, out preDetail);
            _trace.Append(
                traceCategory,
                traceAction,
                preReleaseOk ? "Y11_PRE_RELEASE" : "Y11_PRE_RELEASE_ERROR",
                source,
                Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=0",
                context + " pre-release Y11 OFF. " + preDetail
            );

            string onDetail = string.Empty;
            var onOk = false;
            if (preReleaseOk)
            {
                Thread.Sleep(PusherEnableSettleMs);
                onOk = TrySetY11OutputBitNoLock(cfg, true, out onDetail);
            }
            _trace.Append(
                traceCategory,
                traceAction,
                onOk ? "Y11_ON" : "Y11_ON_ERROR",
                source,
                Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=1",
                context + " Y11 ON pendant " + pulseMs.ToString(CultureInfo.InvariantCulture) + " ms. " + onDetail
            );

            if (onOk)
            {
                Thread.Sleep(pulseMs);
            }

            var releaseOk = false;
            var releaseDetail = string.Empty;
            for (var retry = 0; retry < 3 && !releaseOk; retry++)
            {
                if (retry > 0)
                {
                    Thread.Sleep(50);
                }
                releaseOk = TrySetY11OutputBitNoLock(cfg, false, out releaseDetail);
            }
            _trace.Append(
                traceCategory,
                traceAction,
                releaseOk ? "Y11_OFF" : "Y11_OFF_ERROR",
                source,
                Y11OutputImageRegister.ToString(CultureInfo.InvariantCulture),
                "bit " + Y11OutputImageBit.ToString(CultureInfo.InvariantCulture) + "=0",
                context + " relachement Y11 OFF. " + releaseDetail
            );

            pulseDetail = "Y11 4X 3144 bit 10 pre=[" + preDetail + "] on=[" + onDetail + "] off=[" + releaseDetail + "]";
            return preReleaseOk && onOk && releaseOk;
        }

        private MachineSpeedState BuildMachineSpeedStateNoLock()
        {
            return new MachineSpeedState
            {
                Register = SpeedModeRegister,
                Mode = _machineSpeedMode,
                Label = BuildMachineSpeedLabel(_machineSpeedMode),
                Available = _machineSpeedAvailable,
                Source = _config.UseSimulator ? "SIMULATEUR" : (_machineSpeedAvailable ? "PLC" : "INDISPONIBLE")
            };
        }

        public void Shutdown()
        {
            // Libere le port serie pour qu'aucun process ne garde l'automate apres fermeture.
            _modbus.Close();
        }

        private void UpdateMachineSwitchesNoLock(int? value)
        {
            if (_machineSwitchesValue == value)
            {
                return;
            }

            _machineSwitchesValue = value;
            _trace.Append(
                "MAINTENANCE",
                "MACHINE_SWITCHES",
                value.HasValue ? "READ" : "UNAVAILABLE",
                "PLC",
                MachineSwitchesRegister.ToString(CultureInfo.InvariantCulture),
                value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                value.HasValue
                    ? "Réglages constructeur 640 (bit0=réarmement auto à la mise sous tension, bit2=scan): valeur " + value.Value.ToString(CultureInfo.InvariantCulture) + "."
                    : "Réglages constructeur 640 illisibles."
            );
        }

        private void UpdateMachineSpeedModeNoLock(int? mode, bool available)
        {
            _machineSpeedAvailable = available && mode.HasValue;
            _machineSpeedMode = _machineSpeedAvailable ? mode : null;

            if (_lastLoggedMachineSpeedMode == _machineSpeedMode)
            {
                return;
            }

            _lastLoggedMachineSpeedMode = _machineSpeedMode;
            _trace.Append(
                "MAINTENANCE",
                "SPEED_MODE",
                _machineSpeedAvailable ? "READ" : "UNAVAILABLE",
                _config.UseSimulator ? "SIMULATEUR" : "PLC",
                SpeedModeRegister.ToString(CultureInfo.InvariantCulture),
                _machineSpeedMode.HasValue ? _machineSpeedMode.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                "Mode vitesse machine : " + BuildMachineSpeedLabel(_machineSpeedMode) + "."
            );
        }

        private static string BuildMachineSpeedLabel(int? mode)
        {
            if (!mode.HasValue)
            {
                return "Non lu";
            }

            switch (mode.Value)
            {
                case 0:
                    return "Lent";
                case 1:
                    return "Moyen";
                case 2:
                    return "Mode 2";
                default:
                    return "Inconnu (" + mode.Value.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        private void ReadFromPlc(MachineConfig cfg, ThresholdSet localThresholds)
        {
            var notes = new List<string>();
            var alarmRegs = new ushort[0];
            var measurementRegs = new ushort[0];
            var displayRegs = new ushort[0];
            var counterRegs = new ushort[0];
            var totalCounterRegs = new ushort[0];
            int? previousHandshake;
            lock (_lock)
            {
                previousHandshake = _lastHandshakeValue;
            }

            var now = DateTime.Now;
            int? handshakeValue = null;
            int? statusValue = null;
            ushort[] gateRegs;
            if (cfg.StatusRegister == cfg.HandshakeRegister + 1 &&
                TryReadRegisters(cfg, cfg.HandshakeRegister, 2, out gateRegs, notes, "Handshake/statut"))
            {
                handshakeValue = gateRegs[0];
                statusValue = gateRegs[1];
            }
            else
            {
                handshakeValue = TryReadSingleRegister(cfg, cfg.HandshakeRegister, notes, "Handshake");
                statusValue = TryReadSingleRegister(cfg, cfg.StatusRegister, notes, "Statut");
            }

            int? speedModeValue = null;
            var speedModeRead = false;
            if ((now - _lastSpeedRead).TotalMilliseconds >= 2000)
            {
                speedModeValue = TryReadSingleRegister(cfg, SpeedModeRegister, notes, "Mode vitesse 23341");
                speedModeRead = true;
                _lastSpeedRead = now;
            }
            int? machineSwitchesValue = null;
            var machineSwitchesRead = false;
            if ((now - _lastSwitchesRead).TotalMilliseconds >= 2000)
            {
                machineSwitchesValue = TryReadSingleRegister(cfg, MachineSwitchesRegister, notes, "Interrupteurs machine 640");
                machineSwitchesRead = true;
                _lastSwitchesRead = now;
            }
            if ((now - _lastEnablesRead).TotalMilliseconds >= 5000)
            {
                ushort[] enablesSnapshot;
                if (TryReadRegisters(cfg, PusherCylinderEnableBaseRegister, NgCounterIndex + 1, out enablesSnapshot, notes, "Enables stations pistons 28414+"))
                {
                    _pusherEnablesSnapshot = enablesSnapshot;
                }
                _lastEnablesRead = now;
            }
            var acceptedNewCycle = false;
            if (handshakeValue.HasValue)
            {
                acceptedNewCycle = EvaluateCycleGate(cfg, handshakeValue.Value);
                var firstHandshakeRead = !previousHandshake.HasValue;
                var handshakeChanged = previousHandshake.HasValue && previousHandshake.Value != handshakeValue.Value;
                UpdateHandshake(handshakeValue.Value);
                if (firstHandshakeRead || handshakeChanged)
                {
                    _trace.Append(
                        "HANDSHAKE",
                        "8230_CHANGE",
                        acceptedNewCycle ? "ACCEPTED" : "IGNORED",
                        "PLC",
                        cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                        handshakeValue.Value.ToString(CultureInfo.InvariantCulture),
                        cfg.ScanEnabled && handshakeValue.Value != 1
                            ? "Cycle refusé par la porte scanner (scan actif, valeur != 1)."
                            : (acceptedNewCycle ? "Nouveau cycle accepté par notre logiciel." : "Changement vu mais non compté comme nouvelle cellule.")
                    );
                }
            }
            if (speedModeRead)
            {
                lock (_lock)
                {
                    UpdateMachineSpeedModeNoLock(speedModeValue, speedModeValue.HasValue);
                }
            }
            if (machineSwitchesRead)
            {
                lock (_lock)
                {
                    UpdateMachineSwitchesNoLock(machineSwitchesValue);
                }
            }

            if (!cfg.UseSimulator && (now - _lastNgPusherReleaseCheck).TotalMilliseconds >= NgPusherReleaseCheckIntervalMs)
            {
                _lastNgPusherReleaseCheck = now;
                if (IsNgAutoReleaseBlockedByRunStateNoLock())
                {
                    if ((now - _lastNgAutoReleaseSkippedTrace).TotalSeconds >= 30)
                    {
                        _lastNgAutoReleaseSkippedTrace = now;
                        _trace.Append(
                            "MAINTENANCE",
                            "AUTO_NG_RELEASE",
                            "SKIPPED_RUN",
                            "LOCAL",
                            cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                            _lastRecordedHandshake.HasValue ? _lastRecordedHandshake.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                            "Libération automatique NG ignorée pendant cycle armé. START et Libérer vérin NG restent les chemins explicites pour 28305=1."
                        );
                    }
                }
                else
                {
                    var ngRelease = ReleaseNgPusherResetNoLock(cfg, "AUTO_NG_RELEASE", "PLC", false);
                    if (!ngRelease.CommandReleased || !ngRelease.FeedbackReleased)
                    {
                        notes.Add(ngRelease.Message);
                    }
                }
            }

            var measurementOk = TryReadRegisters(cfg, cfg.MeasurementRegister, 4, out measurementRegs, notes, "Mesures");
            displayRegs = measurementRegs;
            if ((now - _lastAlarmRead).TotalMilliseconds >= 250 || _lastAlarmRegisters == null || _lastAlarmRegisters.Length == 0)
            {
                if (TryReadRegisters(cfg, cfg.AlarmRegister, 4, out alarmRegs, notes, "Alarmes"))
                {
                    _lastAlarmRegisters = alarmRegs;
                    _lastAlarmRead = now;
                }
            }
            else
            {
                alarmRegs = _lastAlarmRegisters;
            }
            var alarms = DecodeAlarms(alarmRegs);
            if (acceptedNewCycle && handshakeValue.HasValue)
            {
                lock (_lock)
                {
                    _alarmsActive = alarms;
                }
            }
            if ((now - _lastCounterRead).TotalMilliseconds >= 500 || !_machineCountersAvailable)
            {
                var counterRegisterCount = cfg.Channels <= 0 ? 0 : cfg.Channels * 2;
                TryReadRegisters(cfg, CounterBaseRegister, counterRegisterCount, out counterRegs, notes, "Compteurs machine 900+");
                TryReadRegisters(cfg, TotalCounterRegister, 2, out totalCounterRegs, notes, "Compteur total 948");
                UpdateMachineCounters(cfg, counterRegs, totalCounterRegs);
                _lastCounterRead = now;
            }

            ThresholdSet observedThresholds = null;
            var observedThresholdsFresh = false;
            if (cfg.ShadowMode && (DateTime.Now - _lastThresholdRead).TotalSeconds >= 10)
            {
                ThresholdSet freshObservedThresholds;
                if (TryReadObservedThresholds(cfg, notes, out freshObservedThresholds))
                {
                    observedThresholds = freshObservedThresholds;
                    observedThresholdsFresh = true;
                }
                else
                {
                    observedThresholds = null;
                }

                _lastThresholdRead = DateTime.Now;
            }

            if (observedThresholds == null)
            {
                lock (_lock)
                {
                    if (_observedThresholds != null)
                    {
                        observedThresholds = CopyThresholds(_observedThresholds);
                    }
                }
            }

            if (!measurementOk || measurementRegs == null || measurementRegs.Length < 4)
            {
                lock (_lock)
                {
                    _connected = false;
                    _alarmsActive = alarms;
                    _lastLaneFullSignalState = false;
                    LogAlarmChangesNoLock(alarms, "PLC");

                    if (acceptedNewCycle)
                    {
                        _trace.Append(
                            "DECISION",
                            "RAW_CYCLE_ONLY",
                            "MEASURE_UNAVAILABLE",
                            "PLC",
                            handshakeValue.HasValue ? cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture) : string.Empty,
                            "HS=" + (handshakeValue.HasValue ? handshakeValue.Value.ToString(CultureInfo.InvariantCulture) : "--"),
                            "Top 8230 accepte mais lecture mesures indisponible; NG gere par le PLC via la voie catch-all." +
                            " routing_control=PLC_THRESHOLDS_NG_CATCHALL"
                        );
                    }

                    _diagnostic = BuildDiagnostic(cfg, handshakeValue, statusValue, measurementRegs, displayRegs, alarmRegs, observedThresholds, localThresholds, notes, "Lecture PLC indisponible");
                }
                if (_lastConnectedLoggedState != false)
                {
                    _trace.Append("PLC", "READ", "OFFLINE", "PLC", cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture), string.Empty, "Lecture des mesures indisponible.");
                    _lastConnectedLoggedState = false;
                }
                return;
            }

            if (_lastConnectedLoggedState != true)
            {
                _trace.Append("PLC", "READ", "ONLINE", "PLC", cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture), string.Empty, "Lecture PLC disponible.");
                _lastConnectedLoggedState = true;
                TrySendConstructorConnectInitNoLock(cfg);
            }

            string measurementDecodeNote;
            var decodedMeasurement = DecodeMeasurementValues(cfg, measurementRegs, out measurementDecodeNote);
            ApplyDetectedSwapWords(cfg, decodedMeasurement.SwapWords, notes);

            var rawIr = decodedMeasurement.Ir;
            var rawVoltage = decodedMeasurement.Voltage;
            var measurementAvailable = decodedMeasurement.IsPlausible;
            var ir = measurementAvailable ? Math.Abs(rawIr) : 0.0;
            var voltage = measurementAvailable ? Math.Abs(rawVoltage) : 0.0;
            if (!string.IsNullOrWhiteSpace(measurementDecodeNote))
            {
                notes.Add(measurementDecodeNote);
            }
            if (!measurementAvailable)
            {
                var measurementRegisterText = cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture);
                var rejectNote = "Mesure " + measurementRegisterText + " rejetée : hors plage plausible (rawV=" +
                                 rawVoltage.ToString("0.0000", CultureInfo.InvariantCulture) +
                                 " rawIR=" + rawIr.ToString("0.000", CultureInfo.InvariantCulture) + ").";
                notes.Add(rejectNote);
                _trace.Append(
                    "MEASURE",
                    measurementRegisterText,
                    "REJECTED",
                    "PLC",
                    measurementRegisterText,
                    "rawV=" + rawVoltage.ToString("0.0000", CultureInfo.InvariantCulture) +
                    " rawIR=" + rawIr.ToString("0.000", CultureInfo.InvariantCulture),
                    rejectNote
                );
            }
            else if (rawVoltage != voltage)
            {
                var absNote = "Tension normalisée en valeur absolue: V=" + voltage.ToString("0.0000", CultureInfo.InvariantCulture);
                notes.Add(absNote);
                if (acceptedNewCycle)
                {
                    _trace.Append("MEASURE", "VOLTAGE", "ABS_NORMALIZED", "PLC", cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture), "rawV=" + rawVoltage.ToString("0.0000", CultureInfo.InvariantCulture), absNote);
                }
            }
            if (measurementAvailable && rawIr != ir)
            {
                var absNote = "IR normalisee en valeur absolue: IR=" + ir.ToString("0.000", CultureInfo.InvariantCulture);
                notes.Add(absNote);
                if (acceptedNewCycle)
                {
                    _trace.Append("MEASURE", "IR", "ABS_NORMALIZED", "PLC", cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture), "rawIR=" + rawIr.ToString("0.000", CultureInfo.InvariantCulture), absNote);
                }
            }
            var barcode = GetBarcode(cfg);

            HandleMeasurement(cfg, localThresholds, observedThresholds, observedThresholdsFresh, notes, handshakeValue, acceptedNewCycle, statusValue, measurementRegs, displayRegs, alarmRegs, voltage, ir, measurementAvailable, barcode, alarms, "PLC");
        }

        private void Simulate(MachineConfig cfg, ThresholdSet thresholds)
        {
            var voltage = Math.Round(3.0 + _random.NextDouble() * 1.2, 5);
            var ir = Math.Round(5.0 + _random.NextDouble() * 35.0, 3);
            if (_random.NextDouble() < 0.03)
            {
                voltage = -voltage;
            }

            var barcode = cfg.ScanEnabled ? "SIM-" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture) : null;
            var handshake = cfg.ScanEnabled ? 1 : (_lastHandshakeValue.HasValue ? _lastHandshakeValue.Value + 1 : 1);
            UpdateHandshake(handshake);
            lock (_lock)
            {
                if (!_machineSpeedMode.HasValue)
                {
                    UpdateMachineSpeedModeNoLock(1, true);
                }
            }

            var alarms = new List<int>();
            if (_random.NextDouble() < 0.02) alarms.Add(14);
            if (_random.NextDouble() < 0.01) alarms.Add(18);

            HandleMeasurement(
                cfg,
                thresholds,
                null,
                false,
                new List<string> { "Mode simulateur actif.", "Aucune lecture machine reelle pendant cette session.", "Safe mode et observation seule restent actifs." },
                handshake,
                true,
                1,
                new ushort[0],
                new ushort[0],
                new ushort[0],
                voltage,
                ir,
                true,
                barcode,
                alarms,
                "SIMULATEUR");
        }

        private void HandleMeasurement(
            MachineConfig cfg,
            ThresholdSet localThresholds,
            ThresholdSet observedThresholds,
            bool observedThresholdsFresh,
            List<string> notes,
            int? handshakeValue,
            bool acceptedNewCycle,
            int? statusValue,
            ushort[] measurementRegs,
            ushort[] displayRegs,
            ushort[] alarmRegs,
            double voltage,
            double ir,
            bool measurementAvailable,
            string barcode,
            List<int> alarms,
            string source)
        {
            var legacyRecipe = BuildLegacyRecipe(cfg, localThresholds);
            var legacyChannel = "NG";
            var shadowChannel = "NG";
            var thresholdSource = observedThresholds != null ? (observedThresholdsFresh ? "SHADOW" : "SHADOW_CACHE") : "LOCAL";
            if (measurementAvailable)
            {
                legacyChannel = _legacyEngine.SortToLane(voltage, ir, legacyRecipe);
                shadowChannel = legacyChannel;
                if (observedThresholds != null)
                {
                    var observedRecipe = BuildLegacyRecipe(cfg, observedThresholds);
                    shadowChannel = _legacyEngine.SortToLane(voltage, ir, observedRecipe);
                }
            }

            var appliedChannel = shadowChannel;
            var appliedThresholdSource = thresholdSource;
            var isNew = IsNewMeasurementEvent(acceptedNewCycle);
            CellDecision decision = null;
            LotSession activeLot = null;
            var laneFullSignal = false;
            var machineBlocked = false;
            var laneFullSignalHandled = false;
            var pusherScheduleAttempted = false;
            var pusherScheduleOk = false;
            var pusherScheduleLane = "--";
            var pusherScheduleMode = "NONE";
            var scannerFallbackResult = ResolveScannerFallbackResultNoLock(cfg, barcode);
            var scannerFallbackApplied = false;
            var scannerHandshakeResponseStatus = "NONE";

            var measurement = new CellMeasurement
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Handshake = handshakeValue,
                Voltage = voltage,
                Ir = ir,
                Barcode = barcode,
                ScannerActive = cfg.ScanEnabled,
                MachineBlocked = false,
                MeasurementAvailable = measurementAvailable,
                MachineLaneFullSignal = false,
                MachineCountersAuthoritative = !cfg.UseSimulator && _machineCountersAvailable,
                ActiveAlarms = new List<int>(alarms)
            };

            lock (_lock)
            {
                _connected = true;
                _alarmsActive = alarms;
                LogAlarmChangesNoLock(alarms, source);
                if (_observedThresholds == null && observedThresholds != null)
                {
                    _observedThresholds = CopyThresholds(observedThresholds);
                }
                else if (observedThresholds != null)
                {
                    _observedThresholds = CopyThresholds(observedThresholds);
                }

                activeLot = GetActiveLotNoLock();
                if (isNew)
                {
                    TryAutoResumeLotControlForLiveCycleNoLock(cfg, activeLot, measurement.Timestamp, source);
                }
                SyncLotFromMachineCountersNoLock(cfg, activeLot);
                if (TryAdvanceLaneFromRoutingLedgerNoLock(cfg, activeLot, measurement.Timestamp, source))
                {
                    TrySyncMachineThresholdsNoLock(cfg, localThresholds, activeLot, notes);
                }
                laneFullSignal = IsLaneFullSignalActiveNoLock(cfg, activeLot, alarms);
                machineBlocked = IsBlockingAlarmActive(alarms, laneFullSignal);
                measurement.MachineBlocked = machineBlocked;
                measurement.MachineLaneFullSignal = laneFullSignal;
                measurement.MachineCountersAuthoritative = !cfg.UseSimulator && _machineCountersAvailable;
                laneFullSignalHandled = TryHandleImmediateLaneFullNoLock(cfg, activeLot, laneFullSignal, measurement.Timestamp, source, alarms);
                if (laneFullSignalHandled)
                {
                    // Le signal plein a déjà été traduit en état de ligne / bascule.
                    // On ne le réinjecte pas dans le moteur pour éviter une double bascule.
                    measurement.MachineLaneFullSignal = false;
                }
                appliedChannel = ResolveAppliedRoutingChannelNoLock(
                    cfg,
                    localThresholds,
                    observedThresholds,
                    observedThresholdsFresh,
                    activeLot,
                    voltage,
                    ir,
                    measurementAvailable,
                    out appliedThresholdSource);
                if (isNew && _lotControlEnabled && _operatorStartArmed)
                {
                    activeLot = EnsureActiveLotNoLock(cfg);
                    SyncLotFromMachineCountersNoLock(cfg, activeLot);
                    if (TryAdvanceLaneFromRoutingLedgerNoLock(cfg, activeLot, measurement.Timestamp, source))
                    {
                        TrySyncMachineThresholdsNoLock(cfg, localThresholds, activeLot, notes);
                    }
                    appliedChannel = ResolveAppliedRoutingChannelNoLock(
                        cfg,
                        localThresholds,
                        observedThresholds,
                        observedThresholdsFresh,
                        activeLot,
                        voltage,
                        ir,
                        measurementAvailable,
                        out appliedThresholdSource);
                    var laneBeforeEvaluation = activeLot == null ? null : activeLot.CurrentGoodLane;
                    if (!string.IsNullOrWhiteSpace(scannerFallbackResult))
                    {
                        decision = BuildScannerFallbackDecisionNoLock(cfg, activeLot, scannerFallbackResult);
                        scannerFallbackApplied = true;
                        appliedChannel = "NG";
                        appliedThresholdSource = "SCANNER_FALLBACK";
                        if (cfg.SortingMode == SortingModes.IntelligentGoodNg)
                        {
                            ApplyScannerFallbackToIntelligentLotNoLock(activeLot, decision, measurement, source);
                        }
                    }
                    else if (cfg.SortingMode == SortingModes.IntelligentGoodNg)
                    {
                        decision = _intelligentEngine.Evaluate(measurement, activeLot, CopyIntelligentRecipe(_intelligentRecipes[cfg.CellType]), _laneCapacityObservations);
                        if (measurement.MachineCountersAuthoritative)
                        {
                            SyncLotFromMachineCountersNoLock(cfg, activeLot);
                        }
                    }
                    else
                    {
                        decision = _legacyEngine.Evaluate(measurement, legacyRecipe);
                    }

                    var pauseRequestedBeforeRoutingAlignment = decision != null && decision.PauseRequested;
                    if (decision != null)
                    {
                        var originalDecision = pauseRequestedBeforeRoutingAlignment ? "PAUSE" : decision.Decision;
                        var originalTargetLane = decision.TargetLane;
                        var originalRejectReason = decision.RejectReason;
                        if (cfg.SortingMode == SortingModes.IntelligentGoodNg)
                        {
                            var expectedChannel = ResolveDecisionRoutingChannelNoLock(decision);
                            if (scannerFallbackApplied)
                            {
                                appliedChannel = "NG";
                                appliedThresholdSource = "SCANNER_FALLBACK";
                            }
                            else
                            {
                                appliedChannel = ResolveEffectiveIntelligentRoutingChannelNoLock(cfg, activeLot, decision);
                                appliedThresholdSource = BuildRoutingLedgerThresholdSourceNoLock(expectedChannel, appliedChannel);
                            }
                            RegisterRoutingTicketNoLock(
                                activeLot,
                                measurement,
                                originalDecision,
                                originalTargetLane,
                                appliedChannel,
                                appliedThresholdSource,
                                originalRejectReason,
                                decision);

                            if (AlignDecisionWithAppliedRoutingNoLock(cfg, activeLot, decision, appliedChannel, measurementAvailable, measurement.Timestamp))
                            {
                                _trace.Append(
                                    "DECISION",
                                    "ROUTING_LEDGER_REALIGN",
                                    "APPLIED",
                                    source,
                                    "ROUTING_LEDGER",
                                    "HS=" + (handshakeValue.HasValue ? handshakeValue.Value.ToString(CultureInfo.InvariantCulture) : "--") +
                                    " voulu=" + (string.IsNullOrWhiteSpace(expectedChannel) ? "NG" : expectedChannel) +
                                    " effectif=" + (string.IsNullOrWhiteSpace(appliedChannel) ? "NG" : appliedChannel),
                                    "Le live, l’historique et les compteurs suivent la voie réellement commandée, pas une intention théorique."
                                );
                            }
                        }
                        else if (AlignDecisionWithAppliedRoutingNoLock(cfg, activeLot, decision, appliedChannel, measurementAvailable, measurement.Timestamp))
                        {
                            _trace.Append(
                                "DECISION",
                                "ROUTING_REALIGN",
                                "APPLIED",
                                source,
                                observedThresholdsFresh ? "1188..1370" : "PROGRAMMED_MODEL",
                                "HS=" + (handshakeValue.HasValue ? handshakeValue.Value.ToString(CultureInfo.InvariantCulture) : "--") +
                                " route=" + (string.IsNullOrWhiteSpace(appliedChannel) ? "NG" : appliedChannel),
                                "Decision locale " + originalDecision +
                                "/" + (string.IsNullOrWhiteSpace(originalTargetLane) ? "--" : originalTargetLane) +
                                "/" + (string.IsNullOrWhiteSpace(originalRejectReason) ? "OK" : originalRejectReason) +
                                " diverge du canal appliqué " + (string.IsNullOrWhiteSpace(appliedChannel) ? "NG" : appliedChannel) +
                                ". Le live, l’historique et les compteurs UI suivent le routage."
                            );
                        }

                        if (pauseRequestedBeforeRoutingAlignment)
                        {
                            _lotControlEnabled = false;
                        }

                        if (decision.PauseRequested &&
                            activeLot != null &&
                            !string.IsNullOrWhiteSpace(decision.AlertMessage))
                        {
                            activeLot.PauseRequested = true;
                            activeLot.AlertMessage = decision.AlertMessage;
                            _lotControlEnabled = false;
                            _forceThresholdSync = false;
                            _suspendThresholdSyncUntil = DateTime.Now.AddSeconds(6);
                            TrySendSafetyStopNoLock(cfg, decision.RejectReason, decision.TargetLane, decision.AlertMessage);
                        }

                        if (cfg.SortingMode == SortingModes.IntelligentGoodNg &&
                            measurement.MachineCountersAuthoritative &&
                            !decision.PauseRequested &&
                            TryAdvanceLaneFromRoutingLedgerNoLock(cfg, activeLot, measurement.Timestamp, source))
                        {
                            TrySyncMachineThresholdsNoLock(cfg, localThresholds, activeLot, notes);
                        }
                    }

                    if (cfg.SortingMode == SortingModes.Legacy && decision != null)
                    {
                        ApplyLegacyDecisionToLot(activeLot, decision);
                    }

                    UpdateLaneObservationMetadata(activeLot);

                    if (cfg.SortingMode == SortingModes.IntelligentGoodNg)
                    {
                        var laneAfterEvaluation = activeLot == null ? null : activeLot.CurrentGoodLane;
                        var qualityIntervalMode = _intelligentRecipes.ContainsKey(cfg.CellType) &&
                            QualityBandRouting.IsQualityIntervalRecipe(_intelligentRecipes[cfg.CellType]);
                        if (measurement.MachineLaneFullSignal)
                        {
                            _trace.Append(
                                "LANE",
                                "FULL_SIGNAL",
                                "DETECTED",
                                source,
                                cfg.AlarmRegister.ToString(CultureInfo.InvariantCulture),
                                BuildAlarmSummary(alarms),
                                "Bac plein détecté sur ligne " + (string.IsNullOrWhiteSpace(laneBeforeEvaluation) ? "--" : laneBeforeEvaluation)
                            );
                        }

                        if (!qualityIntervalMode &&
                            !string.Equals(laneBeforeEvaluation, laneAfterEvaluation, StringComparison.OrdinalIgnoreCase))
                        {
                            var switchReason = measurement.MachineLaneFullSignal ? "bac plein" : "capacité atteinte";
                            if (activeLot != null)
                            {
                                if (string.IsNullOrWhiteSpace(laneAfterEvaluation))
                                {
                                    activeLot.AlertMessage = "Ligne " + laneBeforeEvaluation + " pleine, aucune ligne suivante disponible.";
                                }
                                else
                                {
                                    activeLot.AlertMessage = "Bascule automatique " + laneBeforeEvaluation + " -> " + laneAfterEvaluation + " (" + switchReason + ").";
                                }
                            }

                            TrySyncMachineThresholdsNoLock(cfg, localThresholds, activeLot, notes);

                            _trace.Append(
                                "LANE",
                                "AUTO_SWITCH",
                                string.IsNullOrWhiteSpace(laneAfterEvaluation) ? "PAUSED" : "OK",
                                source,
                                "1188..1370",
                                (string.IsNullOrWhiteSpace(laneBeforeEvaluation) ? "--" : laneBeforeEvaluation) + "->" +
                                (string.IsNullOrWhiteSpace(laneAfterEvaluation) ? "--" : laneAfterEvaluation),
                                "Bascule automatique de ligne sur " + switchReason + "."
                            );
                        }
                    }
                }

                var result = _live == null ? "ATTENTE" : _live.Result;
                var displayChannel = _live == null ? shadowChannel : _live.Channel;
                var targetLane = _live == null ? null : _live.TargetLane;
                var rejectReason = _live == null ? null : _live.RejectReason;
                var learningStatus = activeLot == null ? LearningStatuses.Idle : activeLot.LearningStatus;
                if (isNew && decision != null)
                {
                    displayChannel = ResolveAppliedRoutingChannelNoLock(appliedChannel);
                    result = string.Equals(decision.Decision, "CON", StringComparison.OrdinalIgnoreCase)
                        ? "CON"
                        : (IsGoodRoutingChannelNoLock(displayChannel) ? "GOOD" : "NG");
                    targetLane = decision.TargetLane;
                    rejectReason = decision.RejectReason;
                    learningStatus = decision.LearningStatus;
                    if (decision.PauseRequested)
                    {
                        _lotControlEnabled = false;
                    }
                }
                else if (statusValue.HasValue && statusValue.Value == ResetRequiredStatusCode && IsBlockingAlarmActive(alarms, false))
                {
                    result = BuildMachineBlockedLiveResult(alarms);
                    rejectReason = BuildMachineBlockedRejectReason(alarms);
                }
                else if (statusValue.HasValue && statusValue.Value == ResetRequiredStatusCode)
                {
                    result = "ATTENTE START";
                    rejectReason = "Statut automate 7 : réarmement requis avant DÉMARRER, même sans alarme détaillée visible.";
                }
                else if (!_lotControlEnabled && cfg.SortingMode == SortingModes.IntelligentGoodNg)
                {
                    result = "ATTENTE START";
                }

                if (isNew && handshakeValue.HasValue)
                {
                    pusherScheduleLane = "NG";
                    pusherScheduleMode = "PLC_NG_CATCHALL";
                }

                var displayVoltage = Math.Abs(voltage);
                var displayIr = Math.Abs(ir);

                _live = new LiveReading
                {
                    Voltage = displayVoltage,
                    Ir = displayIr,
                    Channel = displayChannel,
                    Result = result,
                    Barcode = barcode,
                    ShadowChannel = shadowChannel,
                    ThresholdSource = appliedThresholdSource,
                    SortingMode = cfg.SortingMode,
                    CellType = cfg.CellType,
                    TargetLane = targetLane,
                    RejectReason = rejectReason,
                    LearningStatus = learningStatus,
                    CurrentLotId = activeLot == null ? 0 : activeLot.Id,
                    CurrentGoodLane = activeLot == null ? null : activeLot.CurrentGoodLane,
                    NextGoodLane = activeLot == null ? null : activeLot.NextGoodLane,
                    ReferenceSummary = BuildReferenceSummary(activeLot)
                };

                if (isNew)
                {
                    if (decision != null)
                    {
                        scannerHandshakeResponseStatus = SendScannerHandshakeResponseNoLock(
                            cfg,
                            handshakeValue,
                            barcode,
                            scannerFallbackApplied,
                            source);
                        if (string.Equals(scannerHandshakeResponseStatus, "ERROR", StringComparison.OrdinalIgnoreCase) && activeLot != null)
                        {
                            activeLot.AlertMessage = "Réponse scan 8230 non envoyée : vérifier liaison PLC, sinon la machine peut attendre la réponse barcode.";
                        }

                        _total++;
                        if (IsGoodRoutingChannelNoLock(displayChannel))
                        {
                            _good++;
                        }
                        else
                        {
                            _ng++;
                        }

                        var laneCounter = displayChannel;
                        int idx;
                        if (int.TryParse(laneCounter, out idx))
                        {
                            idx -= 1;
                            if (idx >= 0 && idx < _counters.Count)
                            {
                                _counters[idx]++;
                            }
                        }

                        _history.Append(new HistoryRow
                        {
                            Timestamp = measurement.Timestamp,
                            SortingMode = cfg.SortingMode,
                            CellType = cfg.CellType,
                            LotId = activeLot == null ? 0 : activeLot.Id,
                            Voltage = displayVoltage,
                            Ir = displayIr,
                            LegacyChannel = legacyChannel,
                            Channel = displayChannel,
                            Result = result,
                            RejectReason = rejectReason,
                            LearningStatus = learningStatus,
                            Barcode = barcode,
                            ThresholdSource = appliedThresholdSource,
                            OdooLotReference = activeLot == null ? null : activeLot.OdooLotReference,
                            OdooLotName = activeLot == null ? null : activeLot.OdooLotName,
                            OdooProductReference = activeLot == null ? null : activeLot.OdooProductReference,
                            OdooProductName = activeLot == null ? null : activeLot.OdooProductName
                        });

                        _observations.Append(new ObservationRow
                        {
                            Timestamp = measurement.Timestamp,
                            Source = source,
                            Handshake = handshakeValue,
                            SortingMode = cfg.SortingMode,
                            CellType = cfg.CellType,
                            Voltage = displayVoltage,
                            Ir = displayIr,
                            Barcode = barcode,
                            LegacyChannel = legacyChannel,
                            Channel = displayChannel,
                            Result = result,
                            RejectReason = rejectReason,
                            LearningStatus = learningStatus,
                            AlarmSummary = BuildAlarmSummary(alarms)
                        });

                        _trace.Append(
                            "DECISION",
                            cfg.SortingMode,
                            result,
                            source,
                            handshakeValue.HasValue ? cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture) : string.Empty,
                            "HS=" + (handshakeValue.HasValue ? handshakeValue.Value.ToString(CultureInfo.InvariantCulture) : "--") +
                            " V=" + voltage.ToString("0.0000", CultureInfo.InvariantCulture) +
                            " IR=" + ir.ToString("0.000", CultureInfo.InvariantCulture),
                            "legacy=" + legacyChannel +
                            " applied=" + (string.IsNullOrWhiteSpace(appliedChannel) ? "NG" : appliedChannel) +
                            " displayed=" + (string.IsNullOrWhiteSpace(displayChannel) ? "--" : displayChannel) +
                            " target=" + (string.IsNullOrWhiteSpace(targetLane) ? "--" : targetLane) +
                            " threshold=" + appliedThresholdSource +
                            " learning=" + learningStatus +
                            " reject=" + rejectReason +
                            " pusher=" + (pusherScheduleAttempted ? (pusherScheduleOk ? "SCHEDULED" : "BLOCKED") : "NONE") +
                            " pusher_lane=" + pusherScheduleLane +
                            " pusher_mode=" + pusherScheduleMode +
                            " scanner_response=" + scannerHandshakeResponseStatus +
                            FormatDecisionWindow(decision) +
                            " routing_control=PLC_THRESHOLDS_NG_CATCHALL"
                        );

                        PersistBusiness();
                    }
                    else
                    {
                        _trace.Append(
                            "DECISION",
                            "RAW_CYCLE_ONLY",
                            "IGNORED",
                            source,
                            handshakeValue.HasValue ? cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture) : string.Empty,
                            "HS=" + (handshakeValue.HasValue ? handshakeValue.Value.ToString(CultureInfo.InvariantCulture) : "--") +
                            " V=" + voltage.ToString("0.0000", CultureInfo.InvariantCulture) +
                            " IR=" + ir.ToString("0.000", CultureInfo.InvariantCulture),
                            "Cycle vu par le PC mais non traité par notre moteur (lot non démarré ou aucune décision locale)."
                        );
                    }

                    _lastRecordedHandshake = handshakeValue;
                }
                else if (handshakeValue.HasValue && !_lastRecordedHandshake.HasValue)
                {
                    _lastRecordedHandshake = handshakeValue.Value;
                }

                _diagnostic = BuildDiagnostic(cfg, handshakeValue, statusValue, measurementRegs, displayRegs, alarmRegs, observedThresholds, localThresholds, notes, thresholdSource);
                TrySyncMachineThresholdsNoLock(cfg, localThresholds, activeLot, notes);
            }
        }

        private bool TryReadObservedThresholds(MachineConfig cfg, List<string> notes, out ThresholdSet observed)
        {
            observed = new ThresholdSet { Channels = new List<ChannelThreshold>() };
            for (var i = 0; i < cfg.Channels; i++)
            {
                observed.Channels.Add(new ChannelThreshold());
            }

            var labels = new[] { "V min", "V max", "IR min", "IR max" };
            for (var block = 0; block < ThresholdBaseRegisters.Length; block++)
            {
                ushort[] registers;
                if (!TryReadRegisters(cfg, ThresholdBaseRegisters[block], cfg.Channels * 2, out registers, notes, "Seuils " + labels[block]))
                {
                    return false;
                }

                for (var channel = 0; channel < cfg.Channels; channel++)
                {
                    var index = channel * 2;
                    if (index + 1 >= registers.Length)
                    {
                        break;
                    }

                    var value = ModbusRtuClient.RegistersToFloat(registers[index], registers[index + 1], cfg.SwapWords);
                    if (block == 0) observed.Channels[channel].VoltageMin = value;
                    if (block == 1) observed.Channels[channel].VoltageMax = value;
                    if (block == 2) observed.Channels[channel].IrMin = value;
                    if (block == 3) observed.Channels[channel].IrMax = value;
                }
            }

            return true;
        }

        private DiagnosticSnapshot BuildDiagnostic(
            MachineConfig cfg,
            int? handshakeValue,
            int? statusValue,
            ushort[] measurementRegs,
            ushort[] displayRegs,
            ushort[] alarmRegs,
            ThresholdSet observedThresholds,
            ThresholdSet localThresholds,
            List<string> notes,
            string thresholdSource)
        {
            var activeLot = GetActiveLotNoLock();
            var expectedThresholds = string.Equals(cfg.SortingMode, SortingModes.Legacy, StringComparison.OrdinalIgnoreCase)
                ? localThresholds
                : BuildProgrammableThresholdsNoLock(cfg, localThresholds, activeLot);
            var differences = BuildThresholdDifferences(expectedThresholds, observedThresholds);
            var thresholdStatus = "Lecture locale uniquement";
            if (observedThresholds != null)
            {
                thresholdStatus = differences.Count == 0 ? "Seuils synchronisés avec la machine" : differences.Count + " écart(s) de seuil(s) détecté(s)";
            }

            if (!string.IsNullOrWhiteSpace(thresholdSource))
            {
                notes.Add("Source de comparaison: " + thresholdSource);
            }

            notes.Add(cfg.ObservationOnly
                ? "Observation uniquement : aucune écriture automate n’est envoyée."
                : "Attention : le mode observation n’est plus forcé.");
            notes.Add(cfg.ShadowMode
                ? "Shadow mode actif: comparaison lecture machine vs configuration locale."
                : "Shadow mode inactif.");
            notes.Add("Mode tri actif: " + cfg.SortingMode);
            notes.Add("Trace runtime: " + _trace.CsvPath);
            notes.Add(HasOdooLotAssociation(activeLot)
                ? "Lot de cellules Odoo associé : les commandes cycle 5978 et les seuils 1188..1370 peuvent piloter la ligne GOOD active."
                : "Lot de cellules Odoo non associé : DÉMARRER reste possible avec traçabilité locale uniquement.");
            if (activeLot != null)
            {
                notes.Add("Lot courant #" + activeLot.Id + " - " + activeLot.LearningStatus);
                if (!string.IsNullOrWhiteSpace(activeLot.CurrentGoodLane))
                {
                    notes.Add("Ligne bonne courante: " + activeLot.CurrentGoodLane);
                }
            }

            var physicalRouting = BuildPhysicalRoutingDiagnosticNoLock(cfg, activeLot, expectedThresholds, observedThresholds, thresholdStatus, statusValue, alarmRegs);

            return new DiagnosticSnapshot
            {
                SourceMode = cfg.UseSimulator ? "SIMULATEUR" : "PLC",
                ObservationOnly = cfg.ObservationOnly,
                ShadowMode = cfg.ShadowMode,
                HandshakeRegister = cfg.HandshakeRegister,
                HandshakeValue = handshakeValue,
                HandshakeChangedAt = _lastHandshakeChange == DateTime.MinValue ? null : _lastHandshakeChange.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                StatusRegister = cfg.StatusRegister,
                StatusValue = statusValue,
                MeasurementRegisters = ToList(measurementRegs),
                DisplayRegisters = ToList(displayRegs),
                AlarmRegisters = ToList(alarmRegs),
                ScannerStatus = BuildScannerStatus(cfg),
                ScannerParity = cfg.ScanParity,
                ThresholdsObserved = observedThresholds != null,
                ThresholdStatus = thresholdStatus,
                ThresholdDifferences = differences,
                PhysicalRouting = physicalRouting,
                StartReadiness = BuildStartReadinessDiagnosticNoLock(cfg, activeLot, physicalRouting, differences, thresholdStatus, handshakeValue, statusValue),
                FieldValidation = BuildFieldValidationDiagnosticNoLock(activeLot),
                ObservationEventCount = _observations.Count,
                Notes = new List<string>(notes)
            };
        }

        private FieldValidationDiagnostic BuildFieldValidationDiagnosticNoLock(LotSession activeLot)
        {
            var currentLotId = activeLot == null ? (int?)null : activeLot.Id;
            var result = new FieldValidationDiagnostic
            {
                HasReport = false,
                Verified = false,
                Status = "NO_REPORT",
                ReportPath = null,
                ReportTimestamp = null,
                ReportLotId = null,
                CurrentLotId = currentLotId,
                MatchesCurrentLot = false,
                TraceVerdict = "UNKNOWN",
                CounterVerdict = "UNKNOWN",
                PhysicalObservationVerdict = "UNKNOWN",
                LaneCoverageVerdict = "UNKNOWN",
                Summary = "Aucun rapport terrain operateur trouve. Lancer validate_tricell_field.bat avant l'essai physique.",
                ValidationCommand = "validate_tricell_field.bat 180",
                CheckCommand = "check_tricell_field_result.bat",
                MissingReasons = new List<string> { "Rapport terrain operateur absent." }
            };

            try
            {
                var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (!Directory.Exists(dataDir))
                {
                    return result;
                }

                FileInfo latest = null;
                foreach (var file in new DirectoryInfo(dataDir).GetFiles("field_validation*.md"))
                {
                    if (file.Name.StartsWith("field_validation_codex", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (latest == null || file.LastWriteTime > latest.LastWriteTime)
                    {
                        latest = file;
                    }
                }

                if (latest == null)
                {
                    return result;
                }

                var text = File.ReadAllText(latest.FullName, Encoding.UTF8);
                var traceVerdict = ExtractValidationVerdict(text, "VERDICT_TRACE_LOGICIEL");
                var counterVerdict = ExtractValidationVerdict(text, "VERDICT_COMPTEURS_MACHINE");
                var physicalVerdict = ExtractValidationVerdict(text, "VERDICT_OBSERVATION_PHYSIQUE");
                var laneCoverageVerdict = ExtractValidationVerdict(text, "VERDICT_COUVERTURE_VOIES_GOOD");
                var laneCoverageDetailsOk = HasValidLaneCoverageDetails(text);
                var reportLotId = ExtractValidationReportLotId(text);
                var matchesCurrentLot = reportLotId.HasValue &&
                    currentLotId.HasValue &&
                    reportLotId.Value == currentLotId.Value;
                var hasCompleteConclusion = ContainsIgnoreCase(text, "Preuve terrain complete");
                var verified = string.Equals(traceVerdict, "OK", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(counterVerdict, "OK", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(physicalVerdict, "OK", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(laneCoverageVerdict, "OK", StringComparison.OrdinalIgnoreCase) &&
                    laneCoverageDetailsOk &&
                    matchesCurrentLot &&
                    hasCompleteConclusion;

                var missing = new List<string>();
                if (!string.Equals(traceVerdict, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add("Trace logiciel terrain incomplete.");
                }
                if (!string.Equals(counterVerdict, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add("Delta compteurs machine insuffisant.");
                }
                if (!string.Equals(physicalVerdict, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add("Observation physique operateur incomplete.");
                }
                if (!string.Equals(laneCoverageVerdict, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add("Couverture terrain des lignes GOOD 1-9 incomplete.");
                }
                else if (!laneCoverageDetailsOk)
                {
                    missing.Add("Detail de couverture voies GOOD incoherent ou incomplet.");
                }
                if (!hasCompleteConclusion)
                {
                    missing.Add("Conclusion de preuve terrain complete absente.");
                }
                if (!reportLotId.HasValue)
                {
                    missing.Add("Lot du rapport terrain introuvable.");
                }
                else if (!matchesCurrentLot)
                {
                    missing.Add("Rapport terrain hors lot courant.");
                }

                result.HasReport = true;
                result.Verified = verified;
                result.Status = verified ? "COMPLETE" : "INCOMPLETE";
                result.ReportPath = latest.FullName;
                result.ReportTimestamp = latest.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                result.ReportLotId = reportLotId;
                result.CurrentLotId = currentLotId;
                result.MatchesCurrentLot = matchesCurrentLot;
                result.TraceVerdict = string.IsNullOrWhiteSpace(traceVerdict) ? "UNKNOWN" : traceVerdict;
                result.CounterVerdict = string.IsNullOrWhiteSpace(counterVerdict) ? "UNKNOWN" : counterVerdict;
                result.PhysicalObservationVerdict = string.IsNullOrWhiteSpace(physicalVerdict) ? "UNKNOWN" : physicalVerdict;
                result.LaneCoverageVerdict = string.IsNullOrWhiteSpace(laneCoverageVerdict) ? "UNKNOWN" : laneCoverageVerdict;
                result.Summary = verified
                    ? "Rapport terrain valide: traces, compteurs, observations physiques et couverture lignes GOOD sont OK."
                    : "Rapport terrain present mais incomplet. Lancer check_tricell_field_result.bat pour le detail.";
                result.MissingReasons = missing;
                return result;
            }
            catch (Exception ex)
            {
                result.Status = "ERROR";
                result.Summary = "Lecture du rapport terrain impossible: " + ex.Message;
                result.MissingReasons = new List<string> { result.Summary };
                return result;
            }
        }

        private static string ExtractValidationVerdict(string text, string name)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(name))
            {
                return "UNKNOWN";
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var rawLine in lines)
            {
                var line = string.IsNullOrWhiteSpace(rawLine) ? string.Empty : rawLine.Trim();
                if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    line = line.Substring(1).Trim();
                }

                if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon >= 0 && colon + 1 < line.Length)
                {
                    return line.Substring(colon + 1).Trim().ToUpperInvariant();
                }
            }

            return "UNKNOWN";
        }

        private static int? ExtractValidationReportLotId(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var rawLine in lines)
            {
                var line = string.IsNullOrWhiteSpace(rawLine) ? string.Empty : rawLine.Trim();
                var marker = "Lot: #";
                var markerIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                {
                    continue;
                }

                var start = markerIndex + marker.Length;
                var end = start;
                while (end < line.Length && char.IsDigit(line[end]))
                {
                    end++;
                }

                int lotId;
                if (end > start &&
                    int.TryParse(line.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out lotId))
                {
                    return lotId;
                }
            }

            return null;
        }

        private static bool HasValidLaneCoverageDetails(string text)
        {
            var required = ExtractValidationLaneList(text, "Couverture voies GOOD requise");
            var observed = ExtractValidationLaneList(text, "Couverture voies GOOD observee");
            var missing = ExtractValidationLaneList(text, "Couverture voies GOOD manquante");
            if (required.Count == 0 || observed.Count == 0 || missing.Count > 0)
            {
                return false;
            }

            var observedSet = new HashSet<string>(observed, StringComparer.OrdinalIgnoreCase);
            foreach (var lane in required)
            {
                if (!observedSet.Contains(lane))
                {
                    return false;
                }
            }

            return ValidationMinimumsCoverRequiredLaneCount(text, required.Count);
        }

        private static List<string> ExtractValidationLaneList(string text, string label)
        {
            var value = ExtractValidationLineValue(text, label);
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value.Trim(), "aucune", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parts = value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawPart in parts)
            {
                var lane = (rawPart ?? string.Empty).Trim().ToUpperInvariant();
                if (lane.StartsWith("L", StringComparison.OrdinalIgnoreCase) && lane.Length > 1)
                {
                    lane = lane.Substring(1);
                }

                if (string.IsNullOrWhiteSpace(lane) || string.Equals(lane, "NG", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen.Add(lane))
                {
                    result.Add(lane);
                }
            }

            return result;
        }

        private static string ExtractValidationLineValue(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var rawLine in lines)
            {
                var line = string.IsNullOrWhiteSpace(rawLine) ? string.Empty : rawLine.Trim();
                if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    line = line.Substring(1).Trim();
                }

                if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var colon = line.IndexOf(':');
                if (colon >= 0 && colon + 1 < line.Length)
                {
                    return line.Substring(colon + 1).Trim();
                }
            }

            return string.Empty;
        }

        private static bool ValidationMinimumsCoverRequiredLaneCount(string text, int requiredLaneCount)
        {
            if (requiredLaneCount <= 0 || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                if (line.IndexOf("Minimums effectifs:", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                int tops;
                int counters;
                int observations;
                return TryExtractIntAfter(line, "tops=", out tops) &&
                    TryExtractIntAfter(line, "compteurs=", out counters) &&
                    TryExtractIntAfter(line, "observations=", out observations) &&
                    tops >= requiredLaneCount &&
                    counters >= requiredLaneCount &&
                    observations >= requiredLaneCount;
            }

            return false;
        }

        private static bool TryExtractIntAfter(string text, string marker, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(marker))
            {
                return false;
            }

            var markerIndex = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return false;
            }

            var start = markerIndex + marker.Length;
            var end = start;
            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }

            return end > start &&
                int.TryParse(text.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private StartReadinessDiagnostic BuildStartReadinessDiagnosticNoLock(
            MachineConfig cfg,
            LotSession activeLot,
            PhysicalRoutingDiagnostic physicalRouting,
            List<ThresholdDifference> thresholdDifferences,
            string thresholdStatus,
            int? handshakeValue,
            int? statusValue)
        {
            var blockingReasons = new List<string>();
            var warnings = new List<string>();
            var operatorChecks = new List<string>
            {
                "Zone machine dégagée et capots/sécurités contrôlés.",
                "Cellule factice ou lot test en place, pas de production utile.",
                "Observer le poussoir 11/NG pousser puis revenir sur chaque cellule non triée GOOD (PLC, voie 11 catch-all).",
                "Comparer au moins 9 cellules: IR, ligne attendue, ligne physique observée, voies GOOD 1-9 couvertes."
            };

            var lotAssociated = HasOdooLotAssociation(activeLot);
            var requiresIntelligentModel = cfg != null &&
                string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase);
            var modelStable = !requiresIntelligentModel ||
                (activeLot != null &&
                    string.Equals(activeLot.LearningStatus, LearningStatuses.Stable, StringComparison.OrdinalIgnoreCase) &&
                    activeLot.Reference != null);
            var thresholdsSynchronized = thresholdDifferences != null &&
                thresholdDifferences.Count == 0 &&
                physicalRouting != null &&
                physicalRouting.ObservedThresholds != null;
            var thresholdsCanBePreloaded = CanPreloadThresholdsBeforeStartNoLock(cfg);
            var machineRequiresReset = statusValue.HasValue && statusValue.Value == ResetRequiredStatusCode;
            var blockingAlarm = IsBlockingAlarmActive(_alarmsActive, false);
            var handshakeReady = cfg != null && (cfg.UseSimulator || handshakeValue.HasValue);

            if (cfg == null)
            {
                blockingReasons.Add("Configuration machine indisponible.");
            }
            else
            {
                if (cfg.UseSimulator)
                {
                    warnings.Add("Mode simulateur actif: ce pré-vol ne prouve pas le tri physique.");
                }

                if (cfg.ObservationOnly)
                {
                    blockingReasons.Add("ObservationOnly actif: aucune commande machine ne sera envoyée.");
                }
            }

            if (!_connected && (cfg == null || !cfg.UseSimulator))
            {
                blockingReasons.Add("PLC non connecté ou lecture mesure indisponible.");
            }

            if (!handshakeReady)
            {
                blockingReasons.Add("Top automate 8230 non lu: DÉMARRER relira 8230 et refusera le départ si la valeur reste indisponible.");
            }

            if (!lotAssociated)
            {
                warnings.Add("Aucun lot Odoo vérifié associé: traçabilité locale uniquement.");
            }

            if (requiresIntelligentModel && !modelStable)
            {
                warnings.Add("Modèle en apprentissage: START_PRELOAD utilisera la ligne 10 jusqu'à stabilisation, puis les lignes GOOD 1-9.");
            }

            if (!thresholdsSynchronized)
            {
                var preloadMessage = string.IsNullOrWhiteSpace(thresholdStatus)
                    ? "Seuils machine non confirmés."
                    : thresholdStatus;
                if (thresholdsCanBePreloaded)
                {
                    warnings.Add(preloadMessage + " START_PRELOAD reprogrammera 1188..1370 avant 5978=31.");
                }
                else
                {
                    blockingReasons.Add(preloadMessage + " Aucun jeu de seuils local programmable n'est disponible pour START_PRELOAD.");
                }
            }

            if (machineRequiresReset)
            {
                blockingReasons.Add("Statut machine 7: réarmement opérateur requis avant DÉMARRER.");
            }

            if (blockingAlarm)
            {
                blockingReasons.Add("Alarme bloquante active: " + BuildAlarmSummary(_alarmsActive) + ".");
            }
            else
            {
                var alarmSummary = BuildAlarmSummary(_alarmsActive);
                if (!string.IsNullOrWhiteSpace(alarmSummary) &&
                    !string.Equals(alarmSummary, "Aucune", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add("Alarme non bloquante visible: " + alarmSummary + ".");
                }
            }

            if (activeLot != null && activeLot.PauseRequested)
            {
                warnings.Add("Lot en pause: DÉMARRER reprendra le lot courant après préchargement seuils.");
            }

            if (_pusherEnablesSnapshot != null && _pusherEnablesSnapshot.Length > NgCounterIndex)
            {
                var disarmed = new List<string>();
                for (var index = 0; index <= NgCounterIndex; index++)
                {
                    if (_pusherEnablesSnapshot[index] == 0)
                    {
                        disarmed.Add(index == NgCounterIndex ? "NG" : (index + 1).ToString(CultureInfo.InvariantCulture));
                    }
                }

                if (disarmed.Count > 0)
                {
                    warnings.Add("Stations pistons désarmées (enable=0) : " + string.Join(",", disarmed.ToArray()) + ". Ces pistons ne tireront pas : utiliser 'Armer toutes les stations pistons' en Maintenance.");
                }
            }

            return new StartReadinessDiagnostic
            {
                ReadyToStart = blockingReasons.Count == 0,
                Connected = _connected || (cfg != null && cfg.UseSimulator),
                HandshakeReady = handshakeReady,
                HandshakeRegister = cfg == null ? 0 : cfg.HandshakeRegister,
                HandshakeValue = handshakeValue,
                HandshakeChangedAt = _lastHandshakeChange == DateTime.MinValue ? null : _lastHandshakeChange.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                LotAssociated = lotAssociated,
                ModelStable = modelStable,
                ThresholdsSynchronized = thresholdsSynchronized,
                MachineRequiresReset = machineRequiresReset,
                BlockingAlarmActive = blockingAlarm,
                OperatorConfirmationRequired = true,
                MachineStatus = statusValue,
                LotStatus = activeLot == null ? "NO_LOT" : (activeLot.PauseRequested ? "PAUSED" : "ACTIVE"),
                ExpectedLane = physicalRouting == null ? null : physicalRouting.ExpectedLane,
                AppliedLane = physicalRouting == null ? null : physicalRouting.AppliedLane,
                BlockingReasons = blockingReasons,
                Warnings = warnings,
                OperatorChecks = operatorChecks
            };
        }

        private bool CanPreloadThresholdsBeforeStartNoLock(MachineConfig cfg)
        {
            if (cfg == null)
            {
                return false;
            }

            if (cfg.UseSimulator)
            {
                return true;
            }

            if (_thresholds == null || !_thresholds.ContainsKey(cfg.CellType))
            {
                return false;
            }

            if (string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase) &&
                (_intelligentRecipes == null || !_intelligentRecipes.ContainsKey(cfg.CellType)))
            {
                return false;
            }

            return true;
        }

        private PhysicalRoutingDiagnostic BuildPhysicalRoutingDiagnosticNoLock(
            MachineConfig cfg,
            LotSession activeLot,
            ThresholdSet programmedThresholds,
            ThresholdSet observedThresholds,
            string thresholdStatus,
            int? statusValue,
            ushort[] alarmRegs)
        {
            var lastTicket = FindLastRoutingTicketNoLock(activeLot);
            var expectedLane = ResolveDiagnosticExpectedLaneNoLock(activeLot, lastTicket);
            var appliedLane = ResolveDiagnosticAppliedLaneNoLock(lastTicket);
            var confirmedLane = ResolveDiagnosticConfirmedLaneNoLock(lastTicket, appliedLane);

            return new PhysicalRoutingDiagnostic
            {
                ExpectedLane = expectedLane,
                AppliedLane = appliedLane,
                ConfirmedLane = confirmedLane,
                HandshakeRegister = cfg.HandshakeRegister,
                LastHandshake = _lastHandshakeValue,
                StatusRegister = cfg.StatusRegister,
                MachineStatus = statusValue,
                ThresholdStatus = thresholdStatus,
                ProgrammedThresholds = CopyThresholds(programmedThresholds),
                ObservedThresholds = observedThresholds == null ? null : CopyThresholds(observedThresholds),
                AlarmRegisters = ToList(alarmRegs),
                AlarmSummary = BuildAlarmSummary(_alarmsActive),
                LastNgPulse = CopyNgPulseDiagnostic(_lastNgPulseDiagnostic),
                PhysicalRoutingMode = "PLC_THRESHOLDS_NG_CATCHALL",
                GoodPusherDirectControlBlocked = true
            };
        }

        private string ResolveDiagnosticExpectedLaneNoLock(LotSession activeLot, RoutingTicket lastTicket)
        {
            if (lastTicket != null && !string.IsNullOrWhiteSpace(lastTicket.IntendedLane))
            {
                return ResolveAppliedRoutingChannelNoLock(lastTicket.IntendedLane);
            }

            if (_live != null && !string.IsNullOrWhiteSpace(_live.TargetLane))
            {
                return ResolveAppliedRoutingChannelNoLock(_live.TargetLane);
            }

            if (activeLot != null && !string.IsNullOrWhiteSpace(activeLot.CurrentGoodLane))
            {
                return ResolveAppliedRoutingChannelNoLock(activeLot.CurrentGoodLane);
            }

            return string.IsNullOrWhiteSpace(_programmedRoutingLaneId) ? "NG" : _programmedRoutingLaneId;
        }

        private string ResolveDiagnosticAppliedLaneNoLock(RoutingTicket lastTicket)
        {
            if (lastTicket != null && !string.IsNullOrWhiteSpace(lastTicket.EffectiveLane))
            {
                return ResolveAppliedRoutingChannelNoLock(lastTicket.EffectiveLane);
            }

            if (!string.IsNullOrWhiteSpace(_programmedRoutingLaneId))
            {
                return ResolveAppliedRoutingChannelNoLock(_programmedRoutingLaneId);
            }

            if (_live != null && !string.IsNullOrWhiteSpace(_live.Channel))
            {
                return ResolveAppliedRoutingChannelNoLock(_live.Channel);
            }

            return "NG";
        }

        private string ResolveDiagnosticConfirmedLaneNoLock(RoutingTicket lastTicket, string appliedLane)
        {
            if (lastTicket == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(lastTicket.ConfirmationLane))
            {
                return ResolveAppliedRoutingChannelNoLock(lastTicket.ConfirmationLane);
            }

            if (string.Equals(lastTicket.Status, RoutingTicketStatuses.Confirmed, StringComparison.OrdinalIgnoreCase))
            {
                return appliedLane;
            }

            return null;
        }

        private RoutingTicket FindLastRoutingTicketNoLock(LotSession lot)
        {
            if (lot == null)
            {
                return null;
            }

            RoutingTicket best = null;
            if (lot.RoutingArchive != null)
            {
                foreach (var ticket in lot.RoutingArchive)
                {
                    best = ChooseLaterRoutingTicket(best, ticket);
                }
            }

            if (lot.RoutingLedger != null && lot.RoutingLedger.Tickets != null)
            {
                foreach (var ticket in lot.RoutingLedger.Tickets)
                {
                    best = ChooseLaterRoutingTicket(best, ticket);
                }
            }

            return best;
        }

        private static RoutingTicket ChooseLaterRoutingTicket(RoutingTicket current, RoutingTicket candidate)
        {
            if (candidate == null)
            {
                return current;
            }

            if (current == null || candidate.Sequence >= current.Sequence)
            {
                return candidate;
            }

            return current;
        }

        private static string ResolveScannerFallbackResultNoLock(MachineConfig cfg, string barcode)
        {
            if (cfg == null || !cfg.ScanEnabled)
            {
                return null;
            }

            var fallback = NormalizeScannerValue(cfg.NoBarcodeValue);
            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = "NG";
            }

            var normalizedBarcode = NormalizeScannerValue(barcode);
            if (!string.IsNullOrWhiteSpace(normalizedBarcode) &&
                !string.Equals(normalizedBarcode, fallback, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return string.Equals(fallback, "CON", StringComparison.OrdinalIgnoreCase) ? "CON" : "NG";
        }

        private static string NormalizeScannerValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }

        private static CellDecision BuildScannerFallbackDecisionNoLock(MachineConfig cfg, LotSession lot, string scannerFallbackResult)
        {
            var result = string.Equals(scannerFallbackResult, "CON", StringComparison.OrdinalIgnoreCase) ? "CON" : "NG";
            return new CellDecision
            {
                SortingMode = cfg == null ? SortingModes.IntelligentGoodNg : cfg.SortingMode,
                Decision = result,
                TargetLane = "NG",
                RejectReason = string.Equals(result, "CON", StringComparison.OrdinalIgnoreCase)
                    ? RejectReasons.ScannerNoBarcodeCon
                    : RejectReasons.ScannerNoBarcodeNg,
                LearningStatus = lot == null ? LearningStatuses.Idle : lot.LearningStatus,
                PauseRequested = false,
                AlertMessage = string.Equals(result, "CON", StringComparison.OrdinalIgnoreCase)
                    ? "Scanner sans code: cellule marquee CON selon NoBarcodeValue."
                    : "Scanner sans code: cellule envoyee NG selon NoBarcodeValue."
            };
        }

        private void ApplyScannerFallbackToIntelligentLotNoLock(LotSession lot, CellDecision decision, CellMeasurement measurement, string source)
        {
            if (lot == null || decision == null)
            {
                return;
            }

            lot.TotalCount++;
            lot.NgCount++;
            lot.PauseRequested = false;
            lot.AlertMessage = decision.AlertMessage;
            _trace.Append(
                "SCANNER",
                "NO_BARCODE_FALLBACK",
                decision.Decision,
                source,
                "HS=" + (measurement != null && measurement.Handshake.HasValue ? measurement.Handshake.Value.ToString(CultureInfo.InvariantCulture) : "--"),
                string.IsNullOrWhiteSpace(measurement == null ? null : measurement.Barcode) ? "--" : measurement.Barcode,
                "Scan actif sans code valide: decision=" + decision.Decision +
                " rejet=" + decision.RejectReason +
                ". La cellule ne modifie pas l'apprentissage qualite."
            );
        }

        private string SendScannerHandshakeResponseNoLock(
            MachineConfig cfg,
            int? handshakeValue,
            string barcode,
            bool scannerFallbackApplied,
            string source)
        {
            if (cfg == null || !cfg.ScanEnabled || !handshakeValue.HasValue)
            {
                return "NONE";
            }

            if (handshakeValue.Value != 1)
            {
                _trace.Append(
                    "HANDSHAKE",
                    "SCAN_RESPONSE",
                    "SKIPPED",
                    source,
                    cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                    handshakeValue.Value.ToString(CultureInfo.InvariantCulture),
                    "Scan actif mais le top accepte n'est pas 1; aucune reponse 8230 envoyee."
                );
                return "SKIPPED";
            }

            var response = scannerFallbackApplied ? (ushort)2 : (ushort)0;
            var detail = scannerFallbackApplied
                ? "Reponse scan constructeur: 8230=2 car barcode absent/repli (" + (string.IsNullOrWhiteSpace(barcode) ? "--" : barcode) + ")."
                : "Reponse scan constructeur: 8230=0 car barcode valide.";

            if (cfg.UseSimulator)
            {
                _trace.Append(
                    "HANDSHAKE",
                    "SCAN_RESPONSE",
                    "SIMULATED",
                    "SIMULATEUR",
                    cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                    response.ToString(CultureInfo.InvariantCulture),
                    detail
                );
                return "SIMULATED_" + response.ToString(CultureInfo.InvariantCulture);
            }

            var ok = WriteHoldingSingleNoLock(cfg, (ushort)cfg.HandshakeRegister, response);
            _trace.Append(
                "HANDSHAKE",
                "SCAN_RESPONSE",
                ok ? "SENT" : "ERROR",
                source,
                cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                response.ToString(CultureInfo.InvariantCulture),
                ok ? detail : "Echec ecriture " + detail
            );
            return ok ? "SENT_" + response.ToString(CultureInfo.InvariantCulture) : "ERROR";
        }

        private void ApplyLegacyDecisionToLot(LotSession lot, CellDecision decision)
        {
            if (lot == null)
            {
                return;
            }

            lot.TotalCount++;
            lot.LearningStatus = LearningStatuses.Idle;
            if (decision.Decision == "GOOD")
            {
                lot.GoodCount++;
            }
            else
            {
                lot.NgCount++;
            }
        }

        private string ResolveAppliedRoutingChannelNoLock(
            MachineConfig cfg,
            ThresholdSet localThresholds,
            ThresholdSet observedThresholds,
            bool observedThresholdsFresh,
            LotSession activeLot,
            double voltage,
            double ir,
            bool measurementAvailable,
            out string appliedThresholdSource)
        {
            appliedThresholdSource = observedThresholdsFresh && observedThresholds != null
                ? "MACHINE_READ"
                : "PROGRAMMED_MODEL";

            if (!measurementAvailable)
            {
                return "NG";
            }

            var routingThresholds = observedThresholdsFresh && observedThresholds != null
                ? observedThresholds
                : BuildProgrammableThresholdsNoLock(cfg, localThresholds, activeLot);
            var routingRecipe = BuildLegacyRecipe(cfg, routingThresholds);
            var rawChannel = _legacyEngine.SortToLane(voltage, ir, routingRecipe);
            return IsGoodRoutingChannelNoLock(rawChannel) ? rawChannel : "NG";
        }

        private string ResolveEffectiveIntelligentRoutingChannelNoLock(
            MachineConfig cfg,
            LotSession activeLot,
            CellDecision decision)
        {
            if (decision == null ||
                !string.Equals(decision.Decision, "GOOD", StringComparison.OrdinalIgnoreCase) ||
                !IsGoodRoutingChannelNoLock(decision.TargetLane))
            {
                return "NG";
            }

            return ResolveDecisionRoutingChannelNoLock(decision);
        }

        private string BuildRoutingLedgerThresholdSourceNoLock(string intendedChannel, string effectiveChannel)
        {
            if (string.Equals(ResolveAppliedRoutingChannelNoLock(intendedChannel), ResolveAppliedRoutingChannelNoLock(effectiveChannel), StringComparison.OrdinalIgnoreCase))
            {
                return "ROUTING_LEDGER";
            }

            return "ROUTING_LEDGER_REALIGN";
        }

        private void RegisterRoutingTicketNoLock(
            LotSession lot,
            CellMeasurement measurement,
            string decision,
            string intendedLane,
            string effectiveLane,
            string thresholdSource,
            string rejectReason,
            CellDecision decisionSnapshot)
        {
            if (lot == null || measurement == null)
            {
                return;
            }

            var ticket = RoutingLedgerService.Append(
                lot,
                measurement.Timestamp,
                measurement.Handshake,
                decision,
                intendedLane,
                ResolveAppliedRoutingChannelNoLock(effectiveLane),
                measurement.Voltage,
                measurement.Ir,
                thresholdSource,
                rejectReason,
                decisionSnapshot == null ? null : decisionSnapshot.RoutingModel,
                decisionSnapshot == null ? null : decisionSnapshot.QualityInterval,
                decisionSnapshot == null ? null : decisionSnapshot.VoltageMin,
                decisionSnapshot == null ? null : decisionSnapshot.VoltageMax,
                decisionSnapshot == null ? null : decisionSnapshot.IrMin,
                decisionSnapshot == null ? null : decisionSnapshot.IrMax);

            _trace.Append(
                "ROUTING",
                "TICKET",
                "RECORDED",
                "LOCAL",
                "HS=" + (measurement.Handshake.HasValue ? measurement.Handshake.Value.ToString(CultureInfo.InvariantCulture) : "--"),
                "#" + ticket.Sequence.ToString(CultureInfo.InvariantCulture) +
                " decision=" + ticket.Decision +
                " voulu=" + (string.IsNullOrWhiteSpace(ticket.IntendedLane) ? "NG" : ticket.IntendedLane) +
                " effectif=" + (string.IsNullOrWhiteSpace(ticket.EffectiveLane) ? "NG" : ticket.EffectiveLane),
                "Cellule engagée dans le registre de routage avant toute bascule de ligne."
            );

        }

        private bool AlignDecisionWithAppliedRoutingNoLock(
            MachineConfig cfg,
            LotSession activeLot,
            CellDecision decision,
            string appliedChannel,
            bool measurementAvailable,
            string timestamp)
        {
            if (decision == null)
            {
                return false;
            }

            var originalDecision = decision.PauseRequested ? "PAUSE" : decision.Decision;
            var originalTargetLane = decision.TargetLane;
            var originalRejectReason = decision.RejectReason;
            var expectedChannel = ResolveDecisionRoutingChannelNoLock(decision);
            var actualChannel = ResolveAppliedRoutingChannelNoLock(appliedChannel);

            if (!measurementAvailable)
            {
                decision.Decision = "NG";
                decision.TargetLane = "NG";
                decision.RejectReason = RejectReasons.NoValidMeasurement;
                decision.PauseRequested = false;
            }
            else if (!decision.PauseRequested &&
                     !string.Equals(originalDecision, "PAUSE", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(expectedChannel, actualChannel, StringComparison.OrdinalIgnoreCase))
            {
                ReconcileLotCountersWithAppliedRouteNoLock(cfg, activeLot, expectedChannel, actualChannel, timestamp);
                if (IsGoodRoutingChannelNoLock(actualChannel))
                {
                    decision.Decision = "GOOD";
                    decision.TargetLane = actualChannel;
                    decision.RejectReason = RejectReasons.None;
                }
                else
                {
                    decision.Decision = "NG";
                    decision.TargetLane = "NG";
                    decision.RejectReason = string.IsNullOrWhiteSpace(originalRejectReason)
                        ? RejectReasons.AppliedRoutingNg
                        : originalRejectReason;
                }

                decision.PauseRequested = false;
                decision.AlertMessage = null;
            }

            return !string.Equals(originalDecision, decision.Decision, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(originalTargetLane, decision.TargetLane, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(originalRejectReason ?? string.Empty, decision.RejectReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private void ReconcileLotCountersWithAppliedRouteNoLock(
            MachineConfig cfg,
            LotSession lot,
            string expectedChannel,
            string actualChannel,
            string timestamp)
        {
            if (lot == null || string.Equals(expectedChannel, actualChannel, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var expectedGood = IsGoodRoutingChannelNoLock(expectedChannel);
            var actualGood = IsGoodRoutingChannelNoLock(actualChannel);

            if (expectedGood)
            {
                var expectedLane = FindLaneInLotNoLock(lot, expectedChannel);
                if (expectedLane != null && expectedLane.CountAssigned > Math.Max(0, expectedLane.MachineCount))
                {
                    expectedLane.CountAssigned--;
                }
            }
            else if (lot.NgCount > 0)
            {
                lot.NgCount--;
            }

            if (actualGood)
            {
                var actualLane = FindLaneInLotNoLock(lot, actualChannel);
                if (actualLane != null)
                {
                    actualLane.CountAssigned++;
                }
            }
            else
            {
                lot.NgCount++;
            }

            if (expectedGood && !actualGood && lot.GoodCount > 0)
            {
                lot.GoodCount--;
            }
            else if (!expectedGood && actualGood)
            {
                lot.GoodCount++;
            }

            var recipe = cfg != null && _intelligentRecipes.ContainsKey(cfg.CellType) ? _intelligentRecipes[cfg.CellType] : null;
            ReflowGoodLaneProgressNoLock(lot, recipe, timestamp);
        }

        private void ReflowGoodLaneProgressNoLock(LotSession lot, IntelligentRecipe recipe, string timestamp)
        {
            if (lot == null || recipe == null || recipe.GoodLanes == null || recipe.GoodLanes.Count == 0)
            {
                return;
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                lot.PauseRequested = false;
                lot.NextGoodLane = lot.Reference == null || lot.LearningStatus != LearningStatuses.Stable ? "1" : null;
                return;
            }

            var previousCurrentLane = lot.CurrentGoodLane;
            string currentLaneId = null;
            foreach (var laneId in recipe.GoodLanes)
            {
                var lane = FindLaneInLotNoLock(lot, laneId);
                if (lane == null || !string.Equals(lane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var capacity = ResolveLaneCapacityNoLock(lane, recipe);
                var physicallyFull = capacity > 0 && lane.MachineCount >= capacity;
                var alreadyClosed = !string.IsNullOrWhiteSpace(lane.LastSwitchOut);
                if (capacity > 0 && (lane.CountAssigned >= capacity || physicallyFull || alreadyClosed))
                {
                    lane.Status = LaneStatuses.Full;
                    if (string.IsNullOrWhiteSpace(lane.LastSwitchOut))
                    {
                        lane.LastSwitchOut = timestamp;
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(currentLaneId))
                {
                    currentLaneId = lane.LaneId;
                    if (lane.Status != LaneStatuses.Blocked)
                    {
                        lane.Status = LaneStatuses.Available;
                    }
                    lane.LastSwitchOut = null;
                    if (string.IsNullOrWhiteSpace(lane.LastSwitchIn) && !string.Equals(lane.LaneId, recipe.GoodLanes[0], StringComparison.OrdinalIgnoreCase))
                    {
                        lane.LastSwitchIn = timestamp;
                    }
                }
                else if (lane.Status != LaneStatuses.Blocked)
                {
                    lane.Status = LaneStatuses.Available;
                    if (lane.CountAssigned <= 0)
                    {
                        lane.LastSwitchOut = null;
                    }
                }
            }

            lot.CurrentGoodLane = currentLaneId;
            lot.NextGoodLane = FindNextAvailableLaneIdNoLock(lot, recipe, currentLaneId);
            if (!string.IsNullOrWhiteSpace(currentLaneId))
            {
                lot.PauseRequested = false;
                if (!string.Equals(previousCurrentLane, currentLaneId, StringComparison.OrdinalIgnoreCase))
                {
                    _lastThresholdProgramSignature = null;
                    _lastThresholdControlSignature = null;
                    _forceThresholdSync = true;
                }
            }
        }

        private string ResolveDecisionRoutingChannelNoLock(CellDecision decision)
        {
            if (decision != null &&
                string.Equals(decision.Decision, "GOOD", StringComparison.OrdinalIgnoreCase) &&
                IsGoodRoutingChannelNoLock(decision.TargetLane))
            {
                return decision.TargetLane;
            }

            return "NG";
        }

        private static string FormatDecisionWindow(CellDecision decision)
        {
            if (decision == null ||
                !decision.VoltageMin.HasValue ||
                !decision.VoltageMax.HasValue ||
                !decision.IrMin.HasValue ||
                !decision.IrMax.HasValue)
            {
                return string.Empty;
            }

            return " window_v=" +
                decision.VoltageMin.Value.ToString("0.000000", CultureInfo.InvariantCulture) +
                ".." +
                decision.VoltageMax.Value.ToString("0.000000", CultureInfo.InvariantCulture) +
                " window_ir=" +
                decision.IrMin.Value.ToString("0.000", CultureInfo.InvariantCulture) +
                ".." +
                decision.IrMax.Value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private string ResolveAppliedRoutingChannelNoLock(string channel)
        {
            return IsGoodRoutingChannelNoLock(channel) ? channel : "NG";
        }

        private static bool IsGoodRoutingChannelNoLock(string channel)
        {
            int lane;
            return int.TryParse(channel, out lane) && lane >= 1 && lane <= NgCounterIndex;
        }

        private bool IsNewMeasurementEvent(bool acceptedNewCycle)
        {
            return acceptedNewCycle;
        }

        private bool EvaluateCycleGate(MachineConfig cfg, int handshakeValue)
        {
            lock (_lock)
            {
                var changed = !_lastHandshakeValue.HasValue || _lastHandshakeValue.Value != handshakeValue;
                if (!changed)
                {
                    return false;
                }

                if (cfg.ScanEnabled && handshakeValue != 1)
                {
                    return false;
                }

                return _lastRecordedHandshake.HasValue;
            }
        }

        private bool IsBlockingAlarmActive(List<int> alarms, bool laneFullSignal)
        {
            if (alarms == null)
            {
                return false;
            }

            foreach (var alarm in alarms)
            {
                if (alarm == 2 || alarm == 3 || alarm == 13 || alarm == 14)
                {
                    return true;
                }

                if (alarm == 18 || alarm == 19)
                {
                    continue;
                }
            }

            return false;
        }

        private static string BuildMachineBlockedLiveResult(List<int> alarms)
        {
            if (HasAlarm(alarms, 13))
            {
                return "PRESSION AIR";
            }

            if (HasAlarm(alarms, 2))
            {
                return "ARRÊT URGENCE";
            }

            return "DÉPART BLOQUÉ";
        }

        private static string BuildMachineBlockedRejectReason(List<int> alarms)
        {
            if (HasAlarm(alarms, 13))
            {
                return "Pression d'air insuffisante";
            }

            if (HasAlarm(alarms, 2))
            {
                return "Arrêt d'urgence appuyé";
            }

            return "Départ bloqué par l'automate";
        }

        private static bool HasAlarm(List<int> alarms, int alarmIndex)
        {
            if (alarms == null)
            {
                return false;
            }

            return alarms.Contains(alarmIndex);
        }

        private bool IsLaneFullSignalActiveNoLock(MachineConfig cfg, LotSession activeLot, List<int> alarms)
        {
            if (alarms == null)
            {
                return false;
            }

            var hasAlarm18 = false;
            var hasAlarm19 = false;
            foreach (var alarm in alarms)
            {
                if (alarm == 18)
                {
                    hasAlarm18 = true;
                }

                if (alarm == 19)
                {
                    hasAlarm19 = true;
                }
            }

            if (cfg == null ||
                activeLot == null ||
                !string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var recipe = _intelligentRecipes.ContainsKey(cfg.CellType) ? _intelligentRecipes[cfg.CellType] : null;
            if (recipe != null && QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return false;
            }

            if (hasAlarm19)
            {
                return true;
            }

            if (!hasAlarm18)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(ResolveLaneFullTargetLaneIdNoLock(activeLot, recipe));
        }

        private LegacyRecipe BuildLegacyRecipe(MachineConfig cfg, ThresholdSet thresholds)
        {
            return new LegacyRecipe
            {
                CellType = cfg.CellType,
                JudgeMode = cfg.JudgeMode,
                ChannelStart = cfg.ChannelStart,
                ChannelEnd = cfg.ChannelEnd,
                NegativeVoltageToNg = cfg.NegativeVoltageToNg,
                Thresholds = CopyThresholds(thresholds)
            };
        }

        private ThresholdSet BuildProgrammableThresholdsNoLock(MachineConfig cfg, ThresholdSet localThresholds, LotSession activeLot)
        {
            var result = BuildProgrammableThresholdsCoreNoLock(cfg, localThresholds, activeLot);
            ClampThresholdsToConstructorDomainNoLock(result);
            return result;
        }

        private ThresholdSet BuildProgrammableThresholdsCoreNoLock(MachineConfig cfg, ThresholdSet localThresholds, LotSession activeLot)
        {
            if (cfg.SortingMode == SortingModes.Legacy)
            {
                var programmable = CopyThresholds(localThresholds);
                if (programmable == null || programmable.Channels == null)
                {
                    programmable = CreateDisabledThresholds(cfg.Channels);
                }

                var start = Math.Max(1, cfg.ChannelStart);
                var end = Math.Min(cfg.Channels, cfg.ChannelEnd);
                for (var i = 0; i < programmable.Channels.Count; i++)
                {
                    var lane = i + 1;
                    if (lane < start || lane > end)
                    {
                        programmable.Channels[i] = CreateDisabledChannelThreshold();
                    }
                }
                return programmable;
            }

            var disabled = CreateDisabledThresholds(cfg.Channels);
            var recipe = _intelligentRecipes[cfg.CellType];
            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return BuildQualityIntervalProgrammableThresholdsNoLock(cfg, disabled, recipe, activeLot);
            }

            var activeLaneId = ResolveThresholdRoutingLaneIdNoLock(activeLot, recipe);
            if (string.IsNullOrWhiteSpace(activeLaneId) && recipe != null && recipe.GoodLanes != null && recipe.GoodLanes.Count > 0)
            {
                activeLaneId = recipe.GoodLanes[0];
            }

            if (activeLot != null && (activeLot.LearningStatus == LearningStatuses.Unstable || activeLot.PauseRequested))
            {
                ProgramPhysicalNgLaneCatchAllNoLock(cfg, disabled, recipe);
                return disabled;
            }

            int activeLane;
            if (!int.TryParse(activeLaneId, out activeLane))
            {
                return disabled;
            }

            var laneIndex = activeLane - 1;
            if (laneIndex < 0 || laneIndex >= disabled.Channels.Count)
            {
                return disabled;
            }

            if (activeLot == null || activeLot.Reference == null || activeLot.LearningStatus != LearningStatuses.Stable)
            {
                disabled.Channels[laneIndex] = CreateLearningChannelThreshold();
                ProgramPhysicalNgLaneCatchAllNoLock(cfg, disabled, recipe);
                return disabled;
            }

            var windowVoltage = IsReferenceConfirmationActiveNoLock(activeLot, recipe)
                ? BuildReferenceConfirmationRoutingWindowNoLock(activeLot, recipe, true)
                : BuildAdaptiveRoutingWindowNoLock(activeLot, recipe, true);
            var windowIr = IsReferenceConfirmationActiveNoLock(activeLot, recipe)
                ? BuildReferenceConfirmationRoutingWindowNoLock(activeLot, recipe, false)
                : BuildAdaptiveRoutingWindowNoLock(activeLot, recipe, false);

            disabled.Channels[laneIndex] = new ChannelThreshold
            {
                VoltageMin = activeLot.Reference.MeanVoltage - windowVoltage,
                VoltageMax = activeLot.Reference.MeanVoltage + windowVoltage,
                IrMin = activeLot.Reference.MeanIr - windowIr,
                IrMax = activeLot.Reference.MeanIr + windowIr
            };

            ProgramPhysicalNgLaneCatchAllNoLock(cfg, disabled, recipe);
            return disabled;
        }

        private ThresholdSet BuildQualityIntervalProgrammableThresholdsNoLock(
            MachineConfig cfg,
            ThresholdSet disabled,
            IntelligentRecipe recipe,
            LotSession activeLot)
        {
            if (activeLot != null && activeLot.LearningStatus == LearningStatuses.Unstable)
            {
                ProgramPhysicalNgLaneCatchAllNoLock(cfg, disabled, recipe);
                return disabled;
            }

            if (activeLot == null || activeLot.Reference == null || activeLot.LearningStatus != LearningStatuses.Stable)
            {
                EnableThresholdForLane(disabled, QualityBandRouting.LearningLaneId, CreateLearningChannelThreshold());
                ProgramPhysicalNgLaneCatchAllNoLock(cfg, disabled, recipe);
                return disabled;
            }

            var windows = QualityBandRouting.BuildWindows(recipe, activeLot.Reference, activeLot.RecentSample);
            for (var band = 1; band <= QualityBandRouting.SortLaneCount; band++)
            {
                var lanes = QualityBandRouting.GetBandLanes(band);
                var laneId = lanes.Length == 0
                    ? band.ToString(CultureInfo.InvariantCulture)
                    : lanes[0];
                if (string.IsNullOrWhiteSpace(laneId))
                {
                    continue;
                }

                EnableThresholdForLane(
                    disabled,
                    laneId,
                    QualityBandRouting.BuildThresholdForBand(windows, band));
            }

            ProgramPhysicalNgLaneCatchAllNoLock(cfg, disabled, recipe);
            return disabled;
        }

        private void ProgramPhysicalNgLaneCatchAllNoLock(MachineConfig cfg, ThresholdSet thresholds, IntelligentRecipe recipe)
        {
            // Comportement constructeur: la voie physique NG recoit une fenetre catch-all pour que
            // le PLC pousse et ramene le verin NG (slot 11) sur chaque cellule non captee par une voie GOOD.
            // Le PLC evalue les voies dans l'ordre 1..N avec NG en dernier: la voie NG ne vole pas les GOOD.
            EnableThresholdForLane(thresholds, ResolvePhysicalNgLaneIdNoLock(cfg, recipe), CreateNgCatchAllChannelThreshold());
        }

        private string ResolvePhysicalNgLaneIdNoLock(MachineConfig cfg, IntelligentRecipe recipe)
        {
            var channels = cfg == null || cfg.Channels <= 0 ? 11 : cfg.Channels;
            int configuredLane;
            if (recipe != null &&
                !string.IsNullOrWhiteSpace(recipe.NgLane) &&
                int.TryParse(recipe.NgLane, out configuredLane) &&
                configuredLane > NgCounterIndex &&
                configuredLane <= channels)
            {
                return configuredLane.ToString(CultureInfo.InvariantCulture);
            }

            return Math.Max(NgCounterIndex + 1, channels).ToString(CultureInfo.InvariantCulture);
        }

        private void EnableThresholdForLane(ThresholdSet thresholds, string laneId, ChannelThreshold threshold)
        {
            if (thresholds == null || thresholds.Channels == null)
            {
                return;
            }

            int lane;
            if (!int.TryParse(laneId, out lane))
            {
                return;
            }

            var index = lane - 1;
            if (index < 0 || index >= thresholds.Channels.Count)
            {
                return;
            }

            thresholds.Channels[index] = threshold;
        }

        private ChannelThreshold CreateReferenceWindowThreshold(LotReference reference, double voltageWindow, double irWindow)
        {
            return new ChannelThreshold
            {
                VoltageMin = reference.MeanVoltage - voltageWindow,
                VoltageMax = reference.MeanVoltage + voltageWindow,
                IrMin = reference.MeanIr - irWindow,
                IrMax = reference.MeanIr + irWindow
            };
        }

        private string ResolveThresholdRoutingLaneIdNoLock(LotSession activeLot, IntelligentRecipe recipe)
        {
            if (activeLot == null)
            {
                return null;
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                if (activeLot.Reference == null || activeLot.LearningStatus != LearningStatuses.Stable)
                {
                    return QualityBandRouting.LearningLaneId;
                }

                return DescribeActiveQualityBandLanesNoLock(activeLot, recipe);
            }

            return activeLot.CurrentGoodLane;
        }

        private string DescribeActiveQualityBandLanesNoLock(LotSession lot, IntelligentRecipe recipe)
        {
            if (lot == null || recipe == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            for (var band = 1; band <= QualityBandRouting.SortLaneCount; band++)
            {
                var lanes = QualityBandRouting.GetBandLanes(band);
                var laneId = lanes.Length == 0
                    ? band.ToString(CultureInfo.InvariantCulture)
                    : lanes[0];
                parts.Add("I" + band.ToString(CultureInfo.InvariantCulture) + "=" + laneId);
            }

            return string.Join("/", parts.ToArray());
        }

        private bool IsReferenceConfirmationActiveNoLock(LotSession lot, IntelligentRecipe recipe)
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
            if (!string.Equals(lot.CurrentGoodLane, firstGoodLaneId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var firstLane = FindLaneInLotNoLock(lot, firstGoodLaneId);
            if (firstLane == null)
            {
                return false;
            }

            var bootstrapTarget = Math.Max(1, Math.Min(
                recipe.SampleSize <= 0 ? QualityBandRouting.LearningSampleCount : recipe.SampleSize,
                QualityBandRouting.LearningSampleCount));
            var refinementTarget = ResolveLaneCapacityNoLock(firstLane, recipe);
            return firstLane.CountAssigned >= bootstrapTarget &&
                firstLane.CountAssigned < refinementTarget;
        }

        private double BuildReferenceConfirmationRoutingWindowNoLock(LotSession lot, IntelligentRecipe recipe, bool forVoltage)
        {
            if (lot == null || recipe == null || lot.Reference == null)
            {
                return forVoltage ? 0.02 : 1.5;
            }

            var minWindow = forVoltage ? recipe.MinWindowVoltage : recipe.MinWindowIr;
            var maxWindow = forVoltage ? recipe.MaxWindowVoltage : recipe.MaxWindowIr;
            if (maxWindow <= 0)
            {
                maxWindow = forVoltage ? 0.12 : 4.0;
            }

            return Math.Max(Math.Abs(maxWindow), Math.Abs(minWindow));
        }

        private double BuildAdaptiveRoutingWindowNoLock(LotSession lot, IntelligentRecipe recipe, bool forVoltage)
        {
            if (lot == null || recipe == null || lot.Reference == null)
            {
                return forVoltage ? 0.02 : 1.5;
            }

            double robustWindow;
            if (RobustWindowCalculator.TryBuildIqrHalfWindow(
                lot.RecentSample,
                forVoltage,
                forVoltage ? recipe.MinWindowVoltage : recipe.MinWindowIr,
                forVoltage ? recipe.MaxWindowVoltage : recipe.MaxWindowIr,
                out robustWindow))
            {
                return robustWindow;
            }

            var sigma = forVoltage ? lot.Reference.SigmaVoltage : lot.Reference.SigmaIr;
            var acceptanceK = forVoltage ? recipe.AcceptanceKVoltage : recipe.AcceptanceKIr;
            var minWindow = forVoltage ? recipe.MinWindowVoltage : recipe.MinWindowIr;
            var maxWindow = forVoltage ? recipe.MaxWindowVoltage : recipe.MaxWindowIr;
            var sigmaWindow = acceptanceK * sigma;
            var center = forVoltage ? lot.Reference.MeanVoltage : lot.Reference.MeanIr;
            var sampleWindow = ComputeSampleWindowNoLock(lot.RecentSample, forVoltage, center);
            const double rangeMultiplier = 1.10;
            var paddedRangeWindow = sampleWindow * rangeMultiplier;
            return Clamp(Math.Max(Math.Max(sigmaWindow, paddedRangeWindow), minWindow), minWindow, maxWindow);
        }

        private double ComputeSampleWindowNoLock(List<SamplePoint> sample, bool forVoltage, double center)
        {
            if (sample == null || sample.Count == 0)
            {
                return 0;
            }

            double maxDeviation = 0;
            foreach (var point in sample)
            {
                var value = forVoltage ? point.Voltage : point.Ir;
                maxDeviation = Math.Max(maxDeviation, Math.Abs(value - center));
            }

            return maxDeviation;
        }

        private void TrySyncMachineThresholdsNoLock(MachineConfig cfg, ThresholdSet localThresholds, LotSession activeLot, List<string> notes)
        {
            if (cfg.UseSimulator)
            {
                return;
            }

            if (DateTime.Now < _suspendThresholdSyncUntil)
            {
                notes.Add("Programmation des seuils temporairement suspendue après commande cycle.");
                return;
            }

            if (activeLot != null && activeLot.PauseRequested)
            {
                notes.Add("Programmation des seuils conservée pendant l'arrêt sécurité : pas de désactivation des voies tant que la machine peut encore évacuer des cellules.");
                return;
            }

            if (!_lotControlEnabled && activeLot == null && !_forceThresholdSync)
            {
                return;
            }

            var programmable = BuildProgrammableThresholdsNoLock(cfg, localThresholds, activeLot);
            var signature = BuildThresholdSignature(programmable);
            var controlSignature = BuildThresholdControlSignatureNoLock(cfg, activeLot, programmable);
            if (!_forceThresholdSync)
            {
                if (UseControlSignatureForThresholdSyncNoLock(cfg, activeLot))
                {
                    if (controlSignature == _lastThresholdControlSignature)
                    {
                        return;
                    }
                }
                else if (signature == _lastThresholdProgramSignature)
                {
                    return;
                }
            }

            if (WriteThresholdsNoLock(cfg, programmable, "AUTO_SYNC"))
            {
                _lastThresholdProgramSignature = signature;
                _lastThresholdControlSignature = controlSignature;
                _forceThresholdSync = false;
                _observedThresholds = CopyThresholds(programmable);
                MarkProgrammedRoutingLaneNoLock(cfg, activeLot, programmable, "AUTO_SYNC");
                notes.Add("Seuils machine programmés depuis notre logiciel.");
            }
            else
            {
                notes.Add("Échec de programmation des seuils machine.");
            }
        }

        private bool TryPreloadThresholdsBeforeStartNoLock(MachineConfig cfg, LotSession activeLot)
        {
            if (cfg == null || cfg.UseSimulator)
            {
                return true;
            }

            ThresholdSet localThresholds;
            if (!_thresholds.TryGetValue(cfg.CellType, out localThresholds))
            {
                return false;
            }

            var programmable = BuildProgrammableThresholdsNoLock(cfg, localThresholds, activeLot);
            var signature = BuildThresholdSignature(programmable);
            var controlSignature = BuildThresholdControlSignatureNoLock(cfg, activeLot, programmable);
            if (!WriteThresholdsNoLock(cfg, programmable, "START_PRELOAD"))
            {
                return false;
            }

            _lastThresholdProgramSignature = signature;
            _lastThresholdControlSignature = controlSignature;
            _forceThresholdSync = false;
            _observedThresholds = CopyThresholds(programmable);
            MarkProgrammedRoutingLaneNoLock(cfg, activeLot, programmable, "START_PRELOAD");
            return true;
        }

        private bool TryPrimeHandshakeGateBeforeStartNoLock(MachineConfig cfg, out string message)
        {
            message = null;
            if (cfg == null || cfg.UseSimulator)
            {
                return true;
            }

            var notes = new List<string>();
            var handshakeValue = TryReadSingleRegister(cfg, cfg.HandshakeRegister, notes, "Handshake START");
            if (!handshakeValue.HasValue)
            {
                message = "DÉMARRER bloqué : lecture du top automate 8230 impossible avant START. Vérifier la liaison PLC avant de lancer le tri, sinon la première cellule peut être ignorée.";
                _trace.Append(
                    "HANDSHAKE",
                    "8230_BASELINE",
                    "UNAVAILABLE",
                    "PLC",
                    cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                    string.Empty,
                    message
                );
                return false;
            }

            _lastHandshakeValue = handshakeValue.Value;
            _lastRecordedHandshake = handshakeValue.Value;
            _lastHandshakeChange = DateTime.Now;
            message = "Base 8230 armée avant START: " + handshakeValue.Value.ToString(CultureInfo.InvariantCulture) + ". Le prochain changement sera traité comme cellule réelle.";
            _trace.Append(
                "HANDSHAKE",
                "8230_BASELINE",
                "ARMED",
                "PLC",
                cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                handshakeValue.Value.ToString(CultureInfo.InvariantCulture),
                message
            );
            return true;
        }

        private void MarkProgrammedRoutingLaneNoLock(
            MachineConfig cfg,
            LotSession activeLot,
            ThresholdSet thresholds,
            string reason)
        {
            if (cfg == null ||
                activeLot == null ||
                !string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase) ||
                activeLot.PauseRequested)
            {
                _programmedRoutingLaneId = null;
                return;
            }

            IntelligentRecipe recipe = null;
            if (_intelligentRecipes != null && !string.IsNullOrWhiteSpace(cfg.CellType))
            {
                _intelligentRecipes.TryGetValue(cfg.CellType, out recipe);
            }

            var routingLane = ResolveThresholdRoutingLaneIdNoLock(activeLot, recipe);
            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                if (!string.Equals(_programmedRoutingLaneId, routingLane, StringComparison.OrdinalIgnoreCase))
                {
                    var detail = "Seuils 9 intervalles programmes: ligne 10 apprentissage, puis lignes 1-9 par resistance.";
                    if (activeLot.Reference != null && activeLot.LearningStatus == LearningStatuses.Stable)
                    {
                        var windows = QualityBandRouting.BuildWindows(recipe, activeLot.Reference, activeLot.RecentSample);
                        var intervalDetails = new List<string>();
                        for (var band = 1; band <= QualityBandRouting.SortLaneCount; band++)
                        {
                            intervalDetails.Add("L" + band.ToString(CultureInfo.InvariantCulture) + " " + QualityBandRouting.DescribeBand(windows, band));
                        }

                        detail = "Seuils 9 intervalles programmes: " + string.Join(", ", intervalDetails.ToArray()) + ".";
                    }

                    detail += " NG physique voie " + ResolvePhysicalNgLaneIdNoLock(cfg, recipe) + " en fenetre catch-all constructeur: le PLC pousse et ramene le verin NG sur chaque cellule non captee par une voie GOOD.";

                    _trace.Append(
                        "ROUTING",
                        "PROGRAMMED_BANDS",
                        "ACTIVE",
                        "LOCAL",
                        "1188..1370",
                        string.IsNullOrWhiteSpace(routingLane) ? "--" : routingLane,
                        detail
                    );
                }

                _programmedRoutingLaneId = routingLane;
                return;
            }

            if (!IsGoodRoutingChannelNoLock(routingLane))
            {
                _programmedRoutingLaneId = null;
                return;
            }

            if (!string.Equals(_programmedRoutingLaneId, routingLane, StringComparison.OrdinalIgnoreCase))
            {
                _trace.Append(
                    "ROUTING",
                    "PROGRAMMED_LANE",
                    "ACTIVE",
                    "LOCAL",
                    "1188..1370",
                    "ligne=" + routingLane + " reason=" + reason,
                    "La voie effective utilisée par le registre de routage vient d'être confirmée après programmation machine."
                );
            }

            _programmedRoutingLaneId = routingLane;
        }

        private bool WriteThresholdsNoLock(MachineConfig cfg, ThresholdSet thresholds, string reason)
        {
            if (thresholds == null || thresholds.Channels == null)
            {
                return false;
            }

            if (string.Equals(reason, "AUTO_SYNC", StringComparison.OrdinalIgnoreCase) &&
                TryWriteThresholdDeltaNoLock(cfg, _observedThresholds, thresholds, reason))
            {
                _trace.Append("THRESHOLDS", "PROGRAM_FAST", "SENT", "LOCAL", "1188..1370", cfg.SortingMode, DescribeProgrammableThresholds(cfg, thresholds, reason));
                return true;
            }

            var blocks = new ushort[4][];
            for (var i = 0; i < 4; i++)
            {
                blocks[i] = new ushort[cfg.Channels * 2];
            }

            for (var channel = 0; channel < cfg.Channels; channel++)
            {
                var th = channel < thresholds.Channels.Count ? thresholds.Channels[channel] : CreateDisabledChannelThreshold();
                WriteFloatInto(blocks[0], channel * 2, th.VoltageMin, cfg.SwapWords);
                WriteFloatInto(blocks[1], channel * 2, th.VoltageMax, cfg.SwapWords);
                WriteFloatInto(blocks[2], channel * 2, th.IrMin, cfg.SwapWords);
                WriteFloatInto(blocks[3], channel * 2, th.IrMax, cfg.SwapWords);
            }

            var labels = new[] { "VMIN", "VMAX", "IRMIN", "IRMAX" };
            for (var block = 0; block < ThresholdBaseRegisters.Length; block++)
            {
                var ok = _modbus.TryWriteHoldingRegisters(
                    cfg.ComPort,
                    cfg.BaudRate,
                    (byte)cfg.SlaveId,
                    (ushort)ThresholdBaseRegisters[block],
                    blocks[block]
                );

                if (!ok)
                {
                    _trace.Append("THRESHOLDS", labels[block], "ERROR", "LOCAL", ThresholdBaseRegisters[block].ToString(CultureInfo.InvariantCulture), string.Empty, "Échec d’écriture des seuils machine (" + reason + ").");
                    return false;
                }
            }

            if (!SendThresholdSavePulseNoLock(cfg, reason))
            {
                _trace.Append(
                    "THRESHOLDS",
                    "SAVE_CHANNEL",
                    "ERROR",
                    "LOCAL",
                    CycleCommandRegister.ToString(CultureInfo.InvariantCulture),
                    SaveChannelCode.ToString(CultureInfo.InvariantCulture),
                    "Seuils écrits, mais échec du Save channel machine (" + reason + ")."
                );
                return false;
            }

            _trace.Append("THRESHOLDS", "PROGRAM", "SENT", "LOCAL", "1188..1370", cfg.SortingMode, DescribeProgrammableThresholds(cfg, thresholds, reason));
            return true;
        }

        private bool TryWriteThresholdDeltaNoLock(MachineConfig cfg, ThresholdSet previous, ThresholdSet next, string reason)
        {
            if (cfg == null ||
                previous == null ||
                previous.Channels == null ||
                next == null ||
                next.Channels == null ||
                previous.Channels.Count < cfg.Channels ||
                next.Channels.Count < cfg.Channels)
            {
                return false;
            }

            var labels = new[] { "VMIN_FAST", "VMAX_FAST", "IRMIN_FAST", "IRMAX_FAST" };
            var wroteAny = false;
            for (var block = 0; block < ThresholdBaseRegisters.Length; block++)
            {
                var channel = 0;
                while (channel < cfg.Channels)
                {
                    while (channel < cfg.Channels &&
                        ThresholdValueEquals(GetThresholdBlockValue(previous.Channels[channel], block), GetThresholdBlockValue(next.Channels[channel], block)))
                    {
                        channel++;
                    }

                    if (channel >= cfg.Channels)
                    {
                        break;
                    }

                    var start = channel;
                    while (channel < cfg.Channels &&
                        !ThresholdValueEquals(GetThresholdBlockValue(previous.Channels[channel], block), GetThresholdBlockValue(next.Channels[channel], block)))
                    {
                        channel++;
                    }

                    var count = channel - start;
                    var regs = new ushort[count * 2];
                    for (var i = 0; i < count; i++)
                    {
                        WriteFloatInto(regs, i * 2, GetThresholdBlockValue(next.Channels[start + i], block), cfg.SwapWords);
                    }

                    var register = ThresholdBaseRegisters[block] + (start * 2);
                    var ok = _modbus.TryWriteHoldingRegisters(
                        cfg.ComPort,
                        cfg.BaudRate,
                        (byte)cfg.SlaveId,
                        (ushort)register,
                        regs
                    );

                    if (!ok)
                    {
                        _trace.Append("THRESHOLDS", labels[block], "ERROR", "LOCAL", register.ToString(CultureInfo.InvariantCulture), string.Empty, "Échec écriture rapide des seuils machine (" + reason + ").");
                        return false;
                    }

                    wroteAny = true;
                }
            }

            if (!wroteAny)
            {
                return true;
            }

            if (!SendThresholdSavePulseNoLock(cfg, reason))
            {
                _trace.Append(
                    "THRESHOLDS",
                    "SAVE_CHANNEL_FAST",
                    "ERROR",
                    "LOCAL",
                    CycleCommandRegister.ToString(CultureInfo.InvariantCulture),
                    SaveChannelCode.ToString(CultureInfo.InvariantCulture),
                    "Seuils rapides écrits, mais échec du Save channel machine (" + reason + ")."
                );
                return false;
            }

            return true;
        }

        private static bool ThresholdValueEquals(double left, double right)
        {
            return Math.Abs(left - right) < 0.0000001;
        }

        private static double GetThresholdBlockValue(ChannelThreshold threshold, int block)
        {
            if (threshold == null)
            {
                threshold = CreateDisabledChannelThreshold();
            }

            switch (block)
            {
                case 0:
                    return threshold.VoltageMin;
                case 1:
                    return threshold.VoltageMax;
                case 2:
                    return threshold.IrMin;
                case 3:
                    return threshold.IrMax;
                default:
                    return 0;
            }
        }

        private bool UseControlSignatureForThresholdSyncNoLock(MachineConfig cfg, LotSession activeLot)
        {
            if (cfg == null ||
                activeLot == null ||
                !activeLot.IsActive ||
                !string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            IntelligentRecipe recipe = null;
            if (_intelligentRecipes != null && !string.IsNullOrWhiteSpace(cfg.CellType))
            {
                _intelligentRecipes.TryGetValue(cfg.CellType, out recipe);
            }

            // En mode 9 intervalles les seuils du lot sont figes: changer de ligne live
            // ne doit pas declencher un Save channel pendant que les cellules passent.
            return !QualityBandRouting.IsQualityIntervalRecipe(recipe);
        }

        private string BuildThresholdControlSignatureNoLock(MachineConfig cfg, LotSession activeLot, ThresholdSet thresholds)
        {
            if (!UseControlSignatureForThresholdSyncNoLock(cfg, activeLot))
            {
                return BuildThresholdSignature(thresholds);
            }

            IntelligentRecipe recipe = null;
            if (_intelligentRecipes != null && !string.IsNullOrWhiteSpace(cfg.CellType))
            {
                _intelligentRecipes.TryGetValue(cfg.CellType, out recipe);
            }

            var currentLane = activeLot.CurrentGoodLane ?? string.Empty;
            var routingLane = ResolveThresholdRoutingLaneIdNoLock(activeLot, recipe) ?? string.Empty;
            var referenceState = activeLot.Reference == null
                ? "NOREF"
                : (activeLot.Reference.Status ?? string.Empty);

            return cfg.CellType + "|" +
                activeLot.Id.ToString(CultureInfo.InvariantCulture) + "|" +
                (activeLot.LearningStatus ?? string.Empty) + "|" +
                referenceState + "|" +
                (activeLot.PauseRequested ? "PAUSE" : "RUN") + "|" +
                currentLane + "|" +
                routingLane;
        }

        private bool SendThresholdSavePulseNoLock(MachineConfig cfg, string reason)
        {
            // Comme le logiciel chinois: Save channel 59 ecrit une seule fois, sans remise a 0.
            var writeOk = _modbus.TryWriteHoldingRegisters(
                cfg.ComPort,
                cfg.BaudRate,
                (byte)cfg.SlaveId,
                CycleCommandRegister,
                new ushort[] { SaveChannelCode });

            _trace.Append(
                "THRESHOLDS",
                "SAVE_CHANNEL",
                writeOk ? "WRITE_ONCE" : "WRITE_ERROR",
                "LOCAL",
                CycleCommandRegister.ToString(CultureInfo.InvariantCulture),
                SaveChannelCode.ToString(CultureInfo.InvariantCulture),
                writeOk
                    ? "Save channel 59 appliqué après programmation seuils (" + reason + ") : écrit une seule fois comme le logiciel chinois, le PLC consomme la commande."
                    : "Échec écriture Save channel 59 après programmation seuils (" + reason + ")."
            );

            return writeOk;
        }

        private static ThresholdSet CreateDisabledThresholds(int channels)
        {
            var set = new ThresholdSet { Channels = new List<ChannelThreshold>() };
            for (var i = 0; i < channels; i++)
            {
                set.Channels.Add(CreateDisabledChannelThreshold());
            }
            return set;
        }

        private static ChannelThreshold CreateDisabledChannelThreshold()
        {
            return new ChannelThreshold
            {
                VoltageMin = 99.9,
                VoltageMax = 99.9,
                IrMin = 999.99,
                IrMax = 999.99
            };
        }

        private static ChannelThreshold CreateLearningChannelThreshold()
        {
            // Domaine constructeur uniquement (V 0..99.9, IR 0..999.99): une borne negative
            // n'est jamais matchee par le PLC, la voie resterait muette (bug ligne 10 historique).
            return new ChannelThreshold
            {
                VoltageMin = 0,
                VoltageMax = 4.5,
                IrMin = 0,
                IrMax = 999.99
            };
        }

        private static void ClampThresholdsToConstructorDomainNoLock(ThresholdSet thresholds)
        {
            // Le PLC ne matche que des fenetres dans le domaine de l'UI chinoise:
            // V 0..99.9, IR 0..999.99. Toute valeur hors domaine est ramenee dedans
            // avant programmation pour qu'aucune voie ne devienne silencieuse.
            if (thresholds == null || thresholds.Channels == null)
            {
                return;
            }

            foreach (var channel in thresholds.Channels)
            {
                if (channel == null)
                {
                    continue;
                }

                channel.VoltageMin = Clamp(channel.VoltageMin, 0, 99.9);
                channel.VoltageMax = Clamp(channel.VoltageMax, 0, 99.9);
                channel.IrMin = Clamp(channel.IrMin, 0, 999.99);
                channel.IrMax = Clamp(channel.IrMax, 0, 999.99);
            }
        }

        private static ChannelThreshold CreateNgCatchAllChannelThreshold()
        {
            // Fenetre constructeur la plus large possible, dans le domaine exact de l'UI chinoise
            // (V 0..99.9, IR 0..999.99). Pas de bornes negatives: le PLC compare en valeur absolue
            // (preuve: routage GOOD correct les 4-5 juin avec fenetres positives et V brute negative).
            return new ChannelThreshold
            {
                VoltageMin = 0,
                VoltageMax = 99.9,
                IrMin = 0,
                IrMax = 999.99
            };
        }

        private static void WriteFloatInto(ushort[] target, int offset, double value, bool swapWords)
        {
            var regs = ModbusRtuClient.FloatToRegisters((float)value, swapWords);
            target[offset] = regs[0];
            target[offset + 1] = regs[1];
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private string BuildThresholdSignature(ThresholdSet thresholds)
        {
            if (thresholds == null || thresholds.Channels == null)
            {
                return "NULL";
            }

            var parts = new List<string>();
            for (var i = 0; i < thresholds.Channels.Count; i++)
            {
                var th = thresholds.Channels[i];
                parts.Add(
                    th.VoltageMin.ToString("0.0000", CultureInfo.InvariantCulture) + "|" +
                    th.VoltageMax.ToString("0.0000", CultureInfo.InvariantCulture) + "|" +
                    th.IrMin.ToString("0.000", CultureInfo.InvariantCulture) + "|" +
                    th.IrMax.ToString("0.000", CultureInfo.InvariantCulture));
            }
            return string.Join(";", parts.ToArray());
        }

        private string DescribeProgrammableThresholds(MachineConfig cfg, ThresholdSet thresholds, string reason)
        {
            if (cfg.SortingMode == SortingModes.Legacy)
            {
                return "reason=" + reason + " mode=LEGACY channels=" + cfg.ChannelStart + ".." + cfg.ChannelEnd;
            }

            var activeLot = GetActiveLotNoLock();
            var status = activeLot == null ? "IDLE" : activeLot.LearningStatus;
            var currentLane = activeLot == null ? "--" : activeLot.CurrentGoodLane;
            var routingLane = activeLot == null ? "--" : ResolveThresholdRoutingLaneIdNoLock(activeLot, _intelligentRecipes[cfg.CellType]);
            if (string.IsNullOrWhiteSpace(currentLane))
            {
                var recipe = _intelligentRecipes[cfg.CellType];
                if (recipe != null && recipe.GoodLanes != null && recipe.GoodLanes.Count > 0)
                {
                    currentLane = recipe.GoodLanes[0];
                }
            }
            if (string.IsNullOrWhiteSpace(routingLane))
            {
                routingLane = currentLane;
            }
            return "reason=" + reason +
                   " mode=INTELLIGENT current_lane=" + currentLane +
                   " routing_lane=" + routingLane +
                   " safety_ng=" + ResolvePhysicalNgLaneIdNoLock(cfg, _intelligentRecipes[cfg.CellType]) +
                   " learning=" + status +
                   " stable=" + ((activeLot != null && activeLot.Reference != null) ? "YES" : "NO");
        }

        private MeasurementDecodeCandidate DecodeMeasurementValues(MachineConfig cfg, ushort[] regs, out string note)
        {
            note = null;
            var normalFirst = ModbusRtuClient.RegistersToFloat(regs[0], regs[1], false);
            var normalSecond = ModbusRtuClient.RegistersToFloat(regs[2], regs[3], false);
            var swappedFirst = ModbusRtuClient.RegistersToFloat(regs[0], regs[1], true);
            var swappedSecond = ModbusRtuClient.RegistersToFloat(regs[2], regs[3], true);

            var preferred = cfg.SwapWords
                ? BuildMeasurementCandidate(cfg, swappedFirst, swappedSecond, "CONFIG_SWAP", true)
                : BuildMeasurementCandidate(cfg, normalFirst, normalSecond, "CONFIG_NORMAL", false);
            var alternate = cfg.SwapWords
                ? BuildMeasurementCandidate(cfg, normalFirst, normalSecond, "AUTO_NORMAL", false)
                : BuildMeasurementCandidate(cfg, swappedFirst, swappedSecond, "AUTO_SWAP", true);

            if (!preferred.IsPlausible && alternate.IsPlausible)
            {
                var measurementRegisterText = cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture);
                note = "Decodage mesure auto-corrige: utilisation ordre mots " + (alternate.SwapWords ? "inverse" : "normal") +
                       " pour " + measurementRegisterText + " (" + alternate.Detail + ").";
                _trace.Append("MEASURE", "DECODE", "AUTO_CORRECT", "PLC", measurementRegisterText, alternate.Detail, note);
                return alternate;
            }

            if (!preferred.IsPlausible && !alternate.IsPlausible)
            {
                var measurementRegisterText = cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture);
                note = "Mesure " + measurementRegisterText + " hors plage plausible. Conservation du decodage prefere (" + preferred.Detail + ").";
                _trace.Append("MEASURE", "DECODE", "IMPLAUSIBLE", "PLC", measurementRegisterText, preferred.Detail + " | " + alternate.Detail, note);
            }

            return preferred;
        }

        private NgPusherReleaseResult ReleaseNgPusherResetNoLock(MachineConfig cfg, string action, string source, bool traceWhenAlreadyReleased)
        {
            var result = new NgPusherReleaseResult();
            if (cfg == null)
            {
                result.Message = "Configuration PLC indisponible : libération NG non envoyée.";
                return result;
            }

            ushort resetBefore;
            ushort feedbackBefore;
            ushort enable;
            ushort output;
            result.HasResetBefore = TryReadHoldingSingleNoLock(cfg, NgPusherResetRegister, out resetBefore);
            result.ResetBefore = resetBefore;
            result.HasFeedbackBefore = TryReadHoldingSingleNoLock(cfg, NgPusherReadbackRegister, out feedbackBefore);
            result.FeedbackBefore = feedbackBefore;
            result.HasEnable = TryReadHoldingSingleNoLock(cfg, NgPusherEnableRegister, out enable);
            result.Enable = enable;
            result.HasOutput = TryReadHoldingSingleNoLock(cfg, NgPusherOutputRegister, out output);
            result.Output = output;

            var needsRelease = !result.HasResetBefore || resetBefore != PusherResetReleasedValue;
            if (needsRelease)
            {
                result.WriteSent = true;
                result.WriteOk = _modbus.TryWriteSingleHoldingRegister(
                    cfg.ComPort,
                    cfg.BaudRate,
                    (byte)cfg.SlaveId,
                    NgPusherResetRegister,
                    PusherResetReleasedValue
                );
                Thread.Sleep(150);
            }
            else
            {
                result.WriteOk = true;
            }

            ushort resetAfter;
            ushort feedbackAfter;
            result.HasResetAfter = TryReadHoldingSingleNoLock(cfg, NgPusherResetRegister, out resetAfter);
            result.ResetAfter = resetAfter;
            result.HasFeedbackAfter = TryReadHoldingSingleNoLock(cfg, NgPusherReadbackRegister, out feedbackAfter);
            result.FeedbackAfter = feedbackAfter;

            result.CommandReleased = result.WriteOk &&
                (!result.HasResetAfter || result.ResetAfter == PusherResetReleasedValue);
            result.FeedbackReleased = result.HasFeedbackAfter && result.FeedbackAfter != 0;

            var detail = "reset 4X " + NgPusherResetRegister.ToString(CultureInfo.InvariantCulture) +
                         " " + (result.HasResetBefore ? result.ResetBefore.ToString(CultureInfo.InvariantCulture) : "--") +
                         "->" + (result.HasResetAfter ? result.ResetAfter.ToString(CultureInfo.InvariantCulture) : "--") +
                         ", retour 4X " + NgPusherReadbackRegister.ToString(CultureInfo.InvariantCulture) +
                         " " + (result.HasFeedbackBefore ? result.FeedbackBefore.ToString(CultureInfo.InvariantCulture) : "--") +
                         "->" + (result.HasFeedbackAfter ? result.FeedbackAfter.ToString(CultureInfo.InvariantCulture) : "--") +
                         ", enable 4X " + NgPusherEnableRegister.ToString(CultureInfo.InvariantCulture) +
                         "=" + (result.HasEnable ? result.Enable.ToString(CultureInfo.InvariantCulture) : "--") +
                         ", sortie 4X " + NgPusherOutputRegister.ToString(CultureInfo.InvariantCulture) +
                         "=" + (result.HasOutput ? result.Output.ToString(CultureInfo.InvariantCulture) : "--") +
                         ". Aucune écriture enable/sortie piston n'est envoyée.";

            if (!result.CommandReleased)
            {
                result.Message = "Échec libération reset NG : " + detail;
            }
            else if (!result.FeedbackReleased)
            {
                result.Message = "Commande reset NG relâchée à 1, mais le retour 28689 reste à 0 : vérifier réarmement, air ou blocage mécanique. " + detail;
            }
            else if (result.WriteSent)
            {
                result.Message = "Vérin NG remis au repos côté commande : " + detail;
            }
            else
            {
                result.Message = "Commande reset NG déjà au repos : " + detail;
            }

            if (result.WriteSent || traceWhenAlreadyReleased || !result.CommandReleased)
            {
                _trace.Append(
                    "MAINTENANCE",
                    action,
                    result.CommandReleased ? (result.FeedbackReleased ? (result.WriteSent ? "RELEASED" : "ALREADY_RELEASED") : "RELEASED_FEEDBACK_OPEN") : "ERROR",
                    source,
                    NgPusherResetRegister.ToString(CultureInfo.InvariantCulture),
                    PusherResetReleasedValue.ToString(CultureInfo.InvariantCulture),
                    result.Message
                );
            }

            return result;
        }

        private bool IsNgAutoReleaseBlockedByRunStateNoLock()
        {
            return _config != null &&
                   !_config.UseSimulator &&
                   (_operatorStartArmed || _lotControlEnabled);
        }

        private void EnsureStoppedBeforeResetNoLock(MachineConfig cfg)
        {
            // Flux operateur constructeur: l'automate ignore Reset=26 tant qu'il est en marche
            // (statut 8231=1, constate par lecture directe le 10 juin 2026). Comme l'operateur
            // chinois, on envoie Stop=29 d'abord, puis on attend la sortie du run avant le 26.
            var notes = new List<string>();
            var status = TryReadSingleRegister(cfg, cfg.StatusRegister, notes, "Statut avant RESET");
            if (!status.HasValue || status.Value != RunningStatusCode)
            {
                return;
            }

            _trace.Append(
                "COMMAND",
                "RESET_AUTO_STOP",
                "ATTEMPT",
                "LOCAL",
                cfg.StatusRegister.ToString(CultureInfo.InvariantCulture),
                status.Value.ToString(CultureInfo.InvariantCulture),
                "Machine en marche (statut 1) : STOP envoyé avant le réarmement, comme le flux opérateur constructeur."
            );
            SendCycleCommandNoLock(cfg, StopCycleCode, "RESET_AUTO_STOP");

            int? current = status;
            var deadline = DateTime.Now.AddSeconds(4);
            while (DateTime.Now < deadline)
            {
                Thread.Sleep(250);
                current = TryReadSingleRegister(cfg, cfg.StatusRegister, notes, "Statut après STOP");
                if (current.HasValue && current.Value != RunningStatusCode)
                {
                    break;
                }
            }

            _trace.Append(
                "COMMAND",
                "RESET_AUTO_STOP",
                current.HasValue && current.Value != RunningStatusCode ? "STOPPED" : "TIMEOUT",
                "PLC",
                cfg.StatusRegister.ToString(CultureInfo.InvariantCulture),
                current.HasValue ? current.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                "Statut lu après le STOP préalable au réarmement."
            );
        }

        private bool SendCycleCommandNoLock(MachineConfig cfg, ushort code, string commandName)
        {
            // Logique constructeur (logiciel chinois): une seule ecriture fonction 16 sur 5978,
            // jamais de remise a 0 cote PC. Le PLC consomme la commande et efface le registre.
            // Reecrire 0 apres coup peut effacer la commande avant que l'automate ne la lise.
            var writeOk = _modbus.TryWriteHoldingRegisters(
                cfg.ComPort,
                cfg.BaudRate,
                (byte)cfg.SlaveId,
                CycleCommandRegister,
                new ushort[] { code });

            _trace.Append(
                "COMMAND",
                commandName,
                writeOk ? "WRITE_ONCE" : "WRITE_ERROR",
                "UI",
                "5978",
                code.ToString(CultureInfo.InvariantCulture),
                writeOk
                    ? "Commande cycle écrite une seule fois comme le logiciel chinois : code " + code.ToString(CultureInfo.InvariantCulture) + " (fonction 16). Le PLC consomme la commande et remet 5978 à 0."
                    : "Échec écriture commande cycle sur le registre 5978."
            );

            return writeOk;
        }

        private void TrySendConstructorConnectInitNoLock(MachineConfig cfg)
        {
            if (cfg == null || cfg.UseSimulator || _constructorConnectInitSent)
            {
                return;
            }

            _constructorConnectInitSent = true;
            // Le logiciel chinois ecrit 5978=57 a chaque connexion (MC.Start). On reproduit.
            var ok = _modbus.TryWriteHoldingRegisters(
                cfg.ComPort,
                cfg.BaudRate,
                (byte)cfg.SlaveId,
                CycleCommandRegister,
                new ushort[] { ConnectInitCode });
            _trace.Append(
                "COMMAND",
                "CONNECT_INIT",
                ok ? "WRITE_ONCE" : "WRITE_ERROR",
                "LOCAL",
                "5978",
                ConnectInitCode.ToString(CultureInfo.InvariantCulture),
                ok
                    ? "Initialisation constructeur à la connexion : 5978=57 écrit une seule fois, comme le logiciel chinois."
                    : "Échec initialisation constructeur 5978=57 à la connexion."
            );

            ArmPusherStationsNoLock(cfg, "CONNECT");
        }

        private void ArmPusherStationsNoLock(MachineConfig cfg, string source)
        {
            // Etat production constructeur: chaque station piston (enables 28414..28424) doit
            // valoir 1, sinon le PLC compte la voie mais ne tire jamais le piston (constate par
            // lecture directe le 10 juin 2026). On rearme automatiquement toute station a 0,
            // un registre a la fois, sans jamais ecrire les sorties 28926..28936.
            if (cfg == null || cfg.UseSimulator)
            {
                return;
            }

            var armed = new List<string>();
            var failed = new List<string>();
            for (var index = 0; index <= NgCounterIndex; index++)
            {
                var enableRegister = (ushort)(PusherCylinderEnableBaseRegister + index);
                var laneLabel = index == NgCounterIndex ? "NG" : (index + 1).ToString(CultureInfo.InvariantCulture);
                ushort current;
                if (TryReadHoldingSingleNoLock(cfg, enableRegister, out current) && current != 0)
                {
                    continue;
                }

                var writeOk = WritePistonIoMaintenanceSingleNoLock(cfg, enableRegister, PusherActiveValue);
                Thread.Sleep(40);
                ushort after;
                var hasAfter = TryReadHoldingSingleNoLock(cfg, enableRegister, out after);
                if (writeOk && hasAfter && after != 0)
                {
                    armed.Add(laneLabel);
                }
                else
                {
                    failed.Add(laneLabel);
                }
            }

            if (armed.Count == 0 && failed.Count == 0)
            {
                return;
            }

            _trace.Append(
                "MAINTENANCE",
                "PUSHER_STATIONS_AUTO_ARM",
                failed.Count == 0 ? "ARMED" : "PARTIAL",
                "LOCAL",
                "28414..28424",
                "1",
                "Armement automatique des stations pistons (" + source + "): armees=" +
                (armed.Count > 0 ? string.Join(",", armed.ToArray()) : "aucune") +
                (failed.Count > 0 ? " ; echecs=" + string.Join(",", failed.ToArray()) : "") +
                ". Etat production constructeur; les sorties 28926..28936 ne sont jamais ecrites."
            );
        }

        private bool MachineRequiresResetBeforeStartNoLock(MachineConfig cfg)
        {
            int? status = null;
            var notes = new List<string>();
            var alarms = _alarmsActive == null ? new List<int>() : new List<int>(_alarmsActive);
            if (cfg != null)
            {
                status = TryReadSingleRegister(cfg, cfg.StatusRegister, notes, "Statut START");
                ushort[] alarmRegs;
                if (TryReadRegisters(cfg, cfg.AlarmRegister, 4, out alarmRegs, notes, "Alarmes START"))
                {
                    alarms = DecodeAlarms(alarmRegs);
                    _alarmsActive = alarms;
                }
            }

            if (!status.HasValue && _diagnostic != null)
            {
                status = _diagnostic.StatusValue;
            }

            var blockingAlarm = IsBlockingAlarmActive(alarms, false);
            var requiresReset = status.HasValue && status.Value == ResetRequiredStatusCode;
            _trace.Append(
                "COMMAND",
                "START_STATUS",
                status.HasValue ? "READ" : "UNAVAILABLE",
                status.HasValue ? "PLC" : "LOCAL",
                cfg == null ? string.Empty : cfg.StatusRegister.ToString(CultureInfo.InvariantCulture),
                status.HasValue ? status.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                requiresReset && blockingAlarm
                    ? "Statut automate 7 avec securite bloquante: " + BuildAlarmSummary(alarms) + "."
                    : (requiresReset
                        ? "Statut automate 7: réarmement requis avant DÉMARRER."
                        : "Statut automate avant DÉMARRER.")
            );

            return requiresReset;
        }

        private string BuildMachineRearmBlockedMessageNoLock()
        {
            if (HasAlarm(_alarmsActive, 13))
            {
                return "Démarrage bloqué : pression d'air insuffisante. Vérifier l'arrivée d'air / compresseur, acquitter côté machine ou logiciel constructeur, puis relancer DÉMARRER dans TriCell Pilot.";
            }

            if (HasAlarm(_alarmsActive, 2))
            {
                return "Démarrage bloqué : arrêt d'urgence appuyé. Relâcher l'arrêt d'urgence, vérifier la sécurité, acquitter côté machine ou logiciel constructeur, puis relancer DÉMARRER dans TriCell Pilot.";
            }

            var alarms = BuildAlarmSummary(_alarmsActive);
            if (!string.IsNullOrWhiteSpace(alarms) && !string.Equals(alarms, "Aucune", StringComparison.OrdinalIgnoreCase))
            {
                return "Démarrage bloqué : " + alarms + ". Corriger l'alarme, acquitter côté machine ou logiciel constructeur, puis relancer DÉMARRER dans TriCell Pilot.";
            }

            return "Démarrage bloqué par sécurité machine. Corriger la sécurité active, acquitter côté machine ou logiciel constructeur, puis relancer DÉMARRER.";
        }

        private MaintenanceCommandResult ExecuteConveyorOnlyForwardNoLock(string commandName, int pulseMs, string detail)
        {
            if (IsMaintenanceConveyorBlockedByRunStateNoLock())
            {
                var runBlockMessage = "Avance tapis maintenance bloquée pendant le tri en cours. Utiliser STOP ou PAUSE, attendre l'arrêt des tops 8230, puis relancer l'avance tapis.";
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "BLOCKED_RUN",
                    "LOCAL",
                    _config.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                    _lastRecordedHandshake.HasValue ? _lastRecordedHandshake.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    runBlockMessage
                );
                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = commandName,
                    Message = runBlockMessage,
                    RequiresExpert = false,
                    BlockedBySafety = true,
                    TerrainValidated = true,
                    Simulated = false,
                    Mode = "CONVEYOR_COIL",
                    Register = ConveyorForwardRegister.ToString(CultureInfo.InvariantCulture),
                    Value = "coil"
                };
            }

            var safetyBlock = _config.UseSimulator ? null : BuildPistonSafetyBlockMessageNoLock();
            if (!string.IsNullOrWhiteSpace(safetyBlock))
            {
                _trace.Append(
                    "MAINTENANCE",
                    commandName,
                    "BLOCKED",
                    "PLC",
                    _config.AlarmRegister.ToString(CultureInfo.InvariantCulture),
                    BuildAlarmSummary(_alarmsActive),
                    "Avance tapis non envoyee: bloquee par securite. " + safetyBlock
                );
                return new MaintenanceCommandResult
                {
                    Ok = false,
                    Command = commandName,
                    Message = "Avance tapis bloquée : " + safetyBlock,
                    RequiresExpert = false,
                    BlockedBySafety = true,
                    TerrainValidated = true,
                    Simulated = _config.UseSimulator,
                    Mode = "CONVEYOR_COIL",
                    Register = ConveyorForwardRegister.ToString(CultureInfo.InvariantCulture),
                    Value = "coil"
                };
            }

            _trace.Append(
                "MAINTENANCE",
                commandName,
                "ATTEMPT",
                _config.UseSimulator ? "SIMULATEUR" : "UI",
                ConveyorForwardRegister.ToString(CultureInfo.InvariantCulture),
                "coil",
                detail + " Adresse coil 1X=" + ConveyorForwardRegister.ToString(CultureInfo.InvariantCulture) +
                ", durée impulsion=" + pulseMs.ToString(CultureInfo.InvariantCulture) + " ms."
            );

            if (_config.UseSimulator)
            {
                return new MaintenanceCommandResult
                {
                    Ok = true,
                    Command = commandName,
                    Message = "Simulateur actif : avance tapis tracée seulement.",
                    RequiresExpert = false,
                    TerrainValidated = true,
                    Simulated = true,
                    Mode = "CONVEYOR_COIL",
                    Register = ConveyorForwardRegister.ToString(CultureInfo.InvariantCulture),
                    Value = "coil"
                };
            }

            var ok = SendCoilPulseNoLock(_config, ConveyorForwardRegister, commandName, pulseMs);

            _trace.Append(
                "MAINTENANCE",
                commandName,
                ok ? "SENT" : "ERROR",
                "UI",
                ConveyorForwardRegister.ToString(CultureInfo.InvariantCulture),
                "coil",
                ok
                    ? detail + " Commande terminée."
                    : "Échec avance tapis; vérifier arrêt d'urgence, air et coil convoyeur."
            );

            return new MaintenanceCommandResult
            {
                Ok = ok,
                Command = commandName,
                Message = ok
                    ? (commandName == "CONVEYOR_FINE_FORWARD"
                        ? "Micro-avance tapis envoyée (coil constructeur 1X 5981)."
                        : "Convoyeur avancé (coil constructeur 1X 5981).")
                    : "Échec de l'avance tapis. Vérifier arrêt d'urgence, air et convoyeur.",
                RequiresExpert = false,
                TerrainValidated = true,
                Simulated = false,
                Mode = "CONVEYOR_COIL",
                Register = ConveyorForwardRegister.ToString(CultureInfo.InvariantCulture),
                Value = "coil 1X ON/OFF"
            };
        }

        private bool IsMaintenanceConveyorBlockedByRunStateNoLock()
        {
            return _config != null &&
                   !_config.UseSimulator &&
                   (_operatorStartArmed || _lotControlEnabled);
        }

        private bool SendCoilPulseNoLock(MachineConfig cfg, ushort address, string commandName, int pulseMs)
        {
            return SendCoilPulseNoLock(cfg, address, "MAINTENANCE", commandName, "UI", pulseMs);
        }

        private bool SendCoilPulseNoLock(MachineConfig cfg, ushort address, string traceCategory, string commandName, string source, int pulseMs)
        {
            var writeOk = WriteCoilSingleNoLock(cfg, address, true);

            _trace.Append(
                traceCategory,
                commandName,
                writeOk ? "COIL_ON" : "COIL_ON_ERROR",
                source,
                address.ToString(CultureInfo.InvariantCulture),
                "1",
                writeOk ? "Coil convoyeur activé." : "Échec activation coil convoyeur."
            );

            if (!writeOk)
            {
                return false;
            }

            Thread.Sleep(Math.Max(10, pulseMs));

            var releaseOk = WriteCoilSingleNoLock(cfg, address, false);

            _trace.Append(
                traceCategory,
                commandName,
                releaseOk ? "COIL_OFF" : "COIL_OFF_ERROR",
                source,
                address.ToString(CultureInfo.InvariantCulture),
                "0",
                releaseOk ? "Coil convoyeur relâché." : "Échec relâchement coil convoyeur."
            );

            return releaseOk;
        }

        private string BuildPistonSafetyBlockMessageNoLock()
        {
            if (!IsBlockingAlarmActive(_alarmsActive, false))
            {
                return null;
            }

            if (HasAlarm(_alarmsActive, 2))
            {
                return "Test piston bloqué : arrêt d'urgence actif. Relâcher l'arrêt d'urgence et acquitter la machine avant tout test vérin.";
            }

            if (HasAlarm(_alarmsActive, 13))
            {
                return "Test piston bloqué : pression d'air insuffisante. Vérifier l'arrivée d'air / compresseur avant tout test vérin.";
            }

            var alarms = BuildAlarmSummary(_alarmsActive);
            return "Test piston bloqué par sécurité machine" +
                   (string.IsNullOrWhiteSpace(alarms) ? "." : " : " + alarms + ".");
        }

        private bool SendHoldingPulseNoLock(MachineConfig cfg, ushort register, ushort code, string commandName)
        {
            return SendHoldingPulseNoLock(cfg, register, code, commandName, ConveyorForwardPulseMs);
        }

        private bool SendHoldingPulseNoLock(MachineConfig cfg, ushort register, ushort code, string commandName, int pulseMs)
        {
            var writeOk = WriteHoldingSingleNoLock(cfg, register, code);

            if (!writeOk)
            {
                return false;
            }

            Thread.Sleep(Math.Max(20, pulseMs));

            var releaseOk = WriteHoldingSingleNoLock(cfg, register, 0);

            _trace.Append(
                "MAINTENANCE",
                commandName,
                releaseOk ? "PULSE_RELEASE" : "PULSE_RELEASE_ERROR",
                "UI",
                register.ToString(CultureInfo.InvariantCulture),
                "0",
                releaseOk ? "Relâchement commande maintenance envoyé." : "Échec relâchement commande maintenance (best-effort, impulsion déjà envoyée)."
            );

            return writeOk;
        }

        private bool WriteHoldingSingleNoLock(MachineConfig cfg, ushort register, ushort value)
        {
            if (IsPusherCommandRegister(register))
            {
                _trace.Append(
                    "SAFETY",
                    "PISTON_WRITE_BLOCKED",
                    "BLOCKED",
                    "LOCAL",
                    register.ToString(CultureInfo.InvariantCulture),
                    value.ToString(CultureInfo.InvariantCulture),
                    "Écriture piston interdite hors chemin dédié non-production. En production, le routage passe par les seuils machine et l'automate."
                );
                return false;
            }

            return _modbus.TryWriteSingleHoldingRegister(
                cfg.ComPort,
                cfg.BaudRate,
                (byte)cfg.SlaveId,
                register,
                value
            );
        }

        private bool WriteCoilSingleNoLock(MachineConfig cfg, ushort address, bool value)
        {
            return _modbus.TryWriteSingleCoil(
                cfg.ComPort,
                cfg.BaudRate,
                (byte)cfg.SlaveId,
                address,
                value
            );
        }

        private bool WritePistonIoSingleNoLock(MachineConfig cfg, ushort register, ushort value)
        {
            if (!IsPusherCommandRegister(register))
            {
                return false;
            }

            if (cfg != null && cfg.UseSimulator)
            {
                return true;
            }

            _trace.Append(
                "SAFETY",
                "PISTON_WRITE_BLOCKED",
                "BLOCKED",
                "LOCAL",
                register.ToString(CultureInfo.InvariantCulture),
                value.ToString(CultureInfo.InvariantCulture),
                "Écriture directe piston bloquée en réel. En production, les GOOD passent par les seuils PLC et le NG validé terrain passe par Y11 4X 3144 bit 10."
            );
            return false;
        }

        private bool WritePistonIoMaintenanceSingleNoLock(MachineConfig cfg, ushort register, ushort value)
        {
            if (!IsPusherMaintenanceIoRegister(register))
            {
                _trace.Append(
                    "SAFETY",
                    "PISTON_MAINT_WRITE_BLOCKED",
                    "BLOCKED",
                    "LOCAL",
                    register.ToString(CultureInfo.InvariantCulture),
                    value.ToString(CultureInfo.InvariantCulture),
                    "Écriture maintenance piston bloquée : seuls les enables 28414..28424 et sorties 28926..28936 sont autorisés. Les resets 28295..28305 ne sont pas pulsés."
                );
                return false;
            }

            if (cfg != null && cfg.UseSimulator)
            {
                return true;
            }

            if (cfg == null)
            {
                return false;
            }

            return _modbus.TryWriteSingleHoldingRegister(
                cfg.ComPort,
                cfg.BaudRate,
                (byte)cfg.SlaveId,
                register,
                value
            );
        }

        private static bool IsPusherCommandRegister(ushort register)
        {
            var resetEnd = PusherResetCommandBaseRegister + 10;
            var enableEnd = PusherCylinderEnableBaseRegister + 10;
            var outputEnd = PusherCylinderOutputBaseRegister + 10;
            return (register >= PusherResetCommandBaseRegister && register <= resetEnd) ||
                   (register >= PusherCylinderEnableBaseRegister && register <= enableEnd) ||
                   (register >= PusherCylinderOutputBaseRegister && register <= outputEnd);
        }

        private static bool IsPusherMaintenanceIoRegister(ushort register)
        {
            var enableEnd = PusherCylinderEnableBaseRegister + 10;
            var outputEnd = PusherCylinderOutputBaseRegister + 10;
            return (register >= PusherCylinderEnableBaseRegister && register <= enableEnd) ||
                   (register >= PusherCylinderOutputBaseRegister && register <= outputEnd);
        }

        private bool TryReadHoldingSingleNoLock(MachineConfig cfg, ushort register, out ushort value)
        {
            ushort[] values;
            var ok = _modbus.TryReadHoldingRegisters(
                cfg.ComPort,
                cfg.BaudRate,
                (byte)cfg.SlaveId,
                register,
                1,
                out values
            );

            if (!ok || values == null || values.Length == 0)
            {
                value = 0;
                return false;
            }

            value = values[0];
            return true;
        }

        private void ApplyDetectedSwapWords(MachineConfig cfg, bool swapWords, List<string> notes)
        {
            var changed = false;
            var observeDetail = string.Empty;
            lock (_lock)
            {
                if (_config.SwapWords == swapWords)
                {
                    _pendingSwapWordsCandidate = null;
                    _pendingSwapWordsConfirmations = 0;
                    return;
                }

                if (!_pendingSwapWordsCandidate.HasValue || _pendingSwapWordsCandidate.Value != swapWords)
                {
                    _pendingSwapWordsCandidate = swapWords;
                    _pendingSwapWordsConfirmations = 1;
                    observeDetail = "Ordre des mots Modbus alternatif détecté, confirmation en cours (1/3).";
                }
                else
                {
                    _pendingSwapWordsConfirmations++;
                    if (_pendingSwapWordsConfirmations < 3)
                    {
                        observeDetail = "Ordre des mots Modbus alternatif confirmé à " +
                                        _pendingSwapWordsConfirmations.ToString(CultureInfo.InvariantCulture) +
                                        "/3.";
                    }
                    else
                    {
                        _config.SwapWords = swapWords;
                        if (cfg != null)
                        {
                            cfg.SwapWords = swapWords;
                        }
                        PersistConfig();
                        _forceThresholdSync = true;
                        _pendingSwapWordsCandidate = null;
                        _pendingSwapWordsConfirmations = 0;
                        changed = true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(observeDetail))
            {
                notes.Add(observeDetail);
                _trace.Append("CONFIG", "SWAP_WORDS", "AUTO_OBSERVE", "PLC", cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture), swapWords ? "true" : "false", observeDetail);
            }

            if (!changed)
            {
                return;
            }

            var detail = "Ordre des mots Modbus memorise automatiquement: " + (swapWords ? "inverse" : "normal") + ".";
            notes.Add(detail);
            _trace.Append("CONFIG", "SWAP_WORDS", "AUTO_APPLY", "PLC", cfg.MeasurementRegister.ToString(CultureInfo.InvariantCulture), swapWords ? "true" : "false", detail);
        }

        private MeasurementDecodeCandidate BuildMeasurementCandidate(MachineConfig cfg, float first, float second, string label, bool swapWords)
        {
            var ir = cfg.IrFirst ? first : second;
            var voltage = cfg.IrFirst ? second : first;
            return new MeasurementDecodeCandidate
            {
                Ir = ir,
                Voltage = voltage,
                Label = label,
                SwapWords = swapWords,
                IsPlausible = IsPlausibleMeasurement(voltage, ir),
                Detail = label +
                         " swap=" + swapWords +
                         " ir=" + ir.ToString("0.000", CultureInfo.InvariantCulture) +
                         " voltage=" + voltage.ToString("0.0000", CultureInfo.InvariantCulture)
            };
        }

        private static bool IsPlausibleMeasurement(double voltage, double ir)
        {
            return Math.Abs(voltage) <= 10.0 &&
                   Math.Abs(ir) <= 1000.0 &&
                   !double.IsNaN(voltage) &&
                   !double.IsInfinity(voltage) &&
                   !double.IsNaN(ir) &&
                   !double.IsInfinity(ir);
        }

        private static uint DecodeCounterValue(ushort reg0, ushort reg1)
        {
            var normal = ModbusRtuClient.RegistersToUInt32(reg0, reg1, false);
            var swapped = ModbusRtuClient.RegistersToUInt32(reg0, reg1, true);

            var normalLooksShifted = normal >= 65536 && normal % 65536 == 0;
            var swappedLooksShifted = swapped >= 65536 && swapped % 65536 == 0;
            if (normalLooksShifted && !swappedLooksShifted)
            {
                return swapped;
            }

            if (swappedLooksShifted && !normalLooksShifted)
            {
                return normal;
            }

            if (normal <= 1000000 && swapped > 1000000)
            {
                return normal;
            }

            if (swapped <= 1000000 && normal > 1000000)
            {
                return swapped;
            }

            return Math.Min(normal, swapped);
        }

        private static string CurrentTimestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private IntelligentRecipe TryGetIntelligentRecipeNoLock(MachineConfig cfg)
        {
            if (cfg == null ||
                !string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase) ||
                _intelligentRecipes == null ||
                !_intelligentRecipes.ContainsKey(cfg.CellType))
            {
                return null;
            }

            return _intelligentRecipes[cfg.CellType];
        }

        private LotSession EnsureActiveLotNoLock(MachineConfig cfg)
        {
            var active = GetActiveLotNoLock();
            var recipe = TryGetIntelligentRecipeNoLock(cfg);
            if (active != null &&
                string.Equals(active.CellType, cfg.CellType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(active.SortingMode, cfg.SortingMode, StringComparison.OrdinalIgnoreCase) &&
                IsLotCompatibleWithRecipeNoLock(active, recipe))
            {
                return active;
            }

            if (active != null)
            {
                active.IsActive = false;
                active.ClosedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var lot = new LotSession
            {
                Id = _nextLotId++,
                StartedAt = now,
                ClosedAt = null,
                IsActive = true,
                ShadowOnly = cfg.ShadowMode,
                SortingMode = cfg.SortingMode,
                CellType = cfg.CellType,
                LearningStatus = cfg.SortingMode == SortingModes.IntelligentGoodNg ? LearningStatuses.Idle : LearningStatuses.Idle,
                TotalCount = 0,
                GoodCount = 0,
                NgCount = 0,
                InvalidMeasurementCount = 0,
                LearnedCellCount = 0,
                CurrentGoodLane = null,
                NextGoodLane = null,
                PauseRequested = false,
                AlertMessage = null,
                Reference = null,
                RecentSample = new List<SamplePoint>(),
                Lanes = new List<LaneState>(),
                MachineCounterBaselines = new List<int>(),
                MachineNgBaseline = _machineNg,
                RoutingLedger = new RoutingLedgerState
                {
                    NextSequence = 1,
                    Tickets = new List<RoutingTicket>()
                },
                RoutingArchive = new List<RoutingTicket>()
            };

            if (cfg.SortingMode == SortingModes.IntelligentGoodNg)
            {
                var lotRecipe = _intelligentRecipes[cfg.CellType];
                if (lotRecipe != null && lotRecipe.GoodLanes != null)
                {
                    var qualityIntervalMode = QualityBandRouting.IsQualityIntervalRecipe(lotRecipe);
                    for (var i = 0; i < lotRecipe.GoodLanes.Count; i++)
                    {
                        var laneId = lotRecipe.GoodLanes[i];
                        var observation = qualityIntervalMode ? null : FindLaneObservation(cfg.CellType, laneId);
                        lot.Lanes.Add(new LaneState
                        {
                            LaneId = laneId,
                            Role = "GOOD",
                            Status = LaneStatuses.Available,
                            CapacityTarget = qualityIntervalMode ? 0 : GetLaneCapacity(lotRecipe, laneId),
                            CapacityObserved = observation == null ? 0 : observation.ObservedCount,
                            CapacityConfidence = observation == null ? 0 : observation.Confidence,
                            CountAssigned = 0
                        });
                    }

                    lot.Lanes.Add(new LaneState
                    {
                        LaneId = lotRecipe.NgLane,
                        Role = "NG",
                        Status = LaneStatuses.Available,
                        CapacityTarget = 0,
                        CountAssigned = 0
                    });
                    if (lotRecipe.GoodLanes.Count > 0)
                    {
                        if (QualityBandRouting.IsQualityIntervalRecipe(lotRecipe))
                        {
                            lot.CurrentGoodLane = QualityBandRouting.LearningLaneId;
                            lot.NextGoodLane = "1";
                        }
                        else
                        {
                            lot.CurrentGoodLane = lotRecipe.GoodLanes[0];
                            if (lotRecipe.GoodLanes.Count > 1)
                            {
                                lot.NextGoodLane = lotRecipe.GoodLanes[1];
                            }
                        }
                    }
                }
            }

            if (_machineCountersAvailable && _machineCounters != null && _machineCounters.Count > 0)
            {
                lot.MachineCounterBaselines = new List<int>(_machineCounters);
                lot.MachineNgBaseline = _machineNg;
            }

            _lastThresholdProgramSignature = null;
            _lastThresholdControlSignature = null;
            _forceThresholdSync = true;
            _programmedRoutingLaneId = null;
            _lots.Add(lot);
            return lot;
        }

        private void ResetLotLinesNoLock(LotSession lot, string message, bool clearPhysicalOccupancy)
        {
            if (lot == null)
            {
                return;
            }

            var timestamp = CurrentTimestamp();
            var recipe = TryGetIntelligentRecipeNoLock(_config);
            var qualityIntervalMode = recipe != null && QualityBandRouting.IsQualityIntervalRecipe(recipe);
            var preservedOccupancy = clearPhysicalOccupancy || qualityIntervalMode
                ? new Dictionary<string, LaneOccupancySnapshot>(StringComparer.OrdinalIgnoreCase)
                : CaptureGoodLaneOccupancyNoLock(lot, recipe);

            lot.StartedAt = timestamp;
            lot.IsActive = true;
            lot.ClosedAt = null;
            lot.SortingMode = _config.SortingMode;
            lot.CellType = _config.CellType;
            lot.ShadowOnly = _config.ShadowMode;
            lot.LearningStatus = LearningStatuses.Idle;
            lot.TotalCount = 0;
            lot.GoodCount = 0;
            lot.NgCount = 0;
            lot.InvalidMeasurementCount = 0;
            lot.LearnedCellCount = 0;
            lot.CurrentGoodLane = null;
            lot.NextGoodLane = null;
            lot.PauseRequested = false;
            lot.AlertMessage = message;
            lot.Reference = null;
            lot.RecentSample = new List<SamplePoint>();
            lot.Lanes = new List<LaneState>();
            RoutingLedgerService.Reset(lot);

            if (_config.SortingMode == SortingModes.IntelligentGoodNg && recipe != null)
            {
                if (recipe != null && recipe.GoodLanes != null)
                {
                    for (var i = 0; i < recipe.GoodLanes.Count; i++)
                    {
                        var laneId = recipe.GoodLanes[i];
                        lot.Lanes.Add(new LaneState
                        {
                            LaneId = laneId,
                            Role = "GOOD",
                            Status = LaneStatuses.Available,
                            CapacityTarget = qualityIntervalMode ? 0 : GetLaneCapacity(recipe, laneId),
                            CountAssigned = 0,
                            MachineCount = 0
                        });
                    }

                    lot.Lanes.Add(new LaneState
                    {
                        LaneId = recipe.NgLane,
                        Role = "NG",
                        Status = LaneStatuses.Available,
                        CapacityTarget = 0,
                        CountAssigned = 0,
                        MachineCount = 0
                    });

                    if (recipe.GoodLanes.Count > 0)
                    {
                        if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
                        {
                            lot.CurrentGoodLane = QualityBandRouting.LearningLaneId;
                            lot.NextGoodLane = "1";
                        }
                        else
                        {
                            lot.CurrentGoodLane = recipe.GoodLanes[0];
                            lot.NextGoodLane = recipe.GoodLanes.Count > 1 ? recipe.GoodLanes[1] : null;
                        }
                    }

                    if (preservedOccupancy.Count > 0)
                    {
                        ApplyPreservedLaneOccupancyNoLock(lot, recipe, preservedOccupancy, timestamp);
                        lot.AlertMessage = message + " Occupation physique conservée: " + DescribePreservedLaneOccupancyNoLock(preservedOccupancy, recipe) + ". Utiliser Bacs vidés seulement après vidage réel.";
                    }
                }
            }

            lot.MachineCounterBaselines = _machineCountersAvailable && _machineCounters != null
                ? new List<int>(_machineCounters)
                : new List<int>();
            lot.MachineNgBaseline = _machineNg;
            _lastThresholdProgramSignature = null;
            _lastThresholdControlSignature = null;
            _programmedRoutingLaneId = null;
            _forceThresholdSync = true;
            ResetShadowCountersNoLock();
            _live = new LiveReading
            {
                Voltage = _live == null ? 0 : _live.Voltage,
                Ir = _live == null ? 0 : _live.Ir,
                Channel = lot.CurrentGoodLane,
                Result = "ATTENTE",
                Barcode = null,
                ShadowChannel = lot.CurrentGoodLane,
                ThresholdSource = "RESET_LINES",
                SortingMode = lot.SortingMode,
                CellType = lot.CellType,
                TargetLane = lot.CurrentGoodLane,
                RejectReason = null,
                LearningStatus = lot.LearningStatus,
                CurrentLotId = lot.Id,
                CurrentGoodLane = lot.CurrentGoodLane,
                NextGoodLane = lot.NextGoodLane,
                ReferenceSummary = BuildReferenceSummary(lot)
            };
        }

        private Dictionary<string, LaneOccupancySnapshot> CaptureGoodLaneOccupancyNoLock(LotSession lot, IntelligentRecipe recipe)
        {
            var result = new Dictionary<string, LaneOccupancySnapshot>(StringComparer.OrdinalIgnoreCase);
            if (lot == null || recipe == null || recipe.GoodLanes == null || lot.Lanes == null)
            {
                return result;
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return result;
            }

            foreach (var laneId in recipe.GoodLanes)
            {
                var lane = FindLaneInLotNoLock(lot, laneId);
                if (lane == null || !string.Equals(lane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var capacity = ResolveLaneCapacityNoLock(lane, recipe);
                var observed = Math.Max(Math.Max(0, lane.CountAssigned), Math.Max(0, lane.MachineCount));
                observed = Math.Max(observed, GetMachineCounterDeltaForLaneNoLock(lot, lane));

                var closedOrFull = lane.MachineFullSignal ||
                    lane.Status == LaneStatuses.Full ||
                    lane.Status == LaneStatuses.Blocked ||
                    !string.IsNullOrWhiteSpace(lane.LastSwitchOut) ||
                    (capacity > 0 && observed >= capacity);

                if (!closedOrFull && observed <= 0)
                {
                    continue;
                }

                if (capacity > 0)
                {
                    observed = closedOrFull ? capacity : Math.Min(observed, capacity);
                }

                result[lane.LaneId] = new LaneOccupancySnapshot
                {
                    Count = Math.Max(0, observed),
                    Full = closedOrFull
                };
            }

            return result;
        }

        private void ApplyPreservedLaneOccupancyNoLock(
            LotSession lot,
            IntelligentRecipe recipe,
            Dictionary<string, LaneOccupancySnapshot> preservedOccupancy,
            string timestamp)
        {
            if (lot == null || recipe == null || lot.Lanes == null || preservedOccupancy == null || preservedOccupancy.Count == 0)
            {
                return;
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return;
            }

            foreach (var item in preservedOccupancy)
            {
                var lane = FindLaneInLotNoLock(lot, item.Key);
                if (lane == null || !string.Equals(lane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var capacity = ResolveLaneCapacityNoLock(lane, recipe);
                var count = Math.Max(0, item.Value.Count);
                if (capacity > 0)
                {
                    count = Math.Min(count, capacity);
                }

                lane.CountAssigned = count;
                lane.MachineCount = count;
                lane.MachineFullSignal = false;

                if (item.Value.Full || (capacity > 0 && count >= capacity))
                {
                    lane.CountAssigned = capacity > 0 ? capacity : count;
                    lane.MachineCount = lane.CountAssigned;
                    lane.Status = LaneStatuses.Full;
                    lane.LastSwitchOut = timestamp;
                }
                else
                {
                    var margin = recipe == null ? 0 : Math.Max(0, recipe.LanePreSwitchMargin);
                    lane.Status = capacity > 0 && count >= Math.Max(0, capacity - margin)
                        ? LaneStatuses.NearFull
                        : LaneStatuses.Available;
                    lane.LastSwitchOut = null;
                }
            }

            ReassignCurrentGoodLaneNoLock(lot, recipe, timestamp);
        }

        private void ReassignCurrentGoodLaneNoLock(LotSession lot, IntelligentRecipe recipe, string timestamp)
        {
            if (lot == null || recipe == null || recipe.GoodLanes == null)
            {
                return;
            }

            lot.CurrentGoodLane = null;
            lot.NextGoodLane = null;
            if (QualityBandRouting.IsQualityIntervalRecipe(recipe) &&
                (lot.Reference == null || lot.LearningStatus != LearningStatuses.Stable))
            {
                var learningLane = FindLaneInLotNoLock(lot, QualityBandRouting.LearningLaneId);
                if (learningLane != null && string.IsNullOrWhiteSpace(learningLane.LastSwitchIn))
                {
                    learningLane.LastSwitchIn = timestamp;
                }

                lot.CurrentGoodLane = QualityBandRouting.LearningLaneId;
                lot.NextGoodLane = "1";
                lot.PauseRequested = false;
                return;
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                lot.CurrentGoodLane = null;
                lot.NextGoodLane = null;
                lot.PauseRequested = false;
                return;
            }

            foreach (var laneId in recipe.GoodLanes)
            {
                var lane = FindLaneInLotNoLock(lot, laneId);
                if (lane == null || lane.Status == LaneStatuses.Full || lane.Status == LaneStatuses.Blocked)
                {
                    continue;
                }

                lot.CurrentGoodLane = lane.LaneId;
                if (string.IsNullOrWhiteSpace(lane.LastSwitchIn))
                {
                    lane.LastSwitchIn = timestamp;
                }
                break;
            }

            lot.NextGoodLane = FindNextAvailableLaneIdNoLock(lot, recipe, lot.CurrentGoodLane);
            lot.PauseRequested = string.IsNullOrWhiteSpace(lot.CurrentGoodLane);
            if (lot.PauseRequested)
            {
                lot.AlertMessage = "Toutes les lignes GOOD sont considérées pleines. Confirmer Bacs vidés uniquement après vidage physique.";
            }
        }

        private string DescribePreservedLaneOccupancyNoLock(Dictionary<string, LaneOccupancySnapshot> preservedOccupancy, IntelligentRecipe recipe)
        {
            if (preservedOccupancy == null || preservedOccupancy.Count == 0)
            {
                return "aucune";
            }

            var parts = new List<string>();
            if (recipe != null && recipe.GoodLanes != null)
            {
                foreach (var laneId in recipe.GoodLanes)
                {
                    LaneOccupancySnapshot snapshot;
                    if (!preservedOccupancy.TryGetValue(laneId, out snapshot))
                    {
                        continue;
                    }

                    parts.Add(FormatLaneOccupancy(laneId, snapshot));
                }
            }

            foreach (var item in preservedOccupancy)
            {
                if (recipe != null && recipe.GoodLanes != null && recipe.GoodLanes.Contains(item.Key))
                {
                    continue;
                }

                parts.Add(FormatLaneOccupancy(item.Key, item.Value));
            }

            return parts.Count == 0 ? "aucune" : string.Join(", ", parts.ToArray());
        }

        private static string FormatLaneOccupancy(string laneId, LaneOccupancySnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "ligne " + laneId + "=0";
            }

            return snapshot.Full
                ? "ligne " + laneId + " pleine"
                : "ligne " + laneId + "=" + snapshot.Count.ToString(CultureInfo.InvariantCulture);
        }

        private int GetMachineCounterDeltaForLaneNoLock(LotSession lot, LaneState lane)
        {
            if (lot == null ||
                lane == null ||
                !_machineCountersAvailable ||
                _machineCounters == null ||
                lot.MachineCounterBaselines == null)
            {
                return 0;
            }

            int laneNumber;
            if (!int.TryParse(lane.LaneId, out laneNumber))
            {
                return 0;
            }

            var machineIndex = laneNumber - 1;
            if (machineIndex < 0 || machineIndex >= _machineCounters.Count || machineIndex >= lot.MachineCounterBaselines.Count)
            {
                return 0;
            }

            return Math.Max(0, _machineCounters[machineIndex] - lot.MachineCounterBaselines[machineIndex]);
        }

        private void ResetShadowCountersNoLock()
        {
            _counters = new List<int>();
            for (var i = 0; i < _config.Channels; i++)
            {
                _counters.Add(0);
            }

            _total = 0;
            _good = 0;
            _ng = 0;
        }

        private LotSession GetActiveLotNoLock()
        {
            for (var i = _lots.Count - 1; i >= 0; i--)
            {
                if (_lots[i].IsActive)
                {
                    return _lots[i];
                }
            }

            return null;
        }

        private LotSession GetDisplayLotNoLock()
        {
            var active = GetActiveLotNoLock();
            if (active != null)
            {
                return active;
            }

            if (_lots == null || _lots.Count == 0)
            {
                return null;
            }

            return _lots[_lots.Count - 1];
        }

        private void CloseActiveLot(string reason)
        {
            var active = GetActiveLotNoLock();
            if (active == null)
            {
                return;
            }

            active.IsActive = false;
            active.ClosedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                active.AlertMessage = reason;
            }
        }

        private void UpdateLaneObservationMetadata(LotSession lot)
        {
            if (lot == null || lot.Lanes == null)
            {
                return;
            }

            IntelligentRecipe recipe = null;
            if (_intelligentRecipes != null && !string.IsNullOrWhiteSpace(lot.CellType))
            {
                _intelligentRecipes.TryGetValue(lot.CellType, out recipe);
            }

            foreach (var lane in lot.Lanes)
            {
                if (lane.Role != "GOOD")
                {
                    continue;
                }

                if (recipe != null && QualityBandRouting.IsQualityIntervalRecipe(recipe))
                {
                    lane.Status = LaneStatuses.Available;
                    lane.CapacityTarget = 0;
                    lane.CapacityObserved = 0;
                    lane.CapacityConfidence = 0;
                    lane.MachineFullSignal = false;
                    lane.LastSwitchOut = null;
                    continue;
                }

                var configuredCapacity = GetLaneCapacity(recipe, lane.LaneId);
                if (configuredCapacity > 0)
                {
                    lane.CapacityTarget = configuredCapacity;
                }

                var observation = FindLaneObservation(lot.CellType, lane.LaneId);
                if (observation == null)
                {
                    lane.CapacityObserved = ClampObservedCapacityCountNoLock(lot.CellType, lane.LaneId, lane.CapacityObserved);
                    continue;
                }

                lane.CapacityObserved = ClampObservedCapacityCountNoLock(lot.CellType, lane.LaneId, observation.ObservedCount);
                lane.CapacityConfidence = observation.Confidence;
            }
        }

        private void SyncLotFromMachineCountersNoLock(MachineConfig cfg, LotSession lot)
        {
            if (cfg == null ||
                lot == null ||
                !_machineCountersAvailable ||
                _machineCounters == null ||
                _machineCounters.Count == 0 ||
                !string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (lot.MachineCounterBaselines == null || lot.MachineCounterBaselines.Count == 0)
            {
                lot.MachineCounterBaselines = new List<int>(_machineCounters);
                lot.MachineNgBaseline = _machineNg;
                return;
            }

            while (lot.MachineCounterBaselines.Count < _machineCounters.Count)
            {
                lot.MachineCounterBaselines.Add(_machineCounters[lot.MachineCounterBaselines.Count]);
            }

            var recipe = _intelligentRecipes.ContainsKey(cfg.CellType) ? _intelligentRecipes[cfg.CellType] : null;
            if (recipe == null || lot.Lanes == null)
            {
                return;
            }

            var qualityIntervalMode = QualityBandRouting.IsQualityIntervalRecipe(recipe);
            foreach (var lane in lot.Lanes)
            {
                if (!string.Equals(lane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int laneNumber;
                if (!int.TryParse(lane.LaneId, out laneNumber))
                {
                    continue;
                }

                var machineIndex = laneNumber - 1;
                if (machineIndex < 0 || machineIndex >= _machineCounters.Count || machineIndex >= lot.MachineCounterBaselines.Count)
                {
                    continue;
                }

                var physicalCount = Math.Max(0, _machineCounters[machineIndex] - lot.MachineCounterBaselines[machineIndex]);
                var previousPhysicalCount = lane.MachineCount;
                var physicalChanged = previousPhysicalCount != physicalCount;
                lane.MachineCount = physicalCount;
                if (qualityIntervalMode)
                {
                    lane.Status = LaneStatuses.Available;
                    lane.MachineFullSignal = false;
                    lane.LastSwitchOut = null;
                    lane.CapacityTarget = 0;
                    lane.CapacityObserved = 0;
                    lane.CapacityConfidence = 0;
                }

                if (lot.IsActive && physicalChanged && physicalCount > lane.CountAssigned)
                {
                    _trace.Append(
                        "COUNTERS",
                        "PHYSICAL_AHEAD",
                        "RECORDED",
                        "PLC",
                        (CounterBaseRegister + machineIndex * 2).ToString(CultureInfo.InvariantCulture),
                        "ligne=" + lane.LaneId +
                        " logic=" + lane.CountAssigned.ToString(CultureInfo.InvariantCulture) +
                        " physical=" + physicalCount.ToString(CultureInfo.InvariantCulture),
                        "Compteur physique en avance conserve en diagnostic; le compteur logique reste base sur les decisions cellule."
                    );
                }

                if (lot.IsActive && physicalChanged)
                {
                    if (physicalCount > previousPhysicalCount)
                    {
                        ReconcileRoutingLedgerForLaneNoLock(lot, lane.LaneId, physicalCount - previousPhysicalCount, "PLC");
                    }

                    _trace.Append(
                        "COUNTERS",
                        "LANE_DELTA",
                        "CHANGE",
                        "PLC",
                        (CounterBaseRegister + machineIndex * 2).ToString(CultureInfo.InvariantCulture),
                        "ligne=" + lane.LaneId + " physical=" + physicalCount.ToString(CultureInfo.InvariantCulture) + " logic=" + lane.CountAssigned.ToString(CultureInfo.InvariantCulture),
                        "Compteur physique relatif au lot mis à jour."
                    );
                }

                var capacity = qualityIntervalMode ? 0 : ResolveLaneCapacityNoLock(lane, recipe);
                if (lot.IsActive && physicalChanged && capacity > 0 && physicalCount > capacity)
                {
                    lane.Status = LaneStatuses.Full;
                    _trace.Append(
                        "COUNTERS",
                        "PHYSICAL_OVERFILL",
                        "RECORDED",
                        "PLC",
                        (CounterBaseRegister + machineIndex * 2).ToString(CultureInfo.InvariantCulture),
                        "ligne=" + lane.LaneId + " physical=" + physicalCount.ToString(CultureInfo.InvariantCulture) +
                        " capacity=" + capacity.ToString(CultureInfo.InvariantCulture),
                        "Sur-remplissage physique enregistré; le tri continue et le comptage direct reste prioritaire."
                    );
                }

                if (physicalChanged && physicalCount > lane.CountAssigned + 2)
                {
                    _trace.Append(
                        "LANE",
                        "PHYSICAL_COUNT",
                        "AHEAD",
                        "900+",
                        (CounterBaseRegister + machineIndex * 2).ToString(CultureInfo.InvariantCulture),
                        "ligne=" + lane.LaneId + " logic=" + lane.CountAssigned.ToString(CultureInfo.InvariantCulture) + " physical=" + physicalCount.ToString(CultureInfo.InvariantCulture),
                        "Le compteur machine est en avance sur le compteur logique; verification du comptage direct."
                    );
                }

                var laneClosed = !qualityIntervalMode &&
                    !string.IsNullOrWhiteSpace(lane.LastSwitchOut) &&
                    !string.Equals(lane.LaneId, lot.CurrentGoodLane, StringComparison.OrdinalIgnoreCase);

                if (laneClosed && lane.Status != LaneStatuses.Blocked)
                {
                    lane.Status = LaneStatuses.Full;
                }
            }

            var physicalNg = GetLotPhysicalNgNoLock(lot);
            var ngLane = FindLaneInLotNoLock(lot, string.IsNullOrWhiteSpace(recipe.NgLane) ? "NG" : recipe.NgLane);
            if (ngLane != null)
            {
                var previousPhysicalNg = ngLane.MachineCount;
                ngLane.MachineCount = physicalNg;
                if (lot.IsActive && physicalNg > previousPhysicalNg)
                {
                    ReconcileRoutingLedgerForLaneNoLock(lot, "NG", physicalNg - previousPhysicalNg, "PLC");
                }
            }

            if (physicalNg > CountNgCellsForLotNoLock(lot))
            {
                AppendPhysicalNgReconciliationRowsNoLock(lot, physicalNg);
            }

            if (physicalNg > lot.NgCount)
            {
                lot.NgCount = physicalNg;
                lot.TotalCount = Math.Max(lot.TotalCount, lot.GoodCount + lot.NgCount);
                _trace.Append(
                    "COUNTERS",
                    "NG_RECONCILE",
                    "APPLIED",
                    "PLC",
                    "NG",
                    "physical_ng=" + physicalNg.ToString(CultureInfo.InvariantCulture),
                    "Compteur NG machine appliqué à l'interface sans arrêt du tri."
                );
            }

        }

        private void ReconcileRoutingLedgerForLaneNoLock(LotSession lot, string laneId, int physicalDelta, string source)
        {
            if (lot == null || physicalDelta <= 0 || string.IsNullOrWhiteSpace(laneId))
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var changed = false;
            for (var i = 0; i < physicalDelta; i++)
            {
                var ticket = RoutingLedgerService.ConfirmOldestForLane(lot, laneId, timestamp);
                if (ticket == null)
                {
                    _trace.Append(
                        "ROUTING",
                        "PHYSICAL_WITHOUT_TICKET",
                        "MISMATCH",
                        source,
                        "ligne=" + laneId,
                        "delta=" + physicalDelta.ToString(CultureInfo.InvariantCulture),
                        "Le compteur physique a avancé sans cellule engagée correspondante dans le registre de routage."
                    );
                    continue;
                }

                changed = true;
                _trace.Append(
                    "ROUTING",
                    "TICKET",
                    "CONFIRMED",
                    source,
                    "ligne=" + laneId,
                    "#" + ticket.Sequence.ToString(CultureInfo.InvariantCulture),
                    "Compteur physique réconcilié avec la cellule engagée."
                );
            }

            if (changed)
            {
                PersistBusiness();
            }
        }

        private int GetLotPhysicalNgNoLock(LotSession lot)
        {
            if (lot == null || !_machineCountersAvailable)
            {
                return 0;
            }

            var baseline = lot.MachineCounterBaselines != null && lot.MachineCounterBaselines.Count > NgCounterIndex
                ? lot.MachineCounterBaselines[NgCounterIndex]
                : lot.MachineNgBaseline;
            return Math.Max(0, _machineNg - baseline);
        }

        private int CountNgCellsForLotNoLock(LotSession lot)
        {
            var source = _history.Latest(5000);
            var count = 0;
            foreach (var row in source)
            {
                if (IsHistoryRowInCurrentLotSessionNoLock(lot, row) && IsNgHistoryRow(row))
                {
                    count++;
                }
            }

            return count;
        }

        private void AppendPhysicalNgReconciliationRowsNoLock(LotSession lot, int physicalNg)
        {
            if (lot == null || physicalNg <= 0)
            {
                return;
            }

            var missing = Math.Max(0, physicalNg - CountNgCellsForLotNoLock(lot));
            if (missing <= 0)
            {
                return;
            }

            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            for (var i = 0; i < Math.Min(missing, 20); i++)
            {
                _history.Append(new HistoryRow
                {
                    Timestamp = now,
                    SortingMode = lot.SortingMode,
                    CellType = lot.CellType,
                    LotId = lot.Id,
                    Voltage = _live == null ? 0 : _live.Voltage,
                    Ir = _live == null ? 0 : _live.Ir,
                    LegacyChannel = "NG",
                    Channel = "NG",
                    Result = "NG",
                    RejectReason = RejectReasons.PhysicalCounterNg,
                    LearningStatus = lot.LearningStatus,
                    Barcode = _live == null ? null : _live.Barcode,
                    ThresholdSource = "MACHINE_COUNTER",
                    OdooLotReference = lot.OdooLotReference,
                    OdooLotName = lot.OdooLotName,
                    OdooProductReference = lot.OdooProductReference,
                    OdooProductName = lot.OdooProductName
                });
            }

            _trace.Append(
                "COUNTERS",
                "NG_RECONCILE",
                "RECORDED",
                "PLC",
                "NG",
                "physical_ng=" + physicalNg.ToString(CultureInfo.InvariantCulture),
                "NG ajoute dans l'historique depuis le compteur machine pour eviter un NG invisible dans l'interface."
            );
        }

        private void TrySendSafetyStopNoLock(MachineConfig cfg, string action, string value, string message)
        {
            if (cfg == null || cfg.UseSimulator)
            {
                return;
            }

            if ((DateTime.Now - _lastSafetyStopSentAt).TotalSeconds < 2)
            {
                return;
            }

            _lastSafetyStopSentAt = DateTime.Now;
            _trace.Append(
                "COMMAND",
                "STOP_SECURITE",
                "BLOCKED_BY_POLICY",
                "LOCAL",
                CycleCommandRegister.ToString(CultureInfo.InvariantCulture),
                StopCycleCode.ToString(CultureInfo.InvariantCulture),
                "STOP automatique interdit après " + (string.IsNullOrWhiteSpace(action) ? "sécurité" : action) +
                (string.IsNullOrWhiteSpace(value) ? string.Empty : " (" + value + ").") +
                " Message logiciel: " + (string.IsNullOrWhiteSpace(message) ? "--" : message)
            );
        }

        private void TryAutoResumeLotControlForLiveCycleNoLock(MachineConfig cfg, LotSession activeLot, string timestamp, string source)
        {
            if (cfg == null ||
                activeLot == null ||
                !activeLot.IsActive ||
                !string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var recipe = TryGetIntelligentRecipeNoLock(cfg);
            if (!IsLotCompatibleWithRecipeNoLock(activeLot, recipe))
            {
                return;
            }

            if (!_operatorStartArmed)
            {
                _trace.Append(
                    "LOT",
                    "AUTO_RESUME",
                    "SKIPPED",
                    source,
                    cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                    activeLot.Id.ToString(CultureInfo.InvariantCulture),
                    "Top cellule ignore pour le routage logiciel: aucun DÉMARRER opérateur actif depuis STOP/PAUSE/RESET. Horodatage=" + timestamp
                );
                return;
            }

            if (_lotControlEnabled && !activeLot.PauseRequested)
            {
                return;
            }

            _lotControlEnabled = true;
            activeLot.PauseRequested = false;
            activeLot.AlertMessage = "Top cellule détecté : tri logiciel réarmé automatiquement sur le lot actif.";
            _trace.Append(
                "LOT",
                "AUTO_RESUME",
                "OK",
                source,
                cfg.HandshakeRegister.ToString(CultureInfo.InvariantCulture),
                "lot=" + activeLot.Id.ToString(CultureInfo.InvariantCulture),
                "Un cycle machine réel est arrivé alors que le lot était en attente. Le moteur de tri est réarmé avant la décision pour éviter toute cellule sans piston. Horodatage=" + timestamp
            );
        }

        private bool TryAdvanceLaneFromRoutingLedgerNoLock(MachineConfig cfg, LotSession activeLot, string timestamp, string source)
        {
            if (cfg == null ||
                activeLot == null ||
                !_lotControlEnabled ||
                !_operatorStartArmed ||
                !string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var recipe = _intelligentRecipes.ContainsKey(cfg.CellType) ? _intelligentRecipes[cfg.CellType] : null;
            if (recipe == null || string.IsNullOrWhiteSpace(activeLot.CurrentGoodLane))
            {
                return false;
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                activeLot.NextGoodLane = activeLot.Reference == null || activeLot.LearningStatus != LearningStatuses.Stable ? "1" : null;
                return false;
            }

            var currentLane = FindLaneInLotNoLock(activeLot, activeLot.CurrentGoodLane);
            if (currentLane == null || !string.Equals(currentLane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var capacity = ResolveLaneCapacityNoLock(currentLane, recipe);
            if (capacity <= 0)
            {
                return false;
            }

            var currentFill = Math.Max(Math.Max(0, currentLane.CountAssigned), Math.Max(0, currentLane.MachineCount));
            var nextLaneId = FindNextAvailableLaneIdNoLock(activeLot, recipe, currentLane.LaneId);

            if (currentFill >= capacity)
            {
                if (currentLane.CountAssigned < capacity)
                {
                    currentLane.CountAssigned = capacity;
                }

                return CompleteLaneSwitchNoLock(
                    activeLot,
                    recipe,
                    currentLane,
                    capacity,
                    currentFill,
                    timestamp,
                    source,
                    "registre_routage"
                );
            }

            activeLot.NextGoodLane = nextLaneId;
            return false;
        }

        private bool CompleteLaneSwitchNoLock(
            LotSession activeLot,
            IntelligentRecipe recipe,
            LaneState currentLane,
            int capacity,
            int switchAt,
            string timestamp,
            string source,
            string trigger)
        {
            if (activeLot == null || currentLane == null || recipe == null)
            {
                return false;
            }

            if (currentLane.CountAssigned >= capacity || currentLane.MachineCount >= capacity)
            {
                currentLane.CountAssigned = capacity;
                currentLane.Status = LaneStatuses.Full;
                RecordLaneCapacityObservationNoLock(
                    activeLot.CellType,
                    currentLane.LaneId,
                    capacity,
                    timestamp);
            }
            else if (currentLane.Status != LaneStatuses.Blocked)
            {
                currentLane.Status = LaneStatuses.NearFull;
            }

            currentLane.LastSwitchOut = timestamp;
            var previousLaneId = currentLane.LaneId;
            if (QualityBandRouting.IsQualityIntervalRecipe(recipe) &&
                string.Equals(previousLaneId, QualityBandRouting.LearningLaneId, StringComparison.OrdinalIgnoreCase) &&
                (activeLot.Reference == null || !string.Equals(activeLot.LearningStatus, LearningStatuses.Stable, StringComparison.OrdinalIgnoreCase)))
            {
                activeLot.CurrentGoodLane = QualityBandRouting.LearningLaneId;
                activeLot.NextGoodLane = "1";
                activeLot.PauseRequested = false;
                activeLot.AlertMessage = "Apprentissage ligne 10 maintenu jusqu'aux 19 cellules requises.";
                return true;
            }

            var nextLaneId = FindNextAvailableLaneIdNoLock(activeLot, recipe, previousLaneId);
            activeLot.CurrentGoodLane = nextLaneId;
            activeLot.NextGoodLane = FindNextAvailableLaneIdNoLock(activeLot, recipe, nextLaneId);
            _lastThresholdProgramSignature = null;
            _lastThresholdControlSignature = null;
            _forceThresholdSync = true;

            if (!string.IsNullOrWhiteSpace(nextLaneId))
            {
                var nextLane = FindLaneInLotNoLock(activeLot, nextLaneId);
                if (nextLane != null)
                {
                    nextLane.LastSwitchIn = timestamp;
                    if (nextLane.Status != LaneStatuses.Blocked)
                    {
                        nextLane.Status = LaneStatuses.Available;
                    }
                }

                activeLot.PauseRequested = false;
                activeLot.AlertMessage = "Bascule automatique " + previousLaneId + " -> " + nextLaneId + " après " + capacity.ToString(CultureInfo.InvariantCulture) + " GOOD validés.";
            }
            else
            {
                activeLot.PauseRequested = true;
                activeLot.AlertMessage = "Dernière ligne GOOD atteinte, arrêt logique demandé.";
            }

            _trace.Append(
                "LANE",
                "AUTO_SWITCH",
                string.IsNullOrWhiteSpace(nextLaneId) ? "PAUSED" : "OK",
                source,
                "900+",
                previousLaneId + "->" + (string.IsNullOrWhiteSpace(nextLaneId) ? "--" : nextLaneId),
                "Bascule après ligne pleine: " +
                currentLane.CountAssigned.ToString(CultureInfo.InvariantCulture) + "/" +
                capacity.ToString(CultureInfo.InvariantCulture) +
                ", seuil=" + switchAt.ToString(CultureInfo.InvariantCulture) +
                ", déclenchement=" + trigger +
                ". Les NG ne sont pas soustraites d'une ligne GOOD, elles sont comptees a part."
            );

            PersistBusiness();
            return true;
        }

        private void RefreshMachineCountsForSnapshotNoLock(LotSession lot)
        {
            if (lot == null ||
                lot.Lanes == null ||
                lot.MachineCounterBaselines == null ||
                !_machineCountersAvailable ||
                _machineCounters == null ||
                _machineCounters.Count == 0)
            {
                return;
            }

            foreach (var lane in lot.Lanes)
            {
                if (!string.Equals(lane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int laneNumber;
                if (!int.TryParse(lane.LaneId, out laneNumber))
                {
                    continue;
                }

                var machineIndex = laneNumber - 1;
                if (machineIndex < 0 || machineIndex >= _machineCounters.Count || machineIndex >= lot.MachineCounterBaselines.Count)
                {
                    continue;
                }

                lane.MachineCount = Math.Max(0, _machineCounters[machineIndex] - lot.MachineCounterBaselines[machineIndex]);
            }
        }

        private bool TryHandleImmediateLaneFullNoLock(MachineConfig cfg, LotSession activeLot, bool laneFullSignal, string timestamp, string source, List<int> alarms)
        {
            if (!laneFullSignal)
            {
                _lastLaneFullSignalState = false;
                return false;
            }

            if (_lastLaneFullSignalState)
            {
                return true;
            }

            _lastLaneFullSignalState = true;

            if (cfg == null ||
                activeLot == null ||
                !_lotControlEnabled ||
                !_operatorStartArmed ||
                !string.Equals(cfg.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var recipe = _intelligentRecipes.ContainsKey(cfg.CellType) ? _intelligentRecipes[cfg.CellType] : null;
            if (recipe == null || string.IsNullOrWhiteSpace(activeLot.CurrentGoodLane))
            {
                return false;
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return false;
            }

            var fullLaneId = ResolveLaneFullTargetLaneIdNoLock(activeLot, recipe);
            if (string.IsNullOrWhiteSpace(fullLaneId))
            {
                return false;
            }

            var activeLaneBeforeSignal = activeLot.CurrentGoodLane;
            var fullLane = FindLaneInLotNoLock(activeLot, fullLaneId);
            if (fullLane != null)
            {
                fullLane.Status = LaneStatuses.Full;
                fullLane.MachineFullSignal = true;
                fullLane.LastSwitchOut = timestamp;
                if (fullLane.CountAssigned > 0)
                {
                    RecordLaneCapacityObservationNoLock(activeLot.CellType, fullLane.LaneId, fullLane.CountAssigned, timestamp);
                }
            }

            _trace.Append(
                "LANE",
                "FULL_SIGNAL",
                "DETECTED",
                source,
                cfg.AlarmRegister.ToString(CultureInfo.InvariantCulture),
                BuildAlarmSummary(alarms),
                "Bac plein détecté sur ligne " + fullLaneId
            );

            if (!string.Equals(fullLaneId, activeLaneBeforeSignal, StringComparison.OrdinalIgnoreCase))
            {
                activeLot.NextGoodLane = FindNextAvailableLaneIdNoLock(activeLot, recipe, activeLaneBeforeSignal);
                activeLot.PauseRequested = false;
                activeLot.AlertMessage = "Bac plein confirmé sur ligne " + fullLaneId + ", maintien de la ligne " + activeLaneBeforeSignal + ".";
                _lastThresholdProgramSignature = null;
                _lastThresholdControlSignature = null;
                _forceThresholdSync = true;
                PersistBusiness();

                _trace.Append(
                    "LANE",
                    "AUTO_SWITCH",
                    "OK",
                    source,
                    "1188..1370",
                    fullLaneId + "=>" + activeLaneBeforeSignal,
                    "Bac plein confirmé sur une ligne déjà quittée, maintien sur la ligne active."
                );

                return true;
            }

            var nextLaneId = FindNextAvailableLaneIdNoLock(activeLot, recipe, fullLaneId);
            activeLot.CurrentGoodLane = nextLaneId;
            activeLot.NextGoodLane = FindNextAvailableLaneIdNoLock(activeLot, recipe, nextLaneId);

            if (!string.IsNullOrWhiteSpace(nextLaneId))
            {
                var nextLane = FindLaneInLotNoLock(activeLot, nextLaneId);
                if (nextLane != null)
                {
                    nextLane.Status = LaneStatuses.Available;
                    nextLane.MachineFullSignal = false;
                    nextLane.LastSwitchIn = timestamp;
                }

                activeLot.PauseRequested = false;
                activeLot.AlertMessage = "Bascule automatique " + fullLaneId + " -> " + nextLaneId + " (bac plein).";

                _trace.Append(
                    "LANE",
                    "AUTO_SWITCH",
                    "OK",
                    source,
                    "1188..1370",
                    fullLaneId + "->" + nextLaneId,
                    "Bascule immediate sur signal bac plein."
                );
            }
            else
            {
                activeLot.PauseRequested = true;
                activeLot.AlertMessage = "Ligne " + fullLaneId + " pleine, aucune ligne suivante disponible.";
                _lotControlEnabled = false;

                _trace.Append(
                    "LANE",
                    "AUTO_SWITCH",
                    "PAUSED",
                    source,
                    "1188..1370",
                    fullLaneId + "->--",
                    "Bac plein sur derniere ligne GOOD, tri mis en attente."
                );
            }

            _lastThresholdProgramSignature = null;
            _lastThresholdControlSignature = null;
            _forceThresholdSync = true;
            PersistBusiness();
            return true;
        }

        private LaneCapacityObservation FindLaneObservation(string cellType, string laneId)
        {
            foreach (var observation in _laneCapacityObservations)
            {
                if (string.Equals(observation.CellType, cellType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(observation.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
                {
                    return observation;
                }
            }

            return null;
        }

        private LaneState FindLaneInLotNoLock(LotSession lot, string laneId)
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

        private string ResolveLaneFullTargetLaneIdNoLock(LotSession lot, IntelligentRecipe recipe)
        {
            if (lot == null || recipe == null || lot.Lanes == null || lot.Lanes.Count == 0)
            {
                return null;
            }

            if (QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return null;
            }

            var recentSwitchedOutLane = FindRecentSwitchedOutGoodLaneNoLock(lot, recipe, 90);
            if (recentSwitchedOutLane != null)
            {
                return recentSwitchedOutLane.LaneId;
            }

            var currentLane = FindLaneInLotNoLock(lot, lot.CurrentGoodLane);
            if (IsLaneCloseToCapacityNoLock(currentLane, recipe))
            {
                return currentLane.LaneId;
            }

            foreach (var lane in lot.Lanes)
            {
                if (IsLaneCloseToCapacityNoLock(lane, recipe))
                {
                    return lane.LaneId;
                }
            }

            return null;
        }

        private LaneState FindRecentSwitchedOutGoodLaneNoLock(LotSession lot, IntelligentRecipe recipe, int graceSeconds)
        {
            if (lot == null || lot.Lanes == null)
            {
                return null;
            }

            LaneState bestLane = null;
            DateTime bestSwitchOut = DateTime.MinValue;
            foreach (var lane in lot.Lanes)
            {
                if (!IsLaneCloseToCapacityNoLock(lane, recipe) || string.IsNullOrWhiteSpace(lane.LastSwitchOut))
                {
                    continue;
                }

                DateTime switchOut;
                if (!DateTime.TryParseExact(lane.LastSwitchOut, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out switchOut))
                {
                    continue;
                }

                if ((DateTime.Now - switchOut).TotalSeconds > graceSeconds)
                {
                    continue;
                }

                if (bestLane == null || switchOut > bestSwitchOut)
                {
                    bestLane = lane;
                    bestSwitchOut = switchOut;
                }
            }

            return bestLane;
        }

        private bool IsLaneCloseToCapacityNoLock(LaneState lane, IntelligentRecipe recipe)
        {
            if (lane == null || !string.Equals(lane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (lane.MachineFullSignal || lane.Status == LaneStatuses.Full || lane.Status == LaneStatuses.NearFull)
            {
                return true;
            }

            var capacity = ResolveLaneCapacityNoLock(lane, recipe);
            if (capacity <= 0)
            {
                return false;
            }

            var margin = recipe == null ? 0 : Math.Max(0, recipe.LanePreSwitchMargin);
            return lane.CountAssigned >= Math.Max(0, capacity - margin);
        }

        private int ResolveLaneCapacityNoLock(LaneState lane, IntelligentRecipe recipe)
        {
            if (lane == null)
            {
                return 0;
            }

            if (lane.CapacityTarget > 0)
            {
                return lane.CapacityTarget;
            }

            var recipeCapacity = GetLaneCapacity(recipe, lane.LaneId);
            if (recipeCapacity > 0)
            {
                return recipeCapacity;
            }

            if (lane.CapacityObserved > 0)
            {
                return lane.CapacityObserved;
            }

            return 0;
        }

        private string FindNextAvailableLaneIdNoLock(LotSession lot, IntelligentRecipe recipe, string currentLaneId)
        {
            if (lot == null || recipe == null || recipe.GoodLanes == null || recipe.GoodLanes.Count == 0)
            {
                return null;
            }

            var currentIndex = recipe.GoodLanes.IndexOf(currentLaneId);
            var startIndex = currentIndex < 0 ? 0 : currentIndex + 1;
            for (var i = startIndex; i < recipe.GoodLanes.Count; i++)
            {
                var lane = FindLaneInLotNoLock(lot, recipe.GoodLanes[i]);
                if (lane == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(lane.LastSwitchOut))
                {
                    continue;
                }

                if (lane.Status != LaneStatuses.Full && lane.Status != LaneStatuses.Blocked)
                {
                    return lane.LaneId;
                }
            }

            return null;
        }

        private void RecordLaneCapacityObservationNoLock(string cellType, string laneId, int count, string timestamp)
        {
            if (string.IsNullOrWhiteSpace(cellType) || string.IsNullOrWhiteSpace(laneId) || count <= 0)
            {
                return;
            }

            var observedCount = ClampObservedCapacityCountNoLock(cellType, laneId, count);
            if (observedCount <= 0)
            {
                return;
            }

            var existing = FindLaneObservation(cellType, laneId);
            if (existing == null)
            {
                _laneCapacityObservations.Add(new LaneCapacityObservation
                {
                    CellType = cellType,
                    LaneId = laneId,
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

        private int GetLaneCapacity(IntelligentRecipe recipe, string laneId)
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

        private void LogAlarmChangesNoLock(List<int> alarms, string source)
        {
            var signature = alarms == null || alarms.Count == 0
                ? "NONE"
                : string.Join(",", alarms.ToArray());

            if (signature == _lastAlarmSignature)
            {
                return;
            }

            _lastAlarmSignature = signature;
            _trace.Append(
                "ALARM",
                "STATE",
                alarms == null || alarms.Count == 0 ? "CLEAR" : "ACTIVE",
                source,
                _config.AlarmRegister.ToString(CultureInfo.InvariantCulture),
                signature,
                BuildAlarmSummary(alarms)
            );
        }

        private bool TryReadRegisters(MachineConfig cfg, int address, int count, out ushort[] registers, List<string> notes, string label)
        {
            if (count <= 0)
            {
                registers = new ushort[0];
                return true;
            }

            ushort[] result;
            var ok = _modbus.TryReadHoldingRegisters(
                cfg.ComPort,
                cfg.BaudRate,
                (byte)cfg.SlaveId,
                (ushort)address,
                (ushort)count,
                out result
            );
            registers = result;
            if (!ok)
            {
                notes.Add(label + ": lecture impossible @" + address);
            }

            return ok;
        }

        private int? TryReadSingleRegister(MachineConfig cfg, int address, List<string> notes, string label)
        {
            ushort[] registers;
            TryReadRegisters(cfg, address, 1, out registers, notes, label);
            if (registers == null || registers.Length == 0)
            {
                return null;
            }

            return registers[0];
        }

        private void UpdateMachineCounters(MachineConfig cfg, ushort[] counterRegs, ushort[] totalCounterRegs)
        {
            lock (_lock)
            {
                var machineCounters = new List<int>();
                if (counterRegs != null)
                {
                    for (var i = 0; i < counterRegs.Length; i += 2)
                    {
                        if (i + 1 >= counterRegs.Length)
                        {
                            break;
                        }

                        machineCounters.Add((int)DecodeCounterValue(counterRegs[i], counterRegs[i + 1]));
                    }
                }

                if (machineCounters.Count > 0)
                {
                    _machineCounters = machineCounters;
                    _machineCountersAvailable = true;
                }

                if (totalCounterRegs != null && totalCounterRegs.Length >= 2)
                {
                    _machineTotal = (int)DecodeCounterValue(totalCounterRegs[0], totalCounterRegs[1]);
                }
                else if (machineCounters.Count > 0)
                {
                    _machineTotal = 0;
                    foreach (var value in machineCounters)
                    {
                        _machineTotal += value;
                    }
                }

                _machineNg = (_machineCountersAvailable && _machineCounters.Count > NgCounterIndex) ? _machineCounters[NgCounterIndex] : 0;
                _machineGood = Math.Max(0, _machineTotal - _machineNg);
                if (_machineTotal != _lastLoggedMachineTotal)
                {
                    _trace.Append(
                        "COUNTERS",
                        "MACHINE_TOTAL",
                        "CHANGE",
                        "PLC",
                        "900+/948",
                        _machineTotal.ToString(CultureInfo.InvariantCulture),
                        "good=" + _machineGood.ToString(CultureInfo.InvariantCulture) +
                        " ng=" + _machineNg.ToString(CultureInfo.InvariantCulture)
                    );
                    _lastLoggedMachineTotal = _machineTotal;
                }
            }
        }

        private void UpdateHandshake(int handshakeValue)
        {
            lock (_lock)
            {
                if (!_lastHandshakeValue.HasValue || _lastHandshakeValue.Value != handshakeValue)
                {
                    _lastHandshakeValue = handshakeValue;
                    _lastHandshakeChange = DateTime.Now;
                }
            }
        }

        private static List<int> DecodeAlarms(ushort[] regs)
        {
            var active = new List<int>();
            if (regs == null)
            {
                return active;
            }

            for (var i = 0; i < regs.Length; i++)
            {
                var value = regs[i];
                for (var bit = 0; bit < 16; bit++)
                {
                    if ((value & (1 << bit)) != 0)
                    {
                        active.Add(i * 16 + bit);
                    }
                }
            }
            return active;
        }

        private void PersistConfig()
        {
            var data = new ConfigData
            {
                Config = _config,
                Thresholds = _thresholds,
                IntelligentRecipes = _intelligentRecipes
            };
            _configStore.Save(data);
        }

        private void PersistBusiness()
        {
            var data = new BusinessData
            {
                Lots = _lots,
                LaneCapacityObservations = _laneCapacityObservations
            };
            _businessStore.Save(data);
        }

        private AlarmState BuildAlarmState()
        {
            var labels = new List<string>();
            foreach (var idx in _alarmsActive)
            {
                if (idx >= 0 && idx < AlarmLabels.Count)
                {
                    labels.Add(AlarmLabels[idx]);
                }
                else
                {
                    labels.Add("Alarme " + idx.ToString(CultureInfo.InvariantCulture));
                }
            }

            return new AlarmState
            {
                ActiveIndices = new List<int>(_alarmsActive),
                Labels = labels
            };
        }

        private CountersState BuildCounters(LotSession lot)
        {
            var perChannel = new List<int>();
            var total = _total;
            var good = _good;
            var ng = _ng;
            var source = "SHADOW";

            if (!_config.UseSimulator && _machineCountersAvailable && _machineCounters != null && _machineCounters.Count > 0)
            {
                source = lot == null ? "MACHINE" : "MACHINE_LOT";
                total = _machineTotal;
                good = _machineGood;
                ng = _machineNg;

                var machineVisibleCount = _machineCounters.Count;
                if (machineVisibleCount > NgCounterIndex)
                {
                    machineVisibleCount = NgCounterIndex;
                }

                for (var i = 0; i < machineVisibleCount; i++)
                {
                    var value = _machineCounters[i];
                    if (lot != null && lot.MachineCounterBaselines != null && i < lot.MachineCounterBaselines.Count)
                    {
                        value = Math.Max(0, value - lot.MachineCounterBaselines[i]);
                    }

                    perChannel.Add(value);
                }

                if (lot != null)
                {
                    good = 0;
                    foreach (var value in perChannel)
                    {
                        good += value;
                    }

                    ng = lot.MachineCounterBaselines != null && lot.MachineCounterBaselines.Count > NgCounterIndex
                        ? Math.Max(0, _machineNg - lot.MachineCounterBaselines[NgCounterIndex])
                        : 0;
                    total = good + ng;
                }
            }
            else
            {
                perChannel = new List<int>(_counters);
            }

            return new CountersState
            {
                PerChannel = perChannel,
                Total = total,
                GoodTotal = good,
                NgTotal = ng,
                Source = source,
                ShadowPerChannel = new List<int>(_counters),
                ShadowTotal = _total,
                ShadowGoodTotal = _good,
                ShadowNgTotal = _ng
            };
        }

        private ProductionSnapshot BuildProductionSnapshot(LotSession lot)
        {
            if (lot == null)
            {
                return new ProductionSnapshot
                {
                    SortingMode = _config.SortingMode,
                    CellType = _config.CellType,
                    LotControlEnabled = _lotControlEnabled,
                    LotStatus = _lotControlEnabled ? "WAITING_LOT" : "IDLE",
                    CurrentLotId = 0,
                    TotalCount = 0,
                    LearningStatus = LearningStatuses.Idle,
                    SampleCount = 0,
                    SampleTarget = 0,
                    LastGoodLane = _intelligentRecipes.ContainsKey(_config.CellType) ? _intelligentRecipes[_config.CellType].LastGoodLane : null,
                    Lanes = new List<LaneState>(),
                    RecentSample = new List<SamplePoint>(),
                    RecentNgCells = new List<HistoryRow>(),
                    QualityIntervals = new List<QualityIntervalSnapshot>()
                };
            }

            RefreshMachineCountsForSnapshotNoLock(lot);
            int physicalGood;
            int physicalNg;
            int physicalTotal;
            GetPhysicalLotCountsNoLock(lot, out physicalGood, out physicalNg, out physicalTotal);
            var snapshotGood = physicalGood > 0 ? Math.Max(lot.GoodCount, physicalGood) : lot.GoodCount;
            var snapshotNg = physicalNg > 0 ? Math.Max(lot.NgCount, physicalNg) : lot.NgCount;
            var snapshotTotal = physicalTotal > 0 ? Math.Max(lot.TotalCount, physicalTotal) : lot.TotalCount;

            return new ProductionSnapshot
            {
                SortingMode = lot.SortingMode,
                CellType = lot.CellType,
                LotControlEnabled = _lotControlEnabled,
                LotStatus = lot.IsActive ? (lot.PauseRequested ? "PAUSED" : "ACTIVE") : "CLOSED",
                CurrentLotId = lot.Id,
                TotalCount = snapshotTotal,
                LearningStatus = lot.LearningStatus,
                SampleCount = lot.RecentSample == null ? 0 : lot.RecentSample.Count,
                SampleTarget = lot.Reference == null ? (_intelligentRecipes.ContainsKey(lot.CellType) ? _intelligentRecipes[lot.CellType].SampleSize : 0) : lot.Reference.SampleSizeTarget,
                MeanVoltage = lot.Reference == null ? 0 : lot.Reference.MeanVoltage,
                SigmaVoltage = lot.Reference == null ? 0 : lot.Reference.SigmaVoltage,
                MeanIr = lot.Reference == null ? 0 : lot.Reference.MeanIr,
                SigmaIr = lot.Reference == null ? 0 : lot.Reference.SigmaIr,
                CurrentGoodLane = lot.CurrentGoodLane,
                NextGoodLane = lot.NextGoodLane,
                LastGoodLane = _intelligentRecipes.ContainsKey(lot.CellType) ? _intelligentRecipes[lot.CellType].LastGoodLane : null,
                OdooLotReference = lot.OdooLotReference,
                OdooLotName = lot.OdooLotName,
                OdooProductReference = lot.OdooProductReference,
                OdooProductName = lot.OdooProductName,
                OdooLinkSource = lot.OdooLinkSource,
                OdooVerified = lot.OdooVerified,
                PauseRequested = lot.PauseRequested,
                AlertMessage = lot.AlertMessage,
                GoodCount = snapshotGood,
                NgCount = snapshotNg,
                Lanes = CopyLanes(lot.Lanes),
                RecentSample = CopySamples(lot.RecentSample),
                RecentNgCells = BuildRecentNgCellsForLotNoLock(lot, physicalNg, 12),
                QualityIntervals = BuildQualityIntervalSnapshotsNoLock(lot)
            };
        }

        private List<QualityIntervalSnapshot> BuildQualityIntervalSnapshotsNoLock(LotSession lot)
        {
            if (lot == null ||
                lot.Reference == null ||
                !string.Equals(lot.LearningStatus, LearningStatuses.Stable, StringComparison.OrdinalIgnoreCase) ||
                !_intelligentRecipes.ContainsKey(lot.CellType))
            {
                return new List<QualityIntervalSnapshot>();
            }

            var recipe = _intelligentRecipes[lot.CellType];
            if (!QualityBandRouting.IsQualityIntervalRecipe(recipe))
            {
                return new List<QualityIntervalSnapshot>();
            }

            var windows = QualityBandRouting.BuildWindows(recipe, lot.Reference, lot.RecentSample);
            return QualityBandRouting.BuildIntervalSnapshots(windows, lot.Reference.SampleSizeValid);
        }

        private void GetPhysicalLotCountsNoLock(LotSession lot, out int good, out int ng, out int total)
        {
            good = 0;
            ng = 0;
            total = 0;
            if (lot == null || !_machineCountersAvailable)
            {
                return;
            }

            if (lot.Lanes != null)
            {
                foreach (var lane in lot.Lanes)
                {
                    if (lane != null && string.Equals(lane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
                    {
                        good += Math.Max(0, lane.MachineCount);
                    }
                }
            }

            ng = GetLotPhysicalNgNoLock(lot);
            total = good + ng;
        }

        private List<HistoryRow> BuildRecentNgCellsForLotNoLock(LotSession lot, int physicalNg, int limit)
        {
            if (lot == null)
            {
                return new List<HistoryRow>();
            }

            var result = GetRecentNgCellsForLotSessionNoLock(lot, limit);
            if (physicalNg <= 0)
            {
                return result;
            }

            var recordedNg = CountNgCellsForLotNoLock(lot);
            var missing = Math.Max(0, physicalNg - recordedNg);
            while (missing > 0 && result.Count < limit)
            {
                result.Add(new HistoryRow
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    SortingMode = lot.SortingMode,
                    CellType = lot.CellType,
                    LotId = lot.Id,
                    Voltage = _live == null ? 0 : _live.Voltage,
                    Ir = _live == null ? 0 : _live.Ir,
                    LegacyChannel = "NG",
                    Channel = "NG",
                    Result = "NG",
                    RejectReason = RejectReasons.PhysicalCounterNg,
                    LearningStatus = lot.LearningStatus,
                    Barcode = _live == null ? null : _live.Barcode,
                    ThresholdSource = "MACHINE_COUNTER",
                    OdooLotReference = lot.OdooLotReference,
                    OdooLotName = lot.OdooLotName,
                    OdooProductReference = lot.OdooProductReference,
                    OdooProductName = lot.OdooProductName
                });
                missing--;
            }

            return result;
        }

        private List<HistoryRow> GetRecentNgCellsForLotNoLock(int lotId, int limit)
        {
            var source = _history.Latest(1000);
            var result = new List<HistoryRow>();
            for (var i = source.Count - 1; i >= 0 && result.Count < limit; i--)
            {
                var row = source[i];
                if (row.LotId != lotId)
                {
                    continue;
                }

                if (IsNgHistoryRow(row))
                {
                    result.Add(row);
                }
            }

            result.Reverse();
            return result;
        }

        private List<HistoryRow> GetRecentNgCellsForLotSessionNoLock(LotSession lot, int limit)
        {
            var source = _history.Latest(1000);
            var result = new List<HistoryRow>();
            for (var i = source.Count - 1; i >= 0 && result.Count < limit; i--)
            {
                var row = source[i];
                if (!IsHistoryRowInCurrentLotSessionNoLock(lot, row))
                {
                    continue;
                }

                if (IsNgHistoryRow(row))
                {
                    result.Add(row);
                }
            }

            result.Reverse();
            return result;
        }

        private static bool IsHistoryRowInCurrentLotSessionNoLock(LotSession lot, HistoryRow row)
        {
            if (lot == null || row == null || row.LotId != lot.Id)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(lot.StartedAt) || string.IsNullOrWhiteSpace(row.Timestamp))
            {
                return true;
            }

            DateTime lotStartedAt;
            DateTime rowTimestamp;
            if (!DateTime.TryParseExact(lot.StartedAt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out lotStartedAt) ||
                !DateTime.TryParseExact(row.Timestamp, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out rowTimestamp))
            {
                return true;
            }

            return rowTimestamp >= lotStartedAt;
        }

        private static bool IsNgHistoryRow(HistoryRow row)
        {
            if (row == null)
            {
                return false;
            }

            if (string.Equals(row.Result, "NG", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(row.Channel, "NG", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(row.RejectReason) &&
                !string.Equals(row.RejectReason, RejectReasons.None, StringComparison.OrdinalIgnoreCase);
        }

        private static MachineConfig CopyConfig(MachineConfig cfg)
        {
            return new MachineConfig
            {
                Channels = cfg.Channels,
                ComPort = cfg.ComPort,
                BaudRate = cfg.BaudRate,
                SlaveId = cfg.SlaveId,
                MeasurementRegister = cfg.MeasurementRegister,
                AlarmRegister = cfg.AlarmRegister,
                HandshakeRegister = cfg.HandshakeRegister,
                StatusRegister = cfg.StatusRegister,
                SwapWords = cfg.SwapWords,
                IrFirst = cfg.IrFirst,
                ScanComPort = cfg.ScanComPort,
                ScanBaudRate = cfg.ScanBaudRate,
                ScanParity = cfg.ScanParity,
                ScanEnabled = cfg.ScanEnabled,
                ScanTimeoutS = cfg.ScanTimeoutS,
                JudgeMode = cfg.JudgeMode,
                ChannelStart = cfg.ChannelStart,
                ChannelEnd = cfg.ChannelEnd,
                NegativeVoltageToNg = cfg.NegativeVoltageToNg,
                NoBarcodeValue = cfg.NoBarcodeValue,
                CellType = cfg.CellType,
                SortingMode = cfg.SortingMode,
                SafeMode = cfg.SafeMode,
                ObservationOnly = cfg.ObservationOnly,
                ShadowMode = cfg.ShadowMode,
                UseSimulator = cfg.UseSimulator
            };
        }

        private void NormalizeConfig()
        {
            if (_config == null)
            {
                _config = MachineConfig.CreateDefault();
            }

            if (_config.Channels <= 0) _config.Channels = 11;
            if (string.IsNullOrWhiteSpace(_config.ComPort)) _config.ComPort = "COM1";
            if (_config.BaudRate <= 0) _config.BaudRate = 19200;
            if (_config.SlaveId <= 0) _config.SlaveId = 1;
            if (_config.MeasurementRegister <= 0 ||
                _config.MeasurementRegister == MachineConfig.KnownErroneousMeasurementRegister)
            {
                _config.MeasurementRegister = MachineConfig.DefaultMeasurementRegister;
            }
            if (_config.AlarmRegister <= 0) _config.AlarmRegister = 22808;
            if (_config.HandshakeRegister <= 0) _config.HandshakeRegister = 8230;
            if (_config.StatusRegister <= 0) _config.StatusRegister = 8231;
            if (string.IsNullOrWhiteSpace(_config.ScanComPort)) _config.ScanComPort = "COM2";
            if (_config.ScanBaudRate <= 0) _config.ScanBaudRate = 115200;
            if (string.IsNullOrWhiteSpace(_config.ScanParity)) _config.ScanParity = "Even";
            if (string.IsNullOrWhiteSpace(_config.JudgeMode)) _config.JudgeMode = "BOTH";
            if (_config.ChannelStart <= 0) _config.ChannelStart = 1;
            if (_config.ChannelEnd <= 0) _config.ChannelEnd = _config.Channels;
            if (_config.ChannelEnd > _config.Channels) _config.ChannelEnd = _config.Channels;
            if (_config.CellType != "21700" && _config.CellType != "18650") _config.CellType = "21700";
            if (_config.SortingMode != SortingModes.Legacy && _config.SortingMode != SortingModes.IntelligentGoodNg)
            {
                _config.SortingMode = SortingModes.IntelligentGoodNg;
            }
            if (_config.SortingMode == SortingModes.IntelligentGoodNg)
            {
                _config.ChannelStart = 1;
                _config.ChannelEnd = _config.Channels;
            }
            _config.SafeMode = false;
            _config.ObservationOnly = false;
            _config.ShadowMode = true;
            _config.NegativeVoltageToNg = false;
        }

        private void MarkRecoveredActiveLotAsWaitingNoLock()
        {
            var activeLot = GetActiveLotNoLock();
            if (activeLot == null)
            {
                return;
            }

            var changed = false;
            if (string.Equals(activeLot.AlertMessage, "Tri démarré.", StringComparison.OrdinalIgnoreCase))
            {
                activeLot.AlertMessage = "Lot actif rechargé : attente du prochain top automate 8230.";
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(activeLot.AlertMessage) &&
                (activeLot.AlertMessage.StartsWith("START envoyé (5978=31), mais aucun top automate 8230", StringComparison.OrdinalIgnoreCase) ||
                 activeLot.AlertMessage.StartsWith("DÉMARRER envoyé : attente du premier top automate 8230", StringComparison.OrdinalIgnoreCase)))
            {
                activeLot.AlertMessage = "Lot actif rechargé : DÉMARRER disponible, attente d’un ordre opérateur.";
                changed = true;
            }

            if (changed)
            {
                PersistBusiness();
            }
        }

        private void ApplyStartCommandArmedNoLock(string message)
        {
            _operatorStartArmed = true;
            _lotControlEnabled = true;
            var startedLot = GetActiveLotNoLock();
            if (startedLot != null)
            {
                startedLot.PauseRequested = false;
                startedLot.AlertMessage = message;
            }
            PersistBusiness();
        }

        private void ApplyCycleCommandNoLock(string command)
        {
            switch (command)
            {
                case "START":
                    CancelScheduledPusherWorkNoLock("START");
                    ApplyStartCommandArmedNoLock("DÉMARRER envoyé : attente du premier top automate 8230.");
                    break;
                case "STOP":
                    _operatorStartArmed = false;
                    CancelScheduledPusherWorkNoLock("STOP");
                    _lotControlEnabled = false;
                    var stoppedLot = GetActiveLotNoLock();
                    if (stoppedLot != null)
                    {
                        stoppedLot.IsActive = true;
                        stoppedLot.ClosedAt = null;
                        stoppedLot.PauseRequested = true;
                        stoppedLot.AlertMessage = "Stop machine demandé. Lot conservé, utiliser DÉMARRER pour repartir.";
                    }
                    PersistBusiness();
                    break;
                case "PAUSE":
                    _operatorStartArmed = false;
                    CancelScheduledPusherWorkNoLock("PAUSE");
                    _lotControlEnabled = false;
                    var pausedLot = GetActiveLotNoLock();
                    if (pausedLot != null)
                    {
                        pausedLot.PauseRequested = true;
                        pausedLot.AlertMessage = "Pause machine demandée. Lot conservé, utiliser DÉMARRER pour repartir.";
                    }
                    PersistBusiness();
                    break;
                case "RESET":
                    _operatorStartArmed = false;
                    CancelScheduledPusherWorkNoLock("RESET");
                    _lotControlEnabled = false;
                    var resetLot = GetActiveLotNoLock();
                    if (resetLot != null)
                    {
                        resetLot.PauseRequested = true;
                        resetLot.AlertMessage = "Réarmement automate demandé. Lot conservé, utiliser DÉMARRER pour repartir.";
                    }
                    PersistBusiness();
                    break;
            }
        }

        private void NormalizeRecipes()
        {
            if (_thresholds == null)
            {
                _thresholds = new Dictionary<string, ThresholdSet>();
            }
            if (!_thresholds.ContainsKey("21700"))
            {
                _thresholds["21700"] = new ThresholdSet { Channels = new List<ChannelThreshold>() };
            }
            if (!_thresholds.ContainsKey("18650"))
            {
                _thresholds["18650"] = new ThresholdSet { Channels = new List<ChannelThreshold>() };
            }

            if (_intelligentRecipes == null)
            {
                _intelligentRecipes = new Dictionary<string, IntelligentRecipe>();
            }
            if (!_intelligentRecipes.ContainsKey("21700"))
            {
                _intelligentRecipes["21700"] = new ConfigStore().Load().IntelligentRecipes["21700"];
            }
            if (!_intelligentRecipes.ContainsKey("18650"))
            {
                _intelligentRecipes["18650"] = new ConfigStore().Load().IntelligentRecipes["18650"];
            }

            NormalizeIntelligentRecipe(_intelligentRecipes["21700"]);
            NormalizeIntelligentRecipe(_intelligentRecipes["18650"]);
        }

        private void NormalizeLaneCapacityObservations()
        {
            if (_laneCapacityObservations == null)
            {
                _laneCapacityObservations = new List<LaneCapacityObservation>();
                return;
            }

            foreach (var observation in _laneCapacityObservations)
            {
                observation.ObservedCount = ClampObservedCapacityCountNoLock(observation.CellType, observation.LaneId, observation.ObservedCount);
                if (observation.Confidence < 1)
                {
                    observation.Confidence = 1;
                }
            }
        }

        private void NormalizeLotLaneMetadata()
        {
            if (_lots == null)
            {
                return;
            }

            foreach (var lot in _lots)
            {
                UpdateLaneObservationMetadata(lot);
                if (lot.RoutingArchive == null)
                {
                    lot.RoutingArchive = new List<RoutingTicket>();
                }
                RoutingLedgerService.Ensure(lot);
                RoutingLedgerService.Trim(lot);
                if (!lot.IsActive)
                {
                    continue;
                }

                IntelligentRecipe recipe;
                if (_intelligentRecipes == null ||
                    string.IsNullOrWhiteSpace(lot.CellType) ||
                    !_intelligentRecipes.TryGetValue(lot.CellType, out recipe))
                {
                    continue;
                }

                if (!IsLotCompatibleWithRecipeNoLock(lot, recipe))
                {
                    lot.IsActive = false;
                    if (string.IsNullOrWhiteSpace(lot.ClosedAt))
                    {
                        lot.ClosedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    }
                    lot.PauseRequested = false;
                    lot.AlertMessage = "Lot clôturé automatiquement : recette intelligente mise à jour.";
                }
            }
        }

        private static ThresholdSet CopyThresholds(ThresholdSet set)
        {
            var list = new List<ChannelThreshold>();
            if (set == null || set.Channels == null)
            {
                return new ThresholdSet { Channels = list };
            }

            foreach (var th in set.Channels)
            {
                list.Add(new ChannelThreshold
                {
                    VoltageMin = th.VoltageMin,
                    VoltageMax = th.VoltageMax,
                    IrMin = th.IrMin,
                    IrMax = th.IrMax
                });
            }

            return new ThresholdSet { Channels = list };
        }

        private static IntelligentRecipe CopyIntelligentRecipe(IntelligentRecipe recipe)
        {
            if (recipe == null)
            {
                return null;
            }

            var goodLanes = new List<string>();
            if (recipe.GoodLanes != null)
            {
                foreach (var lane in recipe.GoodLanes)
                {
                    goodLanes.Add(lane);
                }
            }

            var capacities = new List<LaneCapacitySetting>();
            if (recipe.LaneCapacities != null)
            {
                foreach (var lane in recipe.LaneCapacities)
                {
                    capacities.Add(new LaneCapacitySetting
                    {
                        LaneId = lane.LaneId,
                        Capacity = lane.Capacity
                    });
                }
            }

            return new IntelligentRecipe
            {
                CellType = recipe.CellType,
                SampleSize = recipe.SampleSize,
                MaxSigmaVoltage = recipe.MaxSigmaVoltage,
                MaxSigmaIr = recipe.MaxSigmaIr,
                AcceptanceKVoltage = recipe.AcceptanceKVoltage,
                AcceptanceKIr = recipe.AcceptanceKIr,
                MinWindowVoltage = recipe.MinWindowVoltage,
                MinWindowIr = recipe.MinWindowIr,
                MaxWindowVoltage = recipe.MaxWindowVoltage,
                MaxWindowIr = recipe.MaxWindowIr,
                GoodLanes = goodLanes,
                LastGoodLane = recipe.LastGoodLane,
                NgLane = recipe.NgLane,
                LaneCapacities = capacities,
                LanePreSwitchMargin = recipe.LanePreSwitchMargin,
                NegativeVoltageToNg = recipe.NegativeVoltageToNg,
                LearningTimeoutCells = recipe.LearningTimeoutCells
            };
        }

        private static LiveReading CopyLive(LiveReading live)
        {
            return new LiveReading
            {
                Voltage = live.Voltage,
                Ir = live.Ir,
                Channel = live.Channel,
                Result = live.Result,
                Barcode = live.Barcode,
                ShadowChannel = live.ShadowChannel,
                ThresholdSource = live.ThresholdSource,
                SortingMode = live.SortingMode,
                CellType = live.CellType,
                TargetLane = live.TargetLane,
                RejectReason = live.RejectReason,
                LearningStatus = live.LearningStatus,
                CurrentLotId = live.CurrentLotId,
                CurrentGoodLane = live.CurrentGoodLane,
                NextGoodLane = live.NextGoodLane,
                ReferenceSummary = live.ReferenceSummary
            };
        }

        private static DiagnosticSnapshot CopyDiagnostic(DiagnosticSnapshot diagnostic)
        {
            if (diagnostic == null)
            {
                return null;
            }

            return new DiagnosticSnapshot
            {
                SourceMode = diagnostic.SourceMode,
                ObservationOnly = diagnostic.ObservationOnly,
                ShadowMode = diagnostic.ShadowMode,
                HandshakeRegister = diagnostic.HandshakeRegister,
                HandshakeValue = diagnostic.HandshakeValue,
                HandshakeChangedAt = diagnostic.HandshakeChangedAt,
                StatusRegister = diagnostic.StatusRegister,
                StatusValue = diagnostic.StatusValue,
                MeasurementRegisters = new List<ushort>(diagnostic.MeasurementRegisters ?? new List<ushort>()),
                DisplayRegisters = new List<ushort>(diagnostic.DisplayRegisters ?? new List<ushort>()),
                AlarmRegisters = new List<ushort>(diagnostic.AlarmRegisters ?? new List<ushort>()),
                ScannerStatus = diagnostic.ScannerStatus,
                ScannerParity = diagnostic.ScannerParity,
                ThresholdsObserved = diagnostic.ThresholdsObserved,
                ThresholdStatus = diagnostic.ThresholdStatus,
                ThresholdDifferences = new List<ThresholdDifference>(diagnostic.ThresholdDifferences ?? new List<ThresholdDifference>()),
                PhysicalRouting = CopyPhysicalRoutingDiagnostic(diagnostic.PhysicalRouting),
                StartReadiness = CopyStartReadinessDiagnostic(diagnostic.StartReadiness),
                FieldValidation = CopyFieldValidationDiagnostic(diagnostic.FieldValidation),
                ObservationEventCount = diagnostic.ObservationEventCount,
                Notes = new List<string>(diagnostic.Notes ?? new List<string>())
            };
        }

        private static FieldValidationDiagnostic CopyFieldValidationDiagnostic(FieldValidationDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                return null;
            }

            return new FieldValidationDiagnostic
            {
                HasReport = diagnostic.HasReport,
                Verified = diagnostic.Verified,
                Status = diagnostic.Status,
                ReportPath = diagnostic.ReportPath,
                ReportTimestamp = diagnostic.ReportTimestamp,
                ReportLotId = diagnostic.ReportLotId,
                CurrentLotId = diagnostic.CurrentLotId,
                MatchesCurrentLot = diagnostic.MatchesCurrentLot,
                TraceVerdict = diagnostic.TraceVerdict,
                CounterVerdict = diagnostic.CounterVerdict,
                PhysicalObservationVerdict = diagnostic.PhysicalObservationVerdict,
                LaneCoverageVerdict = diagnostic.LaneCoverageVerdict,
                Summary = diagnostic.Summary,
                ValidationCommand = diagnostic.ValidationCommand,
                CheckCommand = diagnostic.CheckCommand,
                MissingReasons = new List<string>(diagnostic.MissingReasons ?? new List<string>())
            };
        }

        private static StartReadinessDiagnostic CopyStartReadinessDiagnostic(StartReadinessDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                return null;
            }

            return new StartReadinessDiagnostic
            {
                ReadyToStart = diagnostic.ReadyToStart,
                Connected = diagnostic.Connected,
                HandshakeReady = diagnostic.HandshakeReady,
                HandshakeRegister = diagnostic.HandshakeRegister,
                HandshakeValue = diagnostic.HandshakeValue,
                HandshakeChangedAt = diagnostic.HandshakeChangedAt,
                LotAssociated = diagnostic.LotAssociated,
                ModelStable = diagnostic.ModelStable,
                ThresholdsSynchronized = diagnostic.ThresholdsSynchronized,
                MachineRequiresReset = diagnostic.MachineRequiresReset,
                BlockingAlarmActive = diagnostic.BlockingAlarmActive,
                OperatorConfirmationRequired = diagnostic.OperatorConfirmationRequired,
                MachineStatus = diagnostic.MachineStatus,
                LotStatus = diagnostic.LotStatus,
                ExpectedLane = diagnostic.ExpectedLane,
                AppliedLane = diagnostic.AppliedLane,
                BlockingReasons = new List<string>(diagnostic.BlockingReasons ?? new List<string>()),
                Warnings = new List<string>(diagnostic.Warnings ?? new List<string>()),
                OperatorChecks = new List<string>(diagnostic.OperatorChecks ?? new List<string>())
            };
        }

        private static PhysicalRoutingDiagnostic CopyPhysicalRoutingDiagnostic(PhysicalRoutingDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                return null;
            }

            return new PhysicalRoutingDiagnostic
            {
                ExpectedLane = diagnostic.ExpectedLane,
                AppliedLane = diagnostic.AppliedLane,
                ConfirmedLane = diagnostic.ConfirmedLane,
                HandshakeRegister = diagnostic.HandshakeRegister,
                LastHandshake = diagnostic.LastHandshake,
                StatusRegister = diagnostic.StatusRegister,
                MachineStatus = diagnostic.MachineStatus,
                ThresholdStatus = diagnostic.ThresholdStatus,
                ProgrammedThresholds = CopyThresholds(diagnostic.ProgrammedThresholds),
                ObservedThresholds = diagnostic.ObservedThresholds == null ? null : CopyThresholds(diagnostic.ObservedThresholds),
                AlarmRegisters = new List<ushort>(diagnostic.AlarmRegisters ?? new List<ushort>()),
                AlarmSummary = diagnostic.AlarmSummary,
                LastNgPulse = CopyNgPulseDiagnostic(diagnostic.LastNgPulse),
                PhysicalRoutingMode = diagnostic.PhysicalRoutingMode,
                GoodPusherDirectControlBlocked = diagnostic.GoodPusherDirectControlBlocked
            };
        }

        private static NgPulseDiagnostic CopyNgPulseDiagnostic(NgPulseDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                return null;
            }

            return new NgPulseDiagnostic
            {
                Timestamp = diagnostic.Timestamp,
                Handshake = diagnostic.Handshake,
                Status = diagnostic.Status,
                OutputPath = diagnostic.OutputPath,
                OutputImageRegister = diagnostic.OutputImageRegister,
                OutputBit = diagnostic.OutputBit,
                EnableRegister = diagnostic.EnableRegister,
                OutputRegister = diagnostic.OutputRegister,
                EnableValue = diagnostic.EnableValue,
                OutputValue = diagnostic.OutputValue,
                Result = diagnostic.Result,
                Source = diagnostic.Source,
                Detail = diagnostic.Detail
            };
        }

        private static LotSession CopyLot(LotSession lot)
        {
            if (lot == null)
            {
                return null;
            }

            return new LotSession
            {
                Id = lot.Id,
                StartedAt = lot.StartedAt,
                ClosedAt = lot.ClosedAt,
                IsActive = lot.IsActive,
                ShadowOnly = lot.ShadowOnly,
                SortingMode = lot.SortingMode,
                CellType = lot.CellType,
                OdooLotReference = lot.OdooLotReference,
                OdooLotName = lot.OdooLotName,
                OdooProductReference = lot.OdooProductReference,
                OdooProductName = lot.OdooProductName,
                OdooLinkSource = lot.OdooLinkSource,
                OdooVerified = lot.OdooVerified,
                OdooLinkedAt = lot.OdooLinkedAt,
                OdooNote = lot.OdooNote,
                LearningStatus = lot.LearningStatus,
                TotalCount = lot.TotalCount,
                GoodCount = lot.GoodCount,
                NgCount = lot.NgCount,
                InvalidMeasurementCount = lot.InvalidMeasurementCount,
                LearnedCellCount = lot.LearnedCellCount,
                CurrentGoodLane = lot.CurrentGoodLane,
                NextGoodLane = lot.NextGoodLane,
                PauseRequested = lot.PauseRequested,
                AlertMessage = lot.AlertMessage,
                Reference = lot.Reference == null ? null : new LotReference
                {
                    Version = lot.Reference.Version,
                    CellType = lot.Reference.CellType,
                    SampleSizeTarget = lot.Reference.SampleSizeTarget,
                    SampleSizeValid = lot.Reference.SampleSizeValid,
                    MeanVoltage = lot.Reference.MeanVoltage,
                    SigmaVoltage = lot.Reference.SigmaVoltage,
                    MeanIr = lot.Reference.MeanIr,
                    SigmaIr = lot.Reference.SigmaIr,
                    RoutingModel = lot.Reference.RoutingModel,
                    VoltageMin = lot.Reference.VoltageMin,
                    VoltageMax = lot.Reference.VoltageMax,
                    IrMin = lot.Reference.IrMin,
                    IrMax = lot.Reference.IrMax,
                    QualityIntervals = CopyQualityIntervals(lot.Reference.QualityIntervals),
                    ValidatedAt = lot.Reference.ValidatedAt,
                    Status = lot.Reference.Status
                },
                RecentSample = CopySamples(lot.RecentSample),
                Lanes = CopyLanes(lot.Lanes),
                MachineCounterBaselines = lot.MachineCounterBaselines == null ? new List<int>() : new List<int>(lot.MachineCounterBaselines),
                MachineNgBaseline = lot.MachineNgBaseline,
                RoutingLedger = CopyRoutingLedger(lot.RoutingLedger),
                RoutingArchive = CopyRoutingTickets(lot.RoutingArchive)
            };
        }

        private static RoutingLedgerState CopyRoutingLedger(RoutingLedgerState ledger)
        {
            if (ledger == null)
            {
                return new RoutingLedgerState
                {
                    NextSequence = 1,
                    Tickets = new List<RoutingTicket>()
                };
            }

            var copy = new RoutingLedgerState
            {
                NextSequence = ledger.NextSequence,
                Tickets = new List<RoutingTicket>()
            };

            copy.Tickets = CopyRoutingTickets(ledger.Tickets);

            return copy;
        }

        private static List<RoutingTicket> CopyRoutingTickets(List<RoutingTicket> tickets)
        {
            var copy = new List<RoutingTicket>();
            if (tickets == null)
            {
                return copy;
            }

            foreach (var ticket in tickets)
            {
                if (ticket == null)
                {
                    continue;
                }

                copy.Add(new RoutingTicket
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
                });
            }

            return copy;
        }

        private static LaneState CopyLane(LaneState lane)
        {
            if (lane == null)
            {
                return null;
            }

            return new LaneState
            {
                LaneId = lane.LaneId,
                Role = lane.Role,
                Status = lane.Status,
                CapacityTarget = lane.CapacityTarget,
                CapacityObserved = lane.CapacityObserved,
                CapacityConfidence = lane.CapacityConfidence,
                CountAssigned = lane.CountAssigned,
                MachineCount = lane.MachineCount,
                MachineFullSignal = lane.MachineFullSignal,
                LastSwitchIn = lane.LastSwitchIn,
                LastSwitchOut = lane.LastSwitchOut
            };
        }

        private static List<LaneState> CopyLanes(List<LaneState> lanes)
        {
            var copy = new List<LaneState>();
            if (lanes == null)
            {
                return copy;
            }

            foreach (var lane in lanes)
            {
                copy.Add(CopyLane(lane));
            }
            return copy;
        }

        private static List<SamplePoint> CopySamples(List<SamplePoint> sample)
        {
            var copy = new List<SamplePoint>();
            if (sample == null)
            {
                return copy;
            }

            foreach (var point in sample)
            {
                copy.Add(new SamplePoint
                {
                    Voltage = point.Voltage,
                    Ir = point.Ir,
                    Timestamp = point.Timestamp
                });
            }
            return copy;
        }

        private static List<QualityIntervalSnapshot> CopyQualityIntervals(List<QualityIntervalSnapshot> intervals)
        {
            var copy = new List<QualityIntervalSnapshot>();
            if (intervals == null)
            {
                return copy;
            }

            foreach (var interval in intervals)
            {
                if (interval == null)
                {
                    continue;
                }

                copy.Add(new QualityIntervalSnapshot
                {
                    Index = interval.Index,
                    LaneId = interval.LaneId,
                    IrMin = interval.IrMin,
                    IrMax = interval.IrMax,
                    VoltageMin = interval.VoltageMin,
                    VoltageMax = interval.VoltageMax,
                    LearningSampleCount = interval.LearningSampleCount
                });
            }

            return copy;
        }

        private static DiagnosticSnapshot CreateEmptyDiagnostic(MachineConfig cfg)
        {
            return new DiagnosticSnapshot
            {
                SourceMode = cfg.UseSimulator ? "SIMULATEUR" : "PLC",
                ObservationOnly = cfg.ObservationOnly,
                ShadowMode = cfg.ShadowMode,
                HandshakeRegister = cfg.HandshakeRegister,
                StatusRegister = cfg.StatusRegister,
                MeasurementRegisters = new List<ushort>(),
                DisplayRegisters = new List<ushort>(),
                AlarmRegisters = new List<ushort>(),
                ScannerStatus = "Aucune lecture",
                ScannerParity = cfg.ScanParity,
                ThresholdsObserved = false,
                ThresholdStatus = "Aucune lecture",
                ThresholdDifferences = new List<ThresholdDifference>(),
                PhysicalRouting = new PhysicalRoutingDiagnostic
                {
                    ExpectedLane = "NG",
                    AppliedLane = "NG",
                    ConfirmedLane = null,
                    HandshakeRegister = cfg.HandshakeRegister,
                    StatusRegister = cfg.StatusRegister,
                    ThresholdStatus = "Aucune lecture",
                    ProgrammedThresholds = new ThresholdSet { Channels = new List<ChannelThreshold>() },
                    ObservedThresholds = null,
                    AlarmRegisters = new List<ushort>(),
                    AlarmSummary = "Aucune alarme",
                    LastNgPulse = CreateEmptyNgPulseDiagnostic(),
                    PhysicalRoutingMode = "PLC_THRESHOLDS_NG_CATCHALL",
                    GoodPusherDirectControlBlocked = true
                },
                StartReadiness = new StartReadinessDiagnostic
                {
                    ReadyToStart = false,
                    Connected = false,
                    HandshakeReady = false,
                    HandshakeRegister = cfg.HandshakeRegister,
                    HandshakeValue = null,
                    HandshakeChangedAt = null,
                    LotAssociated = false,
                    ModelStable = false,
                    ThresholdsSynchronized = false,
                    MachineRequiresReset = false,
                    BlockingAlarmActive = false,
                    OperatorConfirmationRequired = true,
                    MachineStatus = null,
                    LotStatus = "INITIALIZING",
                    ExpectedLane = "NG",
                    AppliedLane = "NG",
                    BlockingReasons = new List<string> { "Initialisation en cours." },
                    Warnings = new List<string>(),
                    OperatorChecks = new List<string>()
                },
                FieldValidation = new FieldValidationDiagnostic
                {
                    HasReport = false,
                    Verified = false,
                    Status = "NO_REPORT",
                    ReportLotId = null,
                    CurrentLotId = null,
                    MatchesCurrentLot = false,
                    TraceVerdict = "UNKNOWN",
                    CounterVerdict = "UNKNOWN",
                    PhysicalObservationVerdict = "UNKNOWN",
                    LaneCoverageVerdict = "UNKNOWN",
                    Summary = "Aucun rapport terrain operateur trouve.",
                    ValidationCommand = "validate_tricell_field.bat 180",
                    CheckCommand = "check_tricell_field_result.bat",
                    MissingReasons = new List<string> { "Rapport terrain operateur absent." }
                },
                ObservationEventCount = 0,
                Notes = new List<string> { "Initialisation en cours." }
            };
        }

        private static NgPulseDiagnostic CreateEmptyNgPulseDiagnostic()
        {
            return new NgPulseDiagnostic
            {
                Timestamp = null,
                Handshake = null,
                Status = "NONE",
                OutputPath = "Y11_4X_3144_BIT_10",
                OutputImageRegister = Y11OutputImageRegister,
                OutputBit = Y11OutputImageBit,
                EnableRegister = Y11OutputImageRegister,
                OutputRegister = Y11OutputImageRegister,
                EnableValue = 0,
                OutputValue = 0,
                Result = "Aucun pulse NG enregistré",
                Source = "LOCAL",
                Detail = "Diagnostic maintenance Y11 uniquement: en production, le vérin NG est poussé par le PLC via la voie 11 catch-all."
            };
        }

        private void NormalizeIntelligentRecipe(IntelligentRecipe recipe)
        {
            if (recipe == null)
            {
                return;
            }

            if (recipe.GoodLanes == null)
            {
                recipe.GoodLanes = new List<string>();
            }

            var defaultGoodLanes = BuildDefaultGoodLanes();
            recipe.GoodLanes = defaultGoodLanes;

            if (string.IsNullOrWhiteSpace(recipe.NgLane))
            {
                recipe.NgLane = "NG";
            }

            if (recipe.LaneCapacities == null)
            {
                recipe.LaneCapacities = new List<LaneCapacitySetting>();
            }

            var defaultCapacity = 20;
            foreach (var laneId in recipe.GoodLanes)
            {
                var targetCapacity = string.Equals(laneId, QualityBandRouting.LearningLaneId, StringComparison.OrdinalIgnoreCase)
                    ? QualityBandRouting.LearningSampleCount
                    : defaultCapacity;
                LaneCapacitySetting capacitySetting = null;
                foreach (var item in recipe.LaneCapacities)
                {
                    if (string.Equals(item.LaneId, laneId, StringComparison.OrdinalIgnoreCase))
                    {
                        capacitySetting = item;
                        break;
                    }
                }

                if (capacitySetting == null)
                {
                    recipe.LaneCapacities.Add(new LaneCapacitySetting
                    {
                        LaneId = laneId,
                        Capacity = targetCapacity
                    });
                    continue;
                }

                if (capacitySetting.Capacity <= 0 || capacitySetting.Capacity != targetCapacity)
                {
                    capacitySetting.Capacity = targetCapacity;
                }
            }

            recipe.SampleSize = QualityBandRouting.LearningSampleCount;

            if (recipe.LearningTimeoutCells < QualityBandRouting.LearningSampleCount)
            {
                recipe.LearningTimeoutCells = QualityBandRouting.LearningSampleCount * 2;
            }

            if (string.Equals(recipe.CellType, "21700", StringComparison.OrdinalIgnoreCase))
            {
                recipe.MaxSigmaVoltage = recipe.MaxSigmaVoltage <= 0 || recipe.MaxSigmaVoltage > 0.010 ? 0.010 : recipe.MaxSigmaVoltage;
                recipe.AcceptanceKVoltage = recipe.AcceptanceKVoltage <= 0 || recipe.AcceptanceKVoltage > 2.0 ? 2.0 : recipe.AcceptanceKVoltage;
                recipe.MinWindowVoltage = recipe.MinWindowVoltage <= 0 || recipe.MinWindowVoltage > 0.001 ? 0.001 : recipe.MinWindowVoltage;
                recipe.MaxWindowVoltage = recipe.MaxWindowVoltage <= 0 || recipe.MaxWindowVoltage > 0.020 ? 0.020 : recipe.MaxWindowVoltage;
                if (recipe.MaxWindowVoltage < recipe.MinWindowVoltage)
                {
                    recipe.MaxWindowVoltage = recipe.MinWindowVoltage;
                }

                recipe.MaxSigmaIr = recipe.MaxSigmaIr <= 0 || recipe.MaxSigmaIr > 2.0 ? 2.0 : recipe.MaxSigmaIr;
                recipe.AcceptanceKIr = recipe.AcceptanceKIr <= 0 ? 2.0 : recipe.AcceptanceKIr;
                recipe.MinWindowIr = recipe.MinWindowIr <= 0 || recipe.MinWindowIr > 0.25 ? 0.25 : recipe.MinWindowIr;
                recipe.MaxWindowIr = recipe.MaxWindowIr <= 0 || recipe.MaxWindowIr > 4.0 ? 4.0 : recipe.MaxWindowIr;
                if (recipe.MaxWindowIr < recipe.MinWindowIr)
                {
                    recipe.MaxWindowIr = recipe.MinWindowIr;
                }
            }
            else
            {
                recipe.MaxSigmaVoltage = recipe.MaxSigmaVoltage <= 0 || recipe.MaxSigmaVoltage > 0.010 ? 0.010 : recipe.MaxSigmaVoltage;
                recipe.AcceptanceKVoltage = recipe.AcceptanceKVoltage <= 0 || recipe.AcceptanceKVoltage > 2.0 ? 2.0 : recipe.AcceptanceKVoltage;
                recipe.MinWindowVoltage = recipe.MinWindowVoltage <= 0 || recipe.MinWindowVoltage > 0.001 ? 0.001 : recipe.MinWindowVoltage;
                recipe.MaxWindowVoltage = recipe.MaxWindowVoltage <= 0 || recipe.MaxWindowVoltage > 0.020 ? 0.020 : recipe.MaxWindowVoltage;
                recipe.MaxSigmaIr = recipe.MaxSigmaIr <= 0 || recipe.MaxSigmaIr > 2.0 ? 2.0 : recipe.MaxSigmaIr;
                recipe.AcceptanceKIr = recipe.AcceptanceKIr <= 0 || recipe.AcceptanceKIr > 2.0 ? 2.0 : recipe.AcceptanceKIr;
                recipe.MinWindowIr = recipe.MinWindowIr <= 0 || recipe.MinWindowIr > 0.25 ? 0.25 : recipe.MinWindowIr;
                recipe.MaxWindowIr = recipe.MaxWindowIr <= 0 || recipe.MaxWindowIr > 4.0 ? 4.0 : recipe.MaxWindowIr;
            }

            recipe.NegativeVoltageToNg = false;

            if (recipe.GoodLanes.Count > 0)
            {
                recipe.LastGoodLane = recipe.GoodLanes[recipe.GoodLanes.Count - 1];
            }

            recipe.LanePreSwitchMargin = 0;
        }

        private static List<string> BuildDefaultGoodLanes()
        {
            return QualityBandRouting.BuildDefaultGoodLanes();
        }

        private bool IsLotCompatibleWithRecipeNoLock(LotSession lot, IntelligentRecipe recipe)
        {
            if (lot == null || recipe == null || !string.Equals(lot.SortingMode, SortingModes.IntelligentGoodNg, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var expectedGoodLaneCount = recipe.GoodLanes == null ? 0 : recipe.GoodLanes.Count;
            if (expectedGoodLaneCount > 0)
            {
                var actualGoodLaneCount = 0;
                if (lot.Lanes != null)
                {
                    foreach (var lane in lot.Lanes)
                    {
                        if (string.Equals(lane.Role, "GOOD", StringComparison.OrdinalIgnoreCase))
                        {
                            actualGoodLaneCount++;
                        }
                    }
                }

                if (actualGoodLaneCount < expectedGoodLaneCount)
                {
                    return false;
                }
            }

            var referenceLaneId = QualityBandRouting.IsQualityIntervalRecipe(recipe)
                ? QualityBandRouting.LearningLaneId
                : (recipe.GoodLanes == null || recipe.GoodLanes.Count == 0 ? null : recipe.GoodLanes[0]);
            var requiredReferenceSample = QualityBandRouting.IsQualityIntervalRecipe(recipe)
                ? QualityBandRouting.LearningSampleCount
                : GetLaneCapacity(recipe, referenceLaneId);
            if (requiredReferenceSample <= 0)
            {
                requiredReferenceSample = recipe.SampleSize;
            }

            if (lot.Reference != null)
            {
                if (lot.Reference.SampleSizeTarget > 0 && lot.Reference.SampleSizeTarget != requiredReferenceSample)
                {
                    return false;
                }
            }

            return true;
        }

        private int ClampObservedCapacityCountNoLock(string cellType, string laneId, int observedCount)
        {
            if (observedCount <= 0)
            {
                return observedCount;
            }

            IntelligentRecipe recipe;
            if (!string.IsNullOrWhiteSpace(cellType) &&
                _intelligentRecipes != null &&
                _intelligentRecipes.TryGetValue(cellType, out recipe))
            {
                var configuredCapacity = GetLaneCapacity(recipe, laneId);
                if (configuredCapacity > 0)
                {
                    return Math.Min(observedCount, configuredCapacity);
                }
            }

            return observedCount;
        }

        private List<ThresholdDifference> BuildThresholdDifferences(ThresholdSet local, ThresholdSet observed)
        {
            var differences = new List<ThresholdDifference>();
            if (local == null || observed == null || local.Channels == null || observed.Channels == null)
            {
                return differences;
            }

            var count = Math.Min(local.Channels.Count, observed.Channels.Count);
            for (var i = 0; i < count; i++)
            {
                AddDifference(differences, i + 1, "V min", local.Channels[i].VoltageMin, observed.Channels[i].VoltageMin);
                AddDifference(differences, i + 1, "V max", local.Channels[i].VoltageMax, observed.Channels[i].VoltageMax);
                AddDifference(differences, i + 1, "IR min", local.Channels[i].IrMin, observed.Channels[i].IrMin);
                AddDifference(differences, i + 1, "IR max", local.Channels[i].IrMax, observed.Channels[i].IrMax);
            }
            return differences;
        }

        private static void AddDifference(List<ThresholdDifference> differences, int channel, string field, double localValue, double observedValue)
        {
            if (Math.Abs(localValue - observedValue) < 0.0005)
            {
                return;
            }

            differences.Add(new ThresholdDifference
            {
                Channel = channel,
                Field = field,
                LocalValue = localValue,
                ObservedValue = observedValue
            });
        }

        private string BuildScannerStatus(MachineConfig cfg)
        {
            if (!cfg.ScanEnabled)
            {
                return "Scanner désactivé";
            }

            if (!_scanner.IsConnected)
            {
                return "Scanner non connecté";
            }

            if (string.IsNullOrWhiteSpace(_scanner.LastBarcode))
            {
                return "Scanner connecté, aucun code récent";
            }

            return "Scanner connecté, dernier code " + _scanner.LastBarcode;
        }

        private string BuildAlarmSummary(List<int> alarms)
        {
            var labels = new List<string>();
            foreach (var idx in alarms)
            {
                if (idx >= 0 && idx < AlarmLabels.Count)
                {
                    labels.Add(AlarmLabels[idx]);
                }
            }

            if (labels.Count == 0)
            {
                return "Aucune";
            }

            return string.Join(" | ", labels.ToArray());
        }

        private string BuildReferenceSummary(LotSession lot)
        {
            if (lot == null || lot.Reference == null)
            {
                return "--";
            }

            IntelligentRecipe recipe;
            if (_intelligentRecipes != null &&
                !string.IsNullOrWhiteSpace(lot.CellType) &&
                _intelligentRecipes.TryGetValue(lot.CellType, out recipe) &&
                QualityBandRouting.IsQualityIntervalRecipe(recipe) &&
                lot.RecentSample != null &&
                lot.RecentSample.Count >= QualityBandRouting.MinimumLearningSampleCount)
            {
                var windows = QualityBandRouting.BuildWindows(recipe, lot.Reference, lot.RecentSample);
                return "9 intervalles IR figes | V garde " +
                    windows.VoltageGuardMin.ToString("0.0000", CultureInfo.InvariantCulture) +
                    "-" +
                    windows.VoltageGuardMax.ToString("0.0000", CultureInfo.InvariantCulture) +
                    " | L1 " + QualityBandRouting.DescribeBand(windows, 1) +
                    " | L9 " + QualityBandRouting.DescribeBand(windows, 9);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "V {0:0.0000} +/- {1:0.0000} | IR {2:0.000} +/- {3:0.000}",
                lot.Reference.MeanVoltage,
                lot.Reference.SigmaVoltage,
                lot.Reference.MeanIr,
                lot.Reference.SigmaIr);
        }

        private static List<ushort> ToList(ushort[] registers)
        {
            return registers == null ? new List<ushort>() : new List<ushort>(registers);
        }

        private void ResetCounters()
        {
            _counters = new List<int>();
            for (var i = 0; i < _config.Channels; i++)
            {
                _counters.Add(0);
            }
            _total = 0;
            _good = 0;
            _ng = 0;
            _machineCounters = new List<int>();
            _machineTotal = 0;
            _machineGood = 0;
            _machineNg = 0;
            _machineCountersAvailable = false;
        }

        private string GetBarcode(MachineConfig cfg)
        {
            if (!cfg.ScanEnabled)
            {
                return null;
            }

            if (!_scanner.EnsureOpen(cfg.ScanComPort, cfg.ScanBaudRate, ParseParity(cfg.ScanParity)))
            {
                return cfg.NoBarcodeValue;
            }

            var last = _scanner.LastBarcode;
            var time = _scanner.LastBarcodeTime;
            if (string.IsNullOrWhiteSpace(last))
            {
                return cfg.NoBarcodeValue;
            }

            if ((DateTime.Now - time).TotalSeconds > cfg.ScanTimeoutS)
            {
                return cfg.NoBarcodeValue;
            }

            return last;
        }

        private static Parity ParseParity(string parityName)
        {
            if (string.IsNullOrWhiteSpace(parityName))
            {
                return Parity.Even;
            }

            try
            {
                return (Parity)Enum.Parse(typeof(Parity), parityName, true);
            }
            catch
            {
                return Parity.Even;
            }
        }

        private static List<string> BuildAlarmLabels()
        {
            return new List<string>
            {
                "Alarme moteur",
                "Erreur de remise à zéro",
                "Arrêt d’urgence appuyé",
                "Système verrouillé",
                "Manque de cellule",
                "Alarme déchargement",
                "Cellule bloquée",
                "Cellule bloquée (zone 2)",
                "Cellule bloquée (zone 3)",
                "Erreur vérin test gauche",
                "Erreur vérin test",
                "Erreur reset vérin test gauche",
                "Erreur reset vérin test",
                "Erreur pression air",
                "Testeur sans réponse",
                "Manque de cellule, arrêt automatique",
                "Erreur reset vérin blocage haut",
                "Erreur reset vérin blocage bas",
                "Bac plein / cellule coincée",
                "Scanner sans réponse",
                "Erreur reset serrage scanner",
                "Quantité de lot atteinte",
                "Alarme réservée 23",
                "Alarme réservée 24",
                "Alarme réservée 25",
                "Alarme réservée 26",
                "Alarme réservée 27",
                "Alarme réservée 28",
                "Alarme réservée 29",
                "Alarme réservée 30",
                "Alarme réservée 31",
                "Alarme réservée 32",
                "Erreur reset poussoir 1",
                "Erreur reset poussoir 2",
                "Erreur reset poussoir 3",
                "Erreur reset poussoir 4",
                "Erreur reset poussoir 5",
                "Erreur reset poussoir 6",
                "Erreur reset poussoir 7",
                "Erreur reset poussoir 8",
                "Erreur reset poussoir 9",
                "Erreur reset poussoir 10",
                "Erreur reset poussoir 11",
                "Erreur reset poussoir 12",
                "Erreur reset poussoir 13",
                "Erreur reset poussoir 14",
                "Erreur reset poussoir 15",
                "Erreur reset poussoir 16",
                "Erreur reset poussoir 17",
                "Erreur reset poussoir 18",
                "Erreur reset poussoir 19",
                "Erreur reset poussoir 20",
                "Erreur reset poussoir 21",
                "Alarme réservée 54",
                "Alarme réservée 55",
                "Alarme réservée 56",
                "Alarme réservée 57",
                "Alarme réservée 58",
                "Alarme réservée 59",
                "Alarme réservée 60",
                "Alarme réservée 61",
                "Alarme réservée 62",
                "Alarme réservée 63",
                "Alarme réservée 64"
            };
        }
    }
}
