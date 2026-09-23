using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Genizah
{
    /// <summary>
    /// Represents a holy name which requires censoring.
    /// </summary>
    public class NameInfo
    {
        /// Internal Id representing this name
        public string Id { get; }

        /// Display name for UI
        public string DisplayName { get; }

        /// Pattern to be used for searching for the name
        public Regex FindPattern { get; }

        /// Default options for replacing the name
        public string[] ReplacementOptions { get; }

        public NameInfo(string id, string displayName, string[] findPattern, string[] replacementOptions)
        {
            Id = id;
            DisplayName = displayName;
            // Wrap the regex with lookarounds ensuring we're at a word boundary,
            // allow for hebrew prefixes, and Nikud.
            string regex = @"(?<=^|\P{L})" + WithNikud(HEBREW_PREFIXES.Concat(findPattern)) + @"(?=$|\P{L})";
            FindPattern = new Regex(regex, RegexOptions.Compiled);
            ReplacementOptions = replacementOptions;
        }

        public string SettingsKey { get { return "name_replacement_" + Id; } }

        /// <returns>The preferred replacement set by the user</returns>
        public string getSelectedReplacement()
        {
            string replacement = (string)Properties.Settings.Default[SettingsKey];
            if (String.IsNullOrEmpty(replacement)) return ReplacementOptions[0];
            else return replacement;
        }

        /// <summary>
        /// Sets the user's preferred replacement
        /// </summary>
        public void setSelectedReplacement(string replacement)
        {
            Properties.Settings.Default[SettingsKey] = replacement;
        }

        public static string DIACRITICS_PATTERN = "[\u0591-\u05c2]*";
        public static string[] HEBREW_PREFIXES = new string[] { "[בהוכלמ]?" };

        private static string WithNikud(IEnumerable<string> letters)
        {
            return string.Join(DIACRITICS_PATTERN, letters) + DIACRITICS_PATTERN;
        }

        /// <summary>
        /// List of holy names to be handled by the add-in
        /// </summary>
        public static NameInfo[] names =
        {
            new NameInfo("havaya", "שם הויה", new string[] { "י", "ה", "ו", "ה" }, new string[] { "ה'", "ד'" }),
            new NameInfo("adnut", "אדני", new string[] { "א", "ד", "נ", "י" }, new string[] { "-דני", "א-דני" }),
            new NameInfo("el", "אל", new string[] { "א", "ל" }, new string[] { "-ל", "קל" }),
            new NameInfo("eloha", "אלוה", new string[] { "א", "ל", "ו?", "ה" }, new string[] { "-לוה", "א-לוה" }),
            new NameInfo("elohim", "אלהים", new string[] { "א", "ל", "ו?", "ה", "י?", "ם" }, new string[] { "-להים", "א-להים", "אלקים" }),
            new NameInfo("ehye", "אהיה", new string[] { "א", "ה", "י", "ה" }, new string[] { "-היה", "א-היה"}),
            new NameInfo("shaday", "שדי", new string[] { "ש", "ד", "י" }, new string[] { "ש-די" }),
            new NameInfo("tsvaot", "צבאות", new string[] { "צ", "ב", "א", "ו?", "ת" }, new string[] { "צב-ות", "צב-אות" })
        };
    }

}
