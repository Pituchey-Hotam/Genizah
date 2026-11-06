using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Genizah.Core
{
    /// <summary>
    /// One decision the user made on a name, as a contribution sends it: the model's window
    /// (never more of the document), what the model said, and what the user decided.
    /// </summary>
    [DataContract]
    public sealed class ContributionRecord
    {
        [DataMember(Name = "id", Order = 0)] public string Id { get; set; }
        [DataMember(Name = "name", Order = 1)] public string Name { get; set; }
        [DataMember(Name = "left", Order = 2)] public string Left { get; set; }
        [DataMember(Name = "target", Order = 3)] public string Target { get; set; }
        [DataMember(Name = "right", Order = 4)] public string Right { get; set; }
        /// <summary>"ask": the model asked and the user answered; "override": the user reversed the model.</summary>
        [DataMember(Name = "kind", Order = 5)] public string Kind { get; set; }
        /// <summary>What the model said: "censor", "ask" or "leave".</summary>
        [DataMember(Name = "verdict", Order = 6)] public string Verdict { get; set; }
        [DataMember(Name = "logit", Order = 7)] public double Logit { get; set; }
        /// <summary>"divine" or "not_divine".</summary>
        [DataMember(Name = "label", Order = 8)] public string Label { get; set; }
        /// <summary>How many times the user decided on this name, changes of mind included.</summary>
        [DataMember(Name = "revisions", Order = 9)] public int Revisions { get; set; }
        /// <summary>From first selecting the name to the last decision on it, or null if it was never selected.</summary>
        [DataMember(Name = "decide_ms", Order = 10)] public long? DecideMs { get; set; }

        /// <summary>
        /// Identifies the decision, not the send: the same window with the same label has the same
        /// key, so a rescan doesn't offer it again (<see cref="SentLog"/>).
        /// </summary>
        public string Key => Contributions.Sha256Hex(string.Join("\n", Name, Left, Target, Right, Label));
    }

    /// <summary>One document's records, as one request.</summary>
    [DataContract]
    public sealed class ContributionBatch
    {
        [DataMember(Name = "schema", Order = 0)] public int Schema { get; set; } = Contributions.Schema;
        /// <summary>Random, made on the first send and kept in the user's settings.</summary>
        [DataMember(Name = "install_id", Order = 1)] public string InstallId { get; set; }
        [DataMember(Name = "batch_id", Order = 2)] public string BatchId { get; set; }
        [DataMember(Name = "addin_version", Order = 3)] public string AddInVersion { get; set; }
        /// <summary>The first 12 hex digits of the scoring graph's sha256.</summary>
        [DataMember(Name = "model", Order = 4)] public string Model { get; set; }
        /// <summary>The first 12 hex digits of the thresholds file's sha256.</summary>
        [DataMember(Name = "thresholds", Order = 5)] public string Thresholds { get; set; }
        [DataMember(Name = "records", Order = 6)] public List<ContributionRecord> Records { get; set; }

        public string ToJson()
        {
            var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(ContributionBatch)).WriteObject(stream, this);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    /// <summary>What the server did with a batch.</summary>
    [DataContract]
    public sealed class ContributionResult
    {
        [DataMember(Name = "accepted")] public int Accepted { get; set; }
        /// <summary>Records it already had: a resend after a lost reply, say.</summary>
        [DataMember(Name = "duplicates")] public int Duplicates { get; set; }
    }

    /// <summary>What the server deleted for an install.</summary>
    [DataContract]
    public sealed class ContributionDeletion
    {
        [DataMember(Name = "deleted_batches")] public int Batches { get; set; }
        [DataMember(Name = "deleted_records")] public int Records { get; set; }
    }

    /// <summary>The server refused a request; <see cref="Exception.Message"/> is its reason, if it gave one.</summary>
    public sealed class ContributionException : Exception
    {
        public ContributionException(HttpStatusCode status, string message) : base(message)
        {
            Status = status;
        }

        public HttpStatusCode Status { get; }
    }

    /// <summary>
    /// Which decisions are worth sending, and sending them. Only decisions the user made on a
    /// name count: an answer to a question the model asked, or a reversal of what it decided.
    /// A decision accepted as the model made it, or a question not yet answered, is not sent.
    /// </summary>
    public static class Contributions
    {
        public const int Schema = 1;

        /// <summary>
        /// The record of a decision, or null if it isn't one to send. <paramref name="verdict"/>
        /// is what the model said ("censor", "ask" or "leave"); <paramref name="censored"/> is
        /// what the user decided, null while undecided; <paramref name="revisions"/> counts the
        /// user's own decisions, so a state reached only by the scan or by Word's undo has none.
        /// </summary>
        public static ContributionRecord Record(string nameId, ModelWindow window, string verdict, float logit,
                                                bool? censored, int revisions, long? decideMs)
        {
            if (censored == null || revisions < 1) return null;
            string kind;
            switch (verdict)
            {
                case "ask": kind = "ask"; break;
                case "censor": kind = censored.Value ? null : "override"; break;
                case "leave": kind = censored.Value ? "override" : null; break;
                default: throw new ArgumentException("unknown verdict " + verdict, nameof(verdict));
            }
            if (kind == null) return null;
            return new ContributionRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = nameId,
                Left = window.Left,
                Target = window.Target,
                Right = window.Right,
                Kind = kind,
                Verdict = verdict,
                Logit = logit,
                Label = censored.Value ? "divine" : "not_divine",
                Revisions = revisions,
                DecideMs = decideMs,
            };
        }

        /// <summary>Sends a batch to the collection server at <paramref name="endpoint"/>. Throws <see cref="ContributionException"/> if it refuses.</summary>
        public static async Task<ContributionResult> SendAsync(HttpClient http, Uri endpoint, ContributionBatch batch, CancellationToken cancel = default)
        {
            var content = new StringContent(batch.ToJson(), Encoding.UTF8, "application/json");
            using (var response = await http.PostAsync(new Uri(endpoint, "v1/batches"), content, cancel).ConfigureAwait(false))
                return await Read<ContributionResult>(response).ConfigureAwait(false);
        }

        /// <summary>Deletes everything <paramref name="installId"/> has sent.</summary>
        public static async Task<ContributionDeletion> DeleteAsync(HttpClient http, Uri endpoint, string installId, CancellationToken cancel = default)
        {
            var url = new Uri(endpoint, "v1/installs/" + Uri.EscapeDataString(installId));
            using (var response = await http.DeleteAsync(url, cancel).ConfigureAwait(false))
                return await Read<ContributionDeletion>(response).ConfigureAwait(false);
        }

        private static async Task<T> Read<T>(HttpResponseMessage response)
        {
            byte[] body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ContributionException(response.StatusCode, ErrorText(body) ?? $"{(int)response.StatusCode} {response.ReasonPhrase}");
            try
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(new MemoryStream(body));
            }
            catch (SerializationException)
            {
                throw new ContributionException(response.StatusCode, "the server's reply wasn't understood");
            }
        }

        [DataContract]
        private sealed class Error
        {
            [DataMember(Name = "error")] public string Text { get; set; }
        }

        private static string ErrorText(byte[] body)
        {
            try { return ((Error)new DataContractJsonSerializer(typeof(Error)).ReadObject(new MemoryStream(body)))?.Text; }
            catch (SerializationException) { return null; }
        }

        /// <summary>Lowercase hex sha256 of <paramref name="text"/>'s UTF-8.</summary>
        public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }
    }

    /// <summary>
    /// The keys of the decisions already sent (<see cref="ContributionRecord.Key"/>), so they
    /// aren't offered again. Hashes only: the file holds no text.
    /// </summary>
    public sealed class SentLog
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(string[]));
        private readonly HashSet<string> keys;

        private SentLog(IEnumerable<string> keys) => this.keys = new HashSet<string>(keys);

        public bool Contains(string key) => keys.Contains(key);

        public void Add(IEnumerable<string> sent)
        {
            foreach (string key in sent) keys.Add(key);
        }

        public void Clear() => keys.Clear();

        /// <summary>The log in <paramref name="path"/>, or an empty one if it is missing or unreadable.</summary>
        public static SentLog Load(string path)
        {
            try
            {
                using (var stream = File.OpenRead(path))
                    return new SentLog((string[])Serializer.ReadObject(stream) ?? new string[0]);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SerializationException)
            {
                return new SentLog(new string[0]);
            }
        }

        /// <summary>Writes the log to <paramref name="path"/>, whole or not at all.</summary>
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            using (var stream = File.Create(temp))
                Serializer.WriteObject(stream, keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
