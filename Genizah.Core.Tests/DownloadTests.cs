using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>Download: resuming, redirects and verification, against a fake server.</summary>
    public class DownloadTests : IDisposable
    {
        private static readonly byte[] Content = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray();
        private static readonly byte[] Sha = TestManifests.Sha256(Content);
        private static readonly Uri Url = new Uri("https://github.com/owner/repo/releases/download/v1.0.0/model.graph.onnx");

        private readonly string dir = Path.Combine(Path.GetTempPath(), "genizah-download-" + Guid.NewGuid().ToString("N"));
        private string Part => Path.Combine(dir, "graph.onnx.part");

        public DownloadTests() => Directory.CreateDirectory(dir);

        public void Dispose() => Directory.Delete(dir, recursive: true);

        [Fact]
        public async Task DownloadsAndVerifies()
        {
            var server = new FakeServer(Url, Content);
            await Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha);
            Assert.Equal(Content, File.ReadAllBytes(Part));
            Assert.Null(server.Requests.Single().Range);
        }

        [Fact]
        public async Task ResumesWithARangeRequest()
        {
            File.WriteAllBytes(Part, Content.Take(100_000).ToArray());
            var server = new FakeServer(Url, Content);
            await Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha);

            Assert.Equal(100_000, server.Requests.Single().Range?.Ranges.Single().From);
            Assert.Equal(Content.Length - 100_000, server.BytesSent);
            Assert.Equal(Content, File.ReadAllBytes(Part));
        }

        [Fact]
        public async Task StartsOverIfTheServerSendsTheWholeFile()
        {
            File.WriteAllBytes(Part, Encoding.ASCII.GetBytes("not the start of the file"));
            var server = new FakeServer(Url, Content) { IgnoreRange = true };
            await Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha);
            Assert.Equal(Content, File.ReadAllBytes(Part));
        }

        [Fact]
        public async Task AWholeFileIsOnlyVerified()
        {
            File.WriteAllBytes(Part, Content);
            var server = new FakeServer(Url, Content);
            await Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha);
            Assert.Empty(server.Requests);
        }

        [Fact]
        public async Task AFileLargerThanItsSizeStartsOver()
        {
            File.WriteAllBytes(Part, Content.Concat(new byte[10]).ToArray());
            var server = new FakeServer(Url, Content);
            await Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha);
            Assert.Null(server.Requests.Single().Range);
            Assert.Equal(Content, File.ReadAllBytes(Part));
        }

        [Fact]
        public async Task FollowsRedirectsKeepingTheRange()
        {
            File.WriteAllBytes(Part, Content.Take(5_000).ToArray());
            var server = new FakeServer(Url, Content) { Redirect = true };
            await Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha);

            Assert.Equal(new[] { Url.ToString(), FakeServer.Cdn + Url.PathAndQuery.TrimStart('/') }, server.Requests.Select(r => r.Url.ToString()));
            Assert.All(server.Requests, r => Assert.Equal(5_000, r.Range?.Ranges.Single().From));
            Assert.Equal(Content, File.ReadAllBytes(Part));
        }

        [Fact]
        public async Task ABrokenOffDownloadKeepsItsBytesForTheNextAttempt()
        {
            var server = new FakeServer(Url, Content);
            server.BreakAfter[Url.ToString()] = 120_000;
            await Assert.ThrowsAnyAsync<IOException>(() => Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha));
            Assert.Equal(120_000, new FileInfo(Part).Length);

            await Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha);
            Assert.Equal(120_000, server.Requests.Last().Range?.Ranges.Single().From);
            Assert.Equal(Content, File.ReadAllBytes(Part));
        }

        [Fact]
        public async Task AFileWithTheWrongHashIsDiscarded()
        {
            byte[] wrong = (byte[])Content.Clone();
            wrong[1234] ^= 1;
            var server = new FakeServer(Url, wrong);
            await Assert.ThrowsAsync<InvalidDataException>(() => Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha));
            Assert.Equal(0, new FileInfo(Part).Length);
        }

        [Fact]
        public async Task AFileLongerThanItsSizeIsRefusedWhileDownloading()
        {
            var server = new FakeServer(Url, Content.Concat(new byte[100]).ToArray());
            await Assert.ThrowsAsync<InvalidDataException>(() => Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha));
            Assert.Equal(0, new FileInfo(Part).Length);
        }

        [Fact]
        public async Task AFailedRequestThrows()
        {
            var server = new FakeServer(Url, Content) { Status = HttpStatusCode.NotFound };
            await Assert.ThrowsAsync<HttpRequestException>(() => Download.ToFileAsync(server.Client, Url, Part, Content.Length, Sha));
        }

        [Fact]
        public async Task BytesAreReadWholeUpToALimit()
        {
            var server = new FakeServer(Url, Content) { Redirect = true };
            Assert.Equal(Content, await Download.BytesAsync(server.Client, Url, Content.Length));
            await Assert.ThrowsAsync<InvalidDataException>(() => Download.BytesAsync(server.Client, Url, Content.Length - 1));
        }
    }
}
