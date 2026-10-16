using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Genizah.Core.Tests
{
    /// <summary>
    /// Serves files by URL, honouring Range unless told not to, the way GitHub serves release
    /// assets: optionally behind a redirect to a CDN.
    /// </summary>
    internal sealed class FakeServer : HttpMessageHandler
    {
        public const string Cdn = "https://cdn.example/";

        public FakeServer() => Client = new HttpClient(this);

        public FakeServer(Uri url, byte[] content) : this() => Files[url.ToString()] = content;

        public HttpClient Client { get; }
        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();
        public bool IgnoreRange { get; set; }
        /// <summary>Redirects every request to the same path under <see cref="Cdn"/>.</summary>
        public bool Redirect { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        /// <summary>A URL's next response breaks off after this many bytes, once.</summary>
        public Dictionary<string, int> BreakAfter { get; } = new Dictionary<string, int>();
        public List<(Uri Url, RangeHeaderValue Range)> Requests { get; } = new List<(Uri, RangeHeaderValue)>();
        public long BytesSent { get; private set; }

        /// <summary>The requests that reached content (after any redirect), by path.</summary>
        public IEnumerable<string> Served => Requests.Where(r => !Redirect || r.Url.ToString().StartsWith(Cdn)).Select(r => r.Url.AbsolutePath);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri, request.Headers.Range));
            string url = request.RequestUri.ToString();
            if (Redirect && !url.StartsWith(Cdn))
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri(Cdn + request.RequestUri.PathAndQuery.TrimStart('/'));
                return Task.FromResult(redirect);
            }
            if (Redirect) url = "https://github.com/" + url.Substring(Cdn.Length);
            if (Status != HttpStatusCode.OK)
                return Task.FromResult(new HttpResponseMessage(Status));
            if (!Files.TryGetValue(url, out byte[] content))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            long from = IgnoreRange ? 0 : request.Headers.Range?.Ranges.Single().From ?? 0;
            if (from >= content.Length && from > 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            byte[] body = content.Skip((int)from).ToArray();
            HttpContent payload;
            if (BreakAfter.TryGetValue(url, out int cut))
            {
                BreakAfter.Remove(url);
                payload = new StreamContent(new BreakingStream(body, cut));
                BytesSent += Math.Min(cut, body.Length);
            }
            else
            {
                payload = new ByteArrayContent(body);
                BytesSent += body.Length;
            }
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = payload };
            if (from > 0)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, content.Length - 1, content.Length);
            return Task.FromResult(response);
        }

        /// <summary>Reads like the body, then fails, as a dropped connection does.</summary>
        private sealed class BreakingStream : MemoryStream
        {
            private readonly int cut;
            public BreakingStream(byte[] body, int cut) : base(body) => this.cut = cut;

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Position >= cut) throw new IOException("connection dropped");
                return base.Read(buffer, offset, (int)Math.Min(count, cut - Position));
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                Task.FromResult(Read(buffer, offset, count));
        }
    }
}
