using System.Collections.Generic;

namespace SortingMachineDesktop
{
    internal static class ApiContractCatalog
    {
        public static ContractBundle Build()
        {
            return new ContractBundle
            {
                Contracts = new List<ContractDefinition>
                {
                    new ContractDefinition
                    {
                        Name = "MachineConfig",
                        Description = "Configuration d’observation et de connexion machine en lecture seule.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "ComPort", Type = "string", Description = "Port série automate." },
                            new ContractField { Name = "BaudRate", Type = "int", Description = "Baudrate Modbus RTU." },
                            new ContractField { Name = "SlaveId", Type = "int", Description = "Adresse esclave Modbus." },
                            new ContractField { Name = "MeasurementRegister", Type = "int", Description = "Adresse de lecture du paquet mesure IR/Tension." },
                            new ContractField { Name = "AlarmRegister", Type = "int", Description = "Base des 4 registres d’alarmes." },
                            new ContractField { Name = "HandshakeRegister", Type = "int", Description = "Registre de top cycle observé." },
                            new ContractField { Name = "SortingMode", Type = "enum", Description = "LEGACY ou INTELLIGENT_GOOD_NG." },
                            new ContractField { Name = "SafeMode", Type = "bool", Description = "Bloque toute commande d’écriture." },
                            new ContractField { Name = "ObservationOnly", Type = "bool", Description = "Force l’application en lecture seule." },
                            new ContractField { Name = "ShadowMode", Type = "bool", Description = "Compare les réglages locaux avec les valeurs lues sur la machine." }
                        }
                    },
                    new ContractDefinition
                    {
                        Name = "LegacyRecipe",
                        Description = "Recette constructeur conservée pour compatibilité.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "CellType", Type = "string", Description = "Type de cellule (21700 ou 18650)." },
                            new ContractField { Name = "Thresholds", Type = "ChannelThreshold[]", Description = "Seuils par canal actif." },
                            new ContractField { Name = "JudgeMode", Type = "enum", Description = "VOLTAGE, IR ou BOTH." },
                            new ContractField { Name = "ChannelStart", Type = "int", Description = "Premier canal pris en compte." },
                            new ContractField { Name = "ChannelEnd", Type = "int", Description = "Dernier canal pris en compte." }
                        }
                    },
                    new ContractDefinition
                    {
                        Name = "IntelligentRecipe",
                        Description = "Recette GOOD / NG intelligent par type cellule.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "SampleSize", Type = "int", Description = "Nombre de cellules d’apprentissage." },
                            new ContractField { Name = "MaxSigmaVoltage", Type = "double", Description = "Dispersion max sur la tension." },
                            new ContractField { Name = "MaxSigmaIr", Type = "double", Description = "Dispersion max sur l’IR." },
                            new ContractField { Name = "AcceptanceKVoltage", Type = "double", Description = "Multiplicateur sigma tension." },
                            new ContractField { Name = "AcceptanceKIr", Type = "double", Description = "Multiplicateur sigma IR." },
                            new ContractField { Name = "GoodLanes", Type = "string[]", Description = "Lignes bonnes ordonnées." },
                            new ContractField { Name = "NgLane", Type = "string", Description = "Voie NG." },
                            new ContractField { Name = "LaneCapacities", Type = "LaneCapacitySetting[]", Description = "Capacité cible par ligne." }
                        }
                    },
                    new ContractDefinition
                    {
                        Name = "LotSession",
                        Description = "État courant ou historique d’un lot.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "SortingMode", Type = "enum", Description = "Mode du lot." },
                            new ContractField { Name = "CellType", Type = "string", Description = "Type de cellule." },
                            new ContractField { Name = "LearningStatus", Type = "enum", Description = "IDLE, LEARNING, STABLE, UNSTABLE." },
                            new ContractField { Name = "Reference", Type = "LotReference", Description = "Référence apprise si disponible." },
                            new ContractField { Name = "CurrentGoodLane", Type = "string", Description = "Ligne bonne courante." },
                            new ContractField { Name = "NextGoodLane", Type = "string", Description = "Prochaine ligne bonne." }
                        }
                    },
                    new ContractDefinition
                    {
                        Name = "MachineObservationEvent",
                        Description = "Événement d’étude capturé à partir d’un cycle observé.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "Timestamp", Type = "string", Description = "Horodatage local de la capture." },
                            new ContractField { Name = "Source", Type = "string", Description = "SIMULATEUR ou PLC." },
                            new ContractField { Name = "Handshake", Type = "int?", Description = "Valeur lue sur le registre 8230." },
                            new ContractField { Name = "SortingMode", Type = "enum", Description = "Mode actif." },
                            new ContractField { Name = "Voltage", Type = "double", Description = "Tension lue ou simulée." },
                            new ContractField { Name = "Ir", Type = "double", Description = "Résistance interne lue ou simulée." },
                            new ContractField { Name = "Barcode", Type = "string", Description = "Dernier code barre connu ou valeur de repli." },
                            new ContractField { Name = "LegacyChannel", Type = "string", Description = "Canal legacy calculé en shadow." },
                            new ContractField { Name = "Channel", Type = "string", Description = "Ligne cible du moteur actif." },
                            new ContractField { Name = "Result", Type = "string", Description = "GOOD, NG ou PAUSE." }
                        }
                    },
                    new ContractDefinition
                    {
                        Name = "PhysicalRoutingDiagnostic",
                        Description = "Diagnostic atelier du routage physique expose par /api/diagnostic/physical-routing.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "ExpectedLane", Type = "string", Description = "Ligne attendue par le moteur de tri." },
                            new ContractField { Name = "AppliedLane", Type = "string", Description = "Ligne programmee/appliquee via seuils machine." },
                            new ContractField { Name = "ConfirmedLane", Type = "string", Description = "Ligne confirmee si le ledger machine la connait." },
                            new ContractField { Name = "LastHandshake", Type = "int?", Description = "Dernier top 8230 accepte." },
                            new ContractField { Name = "MachineStatus", Type = "int?", Description = "Statut machine 8231." },
                            new ContractField { Name = "ThresholdStatus", Type = "string", Description = "Synchronisation seuils programmes vs seuils relus." },
                            new ContractField { Name = "ProgrammedThresholds", Type = "ThresholdSet", Description = "Seuils 1188..1370 attendus/programmes." },
                            new ContractField { Name = "ObservedThresholds", Type = "ThresholdSet", Description = "Seuils relus sur PLC si disponibles." },
                            new ContractField { Name = "AlarmRegisters", Type = "ushort[]", Description = "Registres d'alarmes 22808..22811." },
                            new ContractField { Name = "LastNgPulse", Type = "NgPulseDiagnostic", Description = "Dernier pulse maintenance de la sortie carte Y11 (diagnostic). En production, le verin NG est pousse par le PLC via la voie 11 catch-all." },
                            new ContractField { Name = "PhysicalRoutingMode", Type = "string", Description = "Mode physique, PLC_THRESHOLDS_NG_CATCHALL en production." },
                            new ContractField { Name = "GoodPusherDirectControlBlocked", Type = "bool", Description = "True: les pistons GOOD restent pilotes par le PLC via les seuils, pas par impulsion PC directe." }
                        }
                    },
                    new ContractDefinition
                    {
                        Name = "NgPulseDiagnostic",
                        Description = "Dernier pulse maintenance de la sortie carte Y11 (diagnostic machine arretee). Les champs EnableRegister/OutputRegister restent conserves pour compatibilite; OutputPath, OutputImageRegister et OutputBit decrivent la sortie carte. Ce diagnostic ne concerne pas le verin NG de production, pilote par le PLC via la voie 11 catch-all.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "Timestamp", Type = "string", Description = "Horodatage local du dernier pulse Y11 maintenance." },
                            new ContractField { Name = "Handshake", Type = "int?", Description = "Top 8230 associe au pulse si connu." },
                            new ContractField { Name = "Status", Type = "string", Description = "NONE, ATTEMPT, SIMULATED, SENT ou ERROR." },
                            new ContractField { Name = "OutputPath", Type = "string", Description = "Chemin sortie carte, Y11_4X_3144_BIT_10." },
                            new ContractField { Name = "OutputImageRegister", Type = "int", Description = "Holding image sortie Y, attendu 3144." },
                            new ContractField { Name = "OutputBit", Type = "int", Description = "Bit Y11 dans l'image 3144, attendu 10." },
                            new ContractField { Name = "Result", Type = "string", Description = "Resultat operateur du dernier pulse Y11 maintenance." },
                            new ContractField { Name = "Detail", Type = "string", Description = "Detail trace, incluant Y11 ON/OFF." }
                        }
                    },
                    new ContractDefinition
                    {
                        Name = "StartReadinessDiagnostic",
                        Description = "Pre-vol operateur expose par /api/diagnostic/start-readiness avant DÉMARRER.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "ReadyToStart", Type = "bool", Description = "Aucun blocage logiciel detecte; l'operateur doit rester present." },
                            new ContractField { Name = "Connected", Type = "bool", Description = "Lecture PLC disponible." },
                            new ContractField { Name = "HandshakeReady", Type = "bool", Description = "True si le top 8230 courant est connu avant START." },
                            new ContractField { Name = "HandshakeRegister", Type = "int", Description = "Registre top cycle, attendu 8230." },
                            new ContractField { Name = "HandshakeValue", Type = "int?", Description = "Derniere valeur 8230 lue dans le diagnostic." },
                            new ContractField { Name = "HandshakeChangedAt", Type = "string", Description = "Horodatage du dernier changement 8230 connu." },
                            new ContractField { Name = "LotAssociated", Type = "bool", Description = "Lot Odoo verifie associe." },
                            new ContractField { Name = "ModelStable", Type = "bool", Description = "Modele 19 cellules stable." },
                            new ContractField { Name = "ThresholdsSynchronized", Type = "bool", Description = "Seuils programmes et relus synchronises." },
                            new ContractField { Name = "MachineRequiresReset", Type = "bool", Description = "Statut machine 7 detecte." },
                            new ContractField { Name = "BlockingReasons", Type = "string[]", Description = "Raisons bloquantes avant START." },
                            new ContractField { Name = "Warnings", Type = "string[]", Description = "Avertissements non bloquants." },
                            new ContractField { Name = "OperatorChecks", Type = "string[]", Description = "Points de controle terrain avant essai physique." }
                        }
                    },
                    new ContractDefinition
                    {
                        Name = "FieldValidationDiagnostic",
                        Description = "Etat du dernier rapport terrain expose par /api/diagnostic/field-validation.",
                        Fields = new List<ContractField>
                        {
                            new ContractField { Name = "HasReport", Type = "bool", Description = "Un rapport field_validation operateur existe hors smoke Codex." },
                            new ContractField { Name = "Verified", Type = "bool", Description = "Trace, compteurs, observation, couverture voies GOOD et lot courant sont OK avec conclusion complete." },
                            new ContractField { Name = "Status", Type = "string", Description = "NO_REPORT, INCOMPLETE, COMPLETE ou ERROR." },
                            new ContractField { Name = "ReportLotId", Type = "int?", Description = "Lot lu dans le rapport terrain." },
                            new ContractField { Name = "CurrentLotId", Type = "int?", Description = "Lot actif courant au moment du diagnostic." },
                            new ContractField { Name = "MatchesCurrentLot", Type = "bool", Description = "Le rapport terrain concerne le lot courant." },
                            new ContractField { Name = "TraceVerdict", Type = "string", Description = "Verdict trace logiciel du rapport." },
                            new ContractField { Name = "CounterVerdict", Type = "string", Description = "Verdict compteurs machine du rapport." },
                            new ContractField { Name = "PhysicalObservationVerdict", Type = "string", Description = "Verdict observation physique operateur du rapport." },
                            new ContractField { Name = "LaneCoverageVerdict", Type = "string", Description = "Verdict couverture terrain des lignes GOOD 1..9." },
                            new ContractField { Name = "ValidationCommand", Type = "string", Description = "Commande de lancement surveillance terrain." },
                            new ContractField { Name = "CheckCommand", Type = "string", Description = "Commande de verification rapport terrain." }
                        }
                    }
                },
                Constraints = new List<string>
                {
                    "Aucune écriture automate tant que SafeMode et ObservationOnly restent actifs.",
                    "Les commandes physiques restent non prouvées tant qu’une validation terrain dédiée n’a pas été faite.",
                    "Le shadow mode compare la configuration locale aux seuils lus sur la machine sans modifier la machine.",
                    "Le mode 9 intervalles ignore la disponibilite de bac: seule la mesure decide GOOD/NG."
                },
                Defaults = new List<string>
                {
                    "Mode intelligent GOOD / NG actif par défaut.",
                    "COM1 / 19200 / esclave 1 pour l’automate.",
                    "Registre 8230 pour le handshake et 8408 pour le paquet mesures IR/tension.",
                    "Scanner COM2 / 115200 / parité Even par défaut."
                }
            };
        }
    }
}
