using System.ComponentModel;
using static SysBot.Pokemon.RotatingRaidSettingsSV;

namespace SysBot.Pokemon
{
    public class TimingSettings
    {
        private const string OpenGame = nameof(OpenGame);
        private const string CloseGame = nameof(CloseGame);
        private const string RestartGame = nameof(RestartGame);
        private const string Raid = nameof(Raid);
        private const string Misc = nameof(Misc);

        public override string ToString() => "Extra Time Settings";

        [Category(OpenGame), Description("Extra time in milliseconds to wait for the overworld to load after the title screen.")]
        public int ExtraTimeLoadOverworld { get; set; } = 3000;

        [Category(OpenGame), Description("Extra time in milliseconds to wait after the seed and storyprogress are injected before clicking A.")]
        public int ExtraTimeInjectSeed { get; set; } = 0;

        [Category(CloseGame), Description("Extra time in milliseconds to wait after pressing HOME to minimize the game.")]
        public int ExtraTimeReturnHome { get; set; }

        [Category(Misc), Description("Extra time in milliseconds to wait for the Poké Portal to load.")]
        public int ExtraTimeLoadPortal { get; set; } = 1000;

        [Category(Misc), Description("Extra time in milliseconds to wait after clicking + to connect to Y-Comm (SWSH) or L to connect online (SV).")]
        public int ExtraTimeConnectOnline { get; set; }

        [Category(Misc), Description("Number of times to attempt reconnecting to a socket connection after a connection is lost. Set this to -1 to try indefinitely.")]
        public int ReconnectAttempts { get; set; } = 30;

        [Category(Misc), Description("Extra time in milliseconds to wait between attempts to reconnect. Base time is 30 seconds.")]
        public int ExtraReconnectDelay { get; set; }

        [Category(Misc), Description("Time to wait after each keypress when navigating Switch menus or entering Link Code.")]
        public int KeypressTime { get; set; } = 200;

        [Category(RestartGame), Description("Settings related to Restarting the game.")]
        public RestartGameSettingsCategory RestartGameSettings { get; set; } = new();

        [Category(RestartGame), TypeConverter(typeof(CategoryConverter<RestartGameSettingsCategory>))]
        public class RestartGameSettingsCategory
        {
            public override string ToString() => "Restart Game Settings";

            [Category(OpenGame), Description("Enable this to decline incoming system updates.")]
            public bool AvoidSystemUpdate { get; set; } = true;

            [Category(OpenGame), Description("Enable this to add a delay for \"Checking if Game Can be Played\" Pop-up.")]
            public bool CheckGameDelay { get; set; } = false;

            [Category(OpenGame), Description("Extra Time to wait for the \"Checking if Game Can Be Played\" Pop-up.")]
            public int ExtraTimeCheckGame { get; set; } = 200;

            [Category(OpenGame), Description("Enable this ONLY when you have DLC on the system and can't use it.")]
            public bool CheckForDLC { get; set; } = false;

            [Category(OpenGame), Description("Extra time in milliseconds to wait to check if DLC is usable.")]
            public int ExtraTimeCheckDLC { get; set; } = 0;

            [Category(OpenGame), Description("Extra time in milliseconds to wait before clicking A in title screen.")]
            public int ExtraTimeLoadGame { get; set; } = 5000;

            [Category(CloseGame), Description("Extra time in milliseconds to wait after clicking to close the game.")]
            public int ExtraTimeCloseGame { get; set; } = 0;

            [Category(RestartGame), Description("Settings related to Restarting the game.")]
            public ProfileSelectSettingsCategory ProfileSelectSettings { get; set; } = new();
        }

        [Category(RestartGame), TypeConverter(typeof(CategoryConverter<ProfileSelectSettingsCategory>))]
        public class ProfileSelectSettingsCategory
        {
            public override string ToString() => "Profile Selection Settings";

            [Category(OpenGame), Description("Enable this if you need to select a profile when starting the game.")]
            public bool ProfileSelectionRequired { get; set; } = true;

            [Category(OpenGame), Description("Extra time in milliseconds to wait for profiles to load when starting the game.")]
            public int ExtraTimeLoadProfile { get; set; } = 0;
        }
    }
}