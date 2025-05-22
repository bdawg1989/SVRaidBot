using Discord;
using PKHeX.Core;
using System;
using System.Text.RegularExpressions;

namespace SysBot.Pokemon.SV.BotRaid;

/// <summary>
/// Handles populating raid embed information from RaidInfoCommand results
/// </summary>
public static class RaidEmbedDataPopulator
{
    /// <summary>
    /// Populates RaidEmbedInfoHelpers with data from RaidInfoCommand results
    /// </summary>
    public static void PopulateFromRaidInfo(PK9 pk, Embed embed, RotatingRaidSettingsSV.RotatingRaidPresetFiltersCategory embedToggles)
    {
        // Basic information
        RaidEmbedInfoHelpers.RaidSpecies = (Species)pk.Species;
        RaidEmbedInfoHelpers.RaidSpeciesForm = pk.Form;
        RaidEmbedInfoHelpers.IsShiny = pk.IsShiny;

        // Embed title from author name
        RaidEmbedInfoHelpers.RaidEmbedTitle = embed.Author?.Name ?? string.Empty;

        // Scale data from Pokémon
        RaidEmbedInfoHelpers.ScaleNumber = pk.Scale;
        RaidEmbedInfoHelpers.ScaleText = PokeSizeDetailedUtil.GetSizeRating(pk.Scale).ToString();

        // Process gender
        RaidEmbedInfoHelpers.RaidSpeciesGender = GetFormattedGender(pk, embedToggles);

        // Extract data from embed fields
        foreach (var field in embed.Fields)
        {
            if (field.Name == "**__Stats__**")
            {
                ExtractStatsInfo(field.Value.ToString() ?? string.Empty);
            }
            else if (field.Name == "**__Moves__**")
            {
                ExtractMovesInfo(field.Value.ToString() ?? string.Empty);
            }
            else if (field.Name == "**__Special Rewards__**")
            {
                RaidEmbedInfoHelpers.SpecialRewards = field.Value.ToString() ?? string.Empty;
            }
        }
    }

    private static string GetFormattedGender(PK9 pk, RotatingRaidSettingsSV.RotatingRaidPresetFiltersCategory embedToggles)
    {
        // Get emojis directly from the EmbedToggles settings
        string maleEmoji = embedToggles?.MaleEmoji?.EmojiString ?? "";
        string femaleEmoji = embedToggles?.FemaleEmoji?.EmojiString ?? "";

        return pk.Gender switch
        {
            0 when !string.IsNullOrEmpty(maleEmoji) => $"{maleEmoji} Male",
            1 when !string.IsNullOrEmpty(femaleEmoji) => $"{femaleEmoji} Female",
            0 => "Male",
            1 => "Female",
            _ => "Genderless"
        };
    }

    private static void ExtractStatsInfo(string statsValue)
    {
        // Extract Tera Type
        var teraMatch = Regex.Match(statsValue, @"\*\*TeraType:\*\*\s*([^\n]+)");
        if (teraMatch.Success)
            RaidEmbedInfoHelpers.RaidSpeciesTeraType = teraMatch.Groups[1].Value.Trim();

        // Extract Level
        var levelMatch = Regex.Match(statsValue, @"\*\*Level:\*\*\s*(\d+)");
        if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out int level))
            RaidEmbedInfoHelpers.RaidLevel = level;

        // Extract Ability
        var abilityMatch = Regex.Match(statsValue, @"\*\*Ability:\*\*\s*([^\n]+)");
        if (abilityMatch.Success)
            RaidEmbedInfoHelpers.RaidSpeciesAbility = abilityMatch.Groups[1].Value.Trim();

        // Extract Nature
        var natureMatch = Regex.Match(statsValue, @"\*\*Nature:\*\*\s*([^\n]+)");
        if (natureMatch.Success)
            RaidEmbedInfoHelpers.RaidSpeciesNature = natureMatch.Groups[1].Value.Trim();

        // Extract IVs
        var ivsMatch = Regex.Match(statsValue, @"\*\*IVs:\*\*\s*([^\n]+)");
        if (ivsMatch.Success)
            RaidEmbedInfoHelpers.RaidSpeciesIVs = ivsMatch.Groups[1].Value.Trim();
    }

    private static void ExtractMovesInfo(string movesValue)
    {
        if (movesValue.Contains("**Extra Moves:**"))
        {
            var parts = movesValue.Split(["**Extra Moves:**"], StringSplitOptions.None);
            RaidEmbedInfoHelpers.Moves = parts[0].Trim();
            RaidEmbedInfoHelpers.ExtraMoves = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        }
        else
        {
            RaidEmbedInfoHelpers.Moves = movesValue.Trim();
            RaidEmbedInfoHelpers.ExtraMoves = string.Empty;
        }
    }
}