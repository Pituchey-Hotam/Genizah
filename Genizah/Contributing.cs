using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Genizah.Core;
using Genizah.Scanning;

namespace Genizah
{
    /// <summary>
    /// Sending the user's decisions on names to the collection server, when they choose to
    /// (<see cref="Contributions"/>). Used on the UI thread.
    /// </summary>
    internal static class Contributing
    {
        /// <summary>The collection server. Until there is one, nothing is offered.</summary>
        private const string BuiltInEndpoint = null;

        public static readonly string SentLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Genizah", "contributed.json");

        private static SentLog sent;
        private static HttpClient http;

        /// <summary>
        /// Where contributions go: the settings' <c>contribution_endpoint</c> if set, for testing,
        /// or the built-in server. Null if there is neither, or it isn't https (http only on this
        /// computer).
        /// </summary>
        public static Uri Endpoint
        {
            get
            {
                string url = AddInSettings.Current.ContributionEndpoint ?? BuiltInEndpoint;
                if (url == null || !Uri.TryCreate(url.EndsWith("/") ? url : url + "/", UriKind.Absolute, out Uri uri))
                    return null;
                return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ? uri : null;
            }
        }

        /// <summary>Whether the pane offers to send decisions at all.</summary>
        public static bool Offered => Endpoint != null && AddInSettings.Current.OfferContributions;

        /// <summary>
        /// Whether the user has shared before, and so has seen what sharing is: until then the
        /// share link explains first. Deleting what was shared starts over.
        /// </summary>
        public static bool Explained => !string.IsNullOrEmpty(AddInSettings.Current.InstallId);

        public static SentLog Sent => sent ?? (sent = SentLog.Load(SentLogPath));

        private static HttpClient Http
        {
            get
            {
                if (http == null)
                {
                    // An add-in's AppDomain otherwise offers only SSL 3 and TLS 1.0 (see Updater).
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                }
                return http;
            }
        }

        /// <summary>
        /// The decisions in <paramref name="results"/> that can be sent and haven't been, one per
        /// window and label. Reversing a name that is always censored says nothing about the
        /// model, so those aren't sent.
        /// </summary>
        public static List<ContributionRecord> Unsent(IEnumerable<Results.SearchResult> results, Thresholds thresholds)
        {
            return results.Select(r => r.ToContribution())
                .Where(r => r != null && !(r.Kind == "override" && thresholds.CensorsAlways(r.Name)) && !Sent.Contains(r.Key))
                .GroupBy(r => r.Key).Select(g => g.First())
                .ToList();
        }

        /// <summary>A new batch of <paramref name="records"/>, under this install's id (made now if there is none).</summary>
        public static ContributionBatch NewBatch(List<ContributionRecord> records, Thresholds thresholds)
        {
            if (string.IsNullOrEmpty(AddInSettings.Current.InstallId))
            {
                AddInSettings.Current.InstallId = Guid.NewGuid().ToString("D");
                AddInSettings.Save();
            }
            return new ContributionBatch
            {
                InstallId = AddInSettings.Current.InstallId,
                BatchId = Guid.NewGuid().ToString("D"),
                AddInVersion = Updater.RunningVersion.ToString(),
                Model = thresholds.ModelSha256.Substring(0, 12).ToLowerInvariant(),
                Thresholds = thresholds.FileSha256.Substring(0, 12),
                Records = records,
            };
        }

        /// <summary>Sends <paramref name="batch"/>, and remembers its records as sent.</summary>
        public static async Task<ContributionResult> Send(ContributionBatch batch)
        {
            var result = await Contributions.SendAsync(Http, Endpoint, batch);
            Sent.Add(batch.Records.Select(r => r.Key));
            SaveSent();
            return result;
        }

        /// <summary>Deletes everything this install has sent, and forgets its id.</summary>
        public static async Task<ContributionDeletion> DeleteAll()
        {
            var deleted = await Contributions.DeleteAsync(Http, Endpoint, AddInSettings.Current.InstallId);
            AddInSettings.Current.InstallId = "";
            AddInSettings.Save();
            Sent.Clear();
            SaveSent();
            return deleted;
        }

        private static void SaveSent()
        {
            try { Sent.Save(SentLogPath); }
            // Then only this session remembers what was sent.
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }
}
