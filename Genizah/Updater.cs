using System;
using System.IO;
using System.Net;
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

        /// <summary>The public half of the private release key used by CD (docs/RELEASING.md).</summary>
        public const string PublicKey = "<RSAKeyValue><Modulus>l+KhMOR15/7DRDaIDYDncJknBnBHJ52rXYN8fC53tVxjUh6Bq14yASSLZgpNKRQPx3KAPb0vMtvCjZjqycWIRupIWsE+KGVYWM9DRkCwfhUp0VXT2AYrrUwiI/MeZ5P3bVG4Q8F7hVn65pQj5iAG6rheJQ6JXBu6MlgX76KRodRQg3IQIBRZ8dxNJJTNiNLOujoKKVe0gSxcdnegrwvaRzYcQpqM4k0IBx315CrwLhGhzYCJNb13y09TS7yCYkpuV8z28RRneRy5M0guyWzVyk6ZcHXjVP4ML64zuwD9BUMUj2BhwSkmTWXLpJ5fDYoP1IedqG3uX/zWrMvFWbCmKMMtUeSjE0byLuvPNdtu9YbvIQd5bXHi3Xg3+2jHzCjRX2RzjrgkHsaz7sX8nbpTQs9LF2TzxkCib5GSxkX3gi0D9RyXHCWzycrca9w9pYCsgPxWeNcHecwsfY0zKbU0n3mxM/ooZSq18EgtmuzZL6GNlWJQSSaRprQCARBe3GiJ</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        /// <summary>Where the installed versions are: one folder each.</summary>
        public static readonly string VersionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Genizah", "versions");

        /// <summary>Each check's outcome, one line per check, since a failure is never shown to the user.</summary>
        public static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Genizah", "update.log");

        /// <summary>Past this size the log starts over, so it can't grow without end.</summary>
        private const long MaxLogBytes = 256 * 1024;

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
                if (!IsInstalled) return;
                if (!AddInSettings.Current.CheckForUpdates)
                {
                    Log(UpdateOutcome.Disabled, null, "");
                    return;
                }
                await Task.Delay(StartupDelay).ConfigureAwait(false);
                // An add-in's AppDomain gets .NET's legacy protocols (SSL 3, TLS 1.0), which GitHub refuses.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // Redirects are left to Download, so a resumed download's range reaches the final server.
                using (var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) })
                {
                    var store = new VersionStore(VersionsDir, http, new Uri(Releases), PublicKey);
                    using (IDisposable held = store.TryLock())
                    {
                        if (held == null)
                        {
                            Log(UpdateOutcome.Busy, null, "");
                            return;
                        }
                        await Update(store, installed).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(UpdateOutcome.Failed, null, ex.ToString());
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
                Log(UpdateOutcome.UpToDate, latest.Version, "");
                return;
            }

            BuiltVersion built = await store.BuildAsync(latest, running).ConfigureAwait(false);
            Registration.Trust(built.Path, PublicKey);
            Registration.Switch(built.Path);
            installed(latest.Version);
            Clean(store, running, built.Path, null);
            Log(UpdateOutcome.Installed, latest.Version, $"copied {built.Copied.Count} files, downloaded {string.Join(", ", built.Downloaded)}");
        }

        /// <summary>Records a session's check in <see cref="LogPath"/>. Never throws.</summary>
        private static void Log(UpdateOutcome outcome, Version latest, string detail)
        {
            try
            {
                UpdateLog.Append(LogPath, new UpdateCheck(DateTime.Now, RunningVersion, outcome, latest, detail), MaxLogBytes);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// The version Word will load from its next start, if that's newer than the running one:
        /// an update this or another session installed. Null otherwise.
        /// </summary>
        public static Version NextStartVersion
        {
            get
            {
                try
                {
                    string registered = Registration.RegisteredDir();
                    return registered != null && IsVersionDir(registered, out Version next) && next > RunningVersion ? next : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
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
        public static bool IsInstalled => IsVersionDir(ModelLoader.AddInDir, out _);

        private static bool IsVersionDir(string dir, out Version version)
        {
            version = null;
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(dir)), Path.GetFullPath(VersionsDir), StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(Path.GetFileName(dir), out version);
        }

        public static Version RunningVersion
        {
            get
            {
                Version v = typeof(Updater).Assembly.GetName().Version;
                return new Version(v.Major, v.Minor, v.Build);
            }
        }
    }
}
