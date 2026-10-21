using System;
using System.IO;
using System.Text;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>UserSettings: defaults, a round trip, and files that are missing, partial or broken.</summary>
    public class UserSettingsTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "genizah-settings-" + Guid.NewGuid().ToString("N"));
        private string File_ => Path.Combine(dir, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        [Fact]
        public void AMissingFileGivesTheDefaults()
        {
            var settings = UserSettings.Load(File_);
            Assert.True(settings.CheckForUpdates);
            Assert.True(settings.CheckBeforePrinting);
            Assert.False(settings.AboutShown);
            Assert.Empty(settings.Replacements);
        }

        [Fact]
        public void SavedSettingsLoadBack()
        {
            var settings = new UserSettings { CheckForUpdates = false, CheckBeforePrinting = false, AboutShown = true };
            settings.Replacements["havaya"] = "ד'";
            settings.Save(File_);
            settings.Replacements["el"] = "קל";
            settings.Save(File_);

            var loaded = UserSettings.Load(File_);
            Assert.False(loaded.CheckForUpdates);
            Assert.False(loaded.CheckBeforePrinting);
            Assert.True(loaded.AboutShown);
            Assert.Equal("ד'", loaded.Replacements["havaya"]);
            Assert.Equal("קל", loaded.Replacements["el"]);
            Assert.False(File.Exists(File_ + ".tmp"));
        }

        [Fact]
        public void TheFileIsPlainJson()
        {
            var settings = new UserSettings();
            settings.Replacements["el"] = "קל";
            settings.Save(File_);
            string json = File.ReadAllText(File_, Encoding.UTF8);
            Assert.Contains("\"replacements\":{\"el\":\"קל\"}", json);
            Assert.Contains("\"check_for_updates\":true", json);
        }

        [Fact]
        public void AMissingMemberKeepsItsDefault()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(File_, "{\"replacements\":{\"el\":\"קל\"}}");
            var settings = UserSettings.Load(File_);
            Assert.True(settings.CheckForUpdates);
            Assert.True(settings.CheckBeforePrinting);
            Assert.False(settings.AboutShown);
            Assert.Equal("קל", settings.Replacements["el"]);

            File.WriteAllText(File_, "{\"check_for_updates\":false}");
            settings = UserSettings.Load(File_);
            Assert.False(settings.CheckForUpdates);
            Assert.Empty(settings.Replacements);
        }

        [Fact]
        public void AByteOrderMarkIsAccepted()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(File_, "{\"check_for_updates\":false}", new UTF8Encoding(true));
            Assert.False(UserSettings.Load(File_).CheckForUpdates);
        }

        [Fact]
        public void ABrokenFileGivesTheDefaults()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(File_, "{\"check_for_updates\":fal");
            Assert.True(UserSettings.Load(File_).CheckForUpdates);
        }
    }
}
