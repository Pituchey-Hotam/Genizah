using System;
using System.IO;
using Genizah.Core;

namespace Genizah
{
    /// <summary>
    /// The user's settings, in %LOCALAPPDATA%\Genizah\settings.json, which every version of the
    /// add-in shares. Read and changed on the UI thread; the updater only reads a bool.
    /// </summary>
    internal static class AddInSettings
    {
        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Genizah", "settings.json");

        public static UserSettings Current { get; } = UserSettings.Load(FilePath);

        public static void Save() => Current.Save(FilePath);
    }
}
