using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;
using static SysBot.Pokemon.RotatingRaidSettingsSV.RotatingRaidPresetFiltersCategory;

namespace SysBot.Pokemon.SV.BotRaid.Language
{
    public static class EmbedLanguageManager
    {
        private static JObject _languageData;

        static EmbedLanguageManager()
        {
            LoadLanguageMappings();
        }

        private static void LoadLanguageMappings()
        {
            try
            {
                string resourceName = "SysBot.Pokemon.SV.BotRaid.Language.EmbedLanguageMappings.json";
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);

                if (stream == null)
                {
                    Console.WriteLine($"Error: Could not find embedded resource: {resourceName}");
                    _languageData = new JObject();
                    return;
                }

                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                _languageData = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading language mappings: {ex.Message}");
                _languageData = new JObject();
            }
        }

        public static string GetLocalizedText(string key, LanguageOptions language)
        {
            if (_languageData == null || !_languageData.ContainsKey(key))
                return key;

            string languageCode = GetLanguageCode(language);

            var mappings = _languageData[key];
            if (mappings == null || !mappings.HasValues || mappings[languageCode] == null)
                return mappings?["en"]?.ToString() ?? key;

            return mappings[languageCode].ToString();
        }

        public static string GetStarRating(int stars, LanguageOptions language)
        {
            if (stars < 1 || stars > 7)
                return $"{stars} ★";

            if (_languageData == null || !_languageData.ContainsKey("Star Ratings"))
                return $"{stars} ★";

            string languageCode = GetLanguageCode(language);
            var starRatings = _languageData["Star Ratings"];

            if (starRatings == null || !starRatings.HasValues ||
                starRatings[stars.ToString()] == null ||
                starRatings[stars.ToString()][languageCode] == null)
            {
                return $"{stars} ★";
            }

            return starRatings[stars.ToString()][languageCode].ToString();
        }

        private static string GetLanguageCode(LanguageOptions language)
        {
            return language switch
            {
                LanguageOptions.Japanese => "ja",
                LanguageOptions.English => "en",
                LanguageOptions.French => "fr",
                LanguageOptions.Italian => "it",
                LanguageOptions.German => "de",
                LanguageOptions.Spanish => "es",
                LanguageOptions.Korean => "ko",
                LanguageOptions.ChineseS => "zh-Hans",
                LanguageOptions.ChineseT => "zh-Hant",
                _ => "en"
            };
        }
    }
}