using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System;
using System.Linq;
using System.Threading;

namespace SysBot.Pokemon
{
    public static class AutoLegalityWrapper
    {
        private static bool Initialized;
        private static readonly EncounterTypeGroup[] EncounterPriority = [EncounterTypeGroup.Egg, EncounterTypeGroup.Slot, EncounterTypeGroup.Static, EncounterTypeGroup.Mystery, EncounterTypeGroup.Trade];

        public static void EnsureInitialized(LegalitySettings cfg)
        {
            if (Initialized)
                return;
            Initialized = true;
            InitializeAutoLegality(cfg);
        }

        private static void InitializeAutoLegality(LegalitySettings cfg)
        {
            InitializeCoreStrings();
            InitializeTrainerDatabase();
            InitializeSettings(cfg);
        }

        private static void InitializeSettings(LegalitySettings cfg)
        {
            APILegality.SetAllLegalRibbons = cfg.SetAllLegalRibbons;
            APILegality.SetMatchingBalls = cfg.SetMatchingBalls;
            APILegality.ForceSpecifiedBall = cfg.ForceSpecifiedBall;
            APILegality.ForceLevel100for50 = cfg.ForceLevel100for50;
            Legalizer.EnableEasterEggs = cfg.EnableEasterEggs;
            APILegality.AllowTrainerOverride = cfg.AllowTrainerDataOverride;
            APILegality.AllowBatchCommands = cfg.AllowBatchCommands;
            APILegality.PrioritizeGame = cfg.PrioritizeGame;
            APILegality.PrioritizeGameVersion = cfg.PrioritizeGameVersion;
            APILegality.SetBattleVersion = cfg.SetBattleVersion;
            APILegality.Timeout = cfg.Timeout;
            var settings = ParseSettings.Settings;
            settings.Handler.CheckActiveHandler = false;
            var validRestriction = new NicknameRestriction { NicknamedTrade = Severity.Fishy, NicknamedMysteryGift = Severity.Fishy };
            settings.Nickname.SetAllTo(validRestriction);

            // As of February 2024, the default setting in PKHeX is Invalid for missing HOME trackers.
            // If the host wants to allow missing HOME trackers, we need to override the default setting.
            bool allowMissingHOME = !cfg.EnableHOMETrackerCheck;
            APILegality.AllowHOMETransferGeneration = allowMissingHOME;
            if (allowMissingHOME)
                settings.HOMETransfer.HOMETransferTrackerNotPresent = Severity.Fishy;

            // We need all the encounter types present, so add the missing ones at the end.
            var missing = EncounterPriority.Except(cfg.PrioritizeEncounters);
            cfg.PrioritizeEncounters.AddRange(missing);
            cfg.PrioritizeEncounters = cfg.PrioritizeEncounters.Distinct().ToList(); // Don't allow duplicates.
            EncounterMovesetGenerator.PriorityList = cfg.PrioritizeEncounters;
        }

        private static void InitializeTrainerDatabase()
        {
            // Seed the Trainer Database with enough fake save files so that we return a generation sensitive format when needed.
            string OT = "Trainer";
            ushort TID = 52345;
            ushort SID = 12345;
            int lang = (int)LanguageID.English;

            for (int i = 1; i < Latest.Generation + 1; i++)
            {
                var versions = GameUtil.GetVersionsInGeneration((byte)i, (GameVersion)Latest.Generation);
                foreach (var v in versions)
                {
                    var gameVersion = v;
                    var fallback = new SimpleTrainerInfo(gameVersion)
                    {
                        Language = lang,
                        TID16 = TID,
                        SID16 = SID,
                        OT = OT,
                        Generation = (byte)i,
                    };

                    var exist = TrainerSettings.GetSavedTrainerData((byte)gameVersion, (GameVersion)(byte)i, fallback);
                    if (exist is SimpleTrainerInfo) // not anything from files; this assumes ALM returns SimpleTrainerInfo for non-user-provided fake templates.
                        TrainerSettings.Register(fallback);
                }
            }
        }

        private static void InitializeCoreStrings()
        {
            var lang = Thread.CurrentThread.CurrentCulture.TwoLetterISOLanguageName[..2];
            LocalizationUtil.SetLocalization(typeof(LegalityCheckStrings), lang);
            LocalizationUtil.SetLocalization(typeof(MessageStrings), lang);
            RibbonStrings.ResetDictionary(GameInfo.Strings.ribbons);
            ParseSettings.ChangeLocalizationStrings(GameInfo.Strings.movelist, GameInfo.Strings.specieslist);
        }

        public static ITrainerInfo GetTrainerInfo<T>() where T : PKM, new()
        {
            if (typeof(T) == typeof(PK8))
                return TrainerSettings.GetSavedTrainerData(GameVersion.SWSH, 8);
            if (typeof(T) == typeof(PB8))
                return TrainerSettings.GetSavedTrainerData(GameVersion.BDSP, 8);
            if (typeof(T) == typeof(PA8))
                return TrainerSettings.GetSavedTrainerData(GameVersion.PLA, 8);
            if (typeof(T) == typeof(PK9))
                return TrainerSettings.GetSavedTrainerData(GameVersion.SV, 9);

            throw new ArgumentException("Type does not have a recognized trainer fetch.", typeof(T).Name);
        }

        public static ITrainerInfo GetTrainerInfo(byte gen) => TrainerSettings.GetSavedTrainerData(gen);

        public static PKM GetLegal(this ITrainerInfo sav, IBattleTemplate set, out string res)
        {
            var result = sav.GetLegalFromSet(set);
            res = result.Status switch
            {
                LegalizationResult.Regenerated => "Regenerated",
                LegalizationResult.Failed => "Failed",
                LegalizationResult.Timeout => "Timeout",
                LegalizationResult.VersionMismatch => "VersionMismatch",
                _ => "",
            };
            return result.Created;
        }

        public static IBattleTemplate GetTemplate(ShowdownSet set) => new RegenTemplate(set);
    }
}