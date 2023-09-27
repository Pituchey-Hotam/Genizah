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
    internal class NameInfo
    {
        /// Internal Id representing this name
        public string Id { get; }

        /// Display name for UI
        public string DisplayName { get; }

        /// Pattern to be used for searching for the name
        public Regex FindPattern { get; }

        /// Default options for replacing the name
        public string[] ReplacementOptions { get; }

        public NameInfo(string id, string displayName, string findPattern, string[] replacementOptions)
        {
            Id = id;
            DisplayName = displayName;
            string regex = @"\b" + string.Join(DIACRITICS_PATTERN, findPattern.ToCharArray()) + @"\b";
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

        /// <summary>
        /// List of holy names to be handled by the add-in
        /// </summary>
        public static NameInfo[] names =
        {
            new NameInfo("havaya", "שם הויה", "יהוה", new string[] { "ה'", "ד'" }),
            new NameInfo("adnut", "אדני", "אדני", new string[] { "-דני", "א-דני" }),
            new NameInfo("el", "אל", "אל", new string[] { "-ל", "קל" }),
            new NameInfo("eloha", "אלוה", "אלוה", new string[] { "-לוה", "א-לוה" }),
            new NameInfo("elohim", "אלהים", "אלהים", new string[] { "-להים", "א-להים", "אלקים" }),
            new NameInfo("ehye", "אהיה", "אהיה", new string[] { "-היה", "א-היה"}),
            new NameInfo("shaday", "שדי", "שדי", new string[] { "ש-די" }),
            new NameInfo("tsvaot", "צבאות", "צבאות", new string[] { "צב-ות", "צב-אות" })
        };
    }

}
