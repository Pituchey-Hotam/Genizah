using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>ModelStore: downloads that resume, follow redirects and verify, against a fake server.</summary>
    public class ModelStoreTests : IDisposable
    {
        private static readonly byte[] Graph = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
        private static readonly ReleaseInfo Model = new ReleaseInfo("1.2.3", "model.onnx", Sha256(Graph));
        private static readonly Uri Url = ModelStore.ReleaseUrl("https://github.com/owner/repo/releases/download/", Model);

        private readonly string root = Path.Combine(Path.GetTempPath(), "genizah-store-" + Guid.NewGuid().ToString("N"));
        private readonly ModelStore store;

        public ModelStoreTests() => store = new ModelStore(root);

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        [Fact]
        public void ReleaseUrlNamesTheModelRelease()
        {
            Assert.Equal($"https://github.com/owner/repo/releases/download/model-{Model.ModelSha256.Substring(0, 12)}/model.onnx", Url.ToString());
        }

        [Fact]
        public async Task DownloadsVerifiesAndReportsProgress()
        {
            var server = new FakeServer(Graph);
            var reports = new List<(long Done, long Total)>();
            string path = await store.DownloadAsync(server.Client, Url, Model, new Sync<(long, long)>(reports.Add));

            Assert.Equal(store.PathFor(Model), path);
            Assert.Equal(Graph, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".part"));
            Assert.Equal((Graph.Length, Graph.Length), reports.Last());
            Assert.Equal(path, store.Find(Model));
        }

        [Fact]
        public async Task AGraphInTheStoreIsNotDownloadedAgain()
        {
            var server = new FakeServer(Graph);
            await store.DownloadAsync(server.Client, Url, Model);
            await store.DownloadAsync(server.Client, Url, Model);
            Assert.Single(server.Requests);
        }

        [Fact]
        public async Task ResumesAPartialDownloadWithARangeRequest()
        {
            WritePart(Graph.Take(100_000).ToArray());
            var server = new FakeServer(Graph);
            string path = await store.DownloadAsync(server.Client, Url, Model);

            Assert.Equal(100_000, server.Requests.Single().Range?.Ranges.Single().From);
            Assert.Equal(Graph.Length - 100_000, server.BytesSent);
            Assert.Equal(Graph, File.ReadAllBytes(path));
        }

        [Fact]
        public async Task StartsOverIfTheServerSendsTheWholeFile()
        {
            WritePart(Encoding.ASCII.GetBytes("not the start of the graph"));
            var server = new FakeServer(Graph) { IgnoreRange = true };
            string path = await store.DownloadAsync(server.Client, Url, Model);
            Assert.Equal(Graph, File.ReadAllBytes(path));
        }

        [Fact]
        public async Task AWholePartFileIsOnlyVerified()
        {
            WritePart(Graph);
            var server = new FakeServer(Graph);
            string path = await store.DownloadAsync(server.Client, Url, Model);
            Assert.Equal(0, server.BytesSent);
            Assert.Equal(Graph, File.ReadAllBytes(path));
        }

        [Fact]
        public async Task FollowsRedirectsKeepingTheRange()
        {
            WritePart(Graph.Take(5_000).ToArray());
            var server = new FakeServer(Graph) { RedirectTo = new Uri("https://cdn.example/signed?sig=abc") };
            string path = await store.DownloadAsync(server.Client, Url, Model);

            Assert.Equal(new[] { Url.ToString(), "https://cdn.example/signed?sig=abc" }, server.Requests.Select(r => r.Url.ToString()));
            Assert.All(server.Requests, r => Assert.Equal(5_000, r.Range?.Ranges.Single().From));
            Assert.Equal(Graph, File.ReadAllBytes(path));
        }

        [Fact]
        public async Task RejectsAGraphWithTheWrongHash()
        {
            byte[] wrong = (byte[])Graph.Clone();
            wrong[1234] ^= 1;
            var server = new FakeServer(wrong);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync(server.Client, Url, Model));
            Assert.Null(store.Find(Model));

            // The next attempt starts from scratch, not from the rejected bytes.
            var good = new FakeServer(Graph);
            await store.DownloadAsync(good.Client, Url, Model);
            Assert.Null(good.Requests.Single().Range);
        }

        [Fact]
        public async Task AFailedRequestThrowsAndKeepsNothing()
        {
            var server = new FakeServer(Graph) { Status = HttpStatusCode.NotFound };
            await Assert.ThrowsAsync<HttpRequestException>(() => store.DownloadAsync(server.Client, Url, Model));
            Assert.Null(store.Find(Model));
        }

        [Fact]
        public async Task PruneKeepsOnlyTheNamedGraphs()
        {
            await store.DownloadAsync(new FakeServer(Graph).Client, Url, Model);
            string other = Path.Combine(root, new string('a', 64));
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "model.onnx.part"), "abandoned");

            store.Prune(Model.ModelSha256);
            Assert.NotNull(store.Find(Model));
            Assert.False(Directory.Exists(other));

            store.Prune();
            Assert.Null(store.Find(Model));
        }

        [Fact]
        public void ReadsReleaseJson()
        {
            string json = "{\"version\": \"1.0.1\", \"model_file\": \"model.int8-int4emb.onnx\", \"model_sha256\": \"" + Model.ModelSha256 + "\"}";
            ReleaseInfo r = ReleaseInfo.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));
            Assert.Equal("1.0.1", r.Version);
            Assert.Equal("model.int8-int4emb.onnx", r.ModelFile);
            Assert.Equal(Model.ModelSha256, r.ModelSha256);
        }

        [Fact]
        public void ReadsReleaseJsonWithAByteOrderMark()
        {
            string json = "{\"model_file\": \"model.onnx\", \"model_sha256\": \"" + Model.ModelSha256 + "\"}";
            byte[] bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(json)).ToArray();
            Assert.Equal("model.onnx", ReleaseInfo.Read(new MemoryStream(bytes)).ModelFile);
        }

        [Fact]
        public void ReadsTheShippedManifest()
        {
            ReleaseInfo m = ReleaseInfo.Load(TestData.Path("model", "manifest.json"));
            Assert.Null(m.Version);
            Assert.EndsWith(".onnx", m.ModelFile);
        }

        [Theory]
        [InlineData("../evil.onnx")]
        [InlineData("..\\evil.onnx")]
        [InlineData("dir/model.onnx")]
        [InlineData("C:\\model.onnx")]
        [InlineData("..")]
        [InlineData("")]
        public void RejectsAModelFileThatIsNotAPlainName(string file)
        {
            Assert.Throws<InvalidDataException>(() => new ReleaseInfo("1.0.0", file, Model.ModelSha256));
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("590E92A925FAB246A46A91AB37F17F30D4A4D690AAEEEA7D9E6EC7897FB4A3DC")]
        [InlineData("../../../../../../../../../../../../../../../../../../../../../../.")]
        public void RejectsAMalformedSha256(string sha)
        {
            Assert.Throws<InvalidDataException>(() => new ReleaseInfo("1.0.0", "model.onnx", sha));
        }

        private void WritePart(byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(store.PathFor(Model)));
            File.WriteAllBytes(store.PathFor(Model) + ".part", bytes);
        }

        private static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }

        /// <summary>Reports synchronously, unlike Progress&lt;T&gt;, so the test sees every report before it asserts.</summary>
        private sealed class Sync<T> : IProgress<T>
        {
            private readonly Action<T> report;
            public Sync(Action<T> report) => this.report = report;
            public void Report(T value) => report(value);
        }

        /// <summary>Serves one file, honouring Range unless told not to, optionally behind one redirect.</summary>
        private sealed class FakeServer : HttpMessageHandler
        {
            private readonly byte[] content;
            public FakeServer(byte[] content)
            {
                this.content = content;
                Client = new HttpClient(this);
            }

            public HttpClient Client { get; }
            public bool IgnoreRange { get; set; }
            public Uri RedirectTo { get; set; }
            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
            public List<(Uri Url, System.Net.Http.Headers.RangeHeaderValue Range)> Requests { get; } = new List<(Uri, System.Net.Http.Headers.RangeHeaderValue)>();
            public long BytesSent { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add((request.RequestUri, request.Headers.Range));
                if (RedirectTo != null && request.RequestUri != RedirectTo)
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    redirect.Headers.Location = RedirectTo;
                    return Task.FromResult(redirect);
                }
                if (Status != HttpStatusCode.OK)
                    return Task.FromResult(new HttpResponseMessage(Status));

                long from = IgnoreRange ? 0 : request.Headers.Range?.Ranges.Single().From ?? 0;
                if (from >= content.Length && from > 0)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
                byte[] body = content.Skip((int)from).ToArray();
                BytesSent += body.Length;
                var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(body)
                };
                if (from > 0)
                    response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, content.Length - 1, content.Length);
                return Task.FromResult(response);
            }
        }
    }
}
