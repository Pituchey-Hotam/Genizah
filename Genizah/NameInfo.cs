﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
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
        public string FindPattern { get; }

        /// Default options for replacing the name
        public string[] ReplacementOptions { get; }

        public NameInfo(string id, string displayName, string findPattern, string[] replacementOptions)
        {
            Id = id;
            DisplayName = displayName;
            FindPattern = findPattern;
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

        /// <summary>
        /// List of holy names to be handled by the add-in, sorted by length.
        /// </summary>
        public static NameInfo[] names =
        {
            new NameInfo("tsvaot", "צבאות", "צבאות", new string[] { "צב-ות", "צב-אות" }),
            new NameInfo("elohim", "אלהים", "אלהים", new string[] { "-להים", "א-להים", "אלקים" }),
            new NameInfo("havaya", "שם הויה", "יהוה", new string[] { "ה'", "ד'" }),
            new NameInfo("adnut", "אדני", "אדני", new string[] { "-דני", "א-דני" }),
            new NameInfo("eloha", "אלוה", "אלוה", new string[] { "-לוה", "א-לוה" }),
            new NameInfo("ehye", "אהיה", "אהיה", new string[] { "-היה", "א-היה"}),
            new NameInfo("shaday", "שדי", "שדי", new string[] { "ש-די" }),
            new NameInfo("el", "אל", "אל", new string[] { "-ל", "א-ל", "קל" })
        };
    }

}
