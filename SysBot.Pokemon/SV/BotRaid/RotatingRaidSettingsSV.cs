using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace SysBot.Pokemon
{

    public class RotatingRaidSettingsSV : IBotStateSettings
    {
        private const string Hosting = nameof(Hosting);
        private const string Counts = nameof(Counts);
        private const string FeatureToggle = nameof(FeatureToggle);
        
        public override string ToString() => "Rotating Raid Settings (Sc/Vi Only)";
        [DisplayName("Active Raid List")]

        [Category(Hosting), Description("Your Active Raid List lives here.")]
        public List<RotatingRaidParameters> ActiveRaids { get; set; } = new();

        [DisplayName("Raid Settings")]
        public RotatingRaidSettingsCategory RaidSettings { get; set; } = new RotatingRaidSettingsCategory();

        [DisplayName("Discord Embed Settings")]
        public RotatingRaidPresetFiltersCategory EmbedToggles { get; set; } = new RotatingRaidPresetFiltersCategory();

        [DisplayName("Raid Lobby Settings")]

        [Category(Hosting), Description("Lobby Options"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public LobbyFiltersCategory LobbyOptions { get; set; } = new();

        [DisplayName("Banned Raiders List")]

        [Category(Hosting), Description("Users NIDs here are banned raiders.")]
        public RemoteControlAccessList RaiderBanList { get; set; } = new() { AllowIfEmpty = false };

        [DisplayName("Random Settings")]
        public MiscSettingsCategory MiscSettings { get; set; } = new MiscSettingsCategory();

        [Browsable(false)]
        public bool ScreenOff
        {
            get => MiscSettings.ScreenOff;
            set => MiscSettings.ScreenOff = value;
        }

        public class RotatingRaidParameters
        {
            public override string ToString() => $"{Title}";

            [DisplayName("Enable Raid?")]
            public bool ActiveInRotation { get; set; } = true;

            [DisplayName("Species")]
            public Species Species { get; set; } = Species.None;

            [DisplayName("Pokemon Form Number")]
            public int SpeciesForm { get; set; } = 0;

            [DisplayName("Is Pokemon Shiny?")]
            public bool IsShiny { get; set; } = false;

            [DisplayName("Crystal Type")]
            public TeraCrystalType CrystalType { get; set; } = TeraCrystalType.Base;

            [DisplayName("Make Raid Coded?")]
            public bool IsCoded { get; set; } = true;

            [DisplayName("Seed")]
            public string Seed { get; set; } = "0";

            [DisplayName("Star Count")]
            public int DifficultyLevel { get; set; } = 0;

            [DisplayName("Game Progress")]
            [TypeConverter(typeof(EnumConverter))]
            public GameProgressEnum StoryProgress { get; set; } = GameProgressEnum.Unlocked6Stars;

            [DisplayName("Raid Battler (Showdown Format)")]
            public string[] PartyPK { get; set; } = [];

            [DisplayName("Action Bot Should Use")]
            public Action1Type Action1 { get; set; } = Action1Type.GoAllOut;

            [DisplayName("Action Delay (In Seconds)")]
            public int Action1Delay { get; set; } = 5;

            [DisplayName("Group ID  (Event Raids Only)")]
            public int? GroupID { get; set; } = 0;

            [DisplayName("Embed Title")]
            public string Title { get; set; } = string.Empty;

            [Browsable(false)]
            public bool AddedByRACommand { get; set; } = false;

            [Browsable(false)]
            public bool SpriteAlternateArt { get; set; } = false; // Not enough alt art to even turn on

            [Browsable(false)]
            public string[] Description { get; set; } = [];

            [Browsable(false)]
            public bool RaidUpNext { get; set; } = false;

            [Browsable(false)]
            public string RequestCommand { get; set; } = string.Empty;

            [Browsable(false)]
            public ulong RequestedByUserID { get; set; }

            [Browsable(false)]
            [System.Text.Json.Serialization.JsonIgnore]
            public SocketUser? User { get; set; }

            [Browsable(false)]
            [System.Text.Json.Serialization.JsonIgnore]
            public List<SocketUser> MentionedUsers { get; set; } = [];
        }

        public class TeraTypeBattlers
        {
            public override string ToString() => $"Define your Raid Battlers";
            [DisplayName("Bug Battler")]
            public string[] BugBattler { get; set; } = [];

            [DisplayName("Dark Battler")]
            public string[] DarkBattler { get; set; } = [];

            [DisplayName("Dragon Battler")]
            public string[] DragonBattler { get; set; } = [];

            [DisplayName("Electric Battler")]
            public string[] ElectricBattler { get; set; } = [];

            [DisplayName("Fairy Battler")]
            public string[] FairyBattler { get; set; } = [];

            [DisplayName("Fighting Battler")]
            public string[] FightingBattler { get; set; } = [];

            [DisplayName("Fire Battler")]
            public string[] FireBattler { get; set; } = [];

            [DisplayName("Flying Battler")]
            public string[] FlyingBattler { get; set; } = [];

            [DisplayName("Ghost Battler")]
            public string[] GhostBattler { get; set; } = [];

            [DisplayName("Grass Battler")]
            public string[] GrassBattler { get; set; } = [];

            [DisplayName("Ground Battler")]
            public string[] GroundBattler { get; set; } = [];

            [DisplayName("Ice Battler")]
            public string[] IceBattler { get; set; } = [];

            [DisplayName("Normal Battler")]
            public string[] NormalBattler { get; set; } = [];

            [DisplayName("Poison Battler")]
            public string[] PoisonBattler { get; set; } = [];

            [DisplayName("Psychic Battler")]
            public string[] PsychicBattler { get; set; } = [];

            [DisplayName("Rock Battler")]
            public string[] RockBattler { get; set; } = [];

            [DisplayName("Steel Battler")]
            public string[] SteelBattler { get; set; } = [];

            [DisplayName("Water Battler")]
            public string[] WaterBattler { get; set; } = [];
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<RotatingRaidSettingsCategory>))]
        public class RotatingRaidSettingsCategory
        {
            private bool _randomRotation = false;
            private bool _mysteryRaids = false;

            public override string ToString() => "Raid Settings";

            [DisplayName("Generate Active Raids from file?")]
            [Category(Hosting), Description("When enabled, the bot will attempt to auto-generate your raids from the \"raidsv.txt\" file on botstart.")]
            public bool GenerateRaidsFromFile { get; set; } = true;

            [DisplayName("Save Active Raids to File On Exit?")]
            [Category(Hosting), Description("When enabled, the bot will save your current ActiveRaids list to the \"savedSeeds.txt\" file on bot stop.")]
            public bool SaveSeedsToFile { get; set; } = true;

            [DisplayName("Total Raids To Host Before Stopping")]
            [Category(Hosting), Description("Enter the total number of raids to host before the bot automatically stops. Default is 0 to ignore this setting.")]
            public int TotalRaidsToHost { get; set; } = 0;

            [DisplayName("Rotate Raid List in Random Order?"), Category(Hosting), Description("When enabled, the bot will randomly pick a Raid to run, while keeping requests prioritized.")]
            public bool RandomRotation
            {
                get => _randomRotation;
                set
                {
                    _randomRotation = value;
                    if (value)
                        _mysteryRaids = false;
                }
            }

            [DisplayName("Turn Mystery Raids On?"), Category(Hosting), Description("When true, bot will add random shiny seeds to queue. Only User Requests and Mystery Raids will be ran.")]
            public bool MysteryRaids
            {
                get => _mysteryRaids;
                set
                {
                    _mysteryRaids = value;
                    if (value)
                        _randomRotation = false;
                }
            }

            [DisplayName("Mystery Raid Settings")]
            [Category("MysteryRaids"), Description("Settings specific to Mystery Raids.")]
            public MysteryRaidsSettings MysteryRaidsSettings { get; set; } = new MysteryRaidsSettings();

            [DisplayName("Disable User Raid Requests?")]
            [Category(Hosting), Description("When true, the bot will not allow user requested raids and will inform them that this setting is on.")]
            public bool DisableRequests { get; set; } = false;

            [DisplayName("Allow Private User Raid Requests?")]
            [Category(Hosting), Description("When true, the bot will allow private raids.")]
            public bool PrivateRaidsEnabled { get; set; } = true;

            [DisplayName("Limit Users Requests")]
            [Category(Hosting), Description("Limit the number of requests a user can issue.  Set to 0 to disable.\nCommands: $lr <number>")]
            public int LimitRequests { get; set; } = 0;

            [DisplayName("Limit Requests Time")]
            [Category(Hosting), Description("Define the time (in minutes) the user must wait for requests once LimitRequests number is reached.  Set to 0 to disable.\nCommands: $lrt <number in minutes>")]
            public int LimitRequestsTime { get; set; } = 0;

            [DisplayName("Limit Request User Error Message")]
            [Category(Hosting), Description("Custom message to display when a user reaches their request limit.")]
            public string LimitRequestMsg { get; set; } = "If you'd like to bypass this limit, please [describe how to get the role].";

            [DisplayName("Users/Roles that can bypass Limit Requests")]
            [Category(Hosting), Description("Dictionary of user and role IDs with names that can bypass request limits.\nCommands: $alb @Role or $alb @User")]
            public Dictionary<ulong, string> BypassLimitRequests { get; set; } = new Dictionary<ulong, string>();

            [DisplayName("Prevent Battles in Overworld?")]
            [Category(FeatureToggle), Description("Prevent attacks.  When true, Overworld Spawns (Pokémon) are disabled on the next seed injection.  When false, Overworld Spawns (Pokémon) are enabled on the next seed injection.")]
            public bool DisableOverworldSpawns { get; set; } = true;

            [DisplayName("Keep Current Day Seed?")]
            [Category(Hosting), Description("When enabled, the bot will inject the current day seed to tomorrow's day seed.")]
            public bool KeepDaySeed { get; set; } = true;

            [DisplayName("Prevent Day Changes?")]
            [Category(FeatureToggle), Description("When enabled, the bot will roll back the time by 5 hours to keep your day from changing.  Be sure that when you start the bot the Switch Time is past 12:01am and before 7:00pm.")]
            public bool EnableTimeRollBack { get; set; } = true;

            [DisplayName("Join Shared Raids Program")]
            [Category(Hosting), Description("Enable to join the Shared Raids Program.")]
            public bool JoinSharedRaidsProgram { get; set; } = true;
        }

        public class MoveTypeEmojiInfo
        {
            [Description("The type of move.")]
            public MoveType MoveType { get; set; }

            [Description("The Discord emoji string for this move type.")]
            public string EmojiCode { get; set; }

            public MoveTypeEmojiInfo() { }

            public MoveTypeEmojiInfo(MoveType moveType)
            {
                MoveType = moveType;
            }
            public override string ToString()
            {
                if (string.IsNullOrEmpty(EmojiCode))
                    return MoveType.ToString();

                return $"{EmojiCode}";
            }
        }

        [TypeConverter(typeof(ExpandableObjectConverter))]
        public class EmojiInfo
        {
            [Description("The full string for the emoji.")]
            [DisplayName("Emoji Code")]
            public string EmojiString { get; set; } = string.Empty;

            public override string ToString()
            {
                return string.IsNullOrEmpty(EmojiString) ? "Not Set" : EmojiString;
            }
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<RotatingRaidPresetFiltersCategory>))]
        public class RotatingRaidPresetFiltersCategory
        {
            public override string ToString() => "Embed Toggles";

            [Category(Hosting), Description("Select the language for raid embeds. This will translate Pokemon names, types, abilities, etc.")]
            [DisplayName("Embed Language")]
            public LanguageOptions EmbedLanguage { get; set; } = LanguageOptions.English;

            [Category(Hosting), Description("Will show Move Type Icons next to moves in trade embed (Discord only).  Requires user to upload the emojis to their server.")]
            [DisplayName("Use Move Type Emoji's?")]
            public bool MoveTypeEmojis { get; set; } = true;

            [Category(Hosting), Description("Custom Emoji information for the move types.")]
            [DisplayName("Custom Move Type Emoji's")]
            public List<MoveTypeEmojiInfo> CustomTypeEmojis { get; set; } =
            [
            new(MoveType.Bug),
            new(MoveType.Fire),
            new(MoveType.Flying),
            new(MoveType.Ground),
            new(MoveType.Water),
            new(MoveType.Grass),
            new(MoveType.Ice),
            new(MoveType.Rock),
            new(MoveType.Ghost),
            new(MoveType.Steel),
            new(MoveType.Fighting),
            new(MoveType.Electric),
            new(MoveType.Dragon),
            new(MoveType.Psychic),
            new(MoveType.Dark),
            new(MoveType.Normal),
            new(MoveType.Poison),
            new(MoveType.Fairy),
            ];

            public enum LanguageOptions
            {
                [Description("Japanese")]
                Japanese = 1,

                [Description("English")]
                English = 2,

                [Description("French")]
                French = 3,

                [Description("Italian")]
                Italian = 4,

                [Description("German")]
                German = 5,

                [Description("Spanish")]
                Spanish = 7,

                [Description("Korean")]
                Korean = 8,

                [Description("Chinese (Simplified)")]
                ChineseS = 9,

                [Description("Chinese (Traditional)")]
                ChineseT = 10
            }

            [Category(Hosting), Description("The full string for the male gender emoji. Leave blank to not use.")]
            [DisplayName("Male Emoji Code")]
            public EmojiInfo MaleEmoji { get; set; } = new EmojiInfo();

            [Category(Hosting), Description("The full string for the female gender emoji. Leave blank to not use.")]
            [DisplayName("Female Emoji Code")]
            public EmojiInfo FemaleEmoji { get; set; } = new EmojiInfo();

            [Category(Hosting), Description("Raid embed description will be shown on every raid posted at the top of the embed.")]
            [DisplayName("Raid Embed Description")]
            public string[] RaidEmbedDescription { get; set; } = Array.Empty<string>();

            [Category(FeatureToggle), Description("Choose the TeraType Icon set to use in the author area of the embed.  Icon1 are custom, Icon2 is not.")]
            [DisplayName("Tera Icon Choice")]
            public TeraIconType SelectedTeraIconType { get; set; } = TeraIconType.Icon1;

            [Category(Hosting), Description("If true, the bot will show Moves on embeds.")]
            [DisplayName("Include Moves/Extra Moves in Embed?")]
            public bool IncludeMoves { get; set; } = true;

            [Category(Hosting), Description("When true, the embed will display current seed.")]
            [DisplayName("Include Current Seed in Embed?")]
            public bool IncludeSeed { get; set; } = true;

            [Category(FeatureToggle), Description("When enabled, the embed will countdown the amount of seconds in \"TimeToWait\" until starting the raid.")]
            [DisplayName("Include Countdown Timer in Embed?")]
            public bool IncludeCountdown { get; set; } = true;

            [Category(Hosting), Description("If true, the bot will show Type Advantages on embeds.")]
            [DisplayName("Include Type Advantage Hints in Embed?")]
            public bool IncludeTypeAdvantage { get; set; } = true;

            [Category(Hosting), Description("If true, the bot will show Special Rewards on embeds.")]
            [DisplayName("Include Rewards in Embed?")]
            public bool IncludeRewards { get; set; } = true;

            [Category(Hosting), Description("Select which rewards to display in the embed.")]
            [DisplayName("Rewards To Show")]
            public List<string> RewardsToShow { get; set; } = new List<string>
            {
                "Rare Candy",
                "Ability Capsule",
                "Bottle Cap",
                "Ability Patch",
                "Exp. Candy L",
                "Exp. Candy XL",
                "Sweet Herba Mystica",
                "Salty Herba Mystica",
                "Sour Herba Mystica",
                "Bitter Herba Mystica",
                "Spicy Herba Mystica",
                "Pokeball",
                "Shards",
                "Nugget",
                "Tiny Mushroom",
                "Big Mushroom",
                "Pearl",
                "Big Pearl",
                "Stardust",
                "Star Piece",
                "Gold Bottle Cap",
                "PP Up"
            };

            private RequestEmbedTimingOptions _requestEmbedTime = RequestEmbedTimingOptions._3500;

            [Category(Hosting)]
            [Description("Amount of time (in milliseconds) to post a requested raid embed.")]
            [DisplayName("Post User Request Embeds in...")]
            public RequestEmbedTimingOptions RequestEmbedTime
            {
                get => _requestEmbedTime;
                set
                {
                    if (value != RequestEmbedTimingOptions._2500 &&
                        value != RequestEmbedTimingOptions._3000 &&
                        value != RequestEmbedTimingOptions._3500 &&
                        value != RequestEmbedTimingOptions._4000 &&
                        value != RequestEmbedTimingOptions._4500)
                    {
                        _requestEmbedTime = RequestEmbedTimingOptions._3500;
                    }
                    else
                    {
                        _requestEmbedTime = value;
                    }
                }
            }

            [Category(FeatureToggle), Description("When enabled, the bot will attempt take screenshots for the Raid Embeds. If you experience crashes often about \"Size/Parameter\" try setting this to false.")]
            [DisplayName("Use Screenshots?")]
            public bool TakeScreenshot { get; set; } = true;

            [Category(Hosting), Description("Delay in milliseconds for capturing a screenshot once in the raid.\n 0 Captures the Raid Mon Up close.\n3500 Captures Players Only.\n10000 Captures players and Raid Mon.")]
            [DisplayName("Screenshot Timing")]
            public ScreenshotTimingOptions ScreenshotTiming { get; set; } = ScreenshotTimingOptions._3500;

            [Category(FeatureToggle), Description("When enabled, the bot will hide the raid code from the Discord embed.")]
            public bool HideRaidCode { get; set; } = false;
        }

        [Category("MysteryRaids"), TypeConverter(typeof(ExpandableObjectConverter))]
        public class MysteryRaidsSettings
        {

            [DisplayName("Tera Type Battlers")]
            [TypeConverter(typeof(ExpandableObjectConverter))]
            public TeraTypeBattlers TeraTypeBattlers { get; set; } = new TeraTypeBattlers();

            [TypeConverter(typeof(ExpandableObjectConverter))]
            [DisplayName("3 Star Progress Settings")]
            public Unlocked3StarSettings Unlocked3StarSettings { get; set; } = new Unlocked3StarSettings();

            [TypeConverter(typeof(ExpandableObjectConverter))]
            [DisplayName("4 Star Progress Settings")]
            public Unlocked4StarSettings Unlocked4StarSettings { get; set; } = new Unlocked4StarSettings();

            [TypeConverter(typeof(ExpandableObjectConverter))]
            [DisplayName("5 Star Progress Settings")]
            public Unlocked5StarSettings Unlocked5StarSettings { get; set; } = new Unlocked5StarSettings();

            [TypeConverter(typeof(ExpandableObjectConverter))]
            [DisplayName("6 Star Progress Settings")]
            public Unlocked6StarSettings Unlocked6StarSettings { get; set; } = new Unlocked6StarSettings();

            public override string ToString() => "Mystery Raids Settings";
        }

        public class Unlocked3StarSettings
        {
            [DisplayName("Enable 3 Star Progress Mystery Raids?")]
            public bool Enabled { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 1* Raids in 3* Unlocked Raids.")]
            public bool Allow1StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 2* Raids in 3* Unlocked Raids.")]
            public bool Allow2StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 3* Raids in 3* Unlocked Raids.")]
            public bool Allow3StarRaids { get; set; } = true;

            public override string ToString() => "3* Raids Settings";
        }

        public class Unlocked4StarSettings
        {
            [DisplayName("Enable 4 Star Progress Mystery Raids?")]
            public bool Enabled { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 1* Raids in 4* Unlocked Raids.")]
            public bool Allow1StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 2* Raids in 4* Unlocked Raids.")]
            public bool Allow2StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 3* Raids in 4* Unlocked Raids.")]
            public bool Allow3StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 4* Raids in 4* Unlocked Raids.")]
            public bool Allow4StarRaids { get; set; } = true;

            public override string ToString() => "4* Raids Settings";
        }

        [Category("MysteryRaids"), TypeConverter(typeof(ExpandableObjectConverter))]
        public class Unlocked5StarSettings
        {
            [DisplayName("Enable 5 Star Progress Mystery Raids?")]
            public bool Enabled { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 3* Raids in 5* Unlocked Raids.")]
            public bool Allow3StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 4* Raids in 5* Unlocked Raids.")]
            public bool Allow4StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 5* Raids in 5* Unlocked Raids.")]
            public bool Allow5StarRaids { get; set; } = true;

            public override string ToString() => "5* Raids Settings";
        }

        [Category("MysteryRaids"), TypeConverter(typeof(ExpandableObjectConverter))]
        public class Unlocked6StarSettings
        {
            [DisplayName("Enable 6 Star Progress Mystery Raids?")]
            public bool Enabled { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 3* Raids in 6* Unlocked Raids.")]
            public bool Allow3StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 4* Raids in 6* Unlocked Raids.")]
            public bool Allow4StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 5* Raids in 6* Unlocked Raids.")]
            public bool Allow5StarRaids { get; set; } = true;

            [Category("DifficultyLevels"), Description("Allow 6* Raids in 6* Unlocked Raids.")]
            public bool Allow6StarRaids { get; set; } = true;

            public override string ToString() => "6* Raids Settings";
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<LobbyFiltersCategory>))]
        public class LobbyFiltersCategory
        {
            public override string ToString() => "Lobby Filters";

            [Category(Hosting), Description("OpenLobby - Opens the Lobby after x Empty Lobbies\nSkipRaid - Moves on after x losses/empty Lobbies\nContinue - Continues hosting the raid")]
            [DisplayName("Lobby Method")]
            public LobbyMethodOptions LobbyMethod { get; set; } = LobbyMethodOptions.SkipRaid;

            private int _raidLimit = 3;

            [Category(Hosting), Description("Empty raid limit per parameter before the bot hosts an uncoded raid. Value must be between 1 and 3.")]
            [DisplayName("Empty Raid Limit")]
            public int EmptyRaidLimit
            {
                get => _raidLimit;
                set => SetRaidLimit(value);
            }

            [Category(Hosting), Description("Empty/Lost raid limit per parameter before the bot moves on to the next one. Value must be between 1 and 3.")]
            [DisplayName("Skip Raid Limit")]
            public int SkipRaidLimit
            {
                get => _raidLimit;
                set => SetRaidLimit(value);
            }

            private void SetRaidLimit(int value)
            {
                _raidLimit = Math.Max(1, Math.Min(3, value));
            }

            [Category(FeatureToggle), Description("Set the action you would want your bot to perform. 'AFK' will make the bot idle, while 'MashA' presses A every 1s")]
            [DisplayName("A Button Action")]
            public RaidAction Action { get; set; } = RaidAction.MashA;

            [Category(FeatureToggle), Description("Delay for the 'MashA' action in seconds.  [1.0 is default]")]
            [DisplayName("A Button Delay (Seconds)")]
            public double MashADelay { get; set; } = 1.0;  // Default value set to 1.0 seconds

            [Category(FeatureToggle), Description("Extra time in milliseconds to wait after Lobby Disbands in Raid before deciding to not capture the raidmon.")]
            [DisplayName("Extra Time To Disband Raid")]
            public int ExtraTimeLobbyDisband { get; set; } = 0;

            [Category(FeatureToggle), Description("Extra time in milliseconds to wait before changing partypk.")]
            [DisplayName("Extra Time to Prepare Raid Battler")]
            public int ExtraTimePartyPK { get; set; } = 0;
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<MiscSettingsCategory>))]
        public class MiscSettingsCategory
        {
            public override string ToString() => "Misc. Settings";

            [Category(FeatureToggle), Description("Set your Switch Date/Time format in the Date/Time settings. The day will automatically rollback by 1 if the Date changes.")]
            public DTFormat DateTimeFormat { get; set; } = DTFormat.MMDDYY;

            [Category(Hosting), Description("When enabled, the bot will use the overshoot method to apply rollover correction, otherwise will use DDOWN clicks.")]
            public bool UseOvershoot { get; set; } = true;

            [Category(Hosting), Description("Time to scroll down duration in milliseconds for accessing date/time settings during rollover correction. You want to have it overshoot the Date/Time setting by 1, as it will click DUP after scrolling down. [Default: 930ms]")]
            public int HoldTimeForRollover { get; set; } = 900;

            [Category(Hosting), Description("When enabled, start the bot when you are on the HOME screen with the game closed. The bot will only run the rollover routine so you can try to configure accurate timing.")]
            public bool ConfigureRolloverCorrection { get; set; } = false;

            [Category(FeatureToggle), Description("When enabled, the screen will be turned off during normal bot loop operation to save power.")]
            public bool ScreenOff { get; set; }

            private int _completedRaids;

            [Category(Counts), Description("Raids Started")]
            public int CompletedRaids
            {
                get => _completedRaids;
                set => _completedRaids = value;
            }

            [Category(Counts), Description("When enabled, the counts will be emitted when a status check is requested.")]
            public bool EmitCountsOnStatusCheck { get; set; }

            public int AddCompletedRaids() => Interlocked.Increment(ref _completedRaids);

            public IEnumerable<string> GetNonZeroCounts()
            {
                if (!EmitCountsOnStatusCheck)
                    yield break;
                if (CompletedRaids != 0)
                    yield return $"Started Raids: {CompletedRaids}";
            }
        }

        public class CategoryConverter<T> : TypeConverter
        {
            public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

            public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) => TypeDescriptor.GetProperties(typeof(T));

            public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType != typeof(string) && base.CanConvertTo(context, destinationType);
        }
    }
}