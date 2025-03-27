using PKHeX.Core;
using SysBot.Pokemon.Discord;
using System.Threading;
using System.Threading.Tasks;
using SysBot.Web;

namespace SysBot.Pokemon
{
    /// <summary>
    /// Bot Environment implementation with Integrations added.
    /// </summary>
    public class PokeBotRunnerImpl<T> : PokeBotRunner<T> where T : PKM, new()
    {
        public PokeBotRunnerImpl(PokeRaidHub<T> hub, BotFactory<T> fac) : base(hub, fac)
        {
        }

        public PokeBotRunnerImpl(PokeRaidHubConfig config, BotFactory<T> fac) : base(config, fac)
        {
        }

        protected override void AddIntegrations()
        {
            AddDiscordBot(Hub.Config.Discord.Token);
            
            // Web-API auf Port 6500 starten (oder nächsten freien Port)
            WebApiIntegration.StartWebApi(this, 6500);
        }

        private void AddDiscordBot(string apiToken)
        {
            if (string.IsNullOrWhiteSpace(apiToken))
                return;
            var bot = new SysCord<T>(this);
            Task.Run(() => bot.MainAsync(apiToken, CancellationToken.None));
        }
    }
}