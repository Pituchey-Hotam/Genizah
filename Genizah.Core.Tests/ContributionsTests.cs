using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>Contributions: which decisions are sent, what a batch looks like on the wire, and the server's replies.</summary>
    public class ContributionsTests : IDisposable
    {
        private static readonly ModelWindow Window = new ModelWindow("ויאמר ", "אל", " משה");
        private static readonly Uri Endpoint = new Uri("https://collect.example/");
        private readonly string dir = Path.Combine(Path.GetTempPath(), "genizah-contrib-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        [Theory]
        // An answer to a question, either way.
        [InlineData("ask", true, "ask", "divine")]
        [InlineData("ask", false, "ask", "not_divine")]
        // A reversal of the model.
        [InlineData("censor", false, "override", "not_divine")]
        [InlineData("leave", true, "override", "divine")]
        // The model's own decision, kept.
        [InlineData("censor", true, null, null)]
        [InlineData("leave", false, null, null)]
        public void OnlyAnswersAndReversalsAreRecorded(string verdict, bool censored, string kind, string label)
        {
            var record = Contributions.Record("el", Window, verdict, -1.5f, censored, 1, 1200);
            if (kind == null)
            {
                Assert.Null(record);
                return;
            }
            Assert.Equal(kind, record.Kind);
            Assert.Equal(label, record.Label);
            Assert.Equal(verdict, record.Verdict);
            Assert.Equal("el", record.Name);
            Assert.Equal(("ויאמר ", "אל", " משה"), (record.Left, record.Target, record.Right));
            Assert.True(Guid.TryParse(record.Id, out _));
        }

        [Fact]
        public void UndecidedOrNotDecidedByTheUserIsNotRecorded()
        {
            Assert.Null(Contributions.Record("el", Window, "ask", 0f, null, 1, null));
            // Censored only by the scan, or by Word's undo: no decision of the user's.
            Assert.Null(Contributions.Record("el", Window, "leave", 0f, true, 0, null));
        }

        [Fact]
        public void TheKeyIgnoresTheIdButNotTheLabel()
        {
            var a = Contributions.Record("el", Window, "ask", 0f, true, 1, null);
            var b = Contributions.Record("el", Window, "ask", 0f, true, 3, 50);
            var c = Contributions.Record("el", Window, "ask", 0f, false, 1, null);
            Assert.NotEqual(a.Id, b.Id);
            Assert.Equal(a.Key, b.Key);
            Assert.NotEqual(a.Key, c.Key);
        }

        [Fact]
        public void ABatchSerializesToTheServersSchema()
        {
            var record = Contributions.Record("el", Window, "ask", -1.5f, false, 2, null);
            var batch = Batch(record);
            using (var json = JsonDocument.Parse(batch.ToJson()))
            {
                var root = json.RootElement;
                Assert.Equal(new[] { "schema", "install_id", "batch_id", "addin_version", "model", "thresholds", "records" },
                             root.EnumerateObject().Select(p => p.Name));
                Assert.Equal(1, root.GetProperty("schema").GetInt32());
                var r = root.GetProperty("records")[0];
                Assert.Equal(new[] { "id", "name", "left", "target", "right", "kind", "verdict", "logit", "label", "revisions", "decide_ms" },
                             r.EnumerateObject().Select(p => p.Name));
                Assert.Equal("ויאמר ", r.GetProperty("left").GetString());
                Assert.Equal(-1.5, r.GetProperty("logit").GetDouble());
                Assert.Equal(JsonValueKind.Null, r.GetProperty("decide_ms").ValueKind);
            }
        }

        [Fact]
        public async Task SendingPostsTheBatchAndReadsTheCounts()
        {
            var server = new Server(HttpStatusCode.OK, "{\"accepted\":1,\"duplicates\":0}");
            var batch = Batch(Contributions.Record("el", Window, "ask", 0f, true, 1, null));
            var result = await Contributions.SendAsync(new HttpClient(server), Endpoint, batch);
            Assert.Equal((1, 0), (result.Accepted, result.Duplicates));
            Assert.Equal(HttpMethod.Post, server.Method);
            Assert.Equal("https://collect.example/v1/batches", server.Url.ToString());
            Assert.Equal(batch.ToJson(), server.Body);
        }

        [Fact]
        public async Task ARefusalCarriesTheServersReason()
        {
            var server = new Server(HttpStatusCode.BadRequest, "{\"error\":\"records[0].left: too long\"}");
            var batch = Batch(Contributions.Record("el", Window, "ask", 0f, true, 1, null));
            var e = await Assert.ThrowsAsync<ContributionException>(() => Contributions.SendAsync(new HttpClient(server), Endpoint, batch));
            Assert.Equal(HttpStatusCode.BadRequest, e.Status);
            Assert.Equal("records[0].left: too long", e.Message);

            server = new Server(HttpStatusCode.BadGateway, "<html>");
            e = await Assert.ThrowsAsync<ContributionException>(() => Contributions.SendAsync(new HttpClient(server), Endpoint, batch));
            Assert.StartsWith("502", e.Message);
        }

        [Fact]
        public async Task DeletingAddressesTheInstall()
        {
            var server = new Server(HttpStatusCode.OK, "{\"deleted_batches\":2,\"deleted_records\":7}");
            string id = Guid.NewGuid().ToString("D");
            var result = await Contributions.DeleteAsync(new HttpClient(server), Endpoint, id);
            Assert.Equal((2, 7), (result.Batches, result.Records));
            Assert.Equal(HttpMethod.Delete, server.Method);
            Assert.Equal("https://collect.example/v1/installs/" + id, server.Url.ToString());
        }

        [Fact]
        public void TheSentLogRoundTripsAndToleratesAMissingFile()
        {
            string path = Path.Combine(dir, "contributed.json");
            var log = SentLog.Load(path);
            Assert.False(log.Contains("a"));
            log.Add(new[] { "a", "b" });
            log.Save(path);
            log.Add(new[] { "c" });
            log.Save(path);
            var loaded = SentLog.Load(path);
            Assert.True(loaded.Contains("a") && loaded.Contains("b") && loaded.Contains("c"));
            Assert.DoesNotContain("אל", File.ReadAllText(path));
        }

        [Fact]
        public void TheWindowIsTheModelsInput()
        {
            string text = TextNormalizer.Normalize("ויאמר אל משה");
            NameCandidate candidate = NameFinder.Find(text).Single();
            ModelWindow window = WindowEncoder.Window(text, candidate);
            Assert.Equal(("ויאמר ", "אל", " משה"), (window.Left, window.Target, window.Right));

            string longer = TextNormalizer.Normalize(new string('ב', 40) + " אל " + new string('ג', 40));
            window = WindowEncoder.Window(longer, NameFinder.Find(longer).Single());
            Assert.Equal(WindowEncoder.Side, window.Left.Length);
            Assert.Equal(WindowEncoder.Side, window.Right.Length);
        }

        private static ContributionBatch Batch(ContributionRecord record) => new ContributionBatch
        {
            InstallId = Guid.NewGuid().ToString("D"),
            BatchId = Guid.NewGuid().ToString("D"),
            AddInVersion = "0.1.2",
            Model = "dbdba75b4584",
            Thresholds = "0123456789ab",
            Records = new List<ContributionRecord> { record },
        };

        /// <summary>Answers every request with one reply, and keeps the last request.</summary>
        private sealed class Server : HttpMessageHandler
        {
            private readonly HttpStatusCode status;
            private readonly string reply;

            public Server(HttpStatusCode status, string reply)
            {
                this.status = status;
                this.reply = reply;
            }

            public HttpMethod Method { get; private set; }
            public Uri Url { get; private set; }
            public string Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Method = request.Method;
                Url = request.RequestUri;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
            }
        }
    }
}
