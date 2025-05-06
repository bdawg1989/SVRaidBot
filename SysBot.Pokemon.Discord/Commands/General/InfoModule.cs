using Discord;
using Discord.Commands;
using SysBot.Pokemon.SV.BotRaid.Helpers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord
{
    // src: https://github.com/foxbot/patek/blob/master/src/Patek/Modules/InfoModule.cs
    // ISC License (ISC)
    // Copyright 2017, Christopher F. <foxbot@protonmail.com>
    public class InfoModule : ModuleBase<SocketCommandContext>
    {
        private const string detail = "I am an Open Source RaidBot powered by PKHeX.Core and other open-source software.";
        public const string version = SVRaidBot.Version;
        private const string support = SVRaidBot.Repo;
        private const ulong DisallowedUserId = 195756980873199618;

        [Command("info")]
        [Alias("about", "whoami", "owner")]
        public async Task InfoAsync()
        {
            if (Context.User.Id == DisallowedUserId)
            {
                await ReplyAsync("We don't let shady people use this command.").ConfigureAwait(false);
                return;
            }
            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            var programIconUrl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/icon4.png";
            var builder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Description = detail,
                ImageUrl = programIconUrl
            };

            builder.AddField("# __Bot Info__",
                $"- **Version**: {version}\n" +
                $"- [Download SVRaidBot]({support})\n- [Join Our Discord!](https://hideoutpk.de)\n" +
                $"- {Format.Bold("Owner")}: {app.Owner} ({app.Owner.Id})\n" +
                $"- {Format.Bold("Uptime")}: {GetUptime()}\n" +
                $"- {Format.Bold("Core Version")}: {GetVersionInfo("PKHeX.Core")}\n" +
                $"- {Format.Bold("AutoLegality Version")}: {GetVersionInfo("PKHeX.Core.AutoMod")}\n"
                );

            builder.AddField("Stats",
                $"- {Format.Bold("Guilds")}: {Context.Client.Guilds.Count}\n" +
                $"- {Format.Bold("Channels")}: {Context.Client.Guilds.Sum(g => g.Channels.Count)}\n" +
                $"- {Format.Bold("Users")}: {Context.Client.Guilds.Sum(g => g.MemberCount)}\n" +
                $"{Format.Bold($"\nVisit [hideoutpk.de]({support}) for more information.")}\n"
                );

            await ReplyAsync("Here's a bit about me!", embed: builder.Build()).ConfigureAwait(false);
        }

        private static string GetUptime() => (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\.hh\:mm\:ss");

        private static string GetVersionInfo(string assemblyName)
        {
            const string _default = "Unknown";
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var assembly = assemblies.FirstOrDefault(x => x.GetName().Name == assemblyName);
            if (assembly is null)
                return _default;

            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute is null)
                return _default;

            var info = attribute.InformationalVersion;
            var split = info.Split('+');
            if (split.Length >= 2)
            {
                var versionParts = split[0].Split('.');
                if (versionParts.Length == 3)
                {
                    var major = versionParts[0].PadLeft(2, '0');
                    var minor = versionParts[1].PadLeft(2, '0');
                    var patch = versionParts[2].PadLeft(2, '0');
                    return $"{major}.{minor}.{patch}";
                }
            }
            return _default;
        }
    }
}