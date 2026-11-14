using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Genizah.Core
{
    /// <summary>
    /// The user's choices, kept in one JSON file shared by every installed version of the add-in
    /// (.NET's user settings are kept per install location and per Word build).
    /// </summary>
    [DataContract]
    public sealed class UserSettings
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(
            typeof(UserSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        /// <summary>A name's id to the replacement the user chose; a name missing here takes its first option.</summary>
        [DataMember(Name = "replacements")] public Dictionary<string, string> Replacements { get; private set; }

        [DataMember(Name = "check_for_updates")] public bool CheckForUpdates { get; set; }
        [DataMember(Name = "check_before_printing")] public bool CheckBeforePrinting { get; set; }

        /// <summary>Whether the About dialog has opened by itself, on the user's first use of the add-in.</summary>
        [DataMember(Name = "about_shown")] public bool AboutShown { get; set; }

        /// <summary>Whether the review pane offers, after a review, to send the user's decisions (<see cref="Contributions"/>).</summary>
        [DataMember(Name = "offer_contributions")] public bool OfferContributions { get; set; }

        /// <summary>Whether a finished review shares its decisions without asking; chosen by the user, never by default.</summary>
        [DataMember(Name = "share_automatically")] public bool ShareAutomatically { get; set; }

        /// <summary>Whether the reporting link's one-time highlight has been shown.</summary>
        [DataMember(Name = "contribution_hint_shown")] public bool ContributionHintShown { get; set; }

        /// <summary>The random id the user's contributions are sent under, or empty before the first.</summary>
        [DataMember(Name = "install_id")] public string InstallId { get; set; }

        /// <summary>A collection server to send contributions to instead of the built-in one; for testing.</summary>
        [DataMember(Name = "contribution_endpoint", EmitDefaultValue = false)] public string ContributionEndpoint { get; set; }

        public UserSettings() => SetDefaults();

        // The serializer doesn't run constructors: a member missing from the file keeps these.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Replacements == null) Replacements = new Dictionary<string, string>();
            if (InstallId == null) InstallId = "";
        }

        private void SetDefaults()
        {
            Replacements = new Dictionary<string, string>();
            CheckForUpdates = true;
            CheckBeforePrinting = true;
            AboutShown = false;
            OfferContributions = true;
            ShareAutomatically = false;
            InstallId = "";
        }

        /// <summary>The settings in <paramref name="path"/>, or the defaults if it is missing or unreadable.</summary>
        public static UserSettings Load(string path)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                // The serializer rejects a UTF-8 byte order mark, which an editor may add.
                int bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
                return (UserSettings)Serializer.ReadObject(new MemoryStream(b, bom, b.Length - bom));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SerializationException)
            {
                return new UserSettings();
            }
        }

        /// <summary>Writes the settings to <paramref name="path"/>, whole or not at all.</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            using (var stream = File.Create(temp))
                Serializer.WriteObject(stream, this);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
