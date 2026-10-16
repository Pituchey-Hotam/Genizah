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
    /// Updates the add-in once per Word session (docs/RELEASING.md). A while after startup it
    /// reads the latest GitHub release's signed Genizah.vsto; if that version is newer, it builds
    /// the version's folder beside the running one (copying unchanged files, downloading the rest,
    /// checking every one against the signed manifests), trusts it, and points Word's registration
    /// at it. The new version, code and model, runs from the next Word start. Any failure leaves
    /// the registered version as it is, until the next session tries again.
    /// </summary>
    internal static class Updater
    {
        /// <summary>The repository's releases. Each holds a version's signed manifests and files.</summary>
        public const string Releases = "https://github.com/Pituchey-Hotam/Genizah/releases/";

        /// <summary>The public half of the key that signs the manifests (Genizah.csproj's ManifestKeyFile).</summary>
        public const string PublicKey = "<RSAKeyValue><Modulus>pP2JOUHp4wFx4GRaTvaJa2OyuE8NpPVIkEkuPKyPmRM1MfpOY0uoN0Tbp0zFjX1Qftw3mQyGWlRyHIEbpsPfpwiC/2s6Erkh5Tt+4ot1SGl0bvaxeOJfBNnPGZRccVZZUlfmqyCmXArOymVhMDJJqnfCM4Wtk8P/FSrdlonHr3U=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        /// <summary>Where the installed versions are: one folder each.</summary>
        public static readonly string VersionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Genizah", "versions");

        /// <summary>Lets Word, the model load and the user's first steps go first.</summary>
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Checks for an update and installs it, unless the user turned updates off or this isn't
        /// an installed copy. Never throws: an update is never worth bothering the user about.
        /// </summary>
        /// <param name="installed">Called, off the UI thread, with the version installed for the next start.</param>
        public static async Task CheckAsync(Action<Version> installed)
        {
            try
            {
                if (!AddInSettings.Current.CheckForUpdates || !IsInstalled) return;
                await Task.Delay(StartupDelay).ConfigureAwait(false);
                // Redirects are left to Download, so a resumed download's range reaches the final server.
                using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) })
                {
                    var store = new VersionStore(VersionsDir, http, new Uri(Releases), PublicKey);
                    using (IDisposable held = store.TryLock())
                    {
                        if (held == null) return;
                        await Update(store, installed).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Genizah update: " + ex);
            }
        }

        private static async Task Update(VersionStore store, Action<Version> installed)
        {
            string running = ModelLoader.AddInDir;
            string registered = Registration.RegisteredDir();
            Release latest = await store.LatestAsync().ConfigureAwait(false);

            // Only ever forward: past the running version, and past one already installed for the next start.
            Version current = RunningVersion;
            if (registered != null && IsVersionDir(registered, out Version next) && next > current)
                current = next;
            if (latest.Version <= current)
            {
                // The previous version's folder goes once the next one has started.
                Clean(store, running, registered, VersionStore.PartialName(latest.Version));
                return;
            }

            BuiltVersion built = await store.BuildAsync(latest, running).ConfigureAwait(false);
            Debug.WriteLine($"Genizah update: built {latest.Version}, copied {built.Copied.Count} files, downloaded {string.Join(", ", built.Downloaded)}");
            Registration.Trust(built.Path, PublicKey);
            Registration.Switch(built.Path);
            installed(latest.Version);
            Clean(store, running, built.Path, null);
        }

        private static void Clean(VersionStore store, string running, string registered, string partial)
        {
            store.Clean(new[] { Path.GetFileName(running), registered == null ? null : Path.GetFileName(registered), partial });
            Registration.ForgetMissing(VersionsDir);
        }

        /// <summary>
        /// True if the add-in runs from a version folder, as an installed copy does. A development
        /// build runs from its bin folder, and isn't updated.
        /// </summary>
        private static bool IsInstalled => IsVersionDir(ModelLoader.AddInDir, out _);

        private static bool IsVersionDir(string dir, out Version version)
        {
            version = null;
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(dir)), Path.GetFullPath(VersionsDir), StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(Path.GetFileName(dir), out version);
        }

        private static Version RunningVersion
        {
            get
            {
                Version v = typeof(Updater).Assembly.GetName().Version;
                return new Version(v.Major, v.Minor, v.Build);
            }
        }
    }
}
