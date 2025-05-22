using Discord;
using System;
using System.Text;
using System.Text.RegularExpressions;
using static SysBot.Pokemon.RotatingRaidSettingsSV.RotatingRaidPresetFiltersCategory;

namespace SysBot.Pokemon.SV.BotRaid.Language
{
    public static class EmbedLocalizationHelper
    {
        /// <summary>
        /// Localizes an embed title based on the user's preferred language
        /// </summary>
        /// <param name="title">The original title</param>
        /// <param name="language">The user's preferred language</param>
        /// <returns>The localized title</returns>
        public static string LocalizeEmbedTitle(string title, LanguageOptions language)
        {
            return EmbedLanguageManager.GetLocalizedText(title, language);
        }

        /// <summary>
        /// Localizes a field name based on the user's preferred language
        /// </summary>
        /// <param name="fieldName">The original field name</param>
        /// <param name="language">The user's preferred language</param>
        /// <returns>The localized field name</returns>
        public static string LocalizeFieldName(string fieldName, LanguageOptions language)
        {
            return EmbedLanguageManager.GetLocalizedText(fieldName, language);
        }

        /// <summary>
        /// Localizes a star rating based on the user's preferred language
        /// </summary>
        /// <param name="stars">The number of stars</param>
        /// <param name="language">The user's preferred language</param>
        /// <returns>The localized star rating</returns>
        public static string LocalizeStarRating(int stars, LanguageOptions language)
        {
            return EmbedLanguageManager.GetStarRating(stars, language);
        }

        /// <summary>
        /// Localizes a gender string based on the user's preferred language
        /// </summary>
        /// <param name="gender">The gender string (Male, Female, or Genderless)</param>
        /// <param name="language">The user's preferred language</param>
        /// <returns>The localized gender string</returns>
        public static string LocalizeGender(string gender, LanguageOptions language)
        {
            return EmbedLanguageManager.GetLocalizedText(gender, language);
        }

        /// <summary>
        /// Builds a localized stats field based on the user's preferred language
        /// </summary>
        /// <param name="language">The user's preferred language</param>
        /// <param name="level">The Pokemon's level</param>
        /// <param name="gender">The Pokemon's gender</param>
        /// <param name="nature">The Pokemon's nature</param>
        /// <param name="ability">The Pokemon's ability</param>
        /// <param name="ivs">The Pokemon's IVs</param>
        /// <param name="scale">The Pokemon's scale</param>
        /// <param name="scaleNumber">The Pokemon's scale number</param>
        /// <param name="includeSeed">Whether to include the seed</param>
        /// <param name="seed">The raid seed</param>
        /// <param name="difficultyLevel">The difficulty level</param>
        /// <param name="storyProgress">The story progress</param>
        /// <returns>A localized stats string</returns>
        public static string BuildLocalizedStatsField(
            LanguageOptions language,
            string level,
            string gender,
            string nature,
            string ability,
            string ivs,
            string scale,
            string scaleNumber,
            bool includeSeed = false,
            string seed = "",
            int difficultyLevel = 0,
            int storyProgress = 0)
        {
            StringBuilder statsField = new();

            statsField.AppendLine($"**{LocalizeFieldName("Level", language)}**: {level}");
            statsField.AppendLine($"**{LocalizeFieldName("Gender", language)}**: {LocalizeGender(gender, language)}");
            statsField.AppendLine($"**{LocalizeFieldName("Nature", language)}**: {nature}");
            statsField.AppendLine($"**{LocalizeFieldName("Ability", language)}**: {ability}");
            statsField.AppendLine($"**{LocalizeFieldName("IVs", language)}**: {ivs}");
            statsField.AppendLine($"**{LocalizeFieldName("Scale", language)}**: {scale}({scaleNumber})");

            if (includeSeed && !string.IsNullOrEmpty(seed))
            {
                statsField.AppendLine($"**{LocalizeFieldName("Seed", language)}**: `{seed} {difficultyLevel} {storyProgress}`");
            }

            return statsField.ToString();
        }

        /// <summary>
        /// Creates a localized embed field based on the user's preferred language
        /// </summary>
        /// <param name="embed">The embed builder</param>
        /// <param name="name">The field name</param>
        /// <param name="value">The field value</param>
        /// <param name="inline">Whether the field should be inline</param>
        /// <param name="language">The user's preferred language</param>
        /// <returns>The updated embed builder</returns>
        public static EmbedBuilder AddLocalizedField(
            this EmbedBuilder embed,
            string name,
            string value,
            bool inline,
            LanguageOptions language)
        {
            string localizedName;

            if (name.StartsWith("**__") && name.EndsWith("__**"))
            {
                string extractedName = name.Substring(4, name.Length - 8);
                localizedName = $"**__{LocalizeFieldName(extractedName, language)}__**";
            }
            else if (name.StartsWith("**") && name.EndsWith("**"))
            {
                string extractedName = name.Substring(2, name.Length - 4);
                localizedName = $"**{LocalizeFieldName(extractedName, language)}**";
            }
            else
            {
                localizedName = LocalizeFieldName(name, language);
            }

            return embed.AddField(localizedName, value, inline);
        }

        /// <summary>
        /// Localizes text related to raid starting timestamps
        /// </summary>
        /// <param name="value">Original value containing timestamp info</param>
        /// <param name="language">Selected language</param>
        /// <returns>Localized raid starting text</returns>
        public static string LocalizeRaidStartingText(string value, LanguageOptions language)
        {
            if (value.Contains("<t:"))
            {
                // Captures timestamp format like: "**__Raid Starting__**:\n**<t:1234567890:R>**"
                var match = Regex.Match(value, @".*:\n\*\*<t:(\d+):R>\*\*");
                if (match.Success)
                {
                    string timestamp = match.Groups[1].Value;
                    return $"**__{LocalizeFieldName("Raid Starting", language)}__**:\n**<t:{timestamp}:R>**";
                }
            }

            return value;
        }

        /// <summary>
        /// Creates a localized author for an embed based on the user's preferred language
        /// </summary>
        /// <param name="embed">The embed builder</param>
        /// <param name="title">The author title</param>
        /// <param name="iconUrl">The icon URL</param>
        /// <param name="language">The user's preferred language</param>
        /// <returns>The updated embed builder</returns>
        public static EmbedBuilder WithLocalizedAuthor(
            this EmbedBuilder embed,
            string title,
            string iconUrl,
            LanguageOptions language)
        {
            // Handle titles with star ratings
            if (title.Contains("★"))
            {
                var match = Regex.Match(title, @"(\d+)\s*★\s*(.*)");
                if (match.Success)
                {
                    int stars = int.Parse(match.Groups[1].Value);
                    string suffix = match.Groups[2].Value.Trim();

                    // Check if it contains "Shiny"
                    bool isShiny = suffix.StartsWith("Shiny ", StringComparison.OrdinalIgnoreCase);
                    if (isShiny)
                    {
                        suffix = suffix.Substring(6).Trim(); // Remove "Shiny " prefix
                        string localizedShiny = LocalizeFieldName("Shiny", language);
                        string starRating = LocalizeStarRating(stars, language);

                        return embed.WithAuthor(auth =>
                        {
                            auth.Name = $"{starRating} {localizedShiny} {suffix}";
                            auth.IconUrl = iconUrl;
                        });
                    }
                    else if (suffix.EndsWith("(Event Raid)"))
                    {
                        string speciesName = suffix.Substring(0, suffix.Length - 12).Trim();
                        string localizedEventRaid = LocalizeFieldName("Event Raid", language);
                        string starRating = LocalizeStarRating(stars, language);

                        return embed.WithAuthor(auth =>
                        {
                            auth.Name = $"{starRating} {speciesName} ({localizedEventRaid})";
                            auth.IconUrl = iconUrl;
                        });
                    }
                    else
                    {
                        string starRating = LocalizeStarRating(stars, language);

                        return embed.WithAuthor(auth =>
                        {
                            auth.Name = $"{starRating} {suffix}";
                            auth.IconUrl = iconUrl;
                        });
                    }
                }
            }

            // For other titles
            return embed.WithAuthor(auth =>
            {
                auth.Name = LocalizeEmbedTitle(title, language);
                auth.IconUrl = iconUrl;
            });
        }

        /// <summary>
        /// Localizes text in the embed footer
        /// </summary>
        /// <param name="embed">The embed builder</param>
        /// <param name="text">The footer text</param>
        /// <param name="iconUrl">The footer icon URL</param>
        /// <param name="language">The user's preferred language</param>
        /// <returns>The updated embed builder</returns>
        public static EmbedBuilder WithLocalizedFooter(
            this EmbedBuilder embed,
            string text,
            string iconUrl,
            LanguageOptions language)
        {
            // Replace common footer terms with localized versions
            string localizedText = text;

            if (localizedText.Contains("Completed Raids:"))
            {
                localizedText = localizedText.Replace("Completed Raids:", LocalizeFieldName("Completed Raids", language) + ":");
            }

            if (localizedText.Contains("ActiveRaids:"))
            {
                localizedText = localizedText.Replace("ActiveRaids:", LocalizeFieldName("ActiveRaids", language) + ":");
            }

            if (localizedText.Contains("Uptime:"))
            {
                localizedText = localizedText.Replace("Uptime:", LocalizeFieldName("Uptime", language) + ":");
            }

            // Localize time units (if included in uptime)
            if (localizedText.Contains(" day"))
            {
                localizedText = Regex.Replace(localizedText, @"(\d+) day(s?)", m =>
                    $"{m.Groups[1].Value} {(m.Groups[1].Value == "1" ? LocalizeFieldName("day", language) : LocalizeFieldName("days", language))}");
            }

            if (localizedText.Contains(" hour"))
            {
                localizedText = Regex.Replace(localizedText, @"(\d+) hour(s?)", m =>
                    $"{m.Groups[1].Value} {(m.Groups[1].Value == "1" ? LocalizeFieldName("hour", language) : LocalizeFieldName("hours", language))}");
            }

            if (localizedText.Contains(" minute"))
            {
                localizedText = Regex.Replace(localizedText, @"(\d+) minute(s?)", m =>
                    $"{m.Groups[1].Value} {(m.Groups[1].Value == "1" ? LocalizeFieldName("minute", language) : LocalizeFieldName("minutes", language))}");
            }

            return embed.WithFooter(new EmbedFooterBuilder
            {
                Text = localizedText,
                IconUrl = iconUrl
            });
        }
    }
}