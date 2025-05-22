using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord.Helpers;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Pokemon.RotatingRaidSettingsSV;
using static SysBot.Pokemon.SV.BotRaid.RotatingRaidBotSV;

namespace SysBot.Pokemon.Discord.Commands.Bots
{
    [Summary("Generates and queues various silly trade additions")]
    public partial class RaidModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private readonly PokeRaidHub<T> Hub = SysCord<T>.Runner.Hub;

        [Command("raidinfo")]
        [Alias("ri", "rv")]
        [Summary("Displays basic Raid Info of the provided seed.")]
        public async Task RaidSeedInfoAsync(
            string seedValue,
            int level,
            int storyProgressLevel = 6,
            string? speciesName = null)
        {
            uint seed;
            try
            {
                seed = uint.Parse(seedValue, NumberStyles.AllowHexSpecifier);
            }
            catch (FormatException)
            {
                await ReplyAsync("Invalid seed format. Please enter a valid seed.");
                return;
            }
            if (level == 7 && storyProgressLevel == 6 && string.IsNullOrEmpty(speciesName))
            {
                var availableSpecies = string.Join(", ", SpeciesToGroupIDMap.Keys);
                await ReplyAsync($"For 7★ raids, please specify the species name. Available species: {availableSpecies}").ConfigureAwait(false);
                return;
            }
            // Check Compatibility of Difficulty and Story Progress Level
            var compatible = CheckProgressandLevel(level, storyProgressLevel);
            if (!compatible)
            {
                string requiredProgress = GetRequiredProgress(level);
                await ReplyAsync($"The selected raid difficulty level ({level}★) is not compatible with your current story progress. " +
                                 $"To access {level}★ raids, you need to have at least {requiredProgress} in the game's story.").ConfigureAwait(false);
                return;
            }

            var settings = Hub.Config.RotatingRaidSV;  // Get RotatingRaidSV settings

            bool isEvent = !string.IsNullOrEmpty(speciesName);

            var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);

            if (isEvent && selectedMap != TeraRaidMapParent.Paldea)
            {
                await ReplyAsync("Events can only be run in the Paldea map.");
                return;
            }

            int raidDeliveryGroupID = -1;
            if (!string.IsNullOrEmpty(speciesName) && SpeciesToGroupIDMap.TryGetValue(speciesName, out var groupIDAndIndices))
            {
                var firstRaidGroupID = groupIDAndIndices.First().GroupID;
                raidDeliveryGroupID = firstRaidGroupID;
            }
            else if (!string.IsNullOrEmpty(speciesName))
            {
                await ReplyAsync("Species name not recognized or not associated with an active event. Please check the name and try again.");
                return;
            }

            var crystalType = level switch
            {
                >= 1 and <= 5 => isEvent ? (TeraCrystalType)2 : 0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("Invalid difficulty level.")
            };

            try
            {
                var rewardsToShow = settings.EmbedToggles.RewardsToShow;
                var (_, embed) = RaidInfoCommand(
                    seedValue,
                    (int)crystalType,
                    selectedMap,
                    storyProgressLevel,
                    raidDeliveryGroupID,
                    rewardsToShow,
                    settings.EmbedToggles.MoveTypeEmojis,
                    settings.EmbedToggles.CustomTypeEmojis,
                    0,
                    isEvent,
                    (int)settings.EmbedToggles.EmbedLanguage);
                var instructionMessage = await ReplyAsync("React with ✅ to add the raid to the queue.");
                var message = await ReplyAsync(embed: embed);
                var checkmarkEmoji = new Emoji("✅");
                await message.AddReactionAsync(checkmarkEmoji);

                SysCord<T>.ReactionService.AddReactionHandler(message.Id, async (reaction) =>
                {
                    if (reaction.UserId == Context.User.Id && reaction.Emote.Name == checkmarkEmoji.Name)
                    {
                        await AddNewRaidParamNext(seedValue, level, storyProgressLevel, speciesName);

                        SysCord<T>.ReactionService.RemoveReactionHandler(reaction.MessageId);
                    }
                });
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));
                    SysCord<T>.ReactionService.RemoveReactionHandler(message.Id);
                    await message.DeleteAsync();
                    await instructionMessage.DeleteAsync();
                });
            }
            catch (Exception ex)
            {
                await ReplyAsync(ex.Message);
            }
            await Context.Message.DeleteAsync().ConfigureAwait(false);
        }

        [Command("banOT")]
        [Alias("ban")]
        [RequireSudo]
        [Summary("Bans a user with the specified OT from participating in raids.")]
        public async Task BanUserAsync(string ot)
        {
            // Load the player data from the file.
            var baseDirectory = AppContext.BaseDirectory;
            var storage = new PlayerDataStorage(baseDirectory);
            var playerData = storage.LoadPlayerData();

            // Filter out player data that matches the OT.
            var matchedPlayers = playerData.Where(pd => pd.Value.OT.Equals(ot, StringComparison.OrdinalIgnoreCase)).ToList();

            // Check if there are duplicates.
            if (matchedPlayers.Count > 1)
            {
                await ReplyAsync($"Multiple players with OT '{ot}' found. Ban skipped. Please review manually.");
                return;
            }

            // If no player is found, notify and return.
            if (matchedPlayers.Count == 0)
            {
                await ReplyAsync($"No player with OT '{ot}' found.");
                return;
            }

            // Get the player's NID to ban.
            var playerToBan = matchedPlayers.First();
            ulong nidToBan = playerToBan.Key;

            // Check if the NID is already in the ban list.
            if (Hub.Config.RotatingRaidSV.RaiderBanList.List.Any(x => x.ID == nidToBan))
            {
                await ReplyAsync($"Player with OT '{ot}' is already banned.");
                return;
            }

            Hub.Config.RotatingRaidSV.RaiderBanList.AddIfNew(new[] { GetReference(nidToBan, ot, "") });

            await ReplyAsync($"Player with OT '{ot}' has been banned.");
        }

        [Command("banNID")]
        [Alias("ban")]
        [RequireSudo]
        [Summary("Bans a user with the specified NID from participating in raids.")]
        public async Task BanUserAsync(ulong nid, [Remainder] string comment)
        {
            var ot = string.Empty;
            try
            {
                var baseDirectory = AppContext.BaseDirectory;
                var storage = new PlayerDataStorage(baseDirectory);
                var playerData = storage.LoadPlayerData();
                var matchedNID = playerData.Where(pd => pd.Key.Equals(nid));
                ot = matchedNID.First().Value.OT;
            }
            catch
            {
                ot = "Unknown";
            }

            Hub.Config.RotatingRaidSV.RaiderBanList.AddIfNew(new[] { GetReference(nid, ot, comment) });
            await ReplyAsync("Done.").ConfigureAwait(false);
        }

        [Command("limitrequests")]
        [Alias("lr")]
        [Summary("Sets the limit on the number of requests a user can make.")]
        [RequireSudo]
        public async Task SetLimitRequestsAsync([Summary("The new limit for requests. Set to 0 to disable.")] int newLimit)
        {
            var settings = Hub.Config.RotatingRaidSV.RaidSettings;
            settings.LimitRequests = newLimit;

            await ReplyAsync($"LimitRequests updated to {newLimit}.").ConfigureAwait(false);
        }

        [Command("limitrequeststime")]
        [Alias("lrt")]
        [Summary("Sets the time users must wait once their request limit is reached.")]
        [RequireSudo]
        public async Task SetLimitRequestsTimeAsync([Summary("The new time in minutes. Set to 0 to disable.")] int newTime)
        {
            var settings = Hub.Config.RotatingRaidSV.RaidSettings;
            settings.LimitRequestsTime = newTime;

            await ReplyAsync($"LimitRequestsTime updated to {newTime} minutes.").ConfigureAwait(false);
        }

        [Command("addlimitbypass")]
        [Alias("alb")]
        [Summary("Adds a user or role to the bypass list for request limits.")]
        [RequireSudo]
        public async Task AddBypassLimitAsync([Remainder] string mention)
        {
            string type;
            string nameToAdd;
            if (MentionUtils.TryParseUser(mention, out ulong idToAdd))
            {
                var user = Context.Guild.GetUser(idToAdd);
                nameToAdd = user?.Username ?? "Unknown User";
                type = "User";
            }
            // Check if mention is a role
            else if (MentionUtils.TryParseRole(mention, out idToAdd))
            {
                var role = Context.Guild.GetRole(idToAdd);
                nameToAdd = role?.Name ?? "Unknown Role";
                type = "Role";
            }
            else
            {
                await ReplyAsync("Invalid user or role.").ConfigureAwait(false);
                return;
            }

            if (Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.TryAdd(idToAdd, nameToAdd))
            {

                await ReplyAsync($"Added {type} '{nameToAdd}' to bypass list.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"{type} '{nameToAdd}' is already in the bypass list.").ConfigureAwait(false);
            }
        }

        [Command("repeek")]
        [Summary("Take and send a screenshot from the currently configured Switch.")]
        [RequireOwner]
        public async Task RePeek()
        {
            string ip = RaidModule<T>.GetBotIPFromJsonConfig(); // Fetch the IP from the config
            var source = new CancellationTokenSource();
            var token = source.Token;

            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No bot found with the specified IP address ({ip}).").ConfigureAwait(false);
                return;
            }

            _ = Array.Empty<byte>();
            byte[]? bytes;
            try
            {
                bytes = await bot.Bot.Connection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error while fetching pixels: {ex.Message}");
                return;
            }

            if (bytes.Length == 0)
            {
                await ReplyAsync("No screenshot data received.");
                return;
            }

            using MemoryStream ms = new(bytes);
            var img = "cap.jpg";
            var embed = new EmbedBuilder { ImageUrl = $"attachment://{img}", Color = Color.Purple }
                .WithFooter(new EmbedFooterBuilder { Text = $"Here's your screenshot." });

            await Context.Channel.SendFileAsync(ms, img, embed: embed.Build());
        }

        private static string GetBotIPFromJsonConfig()
        {
            try
            {
                // Read the file and parse the JSON
                var jsonData = File.ReadAllText(SVRaidBot.ConfigPath);
                var config = JObject.Parse(jsonData);

                // Access the IP address from the first bot in the Bots array
                var ip = config["Bots"][0]["Connection"]["IP"].ToString();
                return ip;
            }
            catch (Exception ex)
            {
                // Handle any errors that occur during reading or parsing the file
                Console.WriteLine($"Error reading config file: {ex.Message}");
                return "192.168.1.1"; // Default IP if error occurs
            }
        }

        [Command("addRaidParams")]
        [Alias("arp")]
        [Summary("Adds new raid parameter.")]
        [RequireSudo]
        public async Task AddNewRaidParam(
            [Summary("Seed")] string seed,
            [Summary("Difficulty Level (1-7)")] int level,
            [Summary("Story Progress Level")] int storyProgressLevel = 6,
            [Summary("Species Name (Optional)")] string? speciesName = null)
        {
            if (seed.Length != 8 || !seed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            {
                await ReplyAsync("Invalid seed format. Please enter a seed consisting of exactly 8 hexadecimal digits.").ConfigureAwait(false);
                return;
            }
            if (level < 1 || level > 7)
            {
                await ReplyAsync("Invalid raid level. Please enter a level between 1 and 7.").ConfigureAwait(false);
                return;
            }

            var gameProgress = ConvertToGameProgress(storyProgressLevel);

            var settings = Hub.Config.RotatingRaidSV;
            bool isEvent = !string.IsNullOrEmpty(speciesName);

            var crystalType = level switch
            {
                >= 1 and <= 5 => isEvent ? (TeraCrystalType)2 : 0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("Invalid difficulty level.")
            };

            int raidDeliveryGroupID = -1;

            if (!string.IsNullOrEmpty(speciesName))
            {
                string speciesKey = string.Join("", speciesName.Split(' ')).ToLower();

                if (SpeciesToGroupIDMap.TryGetValue(speciesKey, out var groupIDAndIndices))
                {
                    var firstRaidGroupID = groupIDAndIndices.First().GroupID;
                    raidDeliveryGroupID = firstRaidGroupID;
                }
                else
                {
                    await ReplyAsync("Species name not recognized or not associated with an active event. Please check the name and try again.");
                    return;
                }
            }

            // Rest of your code remains the same...
            var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);
            var rewardsToShow = settings.EmbedToggles.RewardsToShow;
            var (pk, raidEmbed) = RaidInfoCommand(
            seed,
            (int)crystalType,
            selectedMap,
            storyProgressLevel,
            raidDeliveryGroupID,
            rewardsToShow,
            settings.EmbedToggles.MoveTypeEmojis,
            settings.EmbedToggles.CustomTypeEmojis,
            0,
            isEvent,
            (int)settings.EmbedToggles.EmbedLanguage);
            var description = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "RaidFilesSV\\bodyparam.txt";
            if (File.Exists(prevpath))
                Directory.Move(filepath, prevpath + Path.GetFileName(filepath));

            if (File.Exists(filepath))
                description = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "RaidFilesSV\\pkparam.txt";
            if (File.Exists(prevpk))
                Directory.Move(pkpath, prevpk + Path.GetFileName(pkpath));

            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            RotatingRaidParameters newparam = new()
            {
                CrystalType = crystalType,
                DifficultyLevel = level,
                Description = new[] { description },
                PartyPK = new[] { "" },
                Species = (Species)pk.Species,
                SpeciesForm = pk.Form,
                StoryProgress = (GameProgressEnum)gameProgress,
                Seed = seed,
                IsCoded = true,
                IsShiny = pk.IsShiny,
                AddedByRACommand = false,
                Title = $"{(Species)pk.Species}",
                GroupID = raidDeliveryGroupID != -1 ? raidDeliveryGroupID : null
            };
            // Check if Species is Ditto and set PartyPK to Showdown template
            if (newparam.Species == Species.Ditto)
            {
                newparam.PartyPK = new string[] {
                    "Happiny",
                    "Shiny: Yes",
                    "Level: 1"
                };
            }
            Hub.Config.RotatingRaidSV.ActiveRaids.Add(newparam);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = $"Your new raid has been added.";
            await ReplyAsync(msg, embed: raidEmbed).ConfigureAwait(false);
        }

        [Command("listevents")]
        [Alias("le")]
        [Summary("Lists all found event raids stored in the SpeciesToGroupID dictionary.")]
        [RequireSudo]
        public async Task ListEventRaids()
        {
            if (SpeciesToGroupIDMap == null || SpeciesToGroupIDMap.Count == 0)
            {
                await ReplyAsync("No raids found in the SpeciesToGroupIDMap.").ConfigureAwait(false);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("**Found Raids:**");

            foreach (var kvp in SpeciesToGroupIDMap)
            {
                var speciesName = kvp.Key;
                var groupIDAndIndices = kvp.Value;

                sb.AppendLine($"\n**{speciesName}:**");

                foreach (var (GroupID, Index, DenIdentifier) in groupIDAndIndices)
                {
                    sb.AppendLine($"- GroupID: `{GroupID}`, Index: `{Index}`, DenIdentifier: `{DenIdentifier}`");
                }
            }

            // Check if the message exceeds Discord's character limit
            if (sb.Length > 2000)
            {
                // Send the output as a file if it exceeds the limit
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
                {
                    await Context.Channel.SendFileAsync(stream, "RaidsList.txt", "The list is too long, so I've attached it as a file.").ConfigureAwait(false);
                }
            }
            else
            {
                await ReplyAsync(sb.ToString()).ConfigureAwait(false);
            }
        }

        [Command("addUserRaid")]
        [Alias("aur", "ra")]
        [Summary("Adds new raid parameter next in the queue.")]
        public async Task AddNewRaidParamNext(
            [Summary("Seed")] string seed,
            [Summary("Difficulty Level (1-7)")] int level,
            [Summary("Story Progress Level")] int storyProgressLevel = 6,
            [Summary("Species Name or User Mention (Optional)")] string? speciesNameOrUserMention = null,
            [Summary("User Mention 2 (Optional)")] SocketGuildUser? user2 = null,
            [Summary("User Mention 3 (Optional)")] SocketGuildUser? user3 = null)
        {
            var botPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;
            if (Hub.Config.RotatingRaidSV.RaidSettings.DisableRequests)
            {
                await ReplyAsync("Raid Requests are currently disabled by the host.").ConfigureAwait(false);
                return;
            }
            var compatible = CheckProgressandLevel(level, storyProgressLevel);
            if (!compatible)
            {
                string requiredProgress = GetRequiredProgress(level);
                await ReplyAsync($"The selected raid difficulty level ({level}★) is not compatible with your current story progress. " +
                                 $"To access {level}★ raids, you need to have at least {requiredProgress} in the game's story.").ConfigureAwait(false);
                return;
            }

            // Check if the first parameter after story progress level is a user mention
            bool isUserMention = speciesNameOrUserMention != null && MyRegex1().IsMatch(speciesNameOrUserMention);
            SocketGuildUser? user1 = null;
            string? speciesName = null;

            if (isUserMention)
            {
                // Extract the user ID from the mention and retrieve the user
                var userId2 = ulong.Parse(Regex.Match(speciesNameOrUserMention, @"\d+").Value);
                user1 = Context.Guild.GetUser(userId2);
            }
            else
            {
                speciesName = speciesNameOrUserMention;
            }

            // Check if private raids are enabled
            if (!Hub.Config.RotatingRaidSV.RaidSettings.PrivateRaidsEnabled && (user1 != null || user2 != null || user3 != null))
            {
                await ReplyAsync("Private raids are currently disabled by the host.").ConfigureAwait(false);
                return;
            }
            // Check if the number of user mentions exceeds the limit
            int mentionCount = (user1 != null ? 1 : 0) + (user2 != null ? 1 : 0) + (user3 != null ? 1 : 0);
            if (mentionCount > 3)
            {
                await ReplyAsync("You can only mention up to 3 users for a private raid.").ConfigureAwait(false);
                return;
            }
            var userId = Context.User.Id;
            if (Hub.Config.RotatingRaidSV.ActiveRaids.Any(r => r.RequestedByUserID == userId))
            {
                await ReplyAsync("You already have an existing raid request in the queue.").ConfigureAwait(false);
                return;
            }
            var userRequestManager = new UserRequestManager();
            var userRoles = (Context.User as SocketGuildUser)?.Roles.Select(r => r.Id) ?? new List<ulong>();

            if (!Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.ContainsKey(userId) &&
                !userRoles.Any(Hub.Config.RotatingRaidSV.RaidSettings.BypassLimitRequests.ContainsKey))
            {
                if (!userRequestManager.CanRequest(userId, Hub.Config.RotatingRaidSV.RaidSettings.LimitRequests, Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestsTime, out var remainingCooldown))
                {
                    string responseMessage = $"You have reached your request limit. Please wait {remainingCooldown.TotalMinutes:N0} minutes before making another request.";

                    if (!string.IsNullOrWhiteSpace(Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestMsg))
                    {
                        responseMessage += $"\n{Hub.Config.RotatingRaidSV.RaidSettings.LimitRequestMsg}";
                    }

                    await ReplyAsync(responseMessage).ConfigureAwait(false);
                    return;
                }
            }

            if (seed.Length != 8 || !seed.All(c => "0123456789abcdefABCDEF".Contains(c)))
            {
                await ReplyAsync("Invalid seed format. Please enter a seed consisting of exactly 8 hexadecimal digits.").ConfigureAwait(false);
                return;
            }
            if (level == 7 && storyProgressLevel == 6 && string.IsNullOrEmpty(speciesName))
            {
                var availableSpecies = string.Join(", ", SpeciesToGroupIDMap.Keys);
                await ReplyAsync($"For 7★ raids, please specify the species name. Available species: {availableSpecies}").ConfigureAwait(false);
                return;
            }
            if (level < 1 || level > 7) 
            {
                await ReplyAsync("Invalid raid level. Please enter a level between 1 and 7.").ConfigureAwait(false); 
                return;
            }
            var gameProgress = ConvertToGameProgress(storyProgressLevel);
            if (gameProgress == GameProgress.None)
            {
                await ReplyAsync("Invalid Story Progress Level. Please enter a value between 1 and 6.").ConfigureAwait(false);
                return;
            }
            var settings = Hub.Config.RotatingRaidSV;
            bool isEvent = !string.IsNullOrEmpty(speciesName);
            var selectedMap = IsBlueberry ? TeraRaidMapParent.Blueberry : (IsKitakami ? TeraRaidMapParent.Kitakami : TeraRaidMapParent.Paldea);

            if (isEvent && selectedMap != TeraRaidMapParent.Paldea)
            {
                await ReplyAsync("Events can only be run in the Paldea map.");
                return;
            }
            var crystalType = level switch
            {
                >= 1 and <= 5 => isEvent ? (TeraCrystalType)2 : 0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("Invalid difficulty level.")
            };

            int raidDeliveryGroupID = -1;

            if (isEvent && SpeciesToGroupIDMap.TryGetValue(speciesName, out var groupIDAndIndices))
            {
                var firstRaidGroupID = groupIDAndIndices.First().GroupID;
                raidDeliveryGroupID = firstRaidGroupID;
            }
            else if (isEvent)
            {
                await ReplyAsync("Species name not recognized or not associated with an active event. Please check the name and try again.");
                return;
            }

            int effectiveQueuePosition = 1;
            var rewardsToShow = settings.EmbedToggles.RewardsToShow;
            var (pk, raidEmbed) = RaidInfoCommand(
            seed,
            (int)crystalType,
            selectedMap,
            storyProgressLevel,
            raidDeliveryGroupID,
            rewardsToShow,
            settings.EmbedToggles.MoveTypeEmojis,
            settings.EmbedToggles.CustomTypeEmojis,
            effectiveQueuePosition,
            isEvent,
            (int)settings.EmbedToggles.EmbedLanguage);
            var description = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "RaidFilesSV\\bodyparam.txt";
            if (File.Exists(prevpath))
                Directory.Move(filepath, prevpath + Path.GetFileName(filepath));

            if (File.Exists(filepath))
                description = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "RaidFilesSV\\pkparam.txt";
            if (File.Exists(prevpk))
                Directory.Move(pkpath, prevpk + Path.GetFileName(pkpath));

            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            RotatingRaidParameters newparam = new()
            {
                CrystalType = crystalType,
                Description = new[] { description },
                PartyPK = new[] { "" },
                Species = (Species)pk.Species,
                DifficultyLevel = level,
                SpeciesForm = pk.Form,
                StoryProgress = (GameProgressEnum)gameProgress,
                Seed = seed,
                IsCoded = true,
                IsShiny = pk.IsShiny,
                GroupID = raidDeliveryGroupID,
                AddedByRACommand = true,
                RequestCommand = $"{botPrefix}ra {seed} {level} {storyProgressLevel}{(isEvent ? $" {speciesName}" : "")}",
                RequestedByUserID = Context.User.Id,
                Title = $"{Context.User.Username}'s Requested Raid{(isEvent ? $" ({speciesName} Event Raid)" : "")}",
                RaidUpNext = false,
                User = Context.User,
                MentionedUsers = new List<SocketUser> { user1, user2, user3 }.Where(u => u != null).ToList(),
            };

            // Check if Species is Ditto and set PartyPK to Showdown template
            if (newparam.Species == Species.Ditto)
            {
                newparam.PartyPK = new string[] {
                    "Happiny",
                    "Shiny: Yes",
                    "Level: 1"
                };
            }
            // Determine the correct position to insert the new raid after the current rotation
            int insertPosition = RotationCount + 1;
            while (insertPosition < Hub.Config.RotatingRaidSV.ActiveRaids.Count && Hub.Config.RotatingRaidSV.ActiveRaids[insertPosition].AddedByRACommand)
            {
                insertPosition++;
            }
            // Set RaidUpNext to true only if the new raid is inserted immediately next in the rotation
            if (insertPosition == RotationCount + 1)
            {
                newparam.RaidUpNext = true;
            }
            // After the new raid is inserted
            Hub.Config.RotatingRaidSV.ActiveRaids.Insert(insertPosition, newparam);

            // Adjust RotationCount
            if (insertPosition <= RotationCount)
            {
                RotationCount++;
            }

            // Calculate the user's position in the queue and the estimated wait time
            effectiveQueuePosition = CalculateEffectiveQueuePosition(Context.User.Id, RotationCount);
            int etaMinutes = effectiveQueuePosition * 6;

            var queuePositionMessage = effectiveQueuePosition > 0
                ? $"You are currently {effectiveQueuePosition} in the queue with an estimated wait time of {etaMinutes} minutes."
                : "Your raid request is up next!";

            var replyMsg = $"{Context.User.Mention}, added your raid to the queue! I'll DM you when it's about to start.";
            await ReplyAsync(replyMsg, embed: raidEmbed).ConfigureAwait(false);

            // Notify the mentioned users
            var mentionedUsers = new List<SocketGuildUser>();
            if (user1 != null) mentionedUsers.Add(user1);
            if (user2 != null) mentionedUsers.Add(user2);
            if (user3 != null) mentionedUsers.Add(user3);

            foreach (var user in mentionedUsers)
            {
                try
                {
                    await user.SendMessageAsync($"{Context.User.Username} invited you to a private raid! I'll DM you the code when it's about to start.", false, raidEmbed).ConfigureAwait(false);
                }
                catch
                {
                    await ReplyAsync($"Failed to send DM to {user.Mention}. Please make sure their DMs are open.").ConfigureAwait(false);
                }
            }
            try
            {
                if (Context.User is SocketGuildUser user)
                {
                    await user.SendMessageAsync($"Here's your raid information:\n{queuePositionMessage}\nYour request command: `{newparam.RequestCommand}`", false, raidEmbed).ConfigureAwait(false);
                }
                else
                {
                    await ReplyAsync("Failed to send DM. Please make sure your DMs are open.").ConfigureAwait(false);
                }
            }
            catch
            {
                await ReplyAsync("Failed to send DM. Please make sure your DMs are open.").ConfigureAwait(false);
            }
        }

        public GameProgress ConvertToGameProgress(int storyProgressLevel)
        {
            return storyProgressLevel switch
            {
                6 => GameProgress.Unlocked6Stars,
                5 => GameProgress.Unlocked5Stars,
                4 => GameProgress.Unlocked4Stars,
                3 => GameProgress.Unlocked3Stars,
                2 => GameProgress.UnlockedTeraRaids,
                1 => GameProgress.UnlockedTeraRaids,
                _ => GameProgress.Unlocked6Stars
            };
        }

        public bool CheckProgressandLevel(int level, int storyProgressLevel)
        {
            switch (storyProgressLevel)
            {
                case 6: // Unlocked 6 Stars
                    return level >= 3 && level <= 7;
                case 5: // Unlocked 5 Stars
                    return level >= 3 && level <= 5;
                case 4: // Unlocked 4 Stars
                    return level >= 3 && level <= 4;
                case 3: // Unlocked 3 Stars
                    return level == 3;
                default: return false; // No 1 or 2 Star Unlocked
            }
        }

        public string GetRequiredProgress(int level)
        {
            return level switch
            {
                7 => "6☆ Unlocked Progress",
                6 => "6☆ Unlocked Progress",
                5 => "5☆ Unlocked Progress",
                4 => "4☆ Unlocked Progress",
                3 => "3☆ Unlocked Progress",
                _ => throw new ArgumentException("Invalid Story Progress Level... where are you getting your seeds?\nUse <hideout-pk.de> to get them."),
            };
        }

        [Command("addRaidPK")]
        [Alias("rp")]
        [Summary("Adds provided showdown set Pokémon to the users Raid in Queue.")]
        public async Task AddRaidPK([Summary("Showdown Set")][Remainder] string content)
        {
            content = ReusableActions.StripCodeBlock(content);
            var set = new ShowdownSet(content);
            var template = AutoLegalityWrapper.GetTemplate(set);
            if (set.InvalidLines.Count != 0 || set.Species <= 0)
            {
                var msg = $"Unable to parse Showdown Set:\n{string.Join("\n", set.InvalidLines)}";
                await ReplyAsync(msg).ConfigureAwait(false);
                return;
            }

            try
            {
                var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
                var pkm = sav.GetLegal(template, out var result);
                var la = new LegalityAnalysis(pkm);
                var spec = GameInfo.Strings.Species[template.Species];
                pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;
                if (pkm is not T pk || !la.Valid)
                {
                    var reason = result == "Timeout" ? $"That {spec} set took too long to generate." : $"I wasn't able to create a {spec} from that set.";
                    var imsg = $"Oops! {reason}";
                    await ReplyAsync(imsg).ConfigureAwait(false);
                    return;
                }

                var userId = Context.User.Id;
                var raidParameters = Hub.Config.RotatingRaidSV.ActiveRaids;
                var raidToUpdate = raidParameters.FirstOrDefault(r => r.RequestedByUserID == userId);
                string[] partyPK = content.Split('\n', StringSplitOptions.RemoveEmptyEntries); // Remove empty lines
                if (raidToUpdate != null)
                {
                    raidToUpdate.PartyPK = partyPK;
                    await Context.Message.DeleteAsync().ConfigureAwait(false);
                    var embed = await RPEmbed.PokeEmbedAsync(pkm, Context.User.Username);
                    await ReplyAsync(embed: embed).ConfigureAwait(false);
                }
                else
                {
                    var msg = "You don't have a raid in queue!";
                    await ReplyAsync(msg).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, nameof(RaidModule<T>));
                var msg = $"Oops! An unexpected problem happened with this Showdown Set:\n```{string.Join("\n", set.GetSetLines())}```";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
        }

        [Command("addRaidPK")]
        [Alias("rp")]
        [Summary("Adds provided showdown set Pokémon to the users Raid in Queue.")]
        public async Task AddRaidPK()
        {
            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment == default)
            {
                await ReplyAsync("No attachment provided!").ConfigureAwait(false);
                return;
            }

            var att = await NetUtil.DownloadPKMAsync(attachment).ConfigureAwait(false);
            var pk = GetRequest(att);
            if (pk == null)
            {
                await ReplyAsync("Attachment provided is not compatible with this module!").ConfigureAwait(false);
                return;
            }
            else
            {
                var userId = Context.User.Id;
                var raidParameters = Hub.Config.RotatingRaidSV.ActiveRaids;
                var raidToUpdate = raidParameters.FirstOrDefault(r => r.RequestedByUserID == userId);
                var set = ShowdownParsing.GetShowdownText(pk);
                string[] partyPK = set.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (raidToUpdate != null)
                {
                    raidToUpdate.PartyPK = partyPK;
                    await Context.Message.DeleteAsync().ConfigureAwait(false);
                    var embed = await RPEmbed.PokeEmbedAsync(pk, Context.User.Username);
                    await ReplyAsync(embed: embed).ConfigureAwait(false);
                }
                else
                {
                    var msg = "You don't have a raid in queue!";
                    await ReplyAsync(msg).ConfigureAwait(false);
                }
            }
        }

        [Command("raidQueueStatus")]
        [Alias("rqs")]
        [Summary("Checks the number of raids before the user's request and gives an ETA.")]
        public async Task CheckQueueStatus()
        {
            var userId = Context.User.Id;
            int currentPosition = RotationCount;

            // Find the index of the user's request in the queue, excluding Mystery Shiny Raids
            var userRequestIndex = Hub.Config.RotatingRaidSV.ActiveRaids.FindIndex(r => r.RequestedByUserID == userId && !r.Title.Contains("Mystery Shiny Raid"));

            EmbedBuilder embed = new();

            if (userRequestIndex == -1)
            {
                embed.Title = "Queue Status";
                embed.Color = Color.Red;
                embed.Description = $"{Context.User.Mention}, you do not have a raid request in the queue.";
            }
            else
            {
                // Calculate the effective position of the user's request in the queue
                int raidsBeforeUser = CalculateEffectiveQueuePosition(userId, currentPosition);

                if (raidsBeforeUser <= 0)
                {
                    embed.Title = "Queue Status";
                    embed.Color = Color.Green;
                    embed.Description = $"{Context.User.Mention}, your raid request is up next!";
                }
                else
                {
                    // Calculate ETA assuming each raid takes 6 minutes
                    int etaMinutes = raidsBeforeUser * 6;

                    embed.Title = "Queue Status";
                    embed.Color = Color.Orange;
                    embed.Description = $"{Context.User.Mention}, here's the status of your raid request:";
                    embed.AddField("Raids Before Yours", raidsBeforeUser.ToString(), true);
                    embed.AddField("Estimated Time", $"{etaMinutes} minutes", true);
                }
            }

            await Context.Message.DeleteAsync().ConfigureAwait(false);
            await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
        }

        private int CalculateEffectiveQueuePosition(ulong userId, int currentPosition)
        {
            int effectivePosition = 0;
            bool userRequestFound = false;

            for (int i = currentPosition; i < Hub.Config.RotatingRaidSV.ActiveRaids.Count + currentPosition; i++)
            {
                int actualIndex = i % Hub.Config.RotatingRaidSV.ActiveRaids.Count;
                var raid = Hub.Config.RotatingRaidSV.ActiveRaids[actualIndex];

                // Check if the raid is added by the RA command and is not a Mystery Shiny Raid
                if (raid.AddedByRACommand && !raid.Title.Contains("Mystery Shiny Raid"))
                {
                    if (raid.RequestedByUserID == userId)
                    {
                        // Found the user's request
                        userRequestFound = true;
                        break;
                    }
                    else if (!userRequestFound)
                    {
                        // Count other user requested raids before the user's request
                        effectivePosition++;
                    }
                }
            }

            // If the user's request was not found after the current position, count from the beginning
            if (!userRequestFound)
            {
                for (int i = 0; i < currentPosition; i++)
                {
                    var raid = Hub.Config.RotatingRaidSV.ActiveRaids[i];
                    if (raid.AddedByRACommand && !raid.Title.Contains("Mystery Shiny Raid"))
                    {
                        if (raid.RequestedByUserID == userId)
                        {
                            // Found the user's request
                            break;
                        }
                        else
                        {
                            effectivePosition++;
                        }
                    }
                }
            }

            return effectivePosition;
        }

        [Command("raidQueueClear")]
        [Alias("rqc")]
        [Summary("Removes the raid added by the user.")]
        public async Task RemoveOwnRaidParam()
        {
            var userId = Context.User.Id;
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;

            // Find the raid added by the user
            var userRaid = list.FirstOrDefault(r => r.RequestedByUserID == userId && r.AddedByRACommand);
            if (userRaid == null)
            {
                await ReplyAsync("You don't have a raid added.").ConfigureAwait(false);
                return;
            }

            // Prevent canceling if the raid is up next
            if (userRaid.RaidUpNext)
            {
                await ReplyAsync("Your raid request is up next and cannot be canceled at this time.").ConfigureAwait(false);
                return;
            }

            // Remove the raid if it's not up next
            list.Remove(userRaid);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = $"Cleared your Raid from the queue.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("removeRaidParams")]
        [Alias("rrp")]
        [Summary("Removes a raid parameter.")]
        [RequireSudo]
        public async Task RemoveRaidParam([Summary("Seed Index")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                list.RemoveAt(index);
                var msg = $"Raid for {raid.Title} | {raid.Seed:X8} has been removed!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter index.").ConfigureAwait(false);
        }

        [Command("toggleRaidParams")]
        [Alias("trp")]
        [Summary("Toggles raid parameter.")]
        [RequireSudo]
        public async Task ToggleRaidParam([Summary("Seed Index")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.ActiveInRotation = !raid.ActiveInRotation;
                var m = raid.ActiveInRotation ? "enabled" : "disabled";
                var msg = $"Raid for {raid.Title} | {raid.Seed:X8} has been {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter Index.").ConfigureAwait(false);
        }

        [Command("togglecodeRaidParams")]
        [Alias("tcrp")]
        [Summary("Toggles code raid parameter.")]
        [RequireSudo]
        public async Task ToggleCodeRaidParam([Summary("Seed Index")] int index)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.IsCoded = !raid.IsCoded;
                var m = raid.IsCoded ? "coded" : "uncoded";
                var msg = $"Raid for {raid.Title} | {raid.Seed:X8} is now {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter Index.").ConfigureAwait(false);
        }

        [Command("changeRaidParamTitle")]
        [Alias("crpt")]
        [Summary("Changes the title of a  raid parameter.")]
        [RequireSudo]
        public async Task ChangeRaidParamTitle([Summary("Seed Index")] int index, [Summary("Title")] string title)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.Title = title;
                var msg = $"Raid Title for {raid.Title} | {raid.Seed:X8} has been changed to: {title}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter Index.").ConfigureAwait(false);
        }

        [Command("viewraidList")]
        [Alias("vrl", "rotatinglist")]
        [Summary("Prints the raids in the current collection.")]
        public async Task GetRaidListAsync()
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            int count = list.Count;
            int fields = (int)Math.Ceiling((double)count / 15);
            var embed = new EmbedBuilder
            {
                Title = "Raid List"
            };
            for (int i = 0; i < fields; i++)
            {
                int start = i * 15;
                int end = Math.Min(start + 14, count - 1);
                var fieldBuilder = new StringBuilder();
                for (int j = start; j <= end; j++)
                {
                    var raid = list[j];
                    int paramNumber = j;
                    fieldBuilder.AppendLine($"{paramNumber}.) {raid.Title} - {raid.Seed} - Status: {(raid.ActiveInRotation ? "Active" : "Inactive")}");
                }
                embed.AddField($"Raid List - Part {i + 1}", fieldBuilder.ToString(), false);
            }
            await ReplyAsync($"These are the raids currently in the list (total: {count}):", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("toggleRaidPK")]
        [Alias("trpk")]
        [Summary("Toggles raid parameter.")]
        [RequireSudo]
        public async Task ToggleRaidParamPK([Summary("Seed Index")] int index, [Summary("Showdown Set")][Remainder] string content)
        {
            var list = Hub.Config.RotatingRaidSV.ActiveRaids;
            if (index >= 0 && index < list.Count)
            {
                var raid = list[index];
                raid.PartyPK = new[] { content };
                var m = string.Join("\n", raid.PartyPK);
                var msg = $"RaidPK for {raid.Title} | {raid.Seed:X8} has been updated to:\n{m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid raid parameter Index.").ConfigureAwait(false);
        }

        [Command("raidhelp")]
        [Alias("rh")]
        [Summary("Prints the raid help command list.")]
        public async Task GetRaidHelpListAsync()
        {
            var embed = new EmbedBuilder();
            List<string> cmds = new()
            {
                "$ban - Ban a user from raids via NID. [Command] [OT] - Sudo only command.\n",
                "$vrl - View all raids in the list.\n",
                "$arp - Add parameter to the collection.\nEx: [Command] [Index] [Species] [Difficulty]\n",
                "$rrp - Remove parameter from the collection.\nEx: [Command] [Index]\n",
                "$trp - Toggle the parameter as Active/Inactive in the collection.\nEx: [Command] [Index]\n",
                "$tcrp - Toggle the parameter as Coded/Uncoded in the collection.\nEx: [Command] [Index]\n",
                "$trpk - Set a PartyPK for the parameter via a showdown set.\nEx: [Command] [Index] [ShowdownSet]\n",
                "$crpt - Set the title for the parameter.\nEx: [Command] [Index]"
            };
            string msg = string.Join("", cmds.ToList());
            embed.AddField(x =>
            {
                x.Name = "Raid Help Commands";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("Here's your raid help!", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("unbanrotatingraider")]
        [Alias("ubrr")]
        [Summary("Removes the specificed NID from the banlist for Raids in SV.")]
        [RequireSudo]
        public async Task UnbanRotatingRaider([Summary("Removes the specificed NID from the banlist for Raids in SV.")] string nid)
        {
            var list = Hub.Config.RotatingRaidSV.RaiderBanList.List.ToArray();
            string msg = $"{Context.User.Mention} no user found with that NID.";
            for (int i = 0; i < list.Length; i++)
                if ($"{list[i].ID}".Equals(nid))
                {
                    msg = $"{Context.User.Mention} user {list[i].Name} - {list[i].ID} has been unbanned.";
                    Hub.Config.RotatingRaidSV.RaiderBanList.List.ToList().Remove(list[i]);
                }
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        private RemoteControlAccess GetReference(ulong id, string name, string comment) => new()
        {
            ID = id,
            Name = name,
            Comment = "Banned on " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC" + $"({comment})"
        };

        private static T? GetRequest(Download<PKM> dl)
        {
            if (!dl.Success)
                return null;
            return dl.Data switch
            {
                null => null,
                T pk => pk,
                _ => EntityConverter.ConvertToType(dl.Data, typeof(T), out _) as T,
            };
        }

        [GeneratedRegex(@"^<@!?\d+>$")]
        private static partial Regex MyRegex1();
    }
}