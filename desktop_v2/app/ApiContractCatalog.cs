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
                    "Registre 8230 pour le handshake et 8408 pour les mesures.",
                    "Scanner COM2 / 115200 / parité Even par défaut."
                }
            };
        }
    }
}
