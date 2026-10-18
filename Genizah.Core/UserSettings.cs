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

        public UserSettings() => SetDefaults();

        // The serializer doesn't run constructors: a member missing from the file keeps these.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Replacements == null) Replacements = new Dictionary<string, string>();
        }

        private void SetDefaults()
        {
            Replacements = new Dictionary<string, string>();
            CheckForUpdates = true;
            CheckBeforePrinting = true;
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
