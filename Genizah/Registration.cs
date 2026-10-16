using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Genizah
{
    /// <summary>
    /// The per-user registry values that make Word load a version folder: the add-in's
    /// registration, and VSTO's inclusion list, which trusts a manifest URL signed by a key.
    /// tools/dist/setup.iss writes the same values; docs/RELEASING.md ("On disk") describes them.
    /// </summary>
    internal static class Registration
    {
        private const string AddInKey = @"Software\Microsoft\Office\Word\Addins\Genizah";
        private const string InclusionKey = @"Software\Microsoft\VSTO\Security\Inclusion";
        private const string Vstolocal = "|vstolocal";

        /// <summary>The URL VSTO knows a version folder's deployment manifest by.</summary>
        public static string ManifestUrl(string versionDir) =>
            new Uri(Path.Combine(versionDir, Core.VersionStore.DeploymentManifestName)).AbsoluteUri;

        /// <summary>The folder Word loads the add-in from, or null if it isn't registered from a folder.</summary>
        public static string RegisteredDir()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AddInKey))
            {
                if (!(key?.GetValue("Manifest") is string manifest)) return null;
                if (manifest.EndsWith(Vstolocal, StringComparison.OrdinalIgnoreCase))
                    manifest = manifest.Substring(0, manifest.Length - Vstolocal.Length);
                return Uri.TryCreate(manifest, UriKind.Absolute, out Uri url) && url.IsFile
                    ? Path.GetDirectoryName(url.LocalPath)
                    : null;
            }
        }

        /// <summary>Puts a version folder's manifest on VSTO's inclusion list, trusted when signed by <paramref name="publicKeyXml"/>.</summary>
        public static void Trust(string versionDir, string publicKeyXml)
        {
            string url = ManifestUrl(versionDir);
            using (RegistryKey inclusion = Registry.CurrentUser.CreateSubKey(InclusionKey))
            {
                foreach (string name in inclusion.GetSubKeyNames())
                    using (RegistryKey entry = inclusion.OpenSubKey(name))
                        if (string.Equals(entry?.GetValue("Url") as string, url, StringComparison.OrdinalIgnoreCase)
                            && entry.GetValue("PublicKey") as string == publicKeyXml)
                            return;
                using (RegistryKey entry = inclusion.CreateSubKey(Guid.NewGuid().ToString()))
                {
                    entry.SetValue("Url", url);
                    entry.SetValue("PublicKey", publicKeyXml);
                }
            }
        }

        /// <summary>
        /// Makes Word load the add-in from <paramref name="versionDir"/> from its next start. This one
        /// value is the switch between versions.
        /// </summary>
        public static void Switch(string versionDir)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AddInKey))
            {
                key.SetValue("Manifest", ManifestUrl(versionDir) + Vstolocal);
                key.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
            }
        }

        /// <summary>Removes the inclusion entries for version folders under <paramref name="root"/> that are gone.</summary>
        public static void ForgetMissing(string root)
        {
            string prefix = new Uri(root.TrimEnd('\\') + "\\").AbsoluteUri;
            using (RegistryKey inclusion = Registry.CurrentUser.OpenSubKey(InclusionKey, writable: true))
            {
                if (inclusion == null) return;
                foreach (string name in inclusion.GetSubKeyNames())
                {
                    string url;
                    using (RegistryKey entry = inclusion.OpenSubKey(name))
                        url = entry?.GetValue("Url") as string;
                    if (url == null || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) || !File.Exists(parsed.LocalPath))
                        inclusion.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                }
            }
        }
    }
}
