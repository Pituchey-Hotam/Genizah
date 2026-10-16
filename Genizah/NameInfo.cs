using System;
using System.Linq;

namespace Genizah
{
    /// <summary>
    /// Represents a holy name which requires censoring.
    /// </summary>
    public class NameInfo
    {
        /// Internal Id representing this name, as Genizah.Core's NameFinder reports it
        public string Id { get; }

        /// Display name for UI
        public string DisplayName { get; }

        /// The name's letters, unpointed: the form the replacement options are written for
        public string BaseForm { get; }

        /// Default options for replacing the name
        public string[] ReplacementOptions { get; }

        public NameInfo(string id, string displayName, string baseForm, string[] replacementOptions)
        {
            Id = id;
            DisplayName = displayName;
            BaseForm = baseForm;
            ReplacementOptions = replacementOptions;
        }

        /// <returns>The preferred replacement set by the user</returns>
        public string getSelectedReplacement()
        {
            string replacement;
            if (AddInSettings.Current.Replacements.TryGetValue(Id, out replacement) && !String.IsNullOrEmpty(replacement))
                return replacement;
            return ReplacementOptions[0];
        }

        /// <summary>
        /// Sets the user's preferred replacement
        /// </summary>
        public void setSelectedReplacement(string replacement)
        {
            AddInSettings.Current.Replacements[Id] = replacement;
        }

        /// <returns>The name with this id</returns>
        public static NameInfo ById(string id)
        {
            return names.Single(n => n.Id == id);
        }

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
