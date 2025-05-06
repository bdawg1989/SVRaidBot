using Discord;
using PKHeX.Core;
using System.Threading.Tasks;
using Color = Discord.Color;

namespace SysBot.Pokemon.Discord.Helpers;

public static class RPEmbed
{
    public static async Task<Embed> PokeEmbedAsync(PKM pk, string username)
    {
        var strings = GameInfo.GetStrings(GameLanguage.DefaultLanguage);
        var items = strings.GetItemStrings(pk.Context, (GameVersion)pk.Version);
        var formName = ShowdownParsing.GetStringFromForm(pk.Form, strings, pk.Species, pk.Context);
        var itemName = items[pk.HeldItem];

        (int R, int G, int B) = await RaidExtensions<PK9>.GetDominantColorAsync(RaidExtensions<PK9>.PokeImg(pk, false, false));
        var embedColor = new Color(R, G, B);

        var embed = new EmbedBuilder
        {
            Color = embedColor,
            ThumbnailUrl = RaidExtensions<PK9>.PokeImg(pk, false, false),
        };

        embed.AddField(x =>
        {
            x.Name = $"{Format.Bold($"{GameInfo.GetStrings(1).Species[pk.Species]}{(pk.Form != 0 ? $"-{formName}" : "")} {(pk.HeldItem != 0 ? $"➜ {itemName}" : "")}")}";
            x.Value = $"{Format.Bold($"Ability:")} {GameInfo.GetStrings(1).Ability[pk.Ability]}\n{Format.Bold("Level:")} {pk.CurrentLevel}\n{Format.Bold("Nature:")} {(Nature)pk.Nature}\n{Format.Bold("IVs:")} {pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}\n{Format.Bold("Move:")} {GameInfo.GetStrings(1).Move[pk.Move1]}";
            x.IsInline = true;
        });

        embed.WithFooter(footer =>
        {
            footer.Text = $"{username}'s Raid Battler";
        });

        embed.WithAuthor(auth =>
        {
            auth.Name = "Pokémon Updated!";
            auth.Url = "https://hideoutpk.de";
        });

        return embed.Build();
    }
}