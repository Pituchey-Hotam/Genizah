using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Genizah.Core
{
    /// <summary>
    /// A version of the add-in and the model graph it needs: model/manifest.json beside the add-in
    /// (no version), or release.json on the update site.
    /// </summary>
    [DataContract]
    public sealed class ReleaseInfo
    {
        /// <summary>MAJOR.MINOR.PATCH; null in model/manifest.json.</summary>
        [DataMember(Name = "version")] public string Version { get; private set; }

        /// <summary>The graph's file name, without a directory.</summary>
        [DataMember(Name = "model_file", IsRequired = true)] public string ModelFile { get; private set; }

        /// <summary>The graph's sha256, 64 lowercase hex digits.</summary>
        [DataMember(Name = "model_sha256", IsRequired = true)] public string ModelSha256 { get; private set; }

        public ReleaseInfo(string version, string modelFile, string modelSha256)
        {
            Version = version;
            ModelFile = modelFile;
            ModelSha256 = modelSha256;
            Check();
        }

        /// <summary>Reads release.json or model/manifest.json. Throws if the graph isn't named by a plain file name and a sha256.</summary>
        public static ReleaseInfo Read(Stream json)
        {
            var bytes = new MemoryStream();
            json.CopyTo(bytes);
            // The serializer rejects a UTF-8 byte order mark, which Windows editors like to add.
            byte[] b = bytes.GetBuffer();
            int bom = bytes.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            var info = (ReleaseInfo)new DataContractJsonSerializer(typeof(ReleaseInfo))
                .ReadObject(new MemoryStream(b, bom, (int)bytes.Length - bom));
            info.Check();
            return info;
        }

        public static ReleaseInfo Load(string path)
        {
            using (var stream = File.OpenRead(path))
                return Read(stream);
        }

        // release.json comes from the network: its file name becomes a path in the store.
        private void Check()
        {
            if (ModelSha256 == null || ModelSha256.Length != 64 || !ModelSha256.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                throw new InvalidDataException($"model_sha256 must be 64 lowercase hex digits, not '{ModelSha256}'");
            if (string.IsNullOrEmpty(ModelFile) || ModelFile != Path.GetFileName(ModelFile) || ModelFile.StartsWith(".")
                || ModelFile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"model_file must be a plain file name, not '{ModelFile}'");
        }
    }

    /// <summary>
    /// The model graphs downloaded for the add-in, one folder per sha256:
    /// &lt;root&gt;/&lt;sha256&gt;/&lt;model_file&gt;. A graph is only ever there whole and
    /// verified: it downloads to a .part file beside it, which a later download resumes.
    /// </summary>
    public sealed class ModelStore
    {
        /// <summary>How long a download may go without receiving anything before it fails.</summary>
        public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        /// <summary>How often a download waits for another process that is downloading the same graph.</summary>
        public static readonly TimeSpan BusyPoll = TimeSpan.FromSeconds(2);

        private const int MaxRedirects = 5;

        public ModelStore(string root) => Root = root;

        public string Root { get; }

        public string PathFor(ReleaseInfo model) => Path.Combine(Root, model.ModelSha256, model.ModelFile);

        /// <summary>The graph's path, or null if it isn't in the store.</summary>
        public string Find(ReleaseInfo model) => File.Exists(PathFor(model)) ? PathFor(model) : null;

        /// <summary>
        /// The graph's download: the GitHub release model-&lt;first 12 hex digits of its sha256&gt;
        /// under <paramref name="releasesBase"/> (".../releases/download/").
        /// </summary>
        public static Uri ReleaseUrl(string releasesBase, ReleaseInfo model) =>
            new Uri(new Uri(releasesBase), "model-" + model.ModelSha256.Substring(0, 12) + "/" + Uri.EscapeDataString(model.ModelFile));

        /// <summary>
        /// Puts the graph in the store, unless it is there already, and returns its path. Resumes a
        /// previous attempt's .part file with a range request, and starts over if the server sends
        /// the whole file instead. Throws <see cref="InvalidDataException"/> if what arrived doesn't
        /// match the sha256, and discards it.
        /// <paramref name="http"/> should not follow redirects itself: they are followed here, so
        /// the range request reaches the final server. (GitHub redirects a release download to a
        /// signed URL that expires, so every attempt starts again from <paramref name="url"/>.)
        /// </summary>
        public async Task<string> DownloadAsync(HttpClient http, Uri url, ReleaseInfo model,
                                                IProgress<(long Done, long Total)> progress = null,
                                                CancellationToken cancel = default)
        {
            string final = PathFor(model);
            Directory.CreateDirectory(Path.GetDirectoryName(final));
            string part = final + ".part";
            while (true)
            {
                if (File.Exists(final)) return final;
                FileStream file;
                try
                {
                    file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1 << 16, useAsync: true);
                }
                catch (IOException)
                {
                    // Another process is downloading it: wait for it to finish, or give up the file.
                    await Task.Delay(BusyPoll, cancel).ConfigureAwait(false);
                    continue;
                }
                using (file)
                {
                    if (File.Exists(final)) return final;
                    await Fetch(http, url, file, progress, cancel).ConfigureAwait(false);
                    file.Position = 0;
                    string got;
                    using (var sha = SHA256.Create())
                        got = string.Concat(sha.ComputeHash(file).Select(b => b.ToString("x2")));
                    if (got != model.ModelSha256)
                    {
                        file.SetLength(0);
                        throw new InvalidDataException($"{url} has sha256 {got}, expected {model.ModelSha256}");
                    }
                }
                try
                {
                    File.Move(part, final);
                    return final;
                }
                catch (IOException)
                {
                    // Another process opened the .part in between, and will finish or verify it.
                }
            }
        }

        /// <summary>Appends what's missing from <paramref name="file"/>, or rewrites it if the server won't resume.</summary>
        private static async Task Fetch(HttpClient http, Uri url, FileStream file, IProgress<(long, long)> progress, CancellationToken cancel)
        {
            long have = file.Length;
            HttpResponseMessage response = null;
            try
            {
                for (int redirects = 0; ; redirects++)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (have > 0) request.Headers.Range = new RangeHeaderValue(have, null);
                    response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
                    int status = (int)response.StatusCode;
                    if (status < 300 || status >= 400 || response.Headers.Location == null) break;
                    if (redirects == MaxRedirects) throw new HttpRequestException($"too many redirects from {url}");
                    url = new Uri(url, response.Headers.Location);
                    response.Dispose();
                    response = null;
                }

                // The .part already holds the whole file: verifying it is all that's left.
                if (have > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) return;
                response.EnsureSuccessStatusCode();
                ContentRangeHeaderValue range = response.Content.Headers.ContentRange;
                bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent && range?.From == have;
                if (!resumed)
                {
                    file.SetLength(0);
                    have = 0;
                }
                long total = resumed && range.Length.HasValue ? range.Length.Value : have + (response.Content.Headers.ContentLength ?? -1);
                file.Position = have;

                using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var buffer = new byte[1 << 16];
                    long done = have;
                    progress?.Report((done, total));
                    while (true)
                    {
                        int read;
                        using (var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                        {
                            stall.CancelAfter(StallTimeout);
                            // .NET Framework's response stream ignores the token: disposing it is what stops a read.
                            try
                            {
                                using (stall.Token.Register(() => response.Dispose()))
                                    read = await body.ReadAsync(buffer, 0, buffer.Length, stall.Token).ConfigureAwait(false);
                            }
                            catch (Exception) when (stall.IsCancellationRequested)
                            {
                                cancel.ThrowIfCancellationRequested();
                                throw new TimeoutException($"{url} sent nothing for {StallTimeout.TotalSeconds:0} s");
                            }
                        }
                        if (read == 0) break;
                        await file.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                        done += read;
                        progress?.Report((done, total));
                    }
                }
                await file.FlushAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Deletes every graph but the ones named, and any abandoned .part files with them. A graph
        /// another Word process has open stays until a later prune.
        /// </summary>
        public void Prune(params string[] keepSha256)
        {
            if (!Directory.Exists(Root)) return;
            foreach (string dir in Directory.GetDirectories(Root))
            {
                if (keepSha256.Contains(Path.GetFileName(dir))) continue;
                try { Directory.Delete(dir, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
