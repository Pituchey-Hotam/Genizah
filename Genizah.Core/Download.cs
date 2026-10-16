using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Genizah.Core
{
    /// <summary>
    /// Downloads that follow redirects themselves, resume, and verify. The HttpClient passed in
    /// should not follow redirects: they are followed here, so a range request reaches the final
    /// server. (GitHub redirects a release download to a signed URL that expires, so every attempt
    /// starts again from the github.com URL.)
    /// </summary>
    public static class Download
    {
        /// <summary>How long a download may go without receiving anything before it fails.</summary>
        public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        private const int MaxRedirects = 5;

        /// <summary>A small file, such as a manifest, whole. Throws if it is larger than <paramref name="maxBytes"/>.</summary>
        public static async Task<byte[]> BytesAsync(HttpClient http, Uri url, long maxBytes, CancellationToken cancel = default)
        {
            var bytes = new MemoryStream();
            await Fetch(http, url, bytes, maxBytes, cancel).ConfigureAwait(false);
            return bytes.ToArray();
        }

        /// <summary>
        /// Downloads <paramref name="url"/> to <paramref name="part"/>, resuming what is there with
        /// a range request (and starting over if the server sends the whole file instead), then
        /// checks it against <paramref name="size"/> and <paramref name="sha256"/>. A download that
        /// breaks off keeps its bytes for the next attempt; one that arrives whole but wrong is
        /// discarded, and throws <see cref="InvalidDataException"/>.
        /// </summary>
        public static async Task ToFileAsync(HttpClient http, Uri url, string part, long size, byte[] sha256, CancellationToken cancel = default)
        {
            using (var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1 << 16, useAsync: true))
            {
                if (file.Length > size) file.SetLength(0);
                if (file.Length < size)
                    await Fetch(http, url, file, size, cancel).ConfigureAwait(false);
                if (file.Length < size)
                    throw new IOException($"{url} ended after {file.Length:N0} of {size:N0} bytes");
                file.Position = 0;
                byte[] got;
                using (var sha = SHA256.Create())
                    got = sha.ComputeHash(file);
                if (!got.SequenceEqual(sha256))
                {
                    file.SetLength(0);
                    throw new InvalidDataException($"{url} doesn't have the sha256 its manifest gives");
                }
            }
        }

        /// <summary>
        /// Appends what's missing from <paramref name="file"/>, or rewrites it if the server won't
        /// resume. Throws <see cref="InvalidDataException"/> past <paramref name="maxBytes"/>.
        /// </summary>
        private static async Task Fetch(HttpClient http, Uri url, Stream file, long maxBytes, CancellationToken cancel)
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

                // The file is already whole: verifying it is all that's left.
                if (have > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) return;
                response.EnsureSuccessStatusCode();
                ContentRangeHeaderValue range = response.Content.Headers.ContentRange;
                bool resumed = have > 0 && response.StatusCode == HttpStatusCode.PartialContent && range?.From == have;
                if (!resumed)
                {
                    file.SetLength(0);
                    have = 0;
                }
                file.Position = have;

                using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    var buffer = new byte[1 << 16];
                    long done = have;
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
                        if (done + read > maxBytes)
                        {
                            file.SetLength(0);
                            throw new InvalidDataException($"{url} is larger than {maxBytes:N0} bytes");
                        }
                        await file.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                        done += read;
                    }
                }
                await file.FlushAsync(cancel).ConfigureAwait(false);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }
}
