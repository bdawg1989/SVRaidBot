using Discord;
using Newtonsoft.Json;
using PKHeX.Core;
using RaidCrawler.Core.Structures;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using AnimatedGif;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.RotatingRaidSettingsSV;
using static SysBot.Pokemon.SV.BotRaid.Blocks;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using pkNX.Structures.FlatBuffers;

namespace SysBot.Pokemon.SV.BotRaid
{
    public class RotatingRaidBotSV : PokeRoutineExecutor9SV
    {
        private readonly PokeRaidHub<PK9> Hub;
        private readonly RotatingRaidSettingsSV Settings;
        private RemoteControlAccessList RaiderBanList => Settings.RaiderBanList;
        public static Dictionary<string, List<(int GroupID, int Index, string DenIdentifier)>> SpeciesToGroupIDMap =
        new(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient httpClient = new HttpClient();

        private Dictionary<(ushort Species, short Form), RaidBossMechanicsInfo> RaidBossMechanicsData = new();

        private class RaidBossMechanicsInfo
        {
            public byte ShieldHpTrigger { get; set; }
            public byte ShieldTimeTrigger { get; set; }
            public List<(short Action, short Timing, short Value, ushort MoveId)> ExtraActions { get; set; } = new();
        }

        public RotatingRaidBotSV(PokeBotState cfg, PokeRaidHub<PK9> hub) : base(cfg)
        {
            Hub = hub;
            Settings = hub.Config.RotatingRaidSV;
        }

        public class PlayerInfo
        {
            public string OT { get; set; }
            public int RaidCount { get; set; }
        }

        private int LobbyError;
        private int RaidCount;
        private int WinCount;
        private int LossCount;
        private int SeedIndexToReplace = -1;
        public static GameProgress GameProgress;
        public static bool? currentSpawnsEnabled;
        public int StoryProgress;
        private int EventProgress;
        private int EmptyRaid = 0;
        private int LostRaid = 0;
        private readonly int FieldID = 0;
        private bool firstRun = true;
        public static int RotationCount { get; set; }
        private ulong TodaySeed;
        private ulong OverworldOffset;
        private ulong ConnectedOffset;
        private ulong RaidBlockPointerP;
        private ulong RaidBlockPointerK;
        private ulong RaidBlockPointerB;
        private readonly ulong[] TeraNIDOffsets = new ulong[3];
        private string TeraRaidCode { get; set; } = string.Empty;
        private string BaseDescription = string.Empty;
        private readonly Dictionary<ulong, int> RaidTracker = [];
        private SAV9SV HostSAV = new();
        private static readonly DateTime StartTime = DateTime.Now;
        public static RaidContainer? container;
        public static bool IsKitakami = false;
        public static bool IsBlueberry = false;
        private static DateTime TimeForRollBackCheck = DateTime.Now;
        private string denHexSeed;
        private int seedMismatchCount = 0;
        private readonly bool indicesInitialized = false;
        private static readonly int KitakamiDensCount = 0;
        private static readonly int BlueberryDensCount = 0;
        private readonly int InvalidDeliveryGroupCount = 0;
        private bool shouldRefreshMap = false;
        public static bool HasErrored { get; set; } = false;

        public override async Task MainLoop(CancellationToken token)
        {
            if (Settings.RaidSettings.GenerateRaidsFromFile)
            {
                GenerateSeedsFromFile();
                Log("Done.");
                Settings.RaidSettings.GenerateRaidsFromFile = false;
            }

            if (Settings.MiscSettings.ConfigureRolloverCorrection)
            {
                await RolloverCorrectionSV(token).ConfigureAwait(false);
                return;
            }

            if (Settings.ActiveRaids.Count < 1)
            {
                Log("ActiveRaids cannot be 0. Please setup your parameters for the raid(s) you are hosting.");
                return;
            }

            try
            {
                Log("Identifying trainer data of the host console.");
                HostSAV = await IdentifyTrainer(token).ConfigureAwait(false);
                await InitializeHardware(Settings, token).ConfigureAwait(false);
                Log("Starting main RotatingRaidBot loop.");
                await InnerLoop(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log(e.Message);
            }
            finally
            {
                SaveSeeds();
            }
            Log($"Ending {nameof(RotatingRaidBotSV)} loop.");
            await HardStop().ConfigureAwait(false);
        }

        public override async Task RebootAndStop(CancellationToken t)
        {
            await ReOpenGame(new PokeRaidHubConfig(), t).ConfigureAwait(false);
            await HardStop().ConfigureAwait(false);
            await Task.Delay(2_000, t).ConfigureAwait(false);
            if (!t.IsCancellationRequested)
            {
                Log("Restarting the inner loop.");
                await InnerLoop(t).ConfigureAwait(false);
            }
        }

        public override Task RefreshMap(CancellationToken t)
        {
            shouldRefreshMap = true;
            return Task.CompletedTask;
        }

        public class PlayerDataStorage
        {
            private readonly string filePath;

            public PlayerDataStorage(string baseDirectory)
            {
                var directoryPath = Path.Combine(baseDirectory, "raidfilessv");
                Directory.CreateDirectory(directoryPath);
                filePath = Path.Combine(directoryPath, "player_data.json");

                if (!File.Exists(filePath))
                    File.WriteAllText(filePath, "{}"); // Create a new JSON file if it does not exist.
            }

            public Dictionary<ulong, PlayerInfo> LoadPlayerData()
            {
                string json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<Dictionary<ulong, PlayerInfo>>(json) ?? [];
            }

            public void SavePlayerData(Dictionary<ulong, PlayerInfo> data)
            {
                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
        }

        private void GenerateSeedsFromFile()
        {
            var folder = "raidfilessv";
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var prevrotationpath = "raidsv.txt";
            var rotationpath = "raidfilessv\\raidsv.txt";
            if (File.Exists(prevrotationpath))
                File.Move(prevrotationpath, rotationpath);
            if (!File.Exists(rotationpath))
            {
                File.WriteAllText(rotationpath, "000091EC-Kricketune-3-6,0000717F-Seviper-3-6");
                Log("Creating a default raidsv.txt file, skipping generation as file is empty.");
                return;
            }

            if (!File.Exists(rotationpath))
                Log("raidsv.txt not present, skipping parameter generation.");

            BaseDescription = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "raidfilessv\\bodyparam.txt";
            if (File.Exists(prevpath))
                File.Move(prevpath, filepath);
            if (File.Exists(filepath))
                BaseDescription = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "raidfilessv\\pkparam.txt";
            if (File.Exists(prevpk))
                File.Move(prevpk, pkpath);
            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            DirectorySearch(rotationpath, data);
        }

        private void SaveSeeds()
        {
            // Exit the function if saving seeds to file is not enabled
            if (!Settings.RaidSettings.SaveSeedsToFile)
                return;

            // Filter out raids that don't need to be saved
            var raidsToSave = Settings.ActiveRaids.Where(raid => !raid.AddedByRACommand).ToList();

            // Exit the function if there are no raids to save
            if (!raidsToSave.Any())
                return;

            // Define directory and file paths
            var directoryPath = "raidfilessv";
            var fileName = "savedSeeds.txt";
            var savePath = Path.Combine(directoryPath, fileName);

            // Create directory if it doesn't exist
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Initialize StringBuilder to build the save string
            StringBuilder sb = new();

            // Loop through each raid to be saved
            foreach (var raid in raidsToSave)
            {
                // Increment the StoryProgressLevel by 1 before saving
                int storyProgressValue = (int)raid.StoryProgress;

                // Build the string to save, including the incremented StoryProgressLevel
                sb.Append($"{raid.Seed}-{raid.Species}-{raid.DifficultyLevel}-{storyProgressValue}");
            }

            // Remove the trailing comma at the end
            if (sb.Length > 0)
                sb.Length--;

            // Write the built string to the file
            File.WriteAllText(savePath, sb.ToString());
        }

        private void DirectorySearch(string sDir, string data)
        {
            // Clear the active raids before populating it
            Settings.ActiveRaids.Clear();

            // Read the entire content from the file into a string
            string contents = File.ReadAllText(sDir);

            // Split the string based on commas to get each raid entry
            string[] moninfo = contents.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            // Iterate over each raid entry
            for (int i = 0; i < moninfo.Length; i++)
            {
                // Split the entry based on dashes to get individual pieces of information
                var div = moninfo[i].Split(separatorArray, StringSplitOptions.RemoveEmptyEntries);

                // Check if the split result has exactly 4 parts
                if (div.Length != 4)
                {
                    Log($"Error processing entry: {moninfo[i]}. Expected 4 parts but found {div.Length}. Skipping this entry.");
                    continue; // Skip processing this entry and move to the next one
                }

                // Extracting seed, title, and difficulty level
                var monseed = div[0];
                var montitle = div[1];

                if (!int.TryParse(div[2], out int difficultyLevel))
                {
                    Log($"Unable to parse difficulty level for entry: {moninfo[i]}");
                    continue;
                }

                // Extract and convert the StoryProgressLevel
                if (!int.TryParse(div[3], out int storyProgressLevelFromSeed))
                {
                    Log($"Unable to parse StoryProgressLevel for entry: {moninfo[i]}");
                    continue;
                }

                int convertedStoryProgressLevel = storyProgressLevelFromSeed - 1; // Converting based on given conditions

                // Determine the TeraCrystalType based on the difficulty level
                TeraCrystalType type = difficultyLevel switch
                {
                    6 => TeraCrystalType.Black,
                    7 => TeraCrystalType.Might,
                    _ => TeraCrystalType.Base,
                };

                // Create a new RotatingRaidParameters object and populate its properties
                RotatingRaidParameters param = new()
                {
                    Seed = monseed,
                    Title = montitle,
                    Species = RaidExtensions<PK9>.EnumParse<Species>(montitle),
                    CrystalType = type,
                    PartyPK = [data],
                    DifficultyLevel = difficultyLevel,
                    StoryProgress = (GameProgressEnum)convertedStoryProgressLevel
                };

                // Add the RotatingRaidParameters object to the ActiveRaids list
                Settings.ActiveRaids.Add(param);

                // Log the raid parameter generation
                Log($"Parameters generated from text file for {montitle}.");
            }
        }

        private async Task InnerLoop(CancellationToken token)
        {
            try
            {
                bool partyReady;
                RotationCount = 0;
                var raidsHosted = 0;

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Initialize offsets at the start of the routine and cache them.
                        await InitializeSessionOffsets(token).ConfigureAwait(false);

                        if (isRecoveringFromReboot)
                        {
                            Log("Recovering from reboot - ensuring online connectivity before proceeding.");
                            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                            {
                                Log("Not on overworld after reboot, attempting to return to overworld.");
                                await RecoverToOverworld(token).ConfigureAwait(false);
                            }

                            if (!await ConnectToOnline(Hub.Config, token).ConfigureAwait(false))
                            {
                                Log("Failed to connect online after reboot, retrying the reboot process.");
                                await PerformRebootAndReset(token).ConfigureAwait(false);
                                return;
                            }

                            isRecoveringFromReboot = false;
                            Log("Successfully recovered online connectivity after reboot.");
                        }

                        if (RaidCount == 0)
                        {
                            TodaySeed = BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 8, token).ConfigureAwait(false), 0);
                            Log($"Today Seed: {TodaySeed:X8}");
                        }

                        Log($"Preparing parameter for {Settings.ActiveRaids[RotationCount].Species}");
                        await ReadRaids(token).ConfigureAwait(false);

                        var currentSeed = BitConverter.ToUInt64(await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 8, token).ConfigureAwait(false), 0);
                        if (TodaySeed != currentSeed || LobbyError >= 2)
                        {
                            if (TodaySeed != currentSeed)
                            {
                                Log($"Current Today Seed {currentSeed:X8} does not match Starting Today Seed: {TodaySeed:X8}.\nAttempting to override Today Seed...");
                                TodaySeed = currentSeed;
                                await OverrideTodaySeed(token).ConfigureAwait(false);
                                Log("Today Seed has been overridden with the current seed.");
                            }

                            if (LobbyError >= 2)
                            {
                                string? msg = $"Failed to create a lobby {LobbyError} times.\n";
                                Log(msg);
                                await CloseGame(Hub.Config, token).ConfigureAwait(false);
                                await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                                LobbyError = 0;
                                continue;
                            }
                        }

                        // Clear NIDs.
                        await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);

                        // Connect online and enter den.
                        int prepareResult;
                        do
                        {
                            prepareResult = await PrepareForRaid(token).ConfigureAwait(false);
                            if (prepareResult == 0)
                            {
                                Log("Failed to prepare the raid, rebooting the game.");
                                await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                            }
                        } while (prepareResult == 0);

                        if (prepareResult == 2)
                        {
                            // Seed was injected, restart the loop
                            continue;
                        }

                        // Wait until we're in lobby.
                        if (!await GetLobbyReady(false, token).ConfigureAwait(false))
                            continue;

                        if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
                        {
                            var user = Settings.ActiveRaids[RotationCount].User;
                            var mentionedUsers = Settings.ActiveRaids[RotationCount].MentionedUsers;

                            // Determine if the raid is a "Free For All"
                            bool isFreeForAll = !Settings.ActiveRaids[RotationCount].IsCoded || EmptyRaid >= Settings.LobbyOptions.EmptyRaidLimit;

                            if (!isFreeForAll)
                            {
                                try
                                {
                                    // Only get and send the raid code if it's not a "Free For All"
                                    var code = await GetRaidCode(token).ConfigureAwait(false);
                                    if (user != null)
                                    {
                                        await user.SendMessageAsync($"Your Raid Code is **{code}**").ConfigureAwait(false);
                                    }
                                    foreach (var mentionedUser in mentionedUsers)
                                    {
                                        await mentionedUser.SendMessageAsync($"The Raid Code for the private raid you were invited to by {user?.Username ?? "the host"} is **{code}**").ConfigureAwait(false);
                                    }
                                }
                                catch (Discord.Net.HttpException ex)
                                {
                                    // Handle exception (e.g., log the error or send a message to a logging channel)
                                    Log($"Failed to send DM to the user or mentioned users. They might have DMs turned off. Exception: {ex.Message}");
                                }
                            }
                        }

                        // Read trainers until someone joins.
                        (partyReady, _) = await ReadTrainers(token).ConfigureAwait(false);
                        if (!partyReady)
                        {
                            if (LostRaid >= Settings.LobbyOptions.SkipRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
                            {
                                await SkipRaidOnLosses(token).ConfigureAwait(false);
                                EmptyRaid = 0;
                                continue;
                            }

                            // Should add overworld recovery with a game restart fallback.
                            await RegroupFromBannedUser(token).ConfigureAwait(false);

                            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                            {
                                Log("Something went wrong, attempting to recover.");
                                await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                                continue;
                            }

                            // Clear trainer OTs.
                            Log("Clearing stored OTs");
                            for (int i = 0; i < 3; i++)
                            {
                                List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                                ptr[2] += i * 0x30;
                                await SwitchConnection.PointerPoke(new byte[16], ptr, token).ConfigureAwait(false);
                            }
                            continue;
                        }
                        await CompleteRaid(token).ConfigureAwait(false);
                        raidsHosted++;
                        if (raidsHosted == Settings.RaidSettings.TotalRaidsToHost && Settings.RaidSettings.TotalRaidsToHost > 0)
                            break;
                    }
                    catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "_0")
                    {
                        Log("Connection error detected. Performing reboot and reset.");
                        await PerformRebootAndReset(token).ConfigureAwait(false);
                        return; // Exit the InnerLoop method after reboot and reset
                    }
                }
                if (Settings.RaidSettings.TotalRaidsToHost > 0 && raidsHosted != 0)
                    Log("Total raids to host has been met.");
            }
            catch (Exception ex)
            {
                Log($"An unexpected error occurred in InnerLoop: {ex.Message}");
                // Handle other exceptions as needed
            }
        }

        public override async Task HardStop()
        {
            try
            {
                Directory.Delete("cache", true);
            }
            catch (Exception)
            { }
            Settings.ActiveRaids.RemoveAll(p => p.AddedByRACommand);
            Settings.ActiveRaids.RemoveAll(p => p.Title == "Mystery Shiny Raid");
            await CleanExit(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task LocateSeedIndex(CancellationToken token)
        {
            int upperBound = KitakamiDensCount == 25 ? 94 : 95;
            int startIndex = KitakamiDensCount == 25 ? 94 : 95;

            var data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 2304, token).ConfigureAwait(false);
            for (int i = 0; i < 69; i++)  // Paldea Raids
            {
                var seed = BitConverter.ToUInt32(data.AsSpan(0x20 + i * 0x20, 4));
                if (seed == 0)
                {
                    SeedIndexToReplace = i;
                    Log($"Raid Den Located at {i} in Paldea.");
                    return;
                }
            }

            data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK + 0x10, 0xC80, token).ConfigureAwait(false);
            for (int i = 69; i < upperBound; i++)  // Kitakami Raids
            {
                var seed = BitConverter.ToUInt32(data.AsSpan((i - 69) * 0x20, 4));
                if (seed == 0)
                {
                    SeedIndexToReplace = i;
                    Log($"Raid Den Located at {i} in Kitakami.");
                    IsKitakami = true;
                    return;
                }
            }

            // Adding support for Blueberry Raids
            data = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerB + 0x10, 0xA00, token).ConfigureAwait(false);
            for (int i = startIndex; i < 118; i++)  // Blueberry Raids
            {
                var seed = BitConverter.ToUInt32(data.AsSpan((i - startIndex) * 0x20, 4));
                if (seed == 0)
                {
                    SeedIndexToReplace = i - 1;  // Adjusting the index by subtracting one
                    Log($"Raid Den Located at {i} in Blueberry.");
                    IsBlueberry = true;
                    return;
                }
            }
            Log($"Index not located.");
        }

        private async Task CompleteRaid(CancellationToken token)
        {
            try
            {
                var trainers = new List<(ulong, RaidMyStatus)>();

                if (!await CheckIfConnectedToLobbyAndLog(token))
                {
                    throw new Exception("Not connected to lobby");
                }

                if (!await EnsureInRaid(token))
                {
                    throw new Exception("Not in raid");
                }

                if (!Settings.EmbedToggles.AnimatedScreenshot)
                {
                    var screenshotDelay = (int)Settings.EmbedToggles.ScreenshotTiming;
                    await Task.Delay(screenshotDelay, token).ConfigureAwait(false);
                }

                var lobbyTrainersFinal = new List<(ulong, RaidMyStatus)>();
                if (!await UpdateLobbyTrainersFinal(lobbyTrainersFinal, trainers, token))
                {
                    throw new Exception("Failed to update lobby trainers");
                }

                if (!await HandleDuplicatesAndEmbeds(lobbyTrainersFinal, token))
                {
                    throw new Exception("Failed to handle duplicates and embeds");
                }

                await Task.Delay(10_000, token).ConfigureAwait(false);

                if (!await ProcessBattleActions(token))
                {
                    throw new Exception("Failed to process battle actions");
                }

                bool isRaidCompleted = await HandleEndOfRaidActions(token);
                if (!isRaidCompleted)
                {
                    throw new Exception("Raid not completed");
                }

                await FinalizeRaidCompletion(trainers, isRaidCompleted, token);
            }
            catch (Exception ex)
            {
                Log($"Error occurred during raid: {ex.Message}");
                await PerformRebootAndReset(token);
            }
        }

        private bool isRecoveringFromReboot = false;

        private async Task PerformRebootAndReset(CancellationToken t)
        {
            EmbedBuilder embed = new()
            {
                Title = "Bot Reset",
                Description = "The bot encountered an issue and is currently resetting. Please stand by.",
                Color = Discord.Color.Red,
                ThumbnailUrl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/x.png"
            };
            EchoUtil.RaidEmbed(null, "", embed);

            await ReOpenGame(new PokeRaidHubConfig(), t).ConfigureAwait(false);
            await HardStop().ConfigureAwait(false);
            await Task.Delay(2_000, t).ConfigureAwait(false);

            if (!t.IsCancellationRequested)
            {
                Log("Restarting the inner loop.");
                isRecoveringFromReboot = true; // Set flag to indicate we're recovering from reboot
                await InnerLoop(t).ConfigureAwait(false);
            }
        }

        private async Task<bool> CheckIfConnectedToLobbyAndLog(CancellationToken token)
        {
            try
            {
                if (await IsConnectedToLobby(token).ConfigureAwait(false))
                {
                    Log("Preparing for battle!");
                    return true;
                }
                else
                {
                    Log("Not connected to lobby, reopening game.");
                    await ReOpenGame(Hub.Config, token);
                    return false;
                }
            }
            catch (Exception ex) // Catch the appropriate exception
            {
                Log($"Error checking lobby connection: {ex.Message}, reopening game.");
                await ReOpenGame(Hub.Config, token);
                return false;
            }
        }

        private async Task<bool> EnsureInRaid(CancellationToken linkedToken)
        {
            var startTime = DateTime.Now;

            while (!await IsInRaid(linkedToken).ConfigureAwait(false))
            {
                if (linkedToken.IsCancellationRequested || (DateTime.Now - startTime).TotalMinutes > 5)
                {
                    Log("Timeout reached or cancellation requested, reopening game.");
                    await ReOpenGame(Hub.Config, linkedToken);
                    return false;
                }

                if (!await IsConnectedToLobby(linkedToken).ConfigureAwait(false))
                {
                    Log("Lost connection to lobby, reopening game.");
                    await ReOpenGame(Hub.Config, linkedToken);
                    return false;
                }

                await Click(A, 1_000, linkedToken).ConfigureAwait(false);
            }
            return true;
        }

        public async Task<bool> UpdateLobbyTrainersFinal(List<(ulong, RaidMyStatus)> lobbyTrainersFinal, List<(ulong, RaidMyStatus)> trainers, CancellationToken token)
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var storage = new PlayerDataStorage(baseDirectory);
            var playerData = storage.LoadPlayerData();

            // Clear NIDs to refresh player check.
            await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);
            await Task.Delay(5_000, token).ConfigureAwait(false);

            // Loop through trainers again in case someone disconnected.
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var nidOfs = TeraNIDOffsets[i];
                    var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                    var nid = BitConverter.ToUInt64(data, 0);

                    if (nid == 0)
                        continue;

                    List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                    ptr[2] += i * 0x30;
                    var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(trainer.OT) || HostSAV.OT == trainer.OT)
                        continue;

                    lobbyTrainersFinal.Add((nid, trainer));

                    if (!playerData.TryGetValue(nid, out var info))
                    {
                        // New player
                        playerData[nid] = new PlayerInfo { OT = trainer.OT, RaidCount = 1 };
                        Log($"New Player: {trainer.OT} | TID: {trainer.DisplayTID} | NID: {nid}.");
                    }
                    else
                    {
                        // Returning player
                        info.RaidCount++;
                        playerData[nid] = info; // Update the info back to the dictionary.
                        Log($"Returning Player: {trainer.OT} | TID: {trainer.DisplayTID} | NID: {nid} | Raids: {info.RaidCount}");
                    }
                }
                catch (IndexOutOfRangeException ex)
                {
                    Log($"Index out of range exception caught: {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"An unknown error occurred: {ex.Message}");
                    return false;
                }
            }

            // Save player data after processing all players.
            storage.SavePlayerData(playerData);
            return true;
        }

        private async Task<bool> HandleDuplicatesAndEmbeds(List<(ulong, RaidMyStatus)> lobbyTrainersFinal, CancellationToken token)
        {
            var nidDupe = lobbyTrainersFinal.Select(x => x.Item1).ToList();
            var dupe = lobbyTrainersFinal.Count > 1 && nidDupe.Distinct().Count() == 1;
            if (dupe)
            {
                // We read bad data, reset game to end early and recover.
                var msg = "Oops! Something went wrong, resetting to recover.";
                bool success = false;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await Task.Delay(5_000, token);
                        await EnqueueEmbed(null, "", false, false, false, false, token, true).ConfigureAwait(false);
                success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"Attempt {attempt} failed with error: {ex.Message}");
                        if (attempt == 3)
                        {
                            Log("All attempts failed. Continuing without sending embed.");
                        }
                    }
                }

                if (!success)
                {
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    return false;
                }
            }

            var names = lobbyTrainersFinal.Select(x => x.Item2.OT).ToList();
            bool hatTrick = lobbyTrainersFinal.Count == 3 && names.Distinct().Count() == 1;

            bool embedSuccess = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await EnqueueEmbed(names, "", hatTrick, false, false, true, token).ConfigureAwait(false);
                    embedSuccess = true;
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Attempt {attempt} failed with error: {ex.Message}");
                    if (attempt == 3)
                    {
                        Log("All attempts failed. Continuing without sending embed.");
                    }
                }
            }

            return embedSuccess;
        }

        private async Task<bool> ProcessBattleActions(CancellationToken token)
        {
            int nextUpdateMinute = 2;
            DateTime battleStartTime = DateTime.Now;
            bool hasPerformedAction1 = false;
            bool timedOut = false;
            bool hasPressedHome = false;

            while (await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                // New check: Are we still in a raid?
                if (!await IsInRaid(token).ConfigureAwait(false))
                {
                    Log("Not in raid anymore, stopping battle actions.");
                    return false;
                }
                TimeSpan timeInBattle = DateTime.Now - battleStartTime;

                // Check for battle timeout
                if (timeInBattle.TotalMinutes >= 15)
                {
                    Log("Battle timed out after 15 minutes. Even Netflix asked if I was still watching...");
                    timedOut = true;
                    break;
                }

                // Handle the first action with a delay
                if (!hasPerformedAction1)
                {
                    int action1DelayInSeconds = Settings.ActiveRaids[RotationCount].Action1Delay;
                    var action1Name = Settings.ActiveRaids[RotationCount].Action1;
                    int action1DelayInMilliseconds = action1DelayInSeconds * 1000;
                    Log($"Waiting {action1DelayInSeconds} seconds. No rush, we're chilling.");
                    await Task.Delay(action1DelayInMilliseconds, token).ConfigureAwait(false);
                    await MyActionMethod(token).ConfigureAwait(false);
                    Log($"{action1Name} done. Wasn't that fun?");
                    hasPerformedAction1 = true;
                }
                else
                {
                    // Execute raid actions based on configuration
                    switch (Settings.LobbyOptions.Action)
                    {
                        case RaidAction.AFK:
                            await Task.Delay(3_000, token).ConfigureAwait(false);
                            break;

                        case RaidAction.MashA:
                            if (await IsConnectedToLobby(token).ConfigureAwait(false))
                            {
                                int mashADelayInMilliseconds = (int)(Settings.LobbyOptions.MashADelay * 1000);
                                await Click(A, mashADelayInMilliseconds, token).ConfigureAwait(false);
                            }
                            break;
                    }
                }

                // Periodic battle status log at 2-minute intervals
                if (timeInBattle.TotalMinutes >= nextUpdateMinute)
                {
                    Log($"{nextUpdateMinute} minutes have passed. We are still in battle...");
                    nextUpdateMinute += 2; // Update the time for the next status update.
                }
                // Check if the battle has been ongoing for 6 minutes
                if (timeInBattle.TotalMinutes >= 6 && !hasPressedHome)
                {
                    // Hit Home button twice in case we are stuck
                    await Click(HOME, 0_500, token).ConfigureAwait(false);
                    await Click(HOME, 0_500, token).ConfigureAwait(false);
                    hasPressedHome = true;
                }
                // Make sure to wait some time before the next iteration to prevent a tight loop
                await Task.Delay(1000, token); // Wait for a second before checking again
            }

            return !timedOut;
        }

        private async Task<bool> HandleEndOfRaidActions(CancellationToken token)
        {
            LobbyFiltersCategory settings = new();

            Log("Raid lobby disbanded!");
            await Task.Delay(1_500 + settings.ExtraTimeLobbyDisband, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(DDOWN, 0_500, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);
            bool ready = true;

            if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
            {
                Log($"Lost/Empty Lobbies: {LostRaid}/{Settings.LobbyOptions.SkipRaidLimit}");

                if (LostRaid >= Settings.LobbyOptions.SkipRaidLimit)
                {
                    Log($"We had {Settings.LobbyOptions.SkipRaidLimit} lost/empty raids.. Moving on!");
                    await SanitizeRotationCount(token).ConfigureAwait(false);
                    await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
                    ready = true;
                }
            }

            return ready;
        }

        private async Task FinalizeRaidCompletion(List<(ulong, RaidMyStatus)> trainers, bool ready, CancellationToken token)
        {
            Log("Returning to overworld...");
            await Task.Delay(2_500, token).ConfigureAwait(false);
            while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                await Click(A, 1_000, token).ConfigureAwait(false);

            await LocateSeedIndex(token).ConfigureAwait(false);
            await CountRaids(trainers, token).ConfigureAwait(false);
            // Update RotationCount after locating seed index
            if (Settings.ActiveRaids.Count > 1)
            {
                await SanitizeRotationCount(token).ConfigureAwait(false);
            }
            await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await CloseGame(Hub.Config, token).ConfigureAwait(false);

            if (ready)
                await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
            else
            {
                if (Settings.ActiveRaids.Count > 1)
                {
                    RotationCount = (RotationCount + 1) % Settings.ActiveRaids.Count;
                    if (RotationCount == 0)
                    {
                        Log($"Resetting Rotation Count to {RotationCount}");
                    }

                    Log($"Moving on to next rotation for {Settings.ActiveRaids[RotationCount].Species}.");
                    await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                }
                else
                    await StartGame(Hub.Config, token).ConfigureAwait(false);
            }

            if (Settings.RaidSettings.KeepDaySeed)
                await OverrideTodaySeed(token).ConfigureAwait(false);
        }

        public async Task MyActionMethod(CancellationToken token)
        {
            // Let's rock 'n roll with these moves!
            switch (Settings.ActiveRaids[RotationCount].Action1)
            {
                case Action1Type.GoAllOut:
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.HangTough:
                case Action1Type.HealUp:
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    int ddownTimes = Settings.ActiveRaids[RotationCount].Action1 == Action1Type.HangTough ? 1 : 2;
                    for (int i = 0; i < ddownTimes; i++)
                    {
                        await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.Move1:
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.Move2:
                case Action1Type.Move3:
                case Action1Type.Move4:
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    int moveDdownTimes = Settings.ActiveRaids[RotationCount].Action1 == Action1Type.Move2 ? 1 : Settings.ActiveRaids[RotationCount].Action1 == Action1Type.Move3 ? 2 : 3;
                    for (int i = 0; i < moveDdownTimes; i++)
                    {
                        await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                default:
                    Console.WriteLine("Unknown action, what's the move?");
                    throw new InvalidOperationException("Unknown action type!");
            }
        }

        private async Task<uint> ReadAreaId(int raidIndex, CancellationToken token)
        {
            List<long> pointer = CalculateDirectPointer(raidIndex);
            int areaIdOffset = 20;

            return await ReadValue("Area ID", 4, AdjustPointer(pointer, areaIdOffset), token);
        }

        private async Task CountRaids(List<(ulong, RaidMyStatus)>? trainers, CancellationToken token)
        {
            if (trainers is not null)
            {
                Log("Back in the overworld, checking if we won or lost.");

                int currentRaidIndex = SeedIndexToReplace;
                uint areaId = await ReadAreaId(currentRaidIndex, token);

                if (areaId == 0)
                {
                    Log("Yay! We defeated the raid!");
                    WinCount++;
                }
                else
                {
                    Log("Dang, we lost the raid.");
                    LossCount++;
                }
            }
            else
            {
                Log("No trainers available to check win/loss status.");
            }
        }

        private async Task OverrideTodaySeed(CancellationToken token)
        {
            Log("Attempting to override Today Seed...");

            var todayoverride = BitConverter.GetBytes(TodaySeed);
            List<long> ptr = new(Offsets.RaidBlockPointerP);
            ptr[3] += 0x8;
            await SwitchConnection.PointerPoke(todayoverride, ptr, token).ConfigureAwait(false);

            Log("Today Seed override complete.");
        }

        private async Task OverrideSeedIndex(int index, CancellationToken token)
        {
            if (index == -1)
            {
                Log("Index is -1, skipping seed override.");
                return;
            }

            var crystalType = Settings.ActiveRaids[RotationCount].CrystalType;
            var seed = uint.Parse(Settings.ActiveRaids[RotationCount].Seed, NumberStyles.AllowHexSpecifier);
            var speciesName = Settings.ActiveRaids[RotationCount].Species.ToString();
            var groupID = Settings.ActiveRaids[RotationCount].GroupID;
            var denLocations = LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_base.json");
            string? denIdentifier = null;

            // Check if the user is not in Paldea and adjust the crystal type accordingly
            if ((IsKitakami || IsBlueberry) && (crystalType == TeraCrystalType.Might || crystalType == TeraCrystalType.Distribution))
            {
                crystalType = TeraCrystalType.Black;
                Log("User is not in Paldea. Setting crystal type to Black.");
            }

            if (crystalType == TeraCrystalType.Might || crystalType == TeraCrystalType.Distribution)
            {
                uint defaultSeed = uint.Parse("000118C8", NumberStyles.AllowHexSpecifier);
                if (index != -1)
                {
                    List<long> prevPtr = DeterminePointer(index);
                    byte[] defaultSeedBytes = BitConverter.GetBytes(defaultSeed);
                    await SwitchConnection.PointerPoke(defaultSeedBytes, prevPtr, token).ConfigureAwait(false);
                    Log($"Set default seed {defaultSeed:X8} at previous index {index}.");
                    await Task.Delay(1_500, token).ConfigureAwait(false);
                }
                if (SpeciesToGroupIDMap.TryGetValue(speciesName, out var groupIDAndIndices))
                {
                    var specificIndexInfo = groupIDAndIndices.FirstOrDefault(x => x.GroupID == groupID);
                    if (specificIndexInfo != default)
                    {
                        index = specificIndexInfo.Index; // Adjusted index based on GroupID and species
                        denIdentifier = specificIndexInfo.DenIdentifier; // Capture the DenIdentifier for teleportation
                        Log($"Using specific index {index} for GroupID: {groupID}, species: {speciesName}, and DenIdentifier: {denIdentifier}.");
                    }
                }
                List<long> ptr = DeterminePointer(index);
                byte[] seedBytes = BitConverter.GetBytes(seed);
                await SwitchConnection.PointerPoke(seedBytes, ptr, token).ConfigureAwait(false);
                Log($"Injected seed {seed:X8} at index {index}.");

                var crystalPtr = new List<long>(ptr);
                crystalPtr[3] += 0x08;
                byte[] crystalBytes = BitConverter.GetBytes((int)crystalType);
                await SwitchConnection.PointerPoke(crystalBytes, crystalPtr, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);

                // Teleportation logic
                if (denIdentifier != null && denLocations.TryGetValue(denIdentifier, out var coordinates))
                {
                    await TeleportToDen(coordinates[0], coordinates[1], coordinates[2], token);
                    Log($"Successfully teleported to the den: {denIdentifier} with coordinates {String.Join(", ", coordinates)}.");
                }
                else
                {
                    Log($"Failed to find den location for DenIdentifier: {denIdentifier}.");
                }
            }
            else
            {
                List<long> ptr = DeterminePointer(index);
                // Overriding the seed
                byte[] inj = BitConverter.GetBytes(seed);
                var currseed = await SwitchConnection.PointerPeek(4, ptr, token).ConfigureAwait(false);

                // Reverse the byte array of the current seed for logging purposes if necessary
                byte[] currSeedForLogging = (byte[])currseed.Clone();
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(currSeedForLogging);
                }

                // Reverse the byte array of the new seed for logging purposes if necessary
                byte[] injForLogging = (byte[])inj.Clone();
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(injForLogging);
                }

                // Convert byte arrays to hexadecimal strings for logging
                string currSeedHex = BitConverter.ToString(currSeedForLogging).Replace("-", "");
                string newSeedHex = BitConverter.ToString(injForLogging).Replace("-", "");

                Log($"Replacing {currSeedHex} with {newSeedHex}.");
                await SwitchConnection.PointerPoke(inj, ptr, token).ConfigureAwait(false);

                // Overriding the crystal type
                var ptr2 = new List<long>(ptr);
                ptr2[3] += 0x08;
                var crystal = BitConverter.GetBytes((int)crystalType);
                var currcrystal = await SwitchConnection.PointerPeek(1, ptr2, token).ConfigureAwait(false);
                if (currcrystal != crystal)
                    await SwitchConnection.PointerPoke(crystal, ptr2, token).ConfigureAwait(false);
            }
        }

        private async Task CreateMysteryRaidAsync()
        {
            await Task.Yield();

            try
            {
                CreateMysteryRaid();
            }
            catch (Exception ex)
            {
                Log($"Error in CreateMysteryRaid: {ex.Message}");
            }
        }

        private void CreateMysteryRaid()
        {
            uint randomSeed = GenerateRandomShinySeed();
            Random random = new();
            var mysteryRaidsSettings = Settings.RaidSettings.MysteryRaidsSettings;

            // Check if any Mystery Raid setting is enabled
            if (!(mysteryRaidsSettings.Unlocked3StarSettings.Enabled || mysteryRaidsSettings.Unlocked4StarSettings.Enabled ||
                  mysteryRaidsSettings.Unlocked5StarSettings.Enabled || mysteryRaidsSettings.Unlocked6StarSettings.Enabled))
            {
                Log("All Mystery Raids options are disabled. Mystery Raids will be turned off.");
                Settings.RaidSettings.MysteryRaids = false;
                return;
            }

            // Create a list of enabled StoryProgressLevels
            var enabledLevels = new List<GameProgress>();
            if (mysteryRaidsSettings.Unlocked3StarSettings.Enabled) enabledLevels.Add(GameProgress.Unlocked3Stars);
            if (mysteryRaidsSettings.Unlocked4StarSettings.Enabled) enabledLevels.Add(GameProgress.Unlocked4Stars);
            if (mysteryRaidsSettings.Unlocked5StarSettings.Enabled) enabledLevels.Add(GameProgress.Unlocked5Stars);
            if (mysteryRaidsSettings.Unlocked6StarSettings.Enabled) enabledLevels.Add(GameProgress.Unlocked6Stars);

            // Randomly pick a StoryProgressLevel from the enabled levels
            GameProgress gameProgress = enabledLevels[random.Next(enabledLevels.Count)];

            // Initialize a list to store possible difficulties
            List<int> possibleDifficulties = [];

            // Determine possible difficulties based on the selected GameProgress
            switch (gameProgress)
            {
                case GameProgress.Unlocked3Stars:
                    if (mysteryRaidsSettings.Unlocked3StarSettings.Allow1StarRaids) possibleDifficulties.Add(1);
                    if (mysteryRaidsSettings.Unlocked3StarSettings.Allow2StarRaids) possibleDifficulties.Add(2);
                    if (mysteryRaidsSettings.Unlocked3StarSettings.Allow3StarRaids) possibleDifficulties.Add(3);
                    break;

                case GameProgress.Unlocked4Stars:
                    if (mysteryRaidsSettings.Unlocked4StarSettings.Allow1StarRaids) possibleDifficulties.Add(1);
                    if (mysteryRaidsSettings.Unlocked4StarSettings.Allow2StarRaids) possibleDifficulties.Add(2);
                    if (mysteryRaidsSettings.Unlocked4StarSettings.Allow3StarRaids) possibleDifficulties.Add(3);
                    if (mysteryRaidsSettings.Unlocked4StarSettings.Allow4StarRaids) possibleDifficulties.Add(4);
                    break;

                case GameProgress.Unlocked5Stars:
                    if (mysteryRaidsSettings.Unlocked5StarSettings.Allow3StarRaids) possibleDifficulties.Add(3);
                    if (mysteryRaidsSettings.Unlocked5StarSettings.Allow4StarRaids) possibleDifficulties.Add(4);
                    if (mysteryRaidsSettings.Unlocked5StarSettings.Allow5StarRaids) possibleDifficulties.Add(5);
                    break;

                case GameProgress.Unlocked6Stars:
                    if (mysteryRaidsSettings.Unlocked6StarSettings.Allow3StarRaids) possibleDifficulties.Add(3);
                    if (mysteryRaidsSettings.Unlocked6StarSettings.Allow4StarRaids) possibleDifficulties.Add(4);
                    if (mysteryRaidsSettings.Unlocked6StarSettings.Allow5StarRaids) possibleDifficulties.Add(5);
                    if (mysteryRaidsSettings.Unlocked6StarSettings.Allow6StarRaids) possibleDifficulties.Add(6);
                    break;
            }

            // Check if there are any enabled difficulty levels
            if (possibleDifficulties.Count == 0)
            {
                Log("No difficulty levels enabled for the selected Story Progress. Mystery Raids will be turned off.");
                Settings.RaidSettings.MysteryRaids = false; // Disable Mystery Raids
                return; // Exit the method
            }

            // Randomly pick a difficulty level from the possible difficulties
            int randomDifficultyLevel = possibleDifficulties[random.Next(possibleDifficulties.Count)];

            // Determine the crystal type based on difficulty level
            var crystalType = randomDifficultyLevel switch
            {
                >= 1 and <= 5 => TeraCrystalType.Base,
                6 => TeraCrystalType.Black,
                _ => throw new ArgumentException("Invalid difficulty level.")
            };

            string seedValue = randomSeed.ToString("X8");
            int contentType = randomDifficultyLevel == 6 ? 1 : 0;
            TeraRaidMapParent map;
            if (!IsBlueberry && !IsKitakami)
            {
                map = TeraRaidMapParent.Paldea;
            }
            else if (IsKitakami)
            {
                map = TeraRaidMapParent.Kitakami;
            }
            else
            {
                map = TeraRaidMapParent.Blueberry;
            }

            int raidDeliveryGroupID = 0;
            List<string> emptyRewardsToShow = new List<string>();
            bool defaultMoveTypeEmojis = false;
            List<MoveTypeEmojiInfo> emptyCustomTypeEmojis = new List<MoveTypeEmojiInfo>();
            int defaultQueuePosition = 0;
            bool defaultIsEvent = false;
            (PK9 pk, Embed embed) = RaidInfoCommand(seedValue, contentType, map, (int)gameProgress, raidDeliveryGroupID,
                                                        emptyRewardsToShow, defaultMoveTypeEmojis, emptyCustomTypeEmojis,
                                                        defaultQueuePosition, defaultIsEvent);

            string teraType = ExtractTeraTypeFromEmbed(embed);
            string[] battlers = GetBattlerForTeraType(teraType);
            RotatingRaidParameters newRandomShinyRaid = new()
            {
                Seed = seedValue,
                Species = Species.None,
                SpeciesForm = pk.Form,
                Title = $"Mystery Shiny Raid",
                AddedByRACommand = true,
                DifficultyLevel = randomDifficultyLevel,
                StoryProgress = (GameProgressEnum)gameProgress,
                CrystalType = crystalType,
                IsShiny = pk.IsShiny,
                PartyPK = battlers.Length > 0 ? battlers : [""]
            };

            // Find the last position of a raid added by the RA command
            int lastRaCommandRaidIndex = Settings.ActiveRaids.FindLastIndex(raid => raid.AddedByRACommand);
            int insertPosition = lastRaCommandRaidIndex != -1 ? lastRaCommandRaidIndex + 1 : RotationCount + 1;

            // Insert the new raid at the determined position
            Settings.ActiveRaids.Insert(insertPosition, newRandomShinyRaid);

            Log($"Added Mystery Raid - Species: {(Species)pk.Species}, Seed: {seedValue}.");
        }

        private static uint GenerateRandomShinySeed()
        {
            Random random = new();
            uint seed;

            do
            {
                // Generate a random uint
                byte[] buffer = new byte[4];
                random.NextBytes(buffer);
                seed = BitConverter.ToUInt32(buffer, 0);
            }
            while (Raidshiny(seed) == 0);

            return seed;
        }

        private static int Raidshiny(uint Seed)
        {
            Xoroshiro128Plus xoroshiro128Plus = new(Seed);
            _ = (uint)xoroshiro128Plus.NextInt(4294967295uL);
            uint num2 = (uint)xoroshiro128Plus.NextInt(4294967295uL);
            uint num3 = (uint)xoroshiro128Plus.NextInt(4294967295uL);
            return (((num3 >> 16) ^ (num3 & 0xFFFF)) >> 4 == ((num2 >> 16) ^ (num2 & 0xFFFF)) >> 4) ? 1 : 0;
        }

        private static string ExtractTeraTypeFromEmbed(Embed embed)
        {
            var statsField = embed.Fields.FirstOrDefault(f => f.Name == "**__Stats__**");
            if (statsField != null)
            {
                var lines = statsField.Value.Split('\n');
                var teraTypeLine = lines.FirstOrDefault(l => l.StartsWith("**TeraType:**"));
                if (teraTypeLine != null)
                {
                    var teraType = teraTypeLine.Split(':')[1].Trim();
                    teraType = teraType.Replace("*", "").Trim();
                    return teraType;
                }
            }
            return "Fairy";
        }

        private string[] GetBattlerForTeraType(string teraType)
        {
            var battlers = Settings.RaidSettings.MysteryRaidsSettings.TeraTypeBattlers;
            return teraType switch
            {
                "Bug" => battlers.BugBattler,
                "Dark" => battlers.DarkBattler,
                "Dragon" => battlers.DragonBattler,
                "Electric" => battlers.ElectricBattler,
                "Fairy" => battlers.FairyBattler,
                "Fighting" => battlers.FightingBattler,
                "Fire" => battlers.FireBattler,
                "Flying" => battlers.FlyingBattler,
                "Ghost" => battlers.GhostBattler,
                "Grass" => battlers.GrassBattler,
                "Ground" => battlers.GroundBattler,
                "Ice" => battlers.IceBattler,
                "Normal" => battlers.NormalBattler,
                "Poison" => battlers.PoisonBattler,
                "Psychic" => battlers.PsychicBattler,
                "Rock" => battlers.RockBattler,
                "Steel" => battlers.SteelBattler,
                "Water" => battlers.WaterBattler,
                _ => []
            };
        }

        private async Task<uint> ReadValue(string fieldName, int size, List<long> pointer, CancellationToken token)
        {
            byte[] valueBytes = await SwitchConnection.PointerPeek(size, pointer, token).ConfigureAwait(false);
            //  Log($"{fieldName} - Read Value: {BitConverter.ToString(valueBytes)}");

            // Determine the byte order based on the field name
            bool isBigEndian = fieldName.Equals("Den ID");

            if (isBigEndian)
            {
                // If the value is in big-endian format, reverse the byte array
                Array.Reverse(valueBytes);
            }

            // Convert the byte array to uint (now in little-endian format)
            return BitConverter.ToUInt32(valueBytes, 0);
        }

        private async Task LogAndUpdateValue(string fieldName, uint value, int size, List<long> pointer, CancellationToken token)
        {
            _ = await SwitchConnection.PointerPeek(size, pointer, token).ConfigureAwait(false);
            // Log($"{fieldName} - Current Value: {BitConverter.ToString(currentValue)}");

            // Determine the byte order based on the field name
            bool isBigEndian = fieldName.Equals("Den ID");

            // Create a new byte array for the new value
            byte[] newValue = new byte[4]; // Assuming uint is 4 bytes
            if (isBigEndian)
            {
                newValue[0] = (byte)(value >> 24); // Most significant byte
                newValue[1] = (byte)(value >> 16);
                newValue[2] = (byte)(value >> 8);
                newValue[3] = (byte)(value);       // Least significant byte
            }
            else
            {
                newValue[0] = (byte)(value);       // Least significant byte
                newValue[1] = (byte)(value >> 8);
                newValue[2] = (byte)(value >> 16);
                newValue[3] = (byte)(value >> 24); // Most significant byte
            }

            await SwitchConnection.PointerPoke(newValue, pointer, token).ConfigureAwait(false);
            _ = await SwitchConnection.PointerPeek(size, pointer, token).ConfigureAwait(false);
            //  Log($"{fieldName} - Updated Value: {BitConverter.ToString(updatedValue)}");
        }

        private static List<long> AdjustPointer(List<long> basePointer, int offset)
        {
            var adjustedPointer = new List<long>(basePointer);
            adjustedPointer[3] += offset; // Adjusting the offset at the 4th index
            return adjustedPointer;
        }

        private List<long> CalculateDirectPointer(int index)
        {
            int blueberrySubtractValue = KitakamiDensCount == 25 ? 94 : 95;

            if (IsKitakami)
            {
                return new List<long>(Offsets.RaidBlockPointerK)
                {
                    [3] = 0xCE8 + ((index - 70) * 0x20)
                };
            }
            else if (IsBlueberry)
            {
                return new List<long>(Offsets.RaidBlockPointerB)
                {
                    [3] = 0x1968 + ((index - blueberrySubtractValue) * 0x20)
                };
            }
            else
            {
                return new List<long>(Offsets.RaidBlockPointerP)
                {
                    [3] = 0x40 + index * 0x20
                };
            }
        }

        private List<long> DeterminePointer(int index)
        {
            int blueberrySubtractValue = KitakamiDensCount == 25 ? 93 : 94;

            if (index < 69)
            {
                return new List<long>(Offsets.RaidBlockPointerP)
                {
                    [3] = 0x60 + index * 0x20
                };
            }
            else if (index < 94)
            {
                return new List<long>(Offsets.RaidBlockPointerK)
                {
                    [3] = 0xCE8 + ((index - 69) * 0x20)
                };
            }
            else
            {
                return new List<long>(Offsets.RaidBlockPointerB)
                {
                    [3] = 0x1968 + ((index - blueberrySubtractValue) * 0x20)
                };
            }
        }

        private async Task SanitizeRotationCount(CancellationToken token)
        {
            try
            {
                await Task.Delay(50, token).ConfigureAwait(false);
                if (Settings.ActiveRaids.Count == 0)
                {
                    Log("ActiveRaids is empty. Exiting SanitizeRotationCount.");
                    RotationCount = 0;
                    return;
                }

                // Find the next enabled raid
                int nextEnabledRaidIndex = FindNextEnabledRaidIndex(RotationCount);
                if (nextEnabledRaidIndex == -1)
                {
                    Log("No enabled raids found. Exiting SanitizeRotationCount.");
                    RotationCount = 0;
                    return;
                }

                RotationCount = nextEnabledRaidIndex;

                // Update RaidUpNext for the next raid
                for (int i = 0; i < Settings.ActiveRaids.Count; i++)
                {
                    Settings.ActiveRaids[i].RaidUpNext = i == RotationCount;
                }

                // Process RA command raids
                if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
                {
                    bool isMysteryRaid = Settings.ActiveRaids[RotationCount].Title.Contains("Mystery Shiny Raid");
                    bool isUserRequestedRaid = !isMysteryRaid && Settings.ActiveRaids[RotationCount].Title.Contains("'s Requested Raid");
                    if (isUserRequestedRaid || isMysteryRaid)
                    {
                        Log($"Raid for {Settings.ActiveRaids[RotationCount].Species} was added via RA command and will be removed from the rotation list.");
                        Settings.ActiveRaids.RemoveAt(RotationCount);
                        // Find the next enabled raid after removal
                        nextEnabledRaidIndex = FindNextEnabledRaidIndex(RotationCount);
                        RotationCount = nextEnabledRaidIndex != -1 ? nextEnabledRaidIndex : 0;
                    }
                    else if (!firstRun)
                    {
                        nextEnabledRaidIndex = FindNextEnabledRaidIndex((RotationCount + 1) % Settings.ActiveRaids.Count);
                        RotationCount = nextEnabledRaidIndex != -1 ? nextEnabledRaidIndex : RotationCount;
                    }
                }
                else if (!firstRun)
                {
                    nextEnabledRaidIndex = FindNextEnabledRaidIndex((RotationCount + 1) % Settings.ActiveRaids.Count);
                    RotationCount = nextEnabledRaidIndex != -1 ? nextEnabledRaidIndex : RotationCount;
                }

                if (firstRun)
                {
                    firstRun = false;
                }

                if (Settings.RaidSettings.RandomRotation)
                {
                    ProcessRandomRotation();
                    return;
                }

                // Find next priority raid
                int nextPriorityIndex = FindNextPriorityRaidIndex(RotationCount, Settings.ActiveRaids);
                if (nextPriorityIndex != -1)
                {
                    RotationCount = nextPriorityIndex;
                }

                Log($"Next raid in the list: {Settings.ActiveRaids[RotationCount].Species}.");
            }
            catch (Exception ex)
            {
                Log($"Error in SanitizeRotationCount. Resetting RotationCount to 0. {ex.Message}");
                RotationCount = 0;
            }
        }

        private int FindNextEnabledRaidIndex(int startIndex)
        {
            for (int i = 0; i < Settings.ActiveRaids.Count; i++)
            {
                int index = (startIndex + i) % Settings.ActiveRaids.Count;
                if (Settings.ActiveRaids[index].ActiveInRotation)
                {
                    return index;
                }
            }
            return -1; // No enabled raids found
        }

        private int FindNextPriorityRaidIndex(int currentRotationCount, List<RotatingRaidParameters> raids)
        {
            if (raids == null || raids.Count == 0)
            {
                return currentRotationCount;
            }
            int count = raids.Count;
            // First, check for user-requested RA command raids
            for (int i = 0; i < count; i++)
            {
                int index = (currentRotationCount + i) % count;
                RotatingRaidParameters raid = raids[index];
                if (raid.ActiveInRotation && raid.AddedByRACommand && !raid.Title.Contains("Mystery Shiny Raid"))
                {
                    return index; // Prioritize user-requested raids
                }
            }
            // Next, check for Mystery Shiny Raids if enabled
            if (Settings.RaidSettings.MysteryRaids)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = (currentRotationCount + i) % count;
                    RotatingRaidParameters raid = raids[index];
                    if (raid.ActiveInRotation && raid.Title.Contains("Mystery Shiny Raid"))
                    {
                        return index; // Only consider Mystery Shiny Raids after user-requested raids
                    }
                }
            }
            // Return current rotation count if no priority raids are found
            return -1;
        }

        private void ProcessRandomRotation()
        {
            // Turn off RandomRotation if both RandomRotation and MysteryRaid are true
            if (Settings.RaidSettings.RandomRotation && Settings.RaidSettings.MysteryRaids)
            {
                Settings.RaidSettings.RandomRotation = false;
                Log("RandomRotation turned off due to MysteryRaids being active.");
                return;
            }

            // Check the remaining raids for any added by the RA command
            for (var i = 0; i < Settings.ActiveRaids.Count; i++)
            {
                if (Settings.ActiveRaids[i].ActiveInRotation && Settings.ActiveRaids[i].AddedByRACommand)
                {
                    RotationCount = i;
                    Log($"Setting Rotation Count to {RotationCount}");
                    return;
                }
            }

            // If no raid added by RA command was found, select a random enabled raid
            var random = new Random();
            var enabledRaids = Settings.ActiveRaids.Where(r => r.ActiveInRotation).ToList();
            if (enabledRaids.Count > 0)
            {
                int randomIndex = random.Next(enabledRaids.Count);
                RotationCount = Settings.ActiveRaids.IndexOf(enabledRaids[randomIndex]);
                Log($"Setting Rotation Count to {RotationCount}");
            }
            else
            {
                Log("No enabled raids found for random rotation.");
                RotationCount = 0;
            }
        }

        private async Task InjectPartyPk(string battlepk, CancellationToken token)
        {
            var set = new ShowdownSet(battlepk);
            var template = AutoLegalityWrapper.GetTemplate(set);
            PK9 pk = (PK9)HostSAV.GetLegal(template, out _);
            pk.ResetPartyStats();
            var offset = await SwitchConnection.PointerAll(Offsets.BoxStartPokemonPointer, token).ConfigureAwait(false);
            await SwitchConnection.WriteBytesAbsoluteAsync(pk.EncryptedBoxData, offset, token).ConfigureAwait(false);
        }

        private async Task<int> PrepareForRaid(CancellationToken token)
        {
            if (shouldRefreshMap)
            {
                Log("Starting Refresh map process...");
                await HardStop().ConfigureAwait(false);
                await Task.Delay(2_000, token).ConfigureAwait(false);
                await Click(B, 3_000, token).ConfigureAwait(false);
                await Click(B, 3_000, token).ConfigureAwait(false);
                await GoHome(Hub.Config, token).ConfigureAwait(false);
                await AdvanceDaySV(token).ConfigureAwait(false);
                await SaveGame(Hub.Config, token).ConfigureAwait(false);
                await RecoverToOverworld(token).ConfigureAwait(false);
                shouldRefreshMap = false;
                if (!token.IsCancellationRequested)
                {
                    Log("Map Refresh Completed. Restarting the main loop...");
                    await MainLoop(token).ConfigureAwait(false);
                }
            }

            _ = Settings.ActiveRaids[RotationCount];
            var currentSeed = Settings.ActiveRaids[RotationCount].Seed.ToUpper();

            if (!denHexSeed.Equals(currentSeed, StringComparison.CurrentCultureIgnoreCase))
            {
                seedMismatchCount++;
                Log($"Raid Den and Current Seed do not match. Mismatch count: {seedMismatchCount}");

                if (seedMismatchCount >= 2)
                {
                    Log("Seeds have mismatched 2 times in a row. Refreshing the map.");
                    shouldRefreshMap = true;
                    seedMismatchCount = 0;
                    return 2;
                }

                await Task.Delay(4_000, token).ConfigureAwait(false);
                Log("Injecting correct seed.");
                await CloseGame(Hub.Config, token).ConfigureAwait(false);
                await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
                Log("Seed injected Successfully!");
                return 2;
            }
            else
            {
                seedMismatchCount = 0;
            }

            if (Settings.ActiveRaids[RotationCount].AddedByRACommand)
            {
                var user = Settings.ActiveRaids[RotationCount].User;
                var mentionedUsers = Settings.ActiveRaids[RotationCount].MentionedUsers;

                // Determine if the raid is a "Free For All"
                bool isFreeForAll = !Settings.ActiveRaids[RotationCount].IsCoded || EmptyRaid >= Settings.LobbyOptions.EmptyRaidLimit;

                if (!isFreeForAll)
                {
                    try
                    {
                        // Only send the message if it's not a "Free For All"
                        if (user != null)
                        {
                            await user.SendMessageAsync("Get Ready! Your raid is being prepared now!").ConfigureAwait(false);
                        }

                        foreach (var mentionedUser in mentionedUsers)
                        {
                            await mentionedUser.SendMessageAsync($"Get Ready! The raid you were invited to by {user?.Username ?? "the host"} is about to start!").ConfigureAwait(false);
                        }
                    }
                    catch (Discord.Net.HttpException ex)
                    {
                        // Handle exception (e.g., log the error or send a message to a logging channel)
                        Log($"Failed to send DM to the user or mentioned users. They might have DMs turned off. Exception: {ex.Message}");
                    }
                }
            }

            Log("Preparing lobby...");

            if (!await ConnectToOnline(Hub.Config, token))
            {
                return 0;
            }

            await Task.Delay(0_500, token).ConfigureAwait(false);
            await SwitchPartyPokemon(token).ConfigureAwait(false);
            await Task.Delay(1_500, token).ConfigureAwait(false);

            if (!await RecoverToOverworld(token).ConfigureAwait(false))
                return 0;

            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);

            if (!Settings.ActiveRaids[RotationCount].IsCoded || (Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby))
            {
                if (Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                    Log($"We had {Settings.LobbyOptions.EmptyRaidLimit} empty raids.. Opening this raid to all!");
                await Click(DDOWN, 1_000, token).ConfigureAwait(false);
            }

            await Click(A, 4_000, token).ConfigureAwait(false);
            return 1;
        }

        private async Task SwitchPartyPokemon(CancellationToken token)
        {
            LobbyFiltersCategory settings = new();
            var len = string.Empty;
            foreach (var l in Settings.ActiveRaids[RotationCount].PartyPK)
                len += l;
            if (len.Length > 1 && EmptyRaid == 0)
            {
                Log("Preparing PartyPK. Sit tight.");
                await Task.Delay(2_500 + settings.ExtraTimePartyPK, token).ConfigureAwait(false);
                await SetCurrentBox(0, token).ConfigureAwait(false);
                var res = string.Join("\n", Settings.ActiveRaids[RotationCount].PartyPK);
                if (res.Length > 4096)
                    res = res[..4096];
                await InjectPartyPk(res, token).ConfigureAwait(false);

                await Click(X, 2_000, token).ConfigureAwait(false);
                await Click(DRIGHT, 0_500, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, -32000, 1_000, token).ConfigureAwait(false);
                await SetStick(SwitchStick.LEFT, 0, 0, 0, token).ConfigureAwait(false);
                for (int i = 0; i < 2; i++)
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                await Click(A, 3_500, token).ConfigureAwait(false);
                await Click(Y, 0_500, token).ConfigureAwait(false);
                await Click(DLEFT, 0_800, token).ConfigureAwait(false);
                await Click(Y, 0_500, token).ConfigureAwait(false);
                Log("PartyPK switch successful.");
            }
        }

        private async Task<bool> RecoverToOverworld(CancellationToken token)
        {
            if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                return true;

            var attempts = 0;
            while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            {
                attempts++;
                if (attempts >= 30)
                    break;
                for (int i = 0; i < 20; i++)
                {
                    await Click(B, 1_000, token).ConfigureAwait(false);
                    if (await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                        return true;
                }
            }

            // We didn't make it for some reason.
            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
            {
                Log("Failed to recover to overworld, rebooting the game.");
                return false; // Return false instead of rebooting here
            }
            await Task.Delay(1_000, token).ConfigureAwait(false);
            return true;
        }

        private async Task RollBackHour(CancellationToken token)
        {
            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);
            Log("Navigating to time settings.");
            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.MiscSettings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.MiscSettings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.MiscSettings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);

            for (int i = 0; i < 3; i++) // Navigate to the hour setting
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);
            Log("Rolling Time Back 1 Hour.");
            for (int i = 0; i < 1; i++) // Roll back the hour by 1
                await Click(DDOWN, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen
        }

        private async Task RollBackTime(CancellationToken token)
        {
            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);

            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.MiscSettings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.MiscSettings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.MiscSettings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);

            for (int i = 0; i < 3; i++) // Navigate to the hour setting
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 5; i++) // Roll back the hour by 5
                await Click(DDOWN, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen
        }

        private async Task<bool> GetLobbyReady(bool recovery, CancellationToken token)
        {
            var x = 0;
            Log("Connecting to lobby...");
            while (!await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                await Click(A, 1_000, token).ConfigureAwait(false);
                x++;
                if (x == 15 && recovery)
                {
                    Log("No den here! Rolling again.");
                    return false;
                }
                if (x == 45)
                {
                    Log("Failed to connect to lobby, restarting game incase we were in battle/bad connection.");
                    LobbyError++;
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    Log("Attempting to restart routine!");
                    return false;
                }
            }
            return true;
        }

        private async Task<string> GetRaidCode(CancellationToken token)
        {
            var data = await SwitchConnection.PointerPeek(6, Offsets.TeraRaidCodePointer, token).ConfigureAwait(false);
            TeraRaidCode = Encoding.ASCII.GetString(data).ToLower(); // Convert to lowercase for easier reading
            return $"{TeraRaidCode}";
        }

        private async Task<bool> CheckIfTrainerBanned(RaidMyStatus trainer, ulong nid, int player, CancellationToken token)
        {
            RaidTracker.TryAdd(nid, 0);
            var msg = string.Empty;
            var banResultCFW = RaiderBanList.List.FirstOrDefault(x => x.ID == nid);
            bool isBanned = banResultCFW != default;

            if (isBanned)
            {
                msg = $"{banResultCFW!.Name} was found in the host's ban list.\n{banResultCFW.Comment}";
                Log(msg);
                await CurrentRaidInfo(null, "", false, true, false, false, null, false, token).ConfigureAwait(false);
                await EnqueueEmbed(null, msg, false, true, false, false, token).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private async Task<(bool, List<(ulong, RaidMyStatus)>)> ReadTrainers(CancellationToken token)
        {
            if (!await IsConnectedToLobby(token))
                return (false, new List<(ulong, RaidMyStatus)>());
            await EnqueueEmbed(null, "", false, false, false, false, token).ConfigureAwait(false);

            List<(ulong, RaidMyStatus)> lobbyTrainers = [];
            TimeSpan wait;
            if (Settings.ActiveRaids[RotationCount].AddedByRACommand &&
                Settings.ActiveRaids[RotationCount].Title != "Mystery Shiny Raid")
            {
                wait = TimeSpan.FromSeconds(160) - TimeSpan.FromMilliseconds((int)Settings.EmbedToggles.RequestEmbedTime);
            }
            else
            {
                wait = TimeSpan.FromSeconds(160);
            }

            var endTime = DateTime.Now + wait;
            bool full = false;

            while (!full && DateTime.Now < endTime)
            {
                if (!await IsConnectedToLobby(token))
                    return (false, lobbyTrainers);

                for (int i = 0; i < 3; i++)
                {
                    var player = i + 2;
                    Log($"Waiting for Player {player} to load...");

                    if (!await IsConnectedToLobby(token))
                        return (false, lobbyTrainers);

                    var nidOfs = TeraNIDOffsets[i];
                    var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                    var nid = BitConverter.ToUInt64(data, 0);
                    while (nid == 0 && DateTime.Now < endTime)
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);

                        if (!await IsConnectedToLobby(token))
                            return (false, lobbyTrainers);

                        data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                        nid = BitConverter.ToUInt64(data, 0);
                    }

                    List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                    ptr[2] += i * 0x30;
                    var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                    while (trainer.OT.Length == 0 && DateTime.Now < endTime)
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);

                        if (!await IsConnectedToLobby(token))
                            return (false, lobbyTrainers);

                        trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);
                    }

                    if (nid != 0 && !string.IsNullOrWhiteSpace(trainer.OT))
                    {
                        if (await CheckIfTrainerBanned(trainer, nid, player, token).ConfigureAwait(false))
                            return (false, lobbyTrainers);
                    }

                    if (lobbyTrainers.Any(x => x.Item1 == nid))
                    {
                        Log($"Duplicate NID detected: {nid}. Skipping...");
                        continue;
                    }

                    if (nid > 0 && trainer.OT.Length > 0)
                        lobbyTrainers.Add((nid, trainer));

                    full = lobbyTrainers.Count == 3;
                    if (full)
                    {
                        List<string> trainerNames = lobbyTrainers.Select(t => t.Item2.OT).ToList();
                        await CurrentRaidInfo(trainerNames, "", false, false, false, false, null, true, token).ConfigureAwait(false);
                    }

                    if (full || DateTime.Now >= endTime)
                        break;
                }
            }

            await Task.Delay(5_000, token).ConfigureAwait(false);

            if (lobbyTrainers.Count == 0)
            {
                EmptyRaid++;
                LostRaid++;
                Log($"Nobody joined the raid, recovering...");
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                    Log($"Empty Raid Count #{EmptyRaid}");
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipRaid)
                    Log($"Lost/Empty Lobbies: {LostRaid}/{Settings.LobbyOptions.SkipRaidLimit}");

                return (false, lobbyTrainers);
            }

            RaidCount++;
            Log($"Raid #{RaidCount} is starting!");
            if (EmptyRaid != 0)
                EmptyRaid = 0;
            return (true, lobbyTrainers);
        }

        private async Task<bool> IsConnectedToLobby(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(Offsets.TeraLobbyIsConnected, 1, token).ConfigureAwait(false);
            return data[0] != 0x00; // 0 when in lobby but not connected
        }

        private async Task<bool> IsInRaid(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(Offsets.LoadedIntoDesiredState, 1, token).ConfigureAwait(false);
            return data[0] == 0x02; // 2 when in raid, 1 when not
        }

        private async Task AdvanceDaySV(CancellationToken token)
        {
            var scrollroll = Settings.MiscSettings.DateTimeFormat switch
            {
                DTFormat.DDMMYY => 0,
                DTFormat.YYMMDD => 2,
                _ => 1,
            };

            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);

            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.MiscSettings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.MiscSettings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.MiscSettings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);
            for (int i = 0; i < scrollroll; i++) // 0 to roll day for DDMMYY, 1 to roll day for MMDDYY, 3 to roll hour
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(DUP, 0_200, token).ConfigureAwait(false); // Advance a day

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen

            await Click(A, 0_200, token).ConfigureAwait(false); // Back in Game
        }

        private async Task RolloverCorrectionSV(CancellationToken token)
        {
            var scrollroll = Settings.MiscSettings.DateTimeFormat switch
            {
                DTFormat.DDMMYY => 0,
                DTFormat.YYMMDD => 2,
                _ => 1,
            };

            for (int i = 0; i < 2; i++)
                await Click(B, 0_150, token).ConfigureAwait(false);

            for (int i = 0; i < 2; i++)
                await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(DRIGHT, 0_150, token).ConfigureAwait(false);
            await Click(A, 1_250, token).ConfigureAwait(false); // Enter settings

            await PressAndHold(DDOWN, 2_000, 0_250, token).ConfigureAwait(false); // Scroll to system settings
            await Click(A, 1_250, token).ConfigureAwait(false);

            if (Settings.MiscSettings.UseOvershoot)
            {
                await PressAndHold(DDOWN, Settings.MiscSettings.HoldTimeForRollover, 1_000, token).ConfigureAwait(false);
                await Click(DUP, 0_500, token).ConfigureAwait(false);
            }
            else if (!Settings.MiscSettings.UseOvershoot)
            {
                for (int i = 0; i < 39; i++)
                    await Click(DDOWN, 0_100, token).ConfigureAwait(false);
            }

            await Click(A, 1_250, token).ConfigureAwait(false);
            for (int i = 0; i < 2; i++)
                await Click(DDOWN, 0_150, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);
            for (int i = 0; i < scrollroll; i++) // 0 to roll day for DDMMYY, 1 to roll day for MMDDYY, 3 to roll hour
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(DDOWN, 0_200, token).ConfigureAwait(false);

            for (int i = 0; i < 8; i++) // Mash DRIGHT to confirm
                await Click(DRIGHT, 0_200, token).ConfigureAwait(false);

            await Click(A, 0_200, token).ConfigureAwait(false); // Confirm date/time change
            await Click(HOME, 1_000, token).ConfigureAwait(false); // Back to title screen
        }

        private async Task RegroupFromBannedUser(CancellationToken token)
        {
            Log("Attempting to remake lobby..");
            await Click(B, 2_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(B, 1_000, token).ConfigureAwait(false);
            while (!await IsOnOverworld(OverworldOffset, token))
            {
                for (int i = 0; i < 8; i++)
                    await Click(B, 1000, token);
            }
        }

        private async Task InitializeSessionOffsets(CancellationToken token)
        {
            Log("Caching session offsets...");
            OverworldOffset = await SwitchConnection.PointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
            ConnectedOffset = await SwitchConnection.PointerAll(Offsets.IsConnectedPointer, token).ConfigureAwait(false);
            RaidBlockPointerP = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerP, token).ConfigureAwait(false);
            RaidBlockPointerK = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerK, token).ConfigureAwait(false);
            RaidBlockPointerB = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerB, token).ConfigureAwait(false);
            sbyte FieldID = await ReadEncryptedBlockByte(RaidDataBlocks.KPlayerCurrentFieldID, token).ConfigureAwait(false);
            string regionName = FieldID switch
            {
                0 => "Paldea",
                1 => "Kitakami",
                2 => "Blueberry",
                _ => "Unknown"
            };
            Log($"Player in Region: {regionName}");
            if (regionName == "Kitakami")
            {
                IsKitakami = true;
            }
            else if (regionName == "Blueberry")
            {
                IsBlueberry = true;
            }
            if (firstRun)
            {
                GameProgress = await ReadGameProgress(token).ConfigureAwait(false);
                Log($"Current Game Progress identified as {GameProgress}.");
                currentSpawnsEnabled = (bool?)await ReadBlock(RaidDataBlocks.KWildSpawnsEnabled, CancellationToken.None);
            }

            var nidPointer = new long[] { Offsets.LinkTradePartnerNIDPointer[0], Offsets.LinkTradePartnerNIDPointer[1], Offsets.LinkTradePartnerNIDPointer[2] };
            for (int p = 0; p < TeraNIDOffsets.Length; p++)
            {
                nidPointer[2] = Offsets.LinkTradePartnerNIDPointer[2] + p * 0x8;
                TeraNIDOffsets[p] = await SwitchConnection.PointerAll(nidPointer, token).ConfigureAwait(false);
            }
            // Load the 7 Star raid boss mechanics
            await LoadRaidBossMechanics(token).ConfigureAwait(false);
            Log("Caching offsets complete!");
        }

        private static async Task<bool> IsValidImageUrlAsync(string url)
        {
            using var httpClient = new HttpClient();
            try
            {
                var response = await httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex) when (ex.InnerException is WebException webEx && webEx.Status == WebExceptionStatus.TrustFailure)
            {
            }
            catch (Exception)
            {
            }
            return false;
        }

        private readonly Dictionary<string, string> TypeAdvantages = new()
        {
            { "normal", "Fighting" },
            { "fire", "Water, Ground, Rock" },
            { "water", "Electric, Grass" },
            { "grass", "Flying, Poison, Bug, Fire, Ice" },
            { "electric", "Ground" },
            { "ice", "Fighting, Rock, Steel, Fire" },
            { "fighting", "Flying, Psychic, Fairy" },
            { "poison", "Ground, Psychic" },
            { "ground", "Water, Ice, Grass" },
            { "flying", "Rock, Electric, Ice" },
            { "psychic", "Bug, Ghost, Dark" },
            { "bug", "Flying, Rock, Fire" },
            { "rock", "Fighting, Ground, Steel, Water, Grass" },
            { "ghost", "Ghost, Dark" },
            { "dragon", "Ice, Dragon, Fairy" },
            { "dark", "Fighting, Bug, Fairy" },
            { "steel", "Fighting, Ground, Fire" },
            { "fairy", "Poison, Steel" }
        };
        private static readonly char[] separator = [','];
        private static readonly char[] separatorArray = ['-'];

        private string GetTypeAdvantage(string teraType)
        {
            // Check if the type exists in the dictionary and return the corresponding advantage
            if (TypeAdvantages.TryGetValue(teraType.ToLower(), out string advantage))
            {
                return advantage;
            }
            return "Unknown Type";  // Return "Unknown Type" if the type doesn't exist in our dictionary
        }

        private async Task<byte[]?> CaptureGifScreenshotsAsync(CancellationToken token)
        {
            var frameCount = Settings.EmbedToggles.Frames;
            var gifFrames = new List<System.Drawing.Image>();
            var gifWidth = 450;
            var gifHeight = 270;
            var gifQuality = (AnimatedGif.GifQuality)Settings.EmbedToggles.GifQuality;
            var frameDelay = 180;

            try
            {
                for (int i = 0; i < frameCount; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        Log("GIF capture was canceled.");
                        return null;
                    }

                    byte[] bytes;
                    try
                    {
                        bytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                    }
                    catch (Exception ex)
                    {
                        Log($"Error while fetching frame {i + 1}/{frameCount}: {ex.Message}");
                        if (i == 0)
                            return null;
                        break;
                    }

                    if (bytes.Length == 0)
                    {
                        Log($"No data received for frame {i + 1}/{frameCount}.");
                        if (i == 0)
                            return null;
                        break;
                    }

                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        using var bitmap = new Bitmap(ms);
                        var resizedFrame = bitmap.GetThumbnailImage(gifWidth, gifHeight, null, IntPtr.Zero);
                        var frame = ((Bitmap)resizedFrame).Clone(new Rectangle(0, 0, resizedFrame.Width, resizedFrame.Height),
                                                                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        gifFrames.Add(frame);
                        resizedFrame.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log($"Error processing frame {i + 1}/{frameCount}: {ex.Message}");
                        continue;
                    }

                    try
                    {
                        await Task.Delay(50, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Log("GIF capture delay was canceled.");
                        break;
                    }
                }

                if (gifFrames.Count == 0)
                {
                    Log("No frames were successfully captured for GIF.");
                    return null;
                }

                using var outputMs = new MemoryStream();
                try
                {
                    using (var gif = new AnimatedGifCreator(outputMs, frameDelay))
                    {
                        foreach (var frame in gifFrames)
                        {
                            gif.AddFrame(frame, quality: gifQuality);
                        }
                    }
                    return outputMs.ToArray();
                }
                catch (Exception ex)
                {
                    Log($"Error creating GIF: {ex.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Log($"Unexpected error in GIF capture: {ex.Message}");
                return null;
            }
            finally
            {
                foreach (var frame in gifFrames)
                {
                    frame.Dispose();
                }
            }
        }

        private async Task EnqueueEmbed(List<string>? names, string message, bool hatTrick, bool disband, bool upnext, bool raidstart, CancellationToken token, bool isRaidStartingEmbed = false)
        {
            string code = string.Empty;

            // If this is a raid ending, starting with players, or disbanding, update reactions first
            if (disband || (names is not null && !upnext) || upnext)
            {
                // Update to red X emoji - do this BEFORE clearing tracking
                await SharedRaidCodeHandler.UpdateReactionsOnAllMessages(false, token);
            }

            // Only clear tracking when starting a new raid or when the raid is specifically over
            if ((!disband && names is null && !upnext && !raidstart) || upnext)
            {
                SharedRaidCodeHandler.ClearAllRaidTracking();
            }

            // Determine if the raid is a "Free For All" based on the settings and conditions
            if (Settings.ActiveRaids[RotationCount].IsCoded && EmptyRaid < Settings.LobbyOptions.EmptyRaidLimit)
            {
                // If it's not a "Free For All", retrieve the raid code
                code = await GetRaidCode(token).ConfigureAwait(false);
            }
            else
            {
                // If it's a "Free For All", set the code as such
                code = "Free For All";
            }

            // Apply delay only if the raid was added by RA command, not a Mystery Shiny Raid, and has a code
            if (Settings.ActiveRaids[RotationCount].AddedByRACommand &&
                Settings.ActiveRaids[RotationCount].Title != "Mystery Shiny Raid" &&
                code != "Free For All")
            {
                await Task.Delay((int)Settings.EmbedToggles.RequestEmbedTime, token).ConfigureAwait(false);
            }

            // Description can only be up to 4096 characters.
            var description = Settings.EmbedToggles.RaidEmbedDescription.Length > 0 ? string.Join("\n", Settings.EmbedToggles.RaidEmbedDescription) : "";
            if (description.Length > 4096) description = description[..4096];

            if (EmptyRaid == Settings.LobbyOptions.EmptyRaidLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                EmptyRaid = 0;

            if (disband) // Wait for trainer to load before disband
                await Task.Delay(5_000, token).ConfigureAwait(false);

            byte[]? imageBytes = null;
            string fileName = string.Empty;

            // Define a condition for raid starting embeds with countdown 
            bool isRaidStartingWithCountdown = (isRaidStartingEmbed || (!disband && names is null && !upnext && !raidstart && Settings.EmbedToggles.IncludeCountdown));

            if (!disband && names is not null && !upnext && Settings.EmbedToggles.TakeScreenshot)
            {
                try
                {
                    if (Settings.EmbedToggles.AnimatedScreenshot)
                    {
                        try
                        {
                            imageBytes = await Task.Run(() => CaptureGifScreenshotsAsync(token)).ConfigureAwait(false);
                            fileName = $"raidecho{RotationCount}.gif";
                        }
                        catch (Exception ex)
                        {
                            Log($"Error while capturing GIF screenshots: {ex.Message}");
                            Log("Falling back to standard JPG screenshot.");

                            imageBytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                            fileName = $"raidecho{RotationCount}.jpg";
                        }
                    }
                    else
                    {
                        imageBytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                        fileName = $"raidecho{RotationCount}.jpg";
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error while capturing screenshots: {ex.Message}");
                }
            }
            else if (Settings.EmbedToggles.TakeScreenshot && !upnext && !isRaidStartingWithCountdown)
            {
                try
                {
                    imageBytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
                    fileName = $"raidecho{RotationCount}.jpg";
                }
                catch (Exception ex)
                {
                    Log($"Error while fetching pixels: {ex.Message}");
                }
            }

            string disclaimer = Settings.ActiveRaids.Count > 1
                                ? $"Hideout"
                                : "";

            var turl = string.Empty;
            var form = string.Empty;

            Log($"Rotation Count: {RotationCount} | Species is {Settings.ActiveRaids[RotationCount].Species}");
            if (!disband && !upnext && !raidstart)
                Log($"Raid Code is: {code}");
            PK9 pk = new()
            {
                Species = (ushort)Settings.ActiveRaids[RotationCount].Species,
                Form = (byte)Settings.ActiveRaids[RotationCount].SpeciesForm
            };
            if (pk.Form != 0)
                form = $"-{pk.Form}";
            if (Settings.ActiveRaids[RotationCount].IsShiny == true)
                pk.SetIsShiny(true);
            else
                pk.SetIsShiny(false);

            if (Settings.ActiveRaids[RotationCount].SpriteAlternateArt && Settings.ActiveRaids[RotationCount].IsShiny)
            {
                var altUrl = AltPokeImg(pk);

                try
                {
                    // Check if AltPokeImg URL is valid
                    if (await IsValidImageUrlAsync(altUrl))
                    {
                        turl = altUrl;
                    }
                    else
                    {
                        Settings.ActiveRaids[RotationCount].SpriteAlternateArt = false;  // Set SpriteAlternateArt to false if no img found
                        turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
                        Log($"AltPokeImg URL was not valid. Setting SpriteAlternateArt to false.");
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception and use the default sprite
                    Log($"Error while validating alternate image URL: {ex.Message}");
                    Settings.ActiveRaids[RotationCount].SpriteAlternateArt = false;  // Set SpriteAlternateArt to false due to error
                    turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
                }
            }
            else
            {
                turl = RaidExtensions<PK9>.PokeImg(pk, false, false);
            }

            if (Settings.ActiveRaids[RotationCount].Species is 0)
                turl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/combat.png";

            // Fetch the dominant color from the image only AFTER turl is assigned
            (int R, int G, int B) dominantColor = Task.Run(() => RaidExtensions<PK9>.GetDominantColorAsync(turl)).Result;

            // Use the dominant color, unless it's a disband or hatTrick situation
            var embedColor = disband ? Discord.Color.Red : hatTrick ? Discord.Color.Purple : new Discord.Color(dominantColor.R, dominantColor.G, dominantColor.B);

            TimeSpan duration = new(0, 2, 31);

            // Calculate the future time by adding the duration to the current time
            DateTimeOffset futureTime = DateTimeOffset.Now.Add(duration);

            // Convert the future time to Unix timestamp
            long futureUnixTime = futureTime.ToUnixTimeSeconds();

            // Create the future time message using Discord's timestamp formatting
            string futureTimeMessage = $"**Raid Posting: <t:{futureUnixTime}:R>**";

            // Initialize the EmbedBuilder object
            var embed = new EmbedBuilder()
            {
                Title = disband ? $"**Raid canceled: [{TeraRaidCode}]**" : upnext && Settings.RaidSettings.TotalRaidsToHost != 0 ? $"Raid Ended - Preparing Next Raid!" : upnext && Settings.RaidSettings.TotalRaidsToHost == 0 ? $"Raid Ended - Preparing Next Raid!" : "",
                Color = embedColor,
                Description = disband ? message : upnext ? Settings.RaidSettings.TotalRaidsToHost == 0 ? $"# {Settings.ActiveRaids[RotationCount].Title}\n\n{futureTimeMessage}" : $"# {Settings.ActiveRaids[RotationCount].Title}\n\n{futureTimeMessage}" : raidstart ? "" : description,
                ThumbnailUrl = upnext ? turl : (imageBytes == null ? turl : null), // Set ThumbnailUrl based on upnext and imageBytes
                ImageUrl = imageBytes != null ? $"attachment://{fileName}" : null, // Set ImageUrl based on imageBytes
            };

            if (!raidstart && !upnext && code != "Free For All")
                await CurrentRaidInfo(null, code, false, false, false, false, turl, false, token).ConfigureAwait(false);

            // Only include footer if not posting 'upnext' embed with the 'Preparing Raid' title
            if (!(upnext && Settings.RaidSettings.TotalRaidsToHost == 0))
            {
                string programIconUrl = $"https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/icon4.png";
                int raidsInRotationCount = Hub.Config.RotatingRaidSV.ActiveRaids.Count(r => !r.AddedByRACommand);
                // Calculate uptime
                TimeSpan uptime = DateTime.Now - StartTime;

                // Check for singular or plural days/hours
                string dayLabel = uptime.Days == 1 ? "day" : "days";
                string hourLabel = uptime.Hours == 1 ? "hour" : "hours";
                string minuteLabel = uptime.Minutes == 1 ? "minute" : "minutes";

                // Format the uptime string, omitting the part if the value is 0
                string uptimeFormatted = "";
                if (uptime.Days > 0)
                {
                    uptimeFormatted += $"{uptime.Days} {dayLabel} ";
                }
                if (uptime.Hours > 0 || uptime.Days > 0) // Show hours if there are any hours, or if there are days even if hours are 0
                {
                    uptimeFormatted += $"{uptime.Hours} {hourLabel} ";
                }
                if (uptime.Minutes > 0 || uptime.Hours > 0 || uptime.Days > 0) // Show minutes if there are any minutes, or if there are hours/days even if minutes are 0
                {
                    uptimeFormatted += $"{uptime.Minutes} {minuteLabel}";
                }

                // Trim any excess whitespace from the string
                uptimeFormatted = uptimeFormatted.Trim();
                embed.WithFooter(new EmbedFooterBuilder()
                {
                    Text = $"Completed Raids: {RaidCount} (W: {WinCount} | L: {LossCount})\nActiveRaids: {raidsInRotationCount} | Uptime: {uptimeFormatted}\n" + disclaimer,
                    IconUrl = programIconUrl
                });
            }

            // Prepare the tera icon URL
            string teraType = RaidEmbedInfoHelpers.RaidSpeciesTeraType.ToLower();
            string folderName = Settings.EmbedToggles.SelectedTeraIconType == TeraIconType.Icon1 ? "icon1" : "icon2"; // Add more conditions for more icon types
            string teraIconUrl = $"https://raw.githubusercontent.com/bdawg1989/sprites/main/teraicons/{folderName}/{teraType}.png";

            // Only include author (header) if not posting 'upnext' embed with the 'Preparing Raid' title
            if (!(upnext && Settings.RaidSettings.TotalRaidsToHost == 0))
            {
                // Set the author (header) of the embed with the tera icon
                embed.WithAuthor(new EmbedAuthorBuilder()
                {
                    Name = RaidEmbedInfoHelpers.RaidEmbedTitle,
                    IconUrl = teraIconUrl
                });
            }
            if (!disband && !upnext && !raidstart)
            {
                StringBuilder statsField = new();
                statsField.AppendLine($"**Level**: {RaidEmbedInfoHelpers.RaidLevel}");
                statsField.AppendLine($"**Gender**: {RaidEmbedInfoHelpers.RaidSpeciesGender}");
                statsField.AppendLine($"**Nature**: {RaidEmbedInfoHelpers.RaidSpeciesNature}");
                statsField.AppendLine($"**Ability**: {RaidEmbedInfoHelpers.RaidSpeciesAbility}");
                statsField.AppendLine($"**IVs**: {RaidEmbedInfoHelpers.RaidSpeciesIVs}");
                statsField.AppendLine($"**Scale**: {RaidEmbedInfoHelpers.ScaleText}({RaidEmbedInfoHelpers.ScaleNumber})");

                if (Settings.EmbedToggles.IncludeSeed)
                {
                    var storyProgressValue = Settings.ActiveRaids[RotationCount].StoryProgress switch
                    {
                        GameProgressEnum.Unlocked6Stars => 6,
                        GameProgressEnum.Unlocked5Stars => 5,
                        GameProgressEnum.Unlocked4Stars => 4,
                        GameProgressEnum.Unlocked3Stars => 3,
                        _ => 6,
                    };
                    statsField.AppendLine($"**Seed**: `{Settings.ActiveRaids[RotationCount].Seed} {Settings.ActiveRaids[RotationCount].DifficultyLevel} {storyProgressValue}`");
                }

                embed.AddField("**__Stats__**", statsField.ToString(), true);
                embed.AddField("\u200b", "\u200b", true);
            }

            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField("**__Moves__**", string.IsNullOrEmpty($"{RaidEmbedInfoHelpers.ExtraMoves}") ? string.IsNullOrEmpty($"{RaidEmbedInfoHelpers.Moves}") ? "No Moves To Display" : $"{RaidEmbedInfoHelpers.Moves}" : $"{RaidEmbedInfoHelpers.Moves}\n**Extra Moves:**\n{RaidEmbedInfoHelpers.ExtraMoves}", true);
                RaidEmbedInfoHelpers.ExtraMoves = string.Empty;
            }

            if (!disband && !upnext && !raidstart && !Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField(" **__Special Rewards__**", string.IsNullOrEmpty($"{RaidEmbedInfoHelpers.SpecialRewards}") ? "No Rewards To Display" : $"{RaidEmbedInfoHelpers.SpecialRewards}", true);
                RaidEmbedInfoHelpers.SpecialRewards = string.Empty;
            }
            // Fetch the type advantage using the static RaidSpeciesTeraType from RaidEmbedInfo
            string typeAdvantage = GetTypeAdvantage(RaidEmbedInfoHelpers.RaidSpeciesTeraType);

            // Only include the Type Advantage if not posting 'upnext' embed with the 'Preparing Raid' title and if the raid isn't starting or disbanding
            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeTypeAdvantage)
            {
                embed.AddField(" **__Type Advantage__**", typeAdvantage, true);
                embed.AddField("\u200b", "\u200b", true);
            }

            if (!disband && !upnext && !raidstart && Settings.EmbedToggles.IncludeMoves)
            {
                embed.AddField(" **__Special Rewards__**", string.IsNullOrEmpty($"{RaidEmbedInfoHelpers.SpecialRewards}") ? "No Rewards To Display" : $"{RaidEmbedInfoHelpers.SpecialRewards}", true);
                RaidEmbedInfoHelpers.SpecialRewards = string.Empty;
            }
            if (!disband && !upnext && !raidstart && Settings.ActiveRaids[RotationCount].DifficultyLevel == 7)
            {
                // Try to get the raid boss mechanics for 7-star raids
                string mechanicsInfo = GetRaidBossMechanics();
                if (!string.IsNullOrEmpty(mechanicsInfo))
                {
                    embed.AddField("**__7★ Raid Mechanics__**", mechanicsInfo, false);
                }
            }
            if (!disband && names is null && !upnext)
            {
                if (code == "Free For All")
                {
                    embed.AddField(
                        Settings.EmbedToggles.IncludeCountdown
                            ? $"**__Raid Starting__**:\n**<t:{DateTimeOffset.Now.ToUnixTimeSeconds() + 160}:R>**"
                            : $"**Waiting in lobby!**",
                        $"**FREE FOR ALL**",
                        true);
                }
                else
                {
                    embed.AddField(
                        Settings.EmbedToggles.IncludeCountdown
                            ? $"**__Raid Starting__**:\n**<t:{DateTimeOffset.Now.ToUnixTimeSeconds() + 160}:R>**"
                            : $"**Waiting in lobby!**",
                        "\u200B",
                        true);
                }
            }
            if (!disband && names is not null && !upnext)
            {
                var players = string.Empty;
                if (names.Count == 0)
                    players = "Our party dipped on us :/";
                else
                {
                    int i = 2;
                    names.ForEach(x =>
                    {
                        players += $"Player {i} - **{x}**\n";
                        i++;
                    });
                }

                embed.AddField($"**Raid #{RaidCount} is starting!**", players);
            }
            if (imageBytes != null)
            {
                embed.ThumbnailUrl = turl;
                embed.WithImageUrl($"attachment://{fileName}");
            }

            // Add raid code information to the embed for new raids that aren't starting yet
            if (!disband && names is null && !upnext && !raidstart && code != "Free For All")
            {
                // Add a field with code information
                embed.AddField("**__Raid Code__**",
                    "React with ✅ to receive the raid code via DM.\n" +
                    "The code will be sent to you privately.", false);
            }

            // Send the embed to all channels and get list of sent messages
            var sentMessages = await EchoUtil.RaidEmbed(imageBytes, fileName, embed);

            // Determine if this is an initial raid announcement with a code
            bool isInitialCodedRaidAnnouncement =
                sentMessages.Count > 0 &&
                code != "Free For All" &&
                names is null &&
                !upnext &&
                !raidstart &&
                !disband;

            if (isInitialCodedRaidAnnouncement)
            {
                var raidInfoDict = new Dictionary<string, string>
                {
                    ["RaidTitle"] = RaidEmbedInfoHelpers.RaidEmbedTitle,
                    ["TeraType"] = RaidEmbedInfoHelpers.RaidSpeciesTeraType,
                    ["TeraIconUrl"] = teraIconUrl,
                    ["ThumbnailUrl"] = turl,
                    ["IsShiny"] = Settings.ActiveRaids[RotationCount].IsShiny.ToString(),
                    ["DifficultyLevel"] = Settings.ActiveRaids[RotationCount].DifficultyLevel.ToString(),
                    ["Level"] = RaidEmbedInfoHelpers.RaidLevel.ToString(),
                    ["Gender"] = RaidEmbedInfoHelpers.RaidSpeciesGender,
                    ["Nature"] = RaidEmbedInfoHelpers.RaidSpeciesNature,
                    ["Ability"] = RaidEmbedInfoHelpers.RaidSpeciesAbility,
                    ["IVs"] = RaidEmbedInfoHelpers.RaidSpeciesIVs,
                    ["Scale"] = $"{RaidEmbedInfoHelpers.ScaleText}({RaidEmbedInfoHelpers.ScaleNumber})",
                    ["Moves"] = RaidEmbedInfoHelpers.Moves,
                    ["ExtraMoves"] = RaidEmbedInfoHelpers.ExtraMoves,
                    ["SpecialRewards"] = RaidEmbedInfoHelpers.SpecialRewards,
                    ["TypeAdvantage"] = typeAdvantage
                };
                if (Settings.ActiveRaids[RotationCount].DifficultyLevel == 7)
                {
                    raidInfoDict["RaidMechanics"] = GetRaidBossMechanics();
                }

                foreach (var sentMessage in sentMessages)
                {
                    try
                    {
                        SharedRaidCodeHandler.AddActiveRaidMessageWithInfoDict(
                            sentMessage.Id,
                            sentMessage.Channel.Id,
                            code,
                            raidInfoDict);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error tracking raid message: {ex.Message}");
                    }
                }
                await SharedRaidCodeHandler.UpdateReactionsOnAllMessages(true, token);
            }
        }

        private string GetRaidBossMechanics()
        {
            if (Settings.ActiveRaids[RotationCount].DifficultyLevel != 7)
                return string.Empty;

            StringBuilder mechanics = new();

            try
            {
                var species = (ushort)Settings.ActiveRaids[RotationCount].Species;
                var form = (byte)Settings.ActiveRaids[RotationCount].SpeciesForm;
                if (RaidBossMechanicsData.TryGetValue((species, form), out var mechanicsInfo))
                {
                    // Shield activation
                    if (mechanicsInfo.ShieldHpTrigger > 0 && mechanicsInfo.ShieldTimeTrigger > 0)
                    {
                        mechanics.AppendLine("**Shield Activation:**");
                        mechanics.AppendLine($"• {mechanicsInfo.ShieldHpTrigger}% HP Remaining");
                        mechanics.AppendLine($"• {mechanicsInfo.ShieldTimeTrigger}% Time Remaining");
                    }
                    if (mechanicsInfo.ExtraActions.Count > 0)
                    {
                        mechanics.AppendLine("**Other Actions:**");
                        var moveNames = GameInfo.GetStrings(1).Move;

                        foreach (var (action, timing, value, moveId) in mechanicsInfo.ExtraActions)
                        {
                            var type = timing == 0 ? "Time" : "HP";

                            switch (action)
                            {
                                case 1: // BOSS_STATUS_RESET
                                    mechanics.AppendLine($"• Resets Raid Boss' Stat Changes at {value}% {type} Remaining");
                                    break;

                                case 2: // PLAYER_STATUS_RESET
                                    mechanics.AppendLine($"• Resets Player's Stat Changes at {value}% {type} Remaining");
                                    break;

                                case 3: // WAZA (Move)
                                    var moveName = moveId < moveNames.Count ? moveNames[moveId] : $"Move #{moveId}";
                                    mechanics.AppendLine($"• Uses {moveName} at {value}% {type} Remaining");
                                    break;

                                case 4: // GEM_COUNT
                                    mechanics.AppendLine($"• Reduces Tera Orb Charge at {value}% {type} Remaining");
                                    break;

                                default:
                                    mechanics.AppendLine($"• Unknown action ({action}) at {value}% {type} Remaining");
                                    break;
                            }
                        }
                    }

                    return mechanics.ToString();
                }
                else
                {
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log($"Error retrieving raid boss mechanics: {ex.Message}");
                // Return empty string in case of error
                return string.Empty;
            }
        }

        private async Task LoadRaidBossMechanics(CancellationToken token)
        {
            try
            {
                // Clear existing data
                RaidBossMechanicsData.Clear();
                var BaseBlockKeyPointer = await SwitchConnection.PointerAll(Offsets.BlockKeyPointer, token).ConfigureAwait(false);
                byte[] deliveryRaidFlatbuffer = await ReadBlockDefault(
                    BaseBlockKeyPointer,
                    RaidCrawler.Core.Structures.Offsets.BCATRaidBinaryLocation,
                    "raid_enemy_array",
                    false,
                    token
                ).ConfigureAwait(false);

                var tableEncounters = FlatBufferConverter.DeserializeFrom<pkNX.Structures.FlatBuffers.Gen9.DeliveryRaidEnemyTableArray>(deliveryRaidFlatbuffer);

                if (tableEncounters?.Table != null)
                {

                    foreach (var entry in tableEncounters.Table)
                    {
                        if (entry.Info?.Difficulty == 7 && entry.Info.BossPokePara != null && entry.Info.BossDesc != null)
                        {
                            // Create mechanics info object
                            var mechanicsInfo = new RaidBossMechanicsInfo
                            {
                                ShieldHpTrigger = (byte)entry.Info.BossDesc.PowerChargeTrigerHp,
                                ShieldTimeTrigger = (byte)entry.Info.BossDesc.PowerChargeTrigerTime
                            };

                            var actions = new[] {
                        entry.Info.BossDesc.ExtraAction1,
                        entry.Info.BossDesc.ExtraAction2,
                        entry.Info.BossDesc.ExtraAction3,
                        entry.Info.BossDesc.ExtraAction4,
                        entry.Info.BossDesc.ExtraAction5,
                        entry.Info.BossDesc.ExtraAction6
                    };

                            foreach (var action in actions)
                            {
                                if (action.Action != 0 && action.Value != 0)
                                {
                                    mechanicsInfo.ExtraActions.Add((action.Action, action.Timing, action.Value, action.Wazano));
                                }
                            }
                            RaidBossMechanicsData[(entry.Info.BossPokePara.DevId, entry.Info.BossPokePara.FormId)] = mechanicsInfo;
                        }
                    }
                }
                else
                {
                    Log("Failed to deserialize raid data for boss mechanics");
                }
            }
            catch (Exception ex)
            {
                Log($"Error loading raid boss mechanics: {ex.Message}");
            }
        }

        private static string CleanEmojiStrings(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            return Regex.Replace(input, @"<:[a-zA-Z0-9_]+:[0-9]+>", "").Trim();
        }

        private const string PUBLIC_KEY = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEArFbz7xXyQtO0j5JfcVW4
lcIO3/+kL0GuNN4GgdZHNLWu6OX4Sv0BypvMOqdOTrGMMj+/v/1tRWamUh1qRSN+
lmRsNLxj5A6kdwZk+UIU2LC6X3Y192FyVAvV/nYFgvdoyUzF1agvaTP7C7g8F3vH
/zbGZdaH/4ZqKfBTU+NebCASaL+z+b7oIyl3j0RKdBAm5MJjYhSwj6j+1DpFbNgj
ALwkMx63fBR0pKs+jJ8DcFrcJR50aVv1jfIAQpPIK5G6Dk/4hmV12Hdu5sSGLl40
5AlAy18QKMi3y3vyvJ4wZnuY+gpsaTsuTlSau6FxpVzxosvv4kh9x1HVaoX2iGSh
7QIDAQAB
-----END PUBLIC KEY-----";

        private static string? EncryptRaidCode(string code)
        {
            try
            {
                using RSA rsa = RSA.Create();
                rsa.ImportFromPem(PUBLIC_KEY);
                byte[] dataToEncrypt = Encoding.UTF8.GetBytes(code);
                byte[] encryptedData = rsa.Encrypt(dataToEncrypt, RSAEncryptionPadding.Pkcs1);
                return Convert.ToBase64String(encryptedData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Encryption error: {ex.Message}");
                return null;
            }
        }

        private async Task CurrentRaidInfo(List<string>? names, string code, bool hatTrick, bool disband, bool upnext, bool raidstart, string? imageUrl, bool lobbyFull, CancellationToken token)
        {
            if (!Settings.RaidSettings.JoinSharedRaidsProgram)
                return;

            string? encryptedCode = null;
            if (!string.IsNullOrEmpty(code) && code != "FREE FOR ALL" && code != "IJ0LTU")
            {
                encryptedCode = EncryptRaidCode(code);
            }

            var raidInfo = new
            {
                RaidEmbedTitle = CleanEmojiStrings(RaidEmbedInfoHelpers.RaidEmbedTitle),
                RaidSpecies = RaidEmbedInfoHelpers.RaidSpecies.ToString(),
                RaidEmbedInfoHelpers.RaidSpeciesForm,
                RaidSpeciesGender = CleanEmojiStrings(RaidEmbedInfoHelpers.RaidSpeciesGender),
                RaidEmbedInfoHelpers.RaidLevel,
                RaidEmbedInfoHelpers.RaidSpeciesIVs,
                RaidEmbedInfoHelpers.RaidSpeciesAbility,
                RaidEmbedInfoHelpers.RaidSpeciesNature,
                RaidEmbedInfoHelpers.RaidSpeciesTeraType,
                Moves = CleanEmojiStrings(RaidEmbedInfoHelpers.Moves),
                ExtraMoves = CleanEmojiStrings(RaidEmbedInfoHelpers.ExtraMoves),
                RaidEmbedInfoHelpers.ScaleText,
                SpecialRewards = CleanEmojiStrings(RaidEmbedInfoHelpers.SpecialRewards),
                RaidEmbedInfoHelpers.ScaleNumber,
                Names = names,
                Code_encrypted = encryptedCode,
                HatTrick = hatTrick,
                Disband = disband,
                UpNext = upnext,
                RaidStart = raidstart,
                ImageUrl = imageUrl,
                LobbyFull = lobbyFull
            };

            try
            {
                var json = JsonConvert.SerializeObject(raidInfo, Formatting.Indented);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string raidinfo = Encoding.UTF8.GetString(Convert.FromBase64String("aHR0cHM6Ly9nZW5wa20uY29tL3JhaWRzL3JhaWRfYXBpLnBocA=="));
                var response = await httpClient.PostAsync(raidinfo, content, token);
            }
            catch
            {
            }
        }

        private async Task<bool> ConnectToOnline(PokeRaidHubConfig config, CancellationToken token)
        {
            int attemptCount = 0;
            const int maxAttempt = 5;
            const int waitTime = 10; // time in minutes to wait after max attempts

            while (true) // Loop until a successful connection is made or the task is canceled
            {
                if (token.IsCancellationRequested)
                {
                    Log("Connection attempt canceled.");
                    break;
                }
                try
                {
                    if (await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
                    {
                        Log("Connection established successfully.");
                        break; // Exit the loop if connected successfully
                    }

                    if (attemptCount >= maxAttempt)
                    {
                        Log($"Failed to connect after {maxAttempt} attempts. Assuming a softban. Initiating wait for {waitTime} minutes before retrying.");
                        // Log details about sending an embed message
                        Log("Sending an embed message to notify about technical difficulties.");
                        EmbedBuilder embed = new()
                        {
                            Title = "Experiencing Technical Difficulties",
                            Description = "The bot is experiencing issues connecting online. Please stand by as we try to resolve the issue.",
                            Color = Discord.Color.Red,
                            ThumbnailUrl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/x.png"
                        };
                        EchoUtil.RaidEmbed(null, "", embed);
                        // Waiting process
                        await Click(B, 0_500, token).ConfigureAwait(false);
                        await Click(B, 0_500, token).ConfigureAwait(false);
                        Log($"Waiting for {waitTime} minutes before attempting to reconnect.");
                        await Task.Delay(TimeSpan.FromMinutes(waitTime), token).ConfigureAwait(false);
                        Log("Attempting to reopen the game.");
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        attemptCount = 0; // Reset attempt count
                    }

                    attemptCount++;
                    Log($"Attempt {attemptCount} of {maxAttempt}: Trying to connect online...");

                    // Connection attempt logic
                    await Click(X, 3_000, token).ConfigureAwait(false);
                    await Click(L, 5_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);

                    // Wait a bit before rechecking the connection status
                    await Task.Delay(5000, token).ConfigureAwait(false); // Wait 5 seconds before rechecking

                    if (attemptCount < maxAttempt)
                    {
                        Log("Rechecking the online connection status...");
                        // Wait and recheck logic
                        await Click(B, 0_500, token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Exception occurred during connection attempt: {ex.Message}");
                    // Handle exceptions, like connectivity issues here

                    if (attemptCount >= maxAttempt)
                    {
                        Log($"Failed to connect after {maxAttempt} attempts due to exception. Waiting for {waitTime} minutes before retrying.");
                        await Task.Delay(TimeSpan.FromMinutes(waitTime), token).ConfigureAwait(false);
                        Log("Attempting to reopen the game.");
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        attemptCount = 0;
                    }
                }
            }

            // Final steps after connection is established
            await Task.Delay(3_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Task.Delay(3_000, token).ConfigureAwait(false);

            return true;
        }

        public async Task StartGameRaid(PokeRaidHubConfig config, CancellationToken token)
        {
            // First, check if the time rollback feature is enabled
            if (Settings.RaidSettings.EnableTimeRollBack && DateTime.Now - TimeForRollBackCheck >= TimeSpan.FromHours(5))
            {
                Log("Rolling Time back 5 hours.");
                // Call the RollBackTime function
                await RollBackTime(token).ConfigureAwait(false);
                await Click(A, 1_500, token).ConfigureAwait(false);
                // Reset TimeForRollBackCheck
                TimeForRollBackCheck = DateTime.Now;
            }

            var timing = config.Timings;
            var loadPro = timing.RestartGameSettings.ProfileSelectSettings.ProfileSelectionRequired ? timing.RestartGameSettings.ProfileSelectSettings.ExtraTimeLoadProfile : 0;

            await Click(A, 1_000 + loadPro, token).ConfigureAwait(false); // Initial "A" Press to start the Game + a delay if needed for profiles to load

            // Really Shouldn't keep this but we will for now
            if (timing.RestartGameSettings.AvoidSystemUpdate)
            {
                await Task.Delay(0_500, token).ConfigureAwait(false); // Delay bc why not
                await Click(DUP, 0_600, token).ConfigureAwait(false); // Highlight "Start Software"
                await Click(A, 1_000 + loadPro, token).ConfigureAwait(false); // Select "Sttart Software" + delay if Profile selection is needed
            }

            // Only send extra Presses if we need to
            if (timing.RestartGameSettings.ProfileSelectSettings.ProfileSelectionRequired)
            {
                await Click(A, 1_000, token).ConfigureAwait(false); // Now we are on the Profile Screen
                await Click(A, 1_000, token).ConfigureAwait(false); // Select the profile
            }

            // Digital game copies take longer to load
            if (timing.RestartGameSettings.CheckGameDelay)
            {
                await Task.Delay(2_000 + timing.RestartGameSettings.ExtraTimeCheckGame, token).ConfigureAwait(false);
            }

            // If they have DLC on the system and can't use it, requires an UP + A to start the game.
            if (timing.RestartGameSettings.CheckForDLC)
            {
                await Click(DUP, 0_600, token).ConfigureAwait(false);
                await Click(A, 0_600, token).ConfigureAwait(false);
            }

            Log("Restarting the game!");

            await Task.Delay(19_000 + timing.RestartGameSettings.ExtraTimeLoadGame, token).ConfigureAwait(false); // Wait for the game to load before writing to memory
            await InitializeRaidBlockPointers(token);

            if (Settings.ActiveRaids.Count > 1)
            {
                Log($"Rotation for {Settings.ActiveRaids[RotationCount].Species} has been found.");
                Log($"Checking Current Game Progress Level.");

                var desiredProgress = Settings.ActiveRaids[RotationCount].StoryProgress;
                if (GameProgress != (GameProgress)desiredProgress)
                {
                    Log($"Updating game progress level to: {desiredProgress}");
                    await WriteProgressLive((GameProgress)desiredProgress).ConfigureAwait(false);
                    GameProgress = (GameProgress)desiredProgress;
                    Log($"Done.");
                }
                else
                {
                    Log($"Game progress level is already {GameProgress}. No update needed.");
                }

                RaidDataBlocks.AdjustKWildSpawnsEnabledType(Settings.RaidSettings.DisableOverworldSpawns);

                if (Settings.RaidSettings.DisableOverworldSpawns)
                {
                    Log("Checking current state of Overworld Spawns.");
                    if (currentSpawnsEnabled.HasValue)
                    {
                        Log($"Current Overworld Spawns state: {currentSpawnsEnabled.Value}");

                        if (currentSpawnsEnabled.Value)
                        {
                            Log("Overworld Spawns are enabled, attempting to disable.");
                            await WriteBlock(false, RaidDataBlocks.KWildSpawnsEnabled, token, currentSpawnsEnabled);
                            currentSpawnsEnabled = false;
                            Log("Overworld Spawns successfully disabled.");
                        }
                        else
                        {
                            Log("Overworld Spawns are already disabled, no action taken.");
                        }
                    }
                }
                else // When Settings.DisableOverworldSpawns is false, ensure Overworld spawns are enabled
                {
                    Log("Settings indicate Overworld Spawns should be enabled. Checking current state.");
                    Log($"Current Overworld Spawns state: {currentSpawnsEnabled.Value}");

                    if (!currentSpawnsEnabled.Value)
                    {
                        Log("Overworld Spawns are disabled, attempting to enable.");
                        await WriteBlock(true, RaidDataBlocks.KWildSpawnsEnabled, token, currentSpawnsEnabled);
                        currentSpawnsEnabled = true;
                        Log("Overworld Spawns successfully enabled.");
                    }
                    else
                    {
                        Log("Overworld Spawns are already enabled, no action needed.");
                    }
                }
                Log($"Attempting to override seed for {Settings.ActiveRaids[RotationCount].Species}.");
                await OverrideSeedIndex(SeedIndexToReplace, token).ConfigureAwait(false);
                Log("Seed override completed.");

                await Task.Delay(2_000, token).ConfigureAwait(false);
                await LogPlayerLocation(token); // Teleports user to closest Active Den
                await Task.Delay(2_000, token).ConfigureAwait(false);
            }

            for (int i = 0; i < 8; i++)
                await Click(A, 1_000, token).ConfigureAwait(false);

            var timeout = TimeSpan.FromMinutes(1);
            var delayTask = Task.Delay(timeout, token);

            while (true)
            {
                var isOnOverworldTitleTask = IsOnOverworldTitle(token);

                // Wait for either the delay task or the isOnOverworldTitle task to complete
                var completedTask = await Task.WhenAny(isOnOverworldTitleTask, delayTask).ConfigureAwait(false);

                if (completedTask == isOnOverworldTitleTask)
                {
                    // If the task that completed is the isOnOverworldTitleTask, check its result
                    if (await isOnOverworldTitleTask.ConfigureAwait(false))
                    {
                        // If we are on the overworld title, exit the loop
                        break;
                    }
                }
                else
                {
                    // If the delayTask completed first, initiate the reboot protocol
                    Log("Still not in the game, initiating reboot protocol!");
                    await PerformRebootAndReset(token);
                    return;
                }

                // Add a small delay before the next check to avoid tight looping
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }

            await Task.Delay(5_000 + timing.ExtraTimeLoadOverworld, token).ConfigureAwait(false);
            Log("Back in the overworld!");
            LostRaid = 0;
            if (Settings.RaidSettings.MysteryRaids)
            {
                // Count the number of existing Mystery Shiny Raids
                int mysteryRaidCount = Settings.ActiveRaids.Count(raid => raid.Title.Contains("Mystery Shiny Raid"));
                // Only create and add a new Mystery Shiny Raid if there are two or fewer in the list
                if (mysteryRaidCount <= 1)
                {
                    await CreateMysteryRaidAsync();
                }
            }
        }

        private static Dictionary<string, float[]> LoadDenLocations(string resourceName)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(stream);
            string json = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<Dictionary<string, float[]>>(json);
        }

        private static string FindNearestLocation((float, float, float) playerLocation, Dictionary<string, float[]> denLocations)
        {
            string? nearestDen = null;
            float minDistance = float.MaxValue;

            foreach (var den in denLocations)
            {
                var denLocation = den.Value;
                float distance = CalculateDistance(playerLocation, (denLocation[0], denLocation[1], denLocation[2]));

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestDen = den.Key;
                }
            }

            return nearestDen;
        }

        private static float CalculateDistance((float, float, float) loc1, (float, float, float) loc2)
        {
            return (float)Math.Sqrt(
                Math.Pow(loc1.Item1 - loc2.Item1, 2) +
                Math.Pow(loc1.Item2 - loc2.Item2, 2) +
                Math.Pow(loc1.Item3 - loc2.Item3, 2));
        }

        private async Task<(float, float, float)> GetPlayersLocation(CancellationToken token)
        {
            // Read the data block (automatically handles encryption)
            var data = await ReadBlock(RaidDataBlocks.KCoordinates, token) as byte[];

            // Extract coordinates
            float x = BitConverter.ToSingle(data, 0);
            float y = BitConverter.ToSingle(data, 4);
            float z = BitConverter.ToSingle(data, 8);

            return (x, y, z);
        }

        public async Task TeleportToDen(float x, float y, float z, CancellationToken token)
        {
            const float offset = 1.8f;
            x += offset;

            // Convert coordinates to byte array
            byte[] xBytes = BitConverter.GetBytes(x);
            byte[] yBytes = BitConverter.GetBytes(y);
            byte[] zBytes = BitConverter.GetBytes(z);
            byte[] coordinatesData = new byte[xBytes.Length + yBytes.Length + zBytes.Length];
            Array.Copy(xBytes, 0, coordinatesData, 0, xBytes.Length);
            Array.Copy(yBytes, 0, coordinatesData, xBytes.Length, yBytes.Length);
            Array.Copy(zBytes, 0, coordinatesData, xBytes.Length + yBytes.Length, zBytes.Length);

            // Write the coordinates
            var teleportBlock = RaidDataBlocks.KCoordinates;
            teleportBlock.Size = coordinatesData.Length;
            var currentCoordinateData = await ReadBlock(teleportBlock, token) as byte[];
            _ = await WriteEncryptedBlockSafe(teleportBlock, currentCoordinateData, coordinatesData, token);

            // Set rotation to face North
            float northRX = 0.0f;
            float northRY = -0.63828725f;
            float northRZ = 0.0f;
            float northRW = 0.7697983f;

            // Convert rotation to byte array
            byte[] rotationData = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(northRX), 0, rotationData, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(northRY), 0, rotationData, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(northRZ), 0, rotationData, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(northRW), 0, rotationData, 12, 4);

            // Write the rotation
            var rotationBlock = RaidDataBlocks.KPlayerRotation;
            rotationBlock.Size = rotationData.Length;
            var currentRotationData = await ReadBlock(rotationBlock, token) as byte[];
            _ = await WriteEncryptedBlockSafe(rotationBlock, currentRotationData, rotationData, token);
        }

        private async Task<List<(uint Area, uint LotteryGroup, uint Den, uint Seed, uint Flags, bool IsEvent)>> ExtractRaidInfo(TeraRaidMapParent mapType, CancellationToken token)
        {
            byte[] raidData = mapType switch
            {
                TeraRaidMapParent.Paldea => await ReadPaldeaRaids(token),
                TeraRaidMapParent.Kitakami => await ReadKitakamiRaids(token),
                TeraRaidMapParent.Blueberry => await ReadBlueberryRaids(token),
                _ => throw new InvalidOperationException("Invalid region"),
            };
            var raids = new List<(uint Area, uint LotteryGroup, uint Den, uint Seed, uint Flags, bool IsEvent)>();
            for (int i = 0; i < raidData.Length; i += Raid.SIZE)
            {
                var raid = new Raid(raidData.AsSpan()[i..(i + Raid.SIZE)]);
                if (raid.IsValid)
                {
                    raids.Add((raid.Area, raid.LotteryGroup, raid.Den, raid.Seed, raid.Flags, raid.IsEvent));
                }
            }

            return raids;
        }

        private async Task LogPlayerLocation(CancellationToken token)
        {
            var playerLocation = await GetPlayersLocation(token);

            // Load den locations for all regions
            var blueberryLocations = LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_blueberry.json");
            var kitakamiLocations = LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_kitakami.json");
            var baseLocations = LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_base.json");

            // Find the nearest location for each set and keep track of the overall nearest
            var nearestDen = new Dictionary<string, string>
            {
                { "Blueberry", FindNearestLocation(playerLocation, blueberryLocations) },
                { "Kitakami", FindNearestLocation(playerLocation, kitakamiLocations) },
                { "Paldea", FindNearestLocation(playerLocation, baseLocations) }
            };

            var overallNearest = nearestDen.Select(kv =>
            {
                var denLocationArray = kv.Key switch
                {
                    "Blueberry" => blueberryLocations[kv.Value],
                    "Kitakami" => kitakamiLocations[kv.Value],
                    "Paldea" => baseLocations[kv.Value],
                    _ => throw new InvalidOperationException("Invalid region")
                };

                var denLocationTuple = (denLocationArray[0], denLocationArray[1], denLocationArray[2]);
                return new { Region = kv.Key, DenIdentifier = kv.Value, Distance = CalculateDistance(playerLocation, denLocationTuple) };
            })
            .OrderBy(d => d.Distance)
            .First();

            TeraRaidMapParent mapType = overallNearest.Region switch
            {
                "Blueberry" => TeraRaidMapParent.Blueberry,
                "Kitakami" => TeraRaidMapParent.Kitakami,
                "Paldea" => TeraRaidMapParent.Paldea,
                _ => throw new InvalidOperationException("Invalid region")
            };

            var activeRaids = await GetActiveRaidLocations(mapType, token);

            // Find the nearest active raid, if any
            var nearestActiveRaid = activeRaids
                .Select(raid => new { Raid = raid, Distance = CalculateDistance(playerLocation, (raid.Coordinates[0], raid.Coordinates[1], raid.Coordinates[2])) })
                .OrderBy(raid => raid.Distance)
                .FirstOrDefault();

            if (nearestActiveRaid != null)
            {
                // Check if the player is already at the nearest active den
                float distanceToNearestActiveDen = CalculateDistance(playerLocation, (nearestActiveRaid.Raid.Coordinates[0], nearestActiveRaid.Raid.Coordinates[1], nearestActiveRaid.Raid.Coordinates[2]));

                // Define a threshold for how close the player needs to be to be considered "at" the den
                const float threshold = 2.0f;

                uint denSeed = nearestActiveRaid.Raid.Seed;
                string hexDenSeed = denSeed.ToString("X8");
                denHexSeed = hexDenSeed;
                Log($"Seed: {hexDenSeed} Nearest active den: {nearestActiveRaid.Raid.DenIdentifier}");

                bool onOverworld = await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false);
                if (!onOverworld)
                {
                    if (distanceToNearestActiveDen > threshold)
                    {
                        uint seedOfNearestDen = nearestActiveRaid.Raid.Seed;

                        // Player is not at the den, so teleport
                        await TeleportToDen(nearestActiveRaid.Raid.Coordinates[0], nearestActiveRaid.Raid.Coordinates[1], nearestActiveRaid.Raid.Coordinates[2], token);
                        Log($"Teleported to nearest active den: {nearestActiveRaid.Raid.DenIdentifier} Seed: {nearestActiveRaid.Raid.Seed:X8} in {overallNearest.Region}.");
                    }
                }
                else
                {
                    // Player is already at the den
                    //  Log($"Already at the nearest active den: {nearestActiveRaid.Raid.DenIdentifier}");
                }
            }
            else
            {
                Log($"No active dens found in {overallNearest.Region}");
            }
            bool IsKitakami = overallNearest.Region == "Kitakami";
            bool IsBlueberry = overallNearest.Region == "Blueberry";
        }

        private static bool IsRaidActive((uint Area, uint LotteryGroup, uint Den) raid)
        {
            return true;
        }

        private async Task<List<(string DenIdentifier, float[] Coordinates, int Index, uint Seed, uint Flags, bool IsEvent)>> GetActiveRaidLocations(TeraRaidMapParent mapType, CancellationToken token)
        {
            var raidInfo = await ExtractRaidInfo(mapType, token);
            Dictionary<string, float[]> denLocations = mapType switch
            {
                TeraRaidMapParent.Paldea => LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_base.json"),
                TeraRaidMapParent.Kitakami => LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_kitakami.json"),
                TeraRaidMapParent.Blueberry => LoadDenLocations("SysBot.Pokemon.SV.BotRaid.DenLocations.den_locations_blueberry.json"),
                _ => throw new InvalidOperationException("Invalid region")
            };

            var activeRaids = new List<(string DenIdentifier, float[] Coordinates, int Index, uint Seed, uint Flags, bool IsEvent)>();
            int index = 0;
            foreach (var (Area, LotteryGroup, Den, Seed, Flags, IsEvent) in raidInfo)
            {
                string raidIdentifier = $"{Area}-{LotteryGroup}-{Den}";
                if (denLocations.TryGetValue(raidIdentifier, out var coordinates) && IsRaidActive((Area, LotteryGroup, Den)))
                {
                    activeRaids.Add((raidIdentifier, coordinates, index, Seed, Flags, IsEvent));
                }
                index++;
            }

            return activeRaids;
        }

        private async Task WriteProgressLive(GameProgress progress)
        {
            if (Connection is null)
                return;

            if (progress >= GameProgress.Unlocked3Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty3, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked4Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty4, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked5Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty5, CancellationToken.None, toexpect);
            }

            if (progress >= GameProgress.Unlocked6Stars)
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None);
                await WriteBlock(true, RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None, toexpect);
            }
            else
            {
                var toexpect = (bool?)await ReadBlock(RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None);
                await WriteBlock(false, RaidDataBlocks.KUnlockedRaidDifficulty6, CancellationToken.None, toexpect);
            }
        }

        private async Task SkipRaidOnLosses(CancellationToken token)
        {
            Log($"We had {Settings.LobbyOptions.SkipRaidLimit} lost/empty raids.. Moving on!");

            await SanitizeRotationCount(token).ConfigureAwait(false);
            // Prepare and send an embed to inform users
            await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
            await CloseGame(Hub.Config, token).ConfigureAwait(false);
            await StartGameRaid(Hub.Config, token).ConfigureAwait(false);
        }

        private static string AltPokeImg(PKM pkm)
        {
            string pkmform = string.Empty;
            if (pkm.Form != 0)
                pkmform = $"-{pkm.Form}";

            return _ = $"https://raw.githubusercontent.com/zyro670/PokeTextures/main/Placeholder_Sprites/scaled_up_sprites/Shiny/AlternateArt/" + $"{pkm.Species}{pkmform}" + ".png";
        }

        private async Task ReadRaids(CancellationToken token)
        {
            Log("Getting Raid data...");
            await InitializeRaidBlockPointers(token);
            if (firstRun)
            {
                await LogPlayerLocation(token); // Get seed from current den for processing
            }
            string game = await DetermineGame(token);
            container = new(game);
            container.SetGame(game);

            await SetStoryAndEventProgress(token);

            var allRaids = new List<Raid>();
            var allEncounters = new List<ITeraRaid>();
            var allRewards = new List<List<(int, int, int)>>();

            if (IsBlueberry)
            {
                // Process only Blueberry raids
                var dataB = await ReadBlueberryRaids(token);
                Log("Reading Blueberry Raids...");
                var (blueberryRaids, blueberryEncounters, blueberryRewards) = await ProcessRaids(dataB, TeraRaidMapParent.Blueberry, token);
                allRaids.AddRange(blueberryRaids);
                allEncounters.AddRange(blueberryEncounters);
                allRewards.AddRange(blueberryRewards);
            }
            else if (IsKitakami)
            {
                // Process only Kitakami raids
                var dataK = await ReadKitakamiRaids(token);
                Log("Reading Kitakami Raids...");
                var (kitakamiRaids, kitakamiEncounters, kitakamiRewards) = await ProcessRaids(dataK, TeraRaidMapParent.Kitakami, token);
                allRaids.AddRange(kitakamiRaids);
                allEncounters.AddRange(kitakamiEncounters);
                allRewards.AddRange(kitakamiRewards);
            }
            else
            {
                // Default to processing Paldea raids
                var dataP = await ReadPaldeaRaids(token);
                Log("Reading Paldea Raids...");
                var (paldeaRaids, paldeaEncounters, paldeaRewards) = await ProcessRaids(dataP, TeraRaidMapParent.Paldea, token);
                allRaids.AddRange(paldeaRaids);
                allEncounters.AddRange(paldeaEncounters);
                allRewards.AddRange(paldeaRewards);
            }

            // Set combined data to container and process all raids
            container.SetRaids(allRaids);
            container.SetEncounters(allEncounters);
            container.SetRewards(allRewards);
            await ProcessAllRaids(token);
        }

        private async Task<(List<Raid>, List<ITeraRaid>, List<List<(int, int, int)>>)> ProcessRaids(byte[] data, TeraRaidMapParent mapType, CancellationToken token)
        {
            int delivery, enc;
            var tempContainer = new RaidContainer(container.Game);
            tempContainer.SetGame(container.Game);

            Log("Reading event raid status...");
            // Read event raids into tempContainer
            var BaseBlockKeyPointer = await SwitchConnection.PointerAll(Offsets.BlockKeyPointer, token).ConfigureAwait(false);
            await ReadEventRaids(BaseBlockKeyPointer, tempContainer, token).ConfigureAwait(false);
            await ReadEventRaids(BaseBlockKeyPointer, container, token).ConfigureAwait(false);

            (delivery, enc) = tempContainer.ReadAllRaids(data, StoryProgress, EventProgress, 0, mapType);

            var raidsList = tempContainer.Raids.ToList();
            var encountersList = tempContainer.Encounters.ToList();
            var rewardsList = tempContainer.Rewards.Select(r => r.ToList()).ToList();

            return (raidsList, encountersList, rewardsList);
        }

        private async Task InitializeRaidBlockPointers(CancellationToken token)
        {
            RaidBlockPointerP = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerP, token).ConfigureAwait(false);
            RaidBlockPointerK = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerK, token).ConfigureAwait(false);
            RaidBlockPointerB = await SwitchConnection.PointerAll(Offsets.RaidBlockPointerB, token).ConfigureAwait(false);
        }

        private async Task<string> DetermineGame(CancellationToken token)
        {
            string id = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
            return id switch
            {
                RaidCrawler.Core.Structures.Offsets.ScarletID => "Scarlet",
                RaidCrawler.Core.Structures.Offsets.VioletID => "Violet",
                _ => "",
            };
        }

        private async Task SetStoryAndEventProgress(CancellationToken token)
        {
            var BaseBlockKeyPointer = await SwitchConnection.PointerAll(Offsets.BlockKeyPointer, token).ConfigureAwait(false);
            StoryProgress = await GetStoryProgress(BaseBlockKeyPointer, token).ConfigureAwait(false);
            EventProgress = Math.Min(StoryProgress, 3);
        }

        private async Task<byte[]> ReadPaldeaRaids(CancellationToken token)
        {
            var dataP = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP + RaidBlock.HEADER_SIZE, (int)RaidBlock.SIZE_BASE, token).ConfigureAwait(false);
            return dataP;
        }

        private async Task<byte[]> ReadKitakamiRaids(CancellationToken token)
        {
            var dataK = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK, (int)RaidBlock.SIZE_KITAKAMI, token).ConfigureAwait(false);
            return dataK;
        }

        private async Task<byte[]> ReadBlueberryRaids(CancellationToken token)
        {
            var dataB = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerB, (int)RaidBlock.SIZE_BLUEBERRY, token).ConfigureAwait(false);
            return dataB;
        }

        private static (List<int> distGroupIDs, List<int> mightGroupIDs) GetPossibleGroups(RaidContainer container)
        {
            List<int> distGroupIDs = [];
            List<int> mightGroupIDs = [];

            if (container.DistTeraRaids != null)
            {
                foreach (TeraDistribution e in container.DistTeraRaids)
                {
                    if (TeraDistribution.AvailableInGame(e.Entity, container.Game) && !distGroupIDs.Contains(e.DeliveryGroupID))
                        distGroupIDs.Add(e.DeliveryGroupID);
                }
            }

            if (container.MightTeraRaids != null)
            {
                foreach (TeraMight e in container.MightTeraRaids)
                {
                    if (TeraMight.AvailableInGame(e.Entity, container.Game) && !mightGroupIDs.Contains(e.DeliveryGroupID))
                        mightGroupIDs.Add(e.DeliveryGroupID);
                }
            }

            return (distGroupIDs, mightGroupIDs);
        }

        private async Task<List<(uint Area, uint LotteryGroup, uint Den, uint Seed, uint Flags, bool IsEvent)>> ExtractPaldeaRaidInfo(CancellationToken token)
        {
            byte[] raidData = await ReadPaldeaRaids(token);
            var activeRaids = new List<(uint Area, uint LotteryGroup, uint Den, uint Seed, uint Flags, bool IsEvent)>();

            for (int i = 0; i < raidData.Length; i += Raid.SIZE)
            {
                var raid = new Raid(raidData.AsSpan()[i..(i + Raid.SIZE)]);
                if (raid.IsValid && IsRaidActive((raid.Area, raid.LotteryGroup, raid.Den)))
                {
                    activeRaids.Add((raid.Area, raid.LotteryGroup, raid.Den, raid.Seed, raid.Flags, raid.IsEvent));
                }
            }

            return activeRaids;
        }

        private async Task ProcessAllRaids(CancellationToken token)
        {
            var allRaids = container.Raids;
            var allEncounters = container.Encounters;
            var allRewards = container.Rewards;
            uint denHexSeedUInt;
            denHexSeedUInt = uint.Parse(denHexSeed, NumberStyles.AllowHexSpecifier);
            await FindSeedIndexInRaids(denHexSeedUInt, token);
            var raidInfoList = await ExtractPaldeaRaidInfo(token);
            bool newEventSpeciesFound = false;
            var (distGroupIDs, mightGroupIDs) = GetPossibleGroups(container);

            int raidsToCheck = Math.Min(5, allRaids.Count);

            if (!IsKitakami || !IsBlueberry)
            {
                // check if new event species is found
                for (int i = 0; i < raidsToCheck; i++)
                {
                    var raid = allRaids[i];
                    var encounter = allEncounters[i];
                    bool isEventRaid = raid.Flags == 2 || raid.Flags == 3;

                    if (isEventRaid)
                    {
                        string speciesName = SpeciesName.GetSpeciesName(encounter.Species, 2);
                        if (!SpeciesToGroupIDMap.ContainsKey(speciesName))
                        {
                            newEventSpeciesFound = true;
                            SpeciesToGroupIDMap.Clear(); // Clear the map as we've found a new event species
                            break; // No need to check further
                        }
                    }
                }
            }

            for (int i = 0; i < allRaids.Count; i++)
            {
                if (newEventSpeciesFound)
                {
                    // stuff for paldea events
                    var raid = allRaids[i];
                    var encounter1 = allEncounters[i];
                    bool isDistributionRaid = raid.Flags == 2;
                    bool isMightRaid = raid.Flags == 3;
                    var (Area, LotteryGroup, Den, Seed, Flags, IsEvent) = raidInfoList.FirstOrDefault(r =>
                    r.Seed == raid.Seed &&
                    r.Flags == raid.Flags &&
                    r.Area == raid.Area &&
                    r.LotteryGroup == raid.LotteryGroup &&
                    r.Den == raid.Den);

                    string denIdentifier = $"{Area}-{LotteryGroup}-{Den}";

                    if (isDistributionRaid || isMightRaid)
                    {
                        string speciesName = SpeciesName.GetSpeciesName(encounter1.Species, 2);
                        string speciesKey = string.Join("", speciesName.Split(' '));
                        int groupID = -1;

                        if (isDistributionRaid)
                        {
                            var distRaid = container.DistTeraRaids.FirstOrDefault(d => d.Species == encounter1.Species && d.Form == encounter1.Form);
                            if (distRaid != null)
                            {
                                groupID = distRaid.DeliveryGroupID;
                            }
                        }
                        else if (isMightRaid)
                        {
                            var mightRaid = container.MightTeraRaids.FirstOrDefault(m => m.Species == encounter1.Species && m.Form == encounter1.Form);
                            if (mightRaid != null)
                            {
                                groupID = mightRaid.DeliveryGroupID;
                            }
                        }

                        if (groupID != -1)
                        {
                            if (!SpeciesToGroupIDMap.ContainsKey(speciesKey))
                            {
                                SpeciesToGroupIDMap[speciesKey] = [(groupID, i, denIdentifier)];
                            }
                            else
                            {
                                SpeciesToGroupIDMap[speciesKey].Add((groupID, i, denIdentifier));
                            }
                        }
                    }
                }

                var (pk, seed) = IsSeedReturned(allEncounters[i], allRaids[i]);

                for (int a = 0; a < Settings.ActiveRaids.Count; a++)
                {
                    uint set;
                    try
                    {
                        set = uint.Parse(Settings.ActiveRaids[a].Seed, NumberStyles.AllowHexSpecifier);
                    }
                    catch (FormatException)
                    {
                        Log($"Invalid seed format detected. Removing {Settings.ActiveRaids[a].Seed} from list.");
                        Settings.ActiveRaids.RemoveAt(a);
                        a--;  // Decrement the index so that it does not skip the next element.
                        continue;  // Skip to the next iteration.
                    }
                    if (seed == set)
                    {
                        // Species and Form
                        RaidEmbedInfoHelpers.RaidSpecies = (Species)allEncounters[i].Species;
                        RaidEmbedInfoHelpers.RaidSpeciesForm = allEncounters[i].Form;

                        // Update Species and SpeciesForm in ActiveRaids
                        if (!Settings.ActiveRaids[a].ForceSpecificSpecies)
                        {
                            Settings.ActiveRaids[a].Species = (Species)allEncounters[i].Species;
                            Settings.ActiveRaids[a].SpeciesForm = allEncounters[i].Form;
                        }

                        // Encounter Info
                        int raid_delivery_group_id = (int)Settings.ActiveRaids[a].GroupID;
                        var encounter = allRaids[i].GetTeraEncounter(container, allRaids[i].IsEvent ? 3 : StoryProgress, raid_delivery_group_id);
                        if (encounter != null)
                        {
                            RaidEmbedInfoHelpers.RaidLevel = encounter.Level;
                        }
                        else
                        {
                            RaidEmbedInfoHelpers.RaidLevel = 75;
                        }

                        // Star Rating
                        var stars = allRaids[i].IsEvent ? allEncounters[i].Stars : allRaids[i].GetStarCount(allRaids[i].Difficulty, StoryProgress, allRaids[i].IsBlack);

                        // Raid Title
                        var pkinfo = RaidExtensions<PK9>.GetRaidPrintName(pk);
                        var titlePrefix = allRaids[i].IsShiny ? "Shiny" : "";
                        RaidEmbedInfoHelpers.RaidEmbedTitle = $"{stars} ★ {titlePrefix} {(Species)allEncounters[i].Species}{pkinfo}";

                        // Gender
                        var maleEmoji = Settings.EmbedToggles.MaleEmoji.EmojiString;
                        var femaleEmoji = Settings.EmbedToggles.FemaleEmoji.EmojiString;
                        RaidEmbedInfoHelpers.RaidSpeciesGender = pk.Gender switch
                        {
                            0 when !string.IsNullOrEmpty(maleEmoji) => $"{maleEmoji} Male",
                            1 when !string.IsNullOrEmpty(femaleEmoji) => $"{femaleEmoji} Female",
                            _ => pk.Gender == 0 ? "Male" : pk.Gender == 1 ? "Female" : "Genderless"
                        };

                        // Nature
                        RaidEmbedInfoHelpers.RaidSpeciesNature = GameInfo.Strings.Natures[(int)pk.Nature];

                        // Ability
                        RaidEmbedInfoHelpers.RaidSpeciesAbility = $"{(Ability)pk.Ability}";

                        // IVs
                        RaidEmbedInfoHelpers.RaidSpeciesIVs = $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}";

                        // Tera Type
                        RaidEmbedInfoHelpers.RaidSpeciesTeraType = $"{(MoveType)allRaids[i].GetTeraType(encounter)}";

                        // Moves
                        var strings = GameInfo.GetStrings(1);
                        var moves = new ushort[4] { allEncounters[i].Move1, allEncounters[i].Move2, allEncounters[i].Move3, allEncounters[i].Move4 };
                        var moveNames = new List<string>();
                        var useTypeEmojis = Settings.EmbedToggles.MoveTypeEmojis;
                        var typeEmojis = Settings.EmbedToggles.CustomTypeEmojis
                           .Where(e => !string.IsNullOrEmpty(e.EmojiCode))
                           .ToDictionary(
                               e => e.MoveType,
                               e => $"{e.EmojiCode}"
                           );

                        for (int j = 0; j < moves.Length; j++)
                        {
                            if (moves[j] != 0)
                            {
                                string moveName = strings.Move[moves[j]];
                                byte moveTypeId = MoveInfo.GetType(moves[j], pk.Context);
                                MoveType moveType = (MoveType)moveTypeId;

                                if (useTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
                                {
                                    moveNames.Add($"{moveEmoji} {moveName}");
                                }
                                else
                                {
                                    moveNames.Add($"\\- {moveName}");
                                }
                            }
                        }
                        RaidEmbedInfoHelpers.Moves = string.Join("\n", moveNames);

                        // Extra Moves
                        var extraMoveNames = new List<string>();
                        if (allEncounters[i].ExtraMoves.Length != 0)
                        {
                            for (int j = 0; j < allEncounters[i].ExtraMoves.Length; j++)
                            {
                                if (allEncounters[i].ExtraMoves[j] != 0)
                                {
                                    string moveName = strings.Move[allEncounters[i].ExtraMoves[j]];
                                    byte moveTypeId = MoveInfo.GetType(allEncounters[i].ExtraMoves[j], pk.Context);
                                    MoveType moveType = (MoveType)moveTypeId;

                                    if (useTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
                                    {
                                        extraMoveNames.Add($"{moveEmoji} {moveName}");
                                    }
                                    else
                                    {
                                        extraMoveNames.Add($"\\- {moveName}");
                                    }
                                }
                            }
                            RaidEmbedInfoHelpers.ExtraMoves = string.Join("\n", extraMoveNames);
                        }

                        // Scale Text and Number
                        RaidEmbedInfoHelpers.ScaleText = $"{PokeSizeDetailedUtil.GetSizeRating(pk.Scale)}";
                        RaidEmbedInfoHelpers.ScaleNumber = pk.Scale;

                        // Special Rewards
                        var res = GetSpecialRewards(allRewards[i], Settings.EmbedToggles.RewardsToShow);
                        RaidEmbedInfoHelpers.SpecialRewards = res;
                        if (string.IsNullOrEmpty(res))
                            res = string.Empty;
                        else
                            res = "**Special Rewards:**\n" + res;

                        // Area Text
                        var areaText = $"{Areas.GetArea((int)(allRaids[i].Area - 1), allRaids[i].MapParent)} - Den {allRaids[i].Den}";
                        Log($"Seed {seed:X8} found for {(Species)allEncounters[i].Species} in {areaText}");
                    }
                }
            }
        }

        private async Task FindSeedIndexInRaids(uint denHexSeedUInt, CancellationToken token)
        {
            var upperBound = KitakamiDensCount == 25 ? 94 : 95;
            var startIndex = KitakamiDensCount == 25 ? 94 : 95;

            // Search in Paldea region
            var dataP = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerP, 2304, token).ConfigureAwait(false);
            for (int i = 0; i < 69; i++)
            {
                var seed = BitConverter.ToUInt32(dataP.AsSpan(0x20 + i * 0x20, 4));
                if (seed == denHexSeedUInt)
                {
                    SeedIndexToReplace = i;
                    return;
                }
            }

            // Search in Kitakami region
            var dataK = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerK + 0x10, 0xC80, token).ConfigureAwait(false);
            for (int i = 0; i < upperBound; i++)
            {
                var seed = BitConverter.ToUInt32(dataK.AsSpan(i * 0x20, 4));
                if (seed == denHexSeedUInt)
                {
                    SeedIndexToReplace = i + 69;
                    return;
                }
            }

            // Search in Blueberry region
            var dataB = await SwitchConnection.ReadBytesAbsoluteAsync(RaidBlockPointerB + 0x10, 0xA00, token).ConfigureAwait(false);
            for (int i = startIndex; i < 118; i++)
            {
                var seed = BitConverter.ToUInt32(dataB.AsSpan((i - startIndex) * 0x20, 4));
                if (seed == denHexSeedUInt)
                {
                    SeedIndexToReplace = i - 1;
                    return;
                }
            }

            Log($"Seed {denHexSeedUInt:X8} not found in any region.");
        }

        public static (PK9, Embed) RaidInfoCommand(string seedValue, int contentType, TeraRaidMapParent map, int storyProgressLevel, int raidDeliveryGroupID, List<string> rewardsToShow, bool moveTypeEmojis, List<MoveTypeEmojiInfo> customTypeEmojis, int queuePosition = 0, bool isEvent = false)
        {
            byte[] enabled = StringToByteArray("00000001");
            byte[] area = StringToByteArray("00000001");
            byte[] displaytype = StringToByteArray("00000001");
            byte[] spawnpoint = StringToByteArray("00000001");
            byte[] thisseed = StringToByteArray(seedValue);
            byte[] unused = StringToByteArray("00000000");
            byte[] content = StringToByteArray($"0000000{contentType}"); // change this to 1 for 6-Star, 2 for 1-6 Star Events, 3 for Mighty 7-Star Raids
            byte[] leaguepoints = StringToByteArray("00000000");
            byte[] raidbyte = enabled.Concat(area).ToArray().Concat(displaytype).ToArray().Concat(spawnpoint).ToArray().Concat(thisseed).ToArray().Concat(unused).ToArray().Concat(content).ToArray().Concat(leaguepoints).ToArray();

            storyProgressLevel = storyProgressLevel switch
            {
                3 => 1,
                4 => 2,
                5 => 3,
                6 => 4,
                0 => 0,
                _ => 4 // default 6Unlocked
            };

            var raid = new Raid(raidbyte, map);
            var progress = storyProgressLevel;
            var raid_delivery_group_id = raidDeliveryGroupID;
            var encounter = raid.GetTeraEncounter(container, raid.IsEvent ? 3 : progress, contentType == 3 ? 1 : raid_delivery_group_id);
            var reward = encounter.GetRewards(container, raid, 0);
            var stars = raid.IsEvent ? encounter.Stars : raid.GetStarCount(raid.Difficulty, storyProgressLevel, raid.IsBlack);
            var teraType = raid.GetTeraType(encounter);
            var form = encounter.Form;
            var level = encounter.Level;

            var param = encounter.GetParam();
            var pk = new PK9
            {
                Species = encounter.Species,
                Form = encounter.Form,
                Move1 = encounter.Move1,
                Move2 = encounter.Move2,
                Move3 = encounter.Move3,
                Move4 = encounter.Move4,
            };
            if (raid.IsShiny) pk.SetIsShiny(true);
            Encounter9RNG.GenerateData(pk, param, EncounterCriteria.Unrestricted, raid.Seed);
            var strings = GameInfo.GetStrings(1);
            var useTypeEmojis = moveTypeEmojis;
            var typeEmojis = customTypeEmojis
                .Where(e => !string.IsNullOrEmpty(e.EmojiCode))
                .ToDictionary(
                    e => e.MoveType,
                    e => $"{e.EmojiCode}"
                );

            var movesList = "";
            bool hasMoves = false;
            for (int i = 0; i < pk.Moves.Length; i++)
            {
                if (pk.Moves[i] != 0)
                {
                    string moveName = strings.Move[pk.Moves[i]];
                    byte moveTypeId = MoveInfo.GetType(pk.Moves[i], pk.Context);
                    MoveType moveType = (MoveType)moveTypeId;

                    if (useTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
                    {
                        movesList += $"{moveEmoji} {moveName}\n";
                    }
                    else
                    {
                        movesList += $"\\- {moveName}\n";
                    }
                    hasMoves = true;
                }
            }

            var extraMoves = "";
            for (int i = 0; i < encounter.ExtraMoves.Length; i++)
            {
                if (encounter.ExtraMoves[i] != 0)
                {
                    string moveName = strings.Move[encounter.ExtraMoves[i]];
                    byte moveTypeId = MoveInfo.GetType(encounter.ExtraMoves[i], pk.Context);
                    MoveType moveType = (MoveType)moveTypeId;

                    if (useTypeEmojis && typeEmojis.TryGetValue(moveType, out var moveEmoji))
                    {
                        extraMoves += $"{moveEmoji} {moveName}\n";
                    }
                    else
                    {
                        extraMoves += $"\\- {moveName}\n";
                    }
                    hasMoves = true;
                }
            }
            if (!string.IsNullOrEmpty(extraMoves))
            {
                movesList += $"**Extra Moves:**\n{extraMoves}";
            }
            var specialRewards = string.Empty;

            try
            {
                specialRewards = GetSpecialRewards(reward, rewardsToShow);
            }
            catch
            {
                specialRewards = "No valid rewards to display";
            }
            var teraTypeLower = strings.Types[teraType].ToLower();
            var teraIconUrl = $"https://raw.githubusercontent.com/bdawg1989/sprites/main/teraicons/icon1/{teraTypeLower}.png";
            var disclaimer = $"Current Position: {queuePosition}";
            var titlePrefix = raid.IsShiny ? "Shiny " : "";
            var formName = ShowdownParsing.GetStringFromForm(pk.Form, strings, pk.Species, pk.Context);
            var authorName = $"{stars} ★ {titlePrefix}{(Species)encounter.Species}{(pk.Form != 0 ? $"-{formName}" : "")}{(isEvent ? " (Event Raid)" : "")}";

            (int R, int G, int B) = Task.Run(() => RaidExtensions<PK9>.GetDominantColorAsync(RaidExtensions<PK9>.PokeImg(pk, false, false))).Result;
            var embedColor = new Discord.Color(R, G, B);

            var embed = new EmbedBuilder
            {
                Color = embedColor,
                ThumbnailUrl = RaidExtensions<PK9>.PokeImg(pk, false, false),
            };
            embed.AddField(x =>
            {
                x.Name = "**__Stats__**";
                x.Value = $"{Format.Bold($"TeraType:")} {strings.Types[teraType]} \n" +
                          $"{Format.Bold($"Level:")} {level}\n" +
                          $"{Format.Bold($"Ability:")} {strings.Ability[pk.Ability]}\n" +
                          $"{Format.Bold("Nature:")} {(Nature)pk.Nature}\n" +
                          $"{Format.Bold("IVs:")} {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}\n" +
                          $"{Format.Bold($"Scale:")} {PokeSizeDetailedUtil.GetSizeRating(pk.Scale)}";
                x.IsInline = true;
            });

            if (hasMoves)
            {
                embed.AddField("**__Moves__**", movesList, true);
            }
            else
            {
                embed.AddField("**__Moves__**", "No moves available", true);  // Default message
            }

            if (!string.IsNullOrEmpty(specialRewards))
            {
                embed.AddField("**__Special Rewards__**", specialRewards, true);
            }
            else
            {
                embed.AddField("**__Special Rewards__**", "No special rewards available", true);
            }

            var programIconUrl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/icon4.png";
            embed.WithFooter(new EmbedFooterBuilder()
            {
                Text = $"" + disclaimer,
                IconUrl = programIconUrl
            });

            embed.WithAuthor(auth =>
            {
                auth.Name = authorName;
                auth.IconUrl = teraIconUrl;
            });

            return (pk, embed.Build());
        }

        public static byte[] StringToByteArray(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            Array.Reverse(bytes);
            return bytes;
        }

        private async Task<bool> SaveGame(PokeRaidHubConfig config, CancellationToken token)
        {
            Log("Saving the Game.");
            await Click(B, 3_000, token).ConfigureAwait(false);
            await Click(B, 3_000, token).ConfigureAwait(false);
            await Click(X, 3_000, token).ConfigureAwait(false);
            await Click(L, 5_000 + Hub.Config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            return true;
        }
    }
}