using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using SysBot.Web;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net;
using System.Text;
using System.ComponentModel;

namespace SysBot.Pokemon.WinForms
{
    public sealed partial class Main : Form
    {
        private readonly List<PokeBotState> Bots = new();
        private ProgramConfig Config { get; set; } // make it a property with private setter
        private IPokeBotRunner RunningEnvironment { get; set; }

        public readonly ISwitchConnectionAsync? SwitchConnection;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static bool IsUpdating { get; set; } = false;
        private System.Windows.Forms.Timer _autoSaveTimer;
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;

        public Main()
        {
            InitializeComponent();
            Load += async (sender, e) => await InitializeAsync();

            TC_Main.SelectedIndexChanged += TC_Main_SelectedIndexChanged;
            RTB_Logs.TextChanged += RTB_Logs_TextChanged;
        }

        private async Task InitializeAsync()
        {
            if (IsUpdating)
                return;
            string discordName = string.Empty;

            // Update checker
            UpdateChecker updateChecker = new();
            await UpdateChecker.CheckForUpdatesAsync();

            if (File.Exists(Program.ConfigPath))
            {
                var lines = File.ReadAllText(Program.ConfigPath);
                Config = JsonSerializer.Deserialize(lines, ProgramConfigContext.Default.ProgramConfig) ?? new ProgramConfig();
                LogConfig.MaxArchiveFiles = Config.Hub.MaxArchiveFiles;
                LogConfig.LoggingEnabled = Config.Hub.LoggingEnabled;

                RunningEnvironment = GetRunner(Config);
                foreach (var bot in Config.Bots)
                {
                    bot.Initialize();
                    AddBot(bot);
                }
            }
            else
            {
                Config = new ProgramConfig();
                RunningEnvironment = GetRunner(Config);
            }

            LoadControls();
            Text = $"{(string.IsNullOrEmpty(Config.Hub.BotName) ? "Hideout" : Config.Hub.BotName)} {SVRaidBot.Version} ({Config.Mode})";
            Task.Run(BotMonitor);
            InitUtil.InitializeStubs(Config.Mode);
            StartTcpListener();
            
            // Starte die Web-API
            WebApiIntegration.StartWebApi(RunningEnvironment, 6500);

            LogUtil.LogInfo($"Bot initialization complete", "System");
        }

        private void TC_Main_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (TC_Main.SelectedTab == Tab_Logs)
            {
                RTB_Logs.Refresh();
            }
        }

        private void RTB_Logs_TextChanged(object sender, EventArgs e)
        {
            RTB_Logs.Invalidate();
            RTB_Logs.Update();
        }

        private static IPokeBotRunner GetRunner(ProgramConfig cfg) => cfg.Mode switch
        {
            ProgramMode.SV => new PokeBotRunnerImpl<PK9>(cfg.Hub, new BotFactory9SV()),
            _ => throw new IndexOutOfRangeException("Unsupported mode."),
        };

        private async Task BotMonitor()
        {
            while (!Disposing)
            {
                try
                {
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                        c.ReadState();
                }
                catch
                {
                    // Updating the collection by adding/removing bots will change the iterator
                    // Can try a for-loop or ToArray, but those still don't prevent concurrent mutations of the array.
                    // Just try, and if failed, ignore. Next loop will be fine. Locks on the collection are kinda overkill, since this task is not critical.
                }
                await Task.Delay(2_000).ConfigureAwait(false);
            }
        }

        private void StartTcpListener(int preferredPort = 0)
        {
            if (_tcpListener != null) return;
            int basePort = 55000;
            int portRange = 5000;

            int initialPort = preferredPort > 0
                ? preferredPort
                : basePort + (Environment.ProcessId % portRange);

            const int maxAttempts = 10;
            int currentAttempt = 0;
            int portToUse = initialPort;
            bool success = false;

            while (!success && currentAttempt < maxAttempts)
            {
                try
                {
                    currentAttempt++;
                    if (currentAttempt > 1)
                    {
                        int randomOffset = (Environment.ProcessId + Environment.TickCount) % portRange;
                        portToUse = basePort + ((initialPort + randomOffset * currentAttempt) % portRange);
                        LogUtil.LogInfo($"TCP port conflict detected. Trying alternate port {portToUse} (attempt {currentAttempt}/{maxAttempts})", "TCP");
                    }
                    _cts = new CancellationTokenSource();
                    _tcpListener = new TcpListener(IPAddress.Loopback, portToUse);
                    _tcpListener.Start();
                    success = true;
                    LogUtil.LogInfo($"TCP Listener started on port {portToUse}", "TCP");
                    string portInfoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), $"SVRaidBot_{Environment.ProcessId}.port");
                    File.WriteAllText(portInfoPath, portToUse.ToString());
                    Task.Run(() => AcceptLoop(_cts.Token));
                }
                catch (Exception ex)
                {
                    if (currentAttempt >= maxAttempts)
                    {
                        LogUtil.LogError($"All attempts to start TCP listener failed. Last attempt on port {portToUse}: {ex.Message}", "TCP");

                        try
                        {
                            string portInfoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), $"SVRaidBot_{Environment.ProcessId}.port");
                            File.WriteAllText(portInfoPath, "ERROR:" + ex.Message);
                        }
                        catch { }
                    }
                    else
                    {
                        LogUtil.LogError($"Failed to start TCP listener on port {portToUse}: {ex.Message}. Will try another port.", "TCP");

                        // Clean up before retry
                        _tcpListener = null;
                        _cts?.Dispose();
                        _cts = null;
                    }
                }
            }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync();
                    if (client != null)
                        _ = Task.Run(() => HandleClient(client, ct));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"AcceptLoop error: {ex.Message}", "TCP");
                    await Task.Delay(500);
                }
            }
        }

        private async Task HandleClient(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    var commandLine = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(commandLine)) return;

                    LogUtil.LogInfo($"Received command: {commandLine}", "TCP");
                    string response = ExecuteBotCommand(commandLine.Trim());
                    await writer.WriteLineAsync(response);
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"HandleClient error: {ex.Message}", "TCP");
                }
            }
        }

        private string ExecuteBotCommand(string cmd)
        {
            switch (cmd)
            {
                case "StopAll":
                    RunningEnvironment.StopAll();
                    return "STOPPED";

                case "StartAll":
                    // Reset error state when starting
                    SysBot.Pokemon.SV.BotRaid.RotatingRaidBotSV.HasErrored = false;
                    RunningEnvironment.InitializeStart();
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                        c.SendCommand(BotControlCommand.Start);
                    return "STARTED";

                case "IdleAll":
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                        c.SendCommand(BotControlCommand.Idle);
                    return "IDLING";

                case "ResumeAll":
                    // Reset error state when resuming
                    SysBot.Pokemon.SV.BotRaid.RotatingRaidBotSV.HasErrored = false;
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                        c.SendCommand(BotControlCommand.Resume);
                    return "RESUMED";

                case "RestartAll":
                    // Reset error state when restarting
                    SysBot.Pokemon.SV.BotRaid.RotatingRaidBotSV.HasErrored = false;
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                    {
                        RunningEnvironment.InitializeStart();
                        c.SendCommand(BotControlCommand.Restart);
                    }
                    return "RESTARTING";

                case "RebootAll":
                    // Reset error state when rebooting
                    SysBot.Pokemon.SV.BotRaid.RotatingRaidBotSV.HasErrored = false;
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                        c.SendCommand(BotControlCommand.RebootAndStop);
                    return "REBOOTING";

                case "Status":
                    // Check actual status of each bot's routine
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                    {
                        try
                        {
                            var botState = c.ReadBotState();
                            // If any bot is not idle, return its status
                            if (botState != "IDLE")
                            {
                                return botState;
                            }
                        }
                        catch
                        {
                            return "ERROR"; // Error reading state
                        }
                    }
                    return "IDLE"; // All bots are idle

                case "IsReady":
                    // First check - are there any configured bots?
                    if (FLP_Bots.Controls.OfType<BotController>().Count() == 0)
                    {
                        LogUtil.LogInfo("No bots are configured", "TCP");
                        return "NOT_READY";
                    }

                    // Second check - static error flag
                    try
                    {
                        if (SysBot.Pokemon.SV.BotRaid.RotatingRaidBotSV.HasErrored)
                        {
                            LogUtil.LogInfo("Static HasErrored flag is true", "TCP");
                            return "NOT_READY";
                        }
                    }
                    catch { /* Ignore if not available */ }

                    // Third check - scan log records for recent errors
                    // If any error message was logged in the last 10 minutes, consider the bot not ready
                    // This ensures we detect post-crash scenarios even when IsRunning is false
                    try
                    {
                        // Check the RTB_Logs text for recent error messages
                        string logText = RTB_Logs.Text;
                        if (!string.IsNullOrEmpty(logText))
                        {
                            // First check for the most definitive crash message
                            if (logText.Contains("Bot has crashed"))
                            {
                                LogUtil.LogInfo("Found 'Bot has crashed' in logs", "TCP");
                                return "NOT_READY";
                            }

                            // Then check for ending message
                            if (logText.Contains("Ending RotatingRaidBotSV loop"))
                            {
                                LogUtil.LogInfo("Found 'Ending RotatingRaidBotSV loop' in logs", "TCP");
                                return "NOT_READY";
                            }

                            // Check if there are connection errors in recent logs
                            if (logText.Contains("Connection error"))
                            {
                                LogUtil.LogInfo("Found 'Connection error' in logs", "TCP");
                                return "NOT_READY";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"Error checking logs: {ex.Message}", "TCP");
                    }

                    // Fourth check - inspect each bot's state
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                    {
                        try
                        {
                            var botSource = c.GetBot();
                            if (botSource == null)
                            {
                                LogUtil.LogInfo("Bot source is null", "TCP");
                                return "NOT_READY";
                            }

                            // Check if the bot is in a known error state
                            string state = c.ReadBotState();
                            if (state == "ERROR" || state == "STOPPING")
                            {
                                LogUtil.LogInfo($"Bot state is {state}", "TCP");
                                return "NOT_READY";
                            }

                            // Check for any connection issues
                            try
                            {
                                if (botSource.Bot == null || botSource.Bot.Connection == null)
                                {
                                    LogUtil.LogInfo("Bot or Bot.Connection is null", "TCP");
                                    return "NOT_READY";
                                }

                                if (!botSource.Bot.Connection.Connected)
                                {
                                    LogUtil.LogInfo("Bot is not connected", "TCP");
                                    return "NOT_READY";
                                }
                            }
                            catch
                            {
                                LogUtil.LogInfo("Exception checking connection status", "TCP");
                                return "NOT_READY";
                            }
                        }
                        catch (Exception ex)
                        {
                            LogUtil.LogInfo($"Exception while checking bot: {ex.Message}", "TCP");
                            return "NOT_READY";
                        }
                    }

                    return "READY"; // All checks passed, bot is ready

                case "RefreshMap":
                    foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                        c.SendCommand(BotControlCommand.RefreshMap, false);
                    return "REFRESHED";

                default:
                    return $"Unknown command: {cmd}";
            }
        }

        private void StopTcpListener()
        {
            try
            {
                // Delete port info file as an extra precaution
                string portInfoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), $"SVRaidBot_{Environment.ProcessId}.port");
                if (File.Exists(portInfoPath))
                    File.Delete(portInfoPath);

                _cts?.Cancel();
                _tcpListener?.Stop();
                _cts = null;
                _tcpListener = null;
            }
            catch { }
        }

        private void LoadControls()
        {
            MinimumSize = Size;
            PG_Hub.SelectedObject = RunningEnvironment.Config;
            _autoSaveTimer = new System.Windows.Forms.Timer
            {
                Interval = 10_000,
                Enabled = true
            };
            _autoSaveTimer.Tick += (s, e) => SaveCurrentConfig();
            var routines = ((PokeRoutineType[])Enum.GetValues(typeof(PokeRoutineType))).Where(z => RunningEnvironment.SupportsRoutine(z));
            var list = routines.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
            CB_Routine.DisplayMember = nameof(ComboItem.Text);
            CB_Routine.ValueMember = nameof(ComboItem.Value);
            CB_Routine.DataSource = list;
            CB_Routine.SelectedValue = (int)PokeRoutineType.RotatingRaidBot; // default option

            var protocols = (SwitchProtocol[])Enum.GetValues(typeof(SwitchProtocol));
            var listP = protocols.Select(z => new ComboItem(z.ToString(), (int)z)).ToArray();
            CB_Protocol.DisplayMember = nameof(ComboItem.Text);
            CB_Protocol.ValueMember = nameof(ComboItem.Value);
            CB_Protocol.DataSource = listP;
            CB_Protocol.SelectedIndex = (int)SwitchProtocol.WiFi; // default option

            comboBox1.Items.Add("Light Mode");
            comboBox1.Items.Add("Dark Mode");
            comboBox1.Items.Add("Poke Mode");
            comboBox1.Items.Add("Gengar Mode");
            comboBox1.Items.Add("Sylveon Mode");

            string theme = Config.Hub.ThemeOption;
            if (string.IsNullOrEmpty(theme) || !comboBox1.Items.Contains(theme))
            {
                comboBox1.SelectedIndex = 0;  // Set default selection to Light Mode if ThemeOption is empty or invalid
            }
            else
            {
                comboBox1.SelectedItem = theme;  // Set the selected item in the combo box based on ThemeOption
                switch (theme)
                {
                    case "Dark Mode":
                        ApplyDarkTheme();
                        break;

                    case "Light Mode":
                        ApplyLightTheme();
                        break;

                    case "Poke Mode":
                        ApplyPokemonTheme();
                        break;

                    case "Gengar Mode":
                        ApplyGengarTheme();
                        break;

                    case "Sylveon Mode":
                        ApplySylveonTheme();
                        break;

                    default:
                        ApplyLightTheme();  // Default to Light Mode if no matching theme is found
                        break;
                }
            }

            LogUtil.Forwarders.Add(AppendLog);
        }

        private void AppendLog(string message, string identity)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] - {identity}: {message}{Environment.NewLine}";
            if (InvokeRequired)
                Invoke((MethodInvoker)(() => UpdateLog(line)));
            else
                UpdateLog(line);
        }

        private void UpdateLog(string line)
        {
            // ghetto truncate
            if (RTB_Logs.Lines.Length > 99_999)
                RTB_Logs.Lines = RTB_Logs.Lines.Skip(25_0000).ToArray();

            RTB_Logs.AppendText(line);
            RTB_Logs.ScrollToCaret();
        }

        private ProgramConfig GetCurrentConfiguration()
        {
            if (Config == null)
            {
                throw new InvalidOperationException("Config has not been initialized because a valid license was not entered.");
            }
            Config.Bots = Bots.ToArray();
            return Config;
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (IsUpdating)
            {
                return;
            }
            if (IsUpdating) return;
            StopTcpListener();

            // Delete the port info file
            try
            {
                string portInfoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), $"SVRaidBot_{Environment.ProcessId}.port");
                if (File.Exists(portInfoPath))
                    File.Delete(portInfoPath);
            }
            catch { /* Ignore cleanup errors */ }

            if (_autoSaveTimer != null)
            {
                _autoSaveTimer.Stop();
                _autoSaveTimer.Dispose();
            }

            // Web-API stoppen
            WebApiIntegration.StopWebApi();

            SaveCurrentConfig();
            var bots = RunningEnvironment;
            if (!bots.IsRunning)
                return;

            async Task WaitUntilNotRunning()
            {
                while (bots.IsRunning)
                    await Task.Delay(10).ConfigureAwait(false);
            }

            // Try to let all bots hard-stop before ending execution of the entire program.
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            bots.StopAll();
            Task.WhenAny(WaitUntilNotRunning(), Task.Delay(5_000)).ConfigureAwait(true).GetAwaiter().GetResult();
        }

        private void SaveCurrentConfig()
        {
            var cfg = GetCurrentConfiguration();
            // The ThemeOption property is part of the Config object, so it will be saved automatically
            var lines = JsonSerializer.Serialize(cfg, ProgramConfigContext.Default.ProgramConfig);
            File.WriteAllText(Program.ConfigPath, lines);
        }

        [JsonSerializable(typeof(ProgramConfig))]
        [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
        public sealed partial class ProgramConfigContext : JsonSerializerContext
        { }

        private void B_Start_Click(object sender, EventArgs e)
        {
            // Reset error state when starting
            SV.BotRaid.RotatingRaidBotSV.HasErrored = false;

            SaveCurrentConfig();

            LogUtil.LogInfo("Starting all bots...", "Form");
            RunningEnvironment.InitializeStart();
            SendAll(BotControlCommand.Start);
            Tab_Logs.Select();

            if (Bots.Count == 0)
                WinFormsUtil.Alert("No bots configured, but all supporting services have been started.");
        }

        private void B_RebootAndStop_Click(object sender, EventArgs e)
        {
            B_Stop_Click(sender, e);
            Task.Run(async () =>
            {
                await Task.Delay(3_500).ConfigureAwait(false);
                SaveCurrentConfig();
                LogUtil.LogInfo("Restarting all the consoles...", "Form");
                RunningEnvironment.InitializeStart();
                SendAll(BotControlCommand.RebootAndStop);
                Tab_Logs.Select();
                if (Bots.Count == 0)
                    WinFormsUtil.Alert("No bots configured, but all supporting services have been issued the reboot command.");
            });
        }

        private async void Updater_Click(object sender, EventArgs e)
        {
            var (updateAvailable, updateRequired, newVersion) = await UpdateChecker.CheckForUpdatesAsync();
            if (!updateAvailable)
            {
                var result = MessageBox.Show(
                    "You are on the latest version. Would you like to re-download the current version?",
                    "Update Check",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    UpdateForm updateForm = new(updateRequired, newVersion, updateAvailable: false);
                    updateForm.ShowDialog();
                }
            }
            else
            {
                UpdateForm updateForm = new(updateRequired, newVersion, updateAvailable: true);
                updateForm.ShowDialog();
            }
        }

        private void RefreshMap_Click(object sender, EventArgs e)
        { 
            SaveCurrentConfig();
            LogUtil.LogInfo("Sending RefreshMap command to all bots.", "Refresh Map");
            SendAll(BotControlCommand.RefreshMap);
            Tab_Logs.Select();
            if (Bots.Count == 0)
                WinFormsUtil.Alert("No bots configured, but all supporting services have been issued the refresh map command.");

        }

        private void SendAll(BotControlCommand cmd)
        {
            foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                c.SendCommand(cmd, false);

            LogUtil.LogText($"All bots have been issued a command to {cmd}.");
        }

        private void B_Stop_Click(object sender, EventArgs e)
        {
            var env = RunningEnvironment;
            if (!env.IsRunning && (ModifierKeys & Keys.Alt) == 0)
            {
                WinFormsUtil.Alert("Nothing is currently running.");
                return;
            }

            var cmd = BotControlCommand.Stop;

            if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0) // either, because remembering which can be hard
            {
                if (env.IsRunning)
                {
                    WinFormsUtil.Alert("Commanding all bots to Idle.", "Press Stop (without a modifier key) to hard-stop and unlock control, or press Stop with the modifier key again to resume.");
                    cmd = BotControlCommand.Idle;
                }
                else
                {
                    WinFormsUtil.Alert("Commanding all bots to resume their original task.", "Press Stop (without a modifier key) to hard-stop and unlock control.");
                    cmd = BotControlCommand.Resume;
                }
            }
            SendAll(cmd);
        }

        private void B_New_Click(object sender, EventArgs e)
        {
            var cfg = CreateNewBotConfig();
            if (!AddBot(cfg))
            {
                WinFormsUtil.Alert("Unable to add bot; ensure details are valid and not duplicate with an already existing bot.");
                return;
            }
            System.Media.SystemSounds.Asterisk.Play();
        }

        private bool AddBot(PokeBotState cfg)
        {
            if (!cfg.IsValid())
                return false;

            // Disallow duplicate routines.
            if (Bots.Any(z => z.Connection.Equals(cfg.Connection) && cfg.NextRoutineType == z.NextRoutineType))
                return false;

            PokeRoutineExecutorBase newBot;
            try
            {
                Console.WriteLine($"Current Mode ({Config.Mode}) does not support this type of bot ({cfg.CurrentRoutineType}).");
                newBot = RunningEnvironment.CreateBotFromConfig(cfg);
            }
            catch
            {
                return false;
            }

            try
            {
                RunningEnvironment.Add(newBot);
            }
            catch (ArgumentException ex)
            {
                WinFormsUtil.Error(ex.Message);
                return false;
            }

            AddBotControl(cfg);
            Bots.Add(cfg);
            return true;
        }

        private void AddBotControl(PokeBotState cfg)
        {
            var row = new BotController { Width = FLP_Bots.Width };
            row.Initialize(RunningEnvironment, cfg);
            FLP_Bots.Controls.Add(row);
            FLP_Bots.SetFlowBreak(row, true);
            row.Click += (s, e) =>
            {
                var details = cfg.Connection;
                TB_IP.Text = details.IP;
                NUD_Port.Value = details.Port;
                CB_Protocol.SelectedIndex = (int)details.Protocol;
                CB_Routine.SelectedValue = (int)cfg.InitialRoutine;
            };

            row.Remove += (s, e) =>
            {
                Bots.Remove(row.State);
                RunningEnvironment.Remove(row.State, !RunningEnvironment.Config.SkipConsoleBotCreation);
                FLP_Bots.Controls.Remove(row);
            };
        }

        private PokeBotState CreateNewBotConfig()
        {
            var ip = TB_IP.Text;
            var port = (int)NUD_Port.Value;
            var cfg = BotConfigUtil.GetConfig<SwitchConnectionConfig>(ip, port);
            cfg.Protocol = (SwitchProtocol)WinFormsUtil.GetIndex(CB_Protocol);

            var pk = new PokeBotState { Connection = cfg };
            var type = (PokeRoutineType)WinFormsUtil.GetIndex(CB_Routine);
            pk.Initialize(type);
            return pk;
        }

        private void FLP_Bots_Resize(object sender, EventArgs e)
        {
            foreach (var c in FLP_Bots.Controls.OfType<BotController>())
                c.Width = FLP_Bots.Width;
        }

        private void CB_Protocol_SelectedIndexChanged(object sender, EventArgs e)
        {
            TB_IP.Visible = CB_Protocol.SelectedIndex == 0;
        }

        private void FLP_Bots_Paint(object sender, PaintEventArgs e)
        {
        }

        private void ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                string selectedTheme = comboBox.SelectedItem.ToString();
                Config.Hub.ThemeOption = selectedTheme;  // Save the selected theme to the config
                SaveCurrentConfig();  // Save the config to file

                switch (selectedTheme)
                {
                    case "Light Mode":
                        ApplyLightTheme();
                        break;

                    case "Dark Mode":
                        ApplyDarkTheme();
                        break;

                    case "Poke Mode":
                        ApplyPokemonTheme();
                        break;

                    case "Gengar Mode":
                        ApplyGengarTheme();
                        break;

                    case "Sylveon Mode":
                        ApplySylveonTheme();
                        break;

                    default:
                        ApplyLightTheme();  // Default to Light Mode if no matching theme is found
                        break;
                }
            }
        }

        private void ApplySylveonTheme()
        {
            // Define Sylveon-theme colors
            Color SoftPink = Color.FromArgb(255, 182, 193);   // A soft pink color inspired by Sylveon's body
            Color DeepPink = Color.FromArgb(255, 105, 180);   // A deeper pink for contrast and visual interest
            Color SkyBlue = Color.FromArgb(135, 206, 250);    // A soft blue color inspired by Sylveon's eyes and ribbons
            Color DeepBlue = Color.FromArgb(70, 130, 180);   // A deeper blue for contrast
            Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
            Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
            Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
            Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
            Color RefreshMap = Color.FromArgb(245, 245, 220);// Refresh Map Button
            // Set the background color of the form
            BackColor = ElegantWhite;

            // Set the foreground color of the form (text color)
            ForeColor = DeepBlue;

            // Set the background color of the tab control
            TC_Main.BackColor = SkyBlue;

            // Set the background color of each tab page
            foreach (TabPage page in TC_Main.TabPages)
            {
                page.BackColor = ElegantWhite;
            }

            // Set the background color of the property grid
            PG_Hub.BackColor = ElegantWhite;
            PG_Hub.LineColor = SkyBlue;
            PG_Hub.CategoryForeColor = DeepBlue;
            PG_Hub.CategorySplitterColor = SkyBlue;
            PG_Hub.HelpBackColor = SoftPink;
            PG_Hub.HelpForeColor = DeepBlue;
            PG_Hub.ViewBackColor = ElegantWhite;
            PG_Hub.ViewForeColor = DeepBlue;

            // Set the background color of the rich text box
            RTB_Logs.BackColor = SoftPink;
            RTB_Logs.ForeColor = DeepBlue;

            // Set colors for other controls
            TB_IP.BackColor = SkyBlue;
            TB_IP.ForeColor = DeepBlue;

            CB_Routine.BackColor = SkyBlue;
            CB_Routine.ForeColor = DeepBlue;

            NUD_Port.BackColor = SkyBlue;
            NUD_Port.ForeColor = DeepBlue;

            B_New.BackColor = DeepPink;
            B_New.ForeColor = ElegantWhite;

            FLP_Bots.BackColor = ElegantWhite;

            CB_Protocol.BackColor = SkyBlue;
            CB_Protocol.ForeColor = DeepBlue;

            comboBox1.BackColor = SkyBlue;
            comboBox1.ForeColor = DeepBlue;

            B_Stop.BackColor = StopRed;
            B_Stop.ForeColor = ElegantWhite;

            B_Start.BackColor = StartGreen;
            B_Start.ForeColor = ElegantWhite;

            B_RebootAndStop.BackColor = RebootBlue;
            B_RebootAndStop.ForeColor = ElegantWhite;

            B_RefreshMap.BackColor = RefreshMap;
            B_RefreshMap.ForeColor = StartGreen;
        }

        private void ApplyGengarTheme()
        {
            // Define Gengar-theme colors
            Color GengarPurple = Color.FromArgb(88, 88, 120);  // A muted purple, the main color of Gengar
            Color DarkShadow = Color.FromArgb(40, 40, 60);     // A deeper shade for shadowing and contrast
            Color GhostlyGrey = Color.FromArgb(200, 200, 215); // A soft grey for text and borders
            Color HauntingBlue = Color.FromArgb(80, 80, 160);  // A haunting blue for accenting and highlights
            Color MidnightBlack = Color.FromArgb(25, 25, 35);  // A near-black for the darkest areas
            Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
            Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
            Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
            Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
            Color RefreshMap = Color.FromArgb(245, 245, 220);// Refresh Map Button

            // Set the background color of the form
            BackColor = MidnightBlack;

            // Set the foreground color of the form (text color)
            ForeColor = GhostlyGrey;

            // Set the background color of the tab control
            TC_Main.BackColor = GengarPurple;

            // Set the background color of each tab page
            foreach (TabPage page in TC_Main.TabPages)
            {
                page.BackColor = DarkShadow;
            }

            // Set the background color of the property grid
            PG_Hub.BackColor = DarkShadow;
            PG_Hub.LineColor = HauntingBlue;
            PG_Hub.CategoryForeColor = GhostlyGrey;
            PG_Hub.CategorySplitterColor = HauntingBlue;
            PG_Hub.HelpBackColor = DarkShadow;
            PG_Hub.HelpForeColor = GhostlyGrey;
            PG_Hub.ViewBackColor = DarkShadow;
            PG_Hub.ViewForeColor = GhostlyGrey;

            // Set the background color of the rich text box
            RTB_Logs.BackColor = MidnightBlack;
            RTB_Logs.ForeColor = GhostlyGrey;

            // Set colors for other controls
            TB_IP.BackColor = GengarPurple;
            TB_IP.ForeColor = GhostlyGrey;

            CB_Routine.BackColor = GengarPurple;
            CB_Routine.ForeColor = GhostlyGrey;

            NUD_Port.BackColor = GengarPurple;
            NUD_Port.ForeColor = GhostlyGrey;

            B_New.BackColor = HauntingBlue;
            B_New.ForeColor = GhostlyGrey;

            FLP_Bots.BackColor = DarkShadow;

            CB_Protocol.BackColor = GengarPurple;
            CB_Protocol.ForeColor = GhostlyGrey;

            comboBox1.BackColor = GengarPurple;
            comboBox1.ForeColor = GhostlyGrey;

            B_Stop.BackColor = StopRed;
            B_Stop.ForeColor = ElegantWhite;

            B_Start.BackColor = StartGreen;
            B_Start.ForeColor = ElegantWhite;

            B_RebootAndStop.BackColor = RebootBlue;
            B_RebootAndStop.ForeColor = ElegantWhite;

            B_RefreshMap.BackColor = RefreshMap;
            B_RefreshMap.ForeColor = StartGreen;
        }

        private void ApplyLightTheme()
        {
            // Define the color palette
            Color SoftBlue = Color.FromArgb(235, 245, 251);
            Color GentleGrey = Color.FromArgb(245, 245, 245);
            Color DarkBlue = Color.FromArgb(26, 13, 171);
            Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
            Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
            Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
            Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
            Color RefreshMap = Color.FromArgb(245, 245, 220);// Refresh Map Button
            // Set the background color of the form
            BackColor = GentleGrey;

            // Set the foreground color of the form (text color)
            ForeColor = DarkBlue;

            // Set the background color of the tab control
            TC_Main.BackColor = SoftBlue;

            // Set the background color of each tab page
            foreach (TabPage page in TC_Main.TabPages)
            {
                page.BackColor = GentleGrey;
            }

            // Set the background color of the property grid
            PG_Hub.BackColor = GentleGrey;
            PG_Hub.LineColor = SoftBlue;
            PG_Hub.CategoryForeColor = DarkBlue;
            PG_Hub.CategorySplitterColor = SoftBlue;
            PG_Hub.HelpBackColor = GentleGrey;
            PG_Hub.HelpForeColor = DarkBlue;
            PG_Hub.ViewBackColor = GentleGrey;
            PG_Hub.ViewForeColor = DarkBlue;

            // Set the background color of the rich text box
            RTB_Logs.BackColor = Color.White;
            RTB_Logs.ForeColor = DarkBlue;

            // Set colors for other controls
            TB_IP.BackColor = Color.White;
            TB_IP.ForeColor = DarkBlue;

            CB_Routine.BackColor = Color.White;
            CB_Routine.ForeColor = DarkBlue;

            NUD_Port.BackColor = Color.White;
            NUD_Port.ForeColor = DarkBlue;

            B_New.BackColor = SoftBlue;
            B_New.ForeColor = DarkBlue;

            FLP_Bots.BackColor = GentleGrey;

            CB_Protocol.BackColor = Color.White;
            CB_Protocol.ForeColor = DarkBlue;

            comboBox1.BackColor = Color.White;
            comboBox1.ForeColor = DarkBlue;

            B_Stop.BackColor = StopRed;
            B_Stop.ForeColor = ElegantWhite;

            B_Start.BackColor = StartGreen;
            B_Start.ForeColor = ElegantWhite;

            B_RebootAndStop.BackColor = RebootBlue;
            B_RebootAndStop.ForeColor = ElegantWhite;

            B_RefreshMap.BackColor = RefreshMap;
            B_RefreshMap.ForeColor = StartGreen;
        }

        private void ApplyPokemonTheme()
        {
            // Define Poke-theme colors
            Color PokeRed = Color.FromArgb(206, 12, 30);      // A classic red tone reminiscent of the Pokeball
            Color DarkPokeRed = Color.FromArgb(164, 10, 24);  // A darker shade of the PokeRed for contrast and depth
            Color SleekGrey = Color.FromArgb(46, 49, 54);     // A sleek grey for background and contrast
            Color SoftWhite = Color.FromArgb(230, 230, 230);  // A soft white for text and borders
            Color MidnightBlack = Color.FromArgb(18, 19, 20); // A near-black for darker elements and depth
            Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
            Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
            Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
            Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
            Color RefreshMap = Color.FromArgb(245, 245, 220);// Refresh Map Button
            // Set the background color of the form
            BackColor = SleekGrey;

            // Set the foreground color of the form (text color)
            ForeColor = SoftWhite;

            // Set the background color of the tab control
            TC_Main.BackColor = DarkPokeRed;

            // Set the background color of each tab page
            foreach (TabPage page in TC_Main.TabPages)
            {
                page.BackColor = SleekGrey;
            }

            // Set the background color of the property grid
            PG_Hub.BackColor = SleekGrey;
            PG_Hub.LineColor = DarkPokeRed;
            PG_Hub.CategoryForeColor = SoftWhite;
            PG_Hub.CategorySplitterColor = DarkPokeRed;
            PG_Hub.HelpBackColor = SleekGrey;
            PG_Hub.HelpForeColor = SoftWhite;
            PG_Hub.ViewBackColor = SleekGrey;
            PG_Hub.ViewForeColor = SoftWhite;

            // Set the background color of the rich text box
            RTB_Logs.BackColor = MidnightBlack;
            RTB_Logs.ForeColor = SoftWhite;

            // Set colors for other controls
            TB_IP.BackColor = DarkPokeRed;
            TB_IP.ForeColor = SoftWhite;

            CB_Routine.BackColor = DarkPokeRed;
            CB_Routine.ForeColor = SoftWhite;

            NUD_Port.BackColor = DarkPokeRed;
            NUD_Port.ForeColor = SoftWhite;

            B_New.BackColor = PokeRed;
            B_New.ForeColor = SoftWhite;

            FLP_Bots.BackColor = SleekGrey;

            CB_Protocol.BackColor = DarkPokeRed;
            CB_Protocol.ForeColor = SoftWhite;

            comboBox1.BackColor = DarkPokeRed;
            comboBox1.ForeColor = SoftWhite;

            B_Stop.BackColor = StopRed;
            B_Stop.ForeColor = ElegantWhite;

            B_Start.BackColor = StartGreen;
            B_Start.ForeColor = ElegantWhite;

            B_RebootAndStop.BackColor = RebootBlue;
            B_RebootAndStop.ForeColor = ElegantWhite;

            B_RefreshMap.BackColor = RefreshMap;
            B_RefreshMap.ForeColor = StartGreen;
        }

        private void ApplyDarkTheme()
        {
            // Define the dark theme colors
            Color DarkRed = Color.FromArgb(90, 0, 0);
            Color DarkGrey = Color.FromArgb(30, 30, 30);
            Color LightGrey = Color.FromArgb(60, 60, 60);
            Color SoftWhite = Color.FromArgb(245, 245, 245);
            Color ElegantWhite = Color.FromArgb(255, 255, 255);// An elegant white for background and contrast
            Color StartGreen = Color.FromArgb(10, 74, 27);// Start Button
            Color StopRed = Color.FromArgb(74, 10, 10);// Stop Button
            Color RebootBlue = Color.FromArgb(10, 35, 74);// Reboot Button
            Color RefreshMap = Color.FromArgb(245, 245, 220);// Refresh Map Button
            // Set the background color of the form
            BackColor = DarkGrey;

            // Set the foreground color of the form (text color)
            ForeColor = SoftWhite;

            // Set the background color of the tab control
            TC_Main.BackColor = LightGrey;

            // Set the background color of each tab page
            foreach (TabPage page in TC_Main.TabPages)
            {
                page.BackColor = DarkGrey;
            }

            // Set the background color of the property grid
            PG_Hub.BackColor = DarkGrey;
            PG_Hub.LineColor = LightGrey;
            PG_Hub.CategoryForeColor = SoftWhite;
            PG_Hub.CategorySplitterColor = LightGrey;
            PG_Hub.HelpBackColor = DarkGrey;
            PG_Hub.HelpForeColor = SoftWhite;
            PG_Hub.ViewBackColor = DarkGrey;
            PG_Hub.ViewForeColor = SoftWhite;

            // Set the background color of the rich text box
            RTB_Logs.BackColor = DarkGrey;
            RTB_Logs.ForeColor = SoftWhite;

            // Set colors for other controls
            TB_IP.BackColor = LightGrey;
            TB_IP.ForeColor = SoftWhite;

            CB_Routine.BackColor = LightGrey;
            CB_Routine.ForeColor = SoftWhite;

            NUD_Port.BackColor = LightGrey;
            NUD_Port.ForeColor = SoftWhite;

            B_New.BackColor = DarkRed;
            B_New.ForeColor = SoftWhite;

            FLP_Bots.BackColor = DarkGrey;

            CB_Protocol.BackColor = LightGrey;
            CB_Protocol.ForeColor = SoftWhite;

            comboBox1.BackColor = LightGrey;
            comboBox1.ForeColor = SoftWhite;

            B_Stop.BackColor = StopRed;
            B_Stop.ForeColor = ElegantWhite;

            B_Start.BackColor = StartGreen;
            B_Start.ForeColor = ElegantWhite;

            B_RebootAndStop.BackColor = RebootBlue;
            B_RebootAndStop.ForeColor = ElegantWhite;

            B_RefreshMap.BackColor = RefreshMap;
            B_RefreshMap.ForeColor = StartGreen;
        }
    }
}