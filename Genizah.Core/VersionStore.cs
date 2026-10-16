using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Genizah.Core
{
    /// <summary>A release's signed deployment manifest, as downloaded.</summary>
    public sealed class Release
    {
        public Release(DeploymentManifest manifest, byte[] bytes)
        {
            Manifest = manifest;
            Bytes = bytes;
        }

        public DeploymentManifest Manifest { get; }
        public byte[] Bytes { get; }
        public Version Version => Manifest.Version;
    }

    /// <summary>What building a version took.</summary>
    public sealed class BuiltVersion
    {
        public BuiltVersion(string path, IReadOnlyList<string> copied, IReadOnlyList<string> downloaded)
        {
            Path = path;
            Copied = copied;
            Downloaded = downloaded;
        }

        /// <summary>The version's folder, holding Genizah.vsto.</summary>
        public string Path { get; }
        /// <summary>Manifest paths of the files taken from the running version.</summary>
        public IReadOnlyList<string> Copied { get; }
        /// <summary>Manifest paths of the files downloaded.</summary>
        public IReadOnlyList<string> Downloaded { get; }
    }

    /// <summary>
    /// The installed versions of the add-in, one folder each: &lt;root&gt;/&lt;MAJOR.MINOR.PATCH&gt;/,
    /// laid out as the build's bin folder (docs/PLAN-updates.md). A version is built in
    /// &lt;version&gt;.partial/ and only renamed to its final name once every file is there and
    /// matches its signed manifest, so a version folder is always complete.
    /// </summary>
    public sealed class VersionStore
    {
        public const string DeploymentManifestName = "Genizah.vsto";

        /// <summary>A manifest larger than this isn't one of ours.</summary>
        private const long MaxManifestBytes = 4 << 20;

        private const string PartialSuffix = ".partial";

        private readonly HttpClient http;
        private readonly Uri releases;
        private readonly string publicKeyXml;

        /// <param name="root">The folder holding the versions.</param>
        /// <param name="http">Should not follow redirects (<see cref="Download"/>).</param>
        /// <param name="releases">The repository's releases, ending in '/': https://github.com/OWNER/REPO/releases/.</param>
        /// <param name="publicKeyXml">The key every manifest must be signed with, as an RSAKeyValue.</param>
        public VersionStore(string root, HttpClient http, Uri releases, string publicKeyXml)
        {
            Root = root;
            this.http = http;
            this.releases = releases;
            this.publicKeyXml = publicKeyXml;
        }

        public string Root { get; }

        public string PathFor(Version version) => Path.Combine(Root, version.ToString());

        /// <summary>The name of the folder a version is built in, for <see cref="Clean"/> to keep.</summary>
        public static string PartialName(Version version) => version + PartialSuffix;

        /// <summary>
        /// The latest release's deployment manifest. Throws <see cref="InvalidDataException"/> if
        /// it isn't signed with the key.
        /// </summary>
        public async Task<Release> LatestAsync(CancellationToken cancel = default)
        {
            byte[] bytes = await Download.BytesAsync(http, new Uri(releases, "latest/download/" + DeploymentManifestName), MaxManifestBytes, cancel).ConfigureAwait(false);
            return new Release(DeploymentManifest.Read(bytes, publicKeyXml), bytes);
        }

        /// <summary>
        /// Builds <paramref name="release"/>'s folder, unless it exists already, and returns it.
        /// Each file is copied from <paramref name="runningDir"/> (the running version's folder) if
        /// the file at the same path there has the manifest's sha256, and downloaded from the
        /// release otherwise. What a failed build already has stays for the next attempt.
        /// Throws <see cref="InvalidDataException"/> if the release doesn't match its signed manifests.
        /// </summary>
        public async Task<BuiltVersion> BuildAsync(Release release, string runningDir, CancellationToken cancel = default)
        {
            string final = PathFor(release.Version);
            var copied = new List<string>();
            var downloaded = new List<string>();
            if (Directory.Exists(final)) return new BuiltVersion(final, copied, downloaded);

            // That exact release, not latest: latest could move to another release mid-build.
            var from = new Uri(releases, $"download/v{release.Version}/");
            ManifestFile pin = release.Manifest.ApplicationManifest;
            byte[] app = await Download.BytesAsync(http, Asset(from, pin), MaxManifestBytes, cancel).ConfigureAwait(false);
            IReadOnlyList<ManifestFile> files = ApplicationManifest.Read(app, release.Manifest, publicKeyXml);

            string partial = final + PartialSuffix;
            Directory.CreateDirectory(partial);
            foreach (ManifestFile file in files)
            {
                cancel.ThrowIfCancellationRequested();
                string target = Local(partial, file);
                if (Matches(target, file)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string part = target + ".part";

                if (runningDir != null && File.Exists(Local(runningDir, file)) && new FileInfo(Local(runningDir, file)).Length == file.Size)
                {
                    File.Copy(Local(runningDir, file), part, overwrite: true);
                    if (Matches(part, file))
                    {
                        Place(part, target);
                        copied.Add(file.Path);
                        continue;
                    }
                    File.Delete(part);
                }
                await Download.ToFileAsync(http, Asset(from, file), part, file.Size, file.Sha256, cancel).ConfigureAwait(false);
                Place(part, target);
                downloaded.Add(file.Path);
            }

            File.WriteAllBytes(Path.Combine(partial, DeploymentManifestName), release.Bytes);
            File.WriteAllBytes(Local(partial, pin), app);

            // Nothing but the version's own files goes into its folder.
            var expected = new HashSet<string>(files.Concat(new[] { pin }).Select(f => Path.GetFullPath(Local(partial, f))), StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(Path.Combine(partial, DeploymentManifestName)),
            };
            foreach (string stray in Directory.GetFiles(partial, "*", SearchOption.AllDirectories))
                if (!expected.Contains(Path.GetFullPath(stray))) File.Delete(stray);

            Directory.Move(partial, final);
            return new BuiltVersion(final, copied, downloaded);
        }

        /// <summary>
        /// Deletes every folder in the store but the ones named (version folders' and
        /// <see cref="PartialName"/>s). A folder in use, by another Word process, stays until a
        /// later clean.
        /// </summary>
        public void Clean(IEnumerable<string> keep)
        {
            if (!Directory.Exists(Root)) return;
            var kept = new HashSet<string>(keep.Where(k => k != null), StringComparer.OrdinalIgnoreCase);
            foreach (string dir in Directory.GetDirectories(Root))
            {
                if (kept.Contains(Path.GetFileName(dir))) continue;
                try { Directory.Delete(dir, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Holds the store for one updater at a time, across Word processes, until disposed.
        /// Null if another one holds it.
        /// </summary>
        public IDisposable TryLock()
        {
            Directory.CreateDirectory(Root);
            try
            {
                return new FileStream(Path.Combine(Root, "update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static Uri Asset(Uri release, ManifestFile file) => new Uri(release, Uri.EscapeDataString(file.AssetName));

        private static string Local(string dir, ManifestFile file) =>
            Path.Combine(dir, file.Path.Replace('\\', Path.DirectorySeparatorChar));

        private static bool Matches(string path, ManifestFile file)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size) return false;
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return sha.ComputeHash(stream).SequenceEqual(file.Sha256);
        }

        private static void Place(string part, string target)
        {
            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
        }
    }
}
