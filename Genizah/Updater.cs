using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Genizah.Core;
using Genizah.Scanning;

namespace Genizah
{
    /// <summary>
    /// Updates the add-in once per Word session (docs/RELEASING.md). VSTO's own update check is off,
    /// so Word never waits on a download at startup. Instead, a while after startup, this reads
    /// release.json from the update site; if a newer version is out, it downloads that version's
    /// model graph into the store first, and only then installs the version with VSTOInstaller.
    /// The new code and its graph take effect together at the next Word start. Any failure leaves
    /// the installed version as it is, until the next session tries again.
    /// </summary>
    internal static class Updater
    {
        /// <summary>The ClickOnce deployment: Genizah.csproj's InstallUrl.</summary>
        public const string Site = "https://pituchey-hotam.github.io/Genizah/";

        /// <summary>Lets Word, the model load and the user's first steps go first.</summary>
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Checks for an update and installs it, unless the user turned updates off or this isn't
        /// an installed copy. Never throws: an update is never worth bothering the user about.
        /// </summary>
        /// <param name="installed">Called, off the UI thread, with the version installed for the next start.</param>
        public static async Task CheckAsync(Action<string> installed)
        {
            try
            {
                if (!Properties.Settings.Default.check_for_updates || !IsInstalled) return;
                await Task.Delay(StartupDelay).ConfigureAwait(false);
                ReleaseInfo latest = await ReadLatest().ConfigureAwait(false);
                if (!IsNewer(latest.Version))
                {
                    Prune();
                    return;
                }
                await ModelLoader.Download(latest).ConfigureAwait(false);
                int exit = await Install().ConfigureAwait(false);
                if (exit != 0)
                {
                    Debug.WriteLine($"Genizah update: VSTOInstaller exited {exit}");
                    return;
                }
                installed(latest.Version);
                Prune(latest.ModelSha256);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Genizah update: " + ex);
            }
        }

        /// <summary>
        /// Deletes the store's graphs but the running version's and <paramref name="next"/>. Only
        /// once an update is settled, and after it: a broken model/manifest.json mustn't stop the
        /// update that would replace it.
        /// </summary>
        private static void Prune(string next = null)
        {
            string current = ModelLoader.Manifest.ModelSha256;
            ModelLoader.Store.Prune(next == null ? new[] { current } : new[] { current, next });
        }

        /// <summary>
        /// True if the add-in runs from the ClickOnce store, as an installed copy does. A
        /// development build runs from its bin folder, and updating that would replace it.
        /// </summary>
        private static bool IsInstalled =>
            ModelLoader.AddInDir.StartsWith(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Apps", "2.0") + "\\",
                StringComparison.OrdinalIgnoreCase);

        private static bool IsNewer(string version)
        {
            Version running = typeof(Updater).Assembly.GetName().Version;
            return Version.TryParse(version, out Version offered)
                && offered > new Version(running.Major, running.Minor, running.Build);
        }

        private static async Task<ReleaseInfo> ReadLatest()
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            // No-cache: Pages' CDN may otherwise serve the release.json from before the last release.
            using (var request = new HttpRequestMessage(HttpMethod.Get, Site + "release.json"))
            {
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                using (HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        return ReleaseInfo.Read(body);
                }
            }
        }

        /// <summary>
        /// Installs the site's current version silently, beside the running one, which stays loaded
        /// until Word restarts. Silent installs need the site on VSTO's inclusion list, which the
        /// first install's trust prompt put there.
        /// </summary>
        private static Task<int> Install() => Task.Run(() =>
        {
            string installer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                                            "microsoft shared", "VSTO", "10.0", "VSTOInstaller.exe");
            var start = new ProcessStartInfo(installer, $"/i \"{Site}Genizah.vsto\" /s")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process process = Process.Start(start))
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        });
    }
}
