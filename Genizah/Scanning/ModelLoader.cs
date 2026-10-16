using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Genizah.Core;
using Microsoft.ML.OnnxRuntime;

namespace Genizah.Scanning
{
    /// <summary>
    /// Loads the classifier once per Word session, off the UI thread. The graph's vocabulary,
    /// thresholds and manifest ship beside Genizah.dll, as does ONNX Runtime's native DLL
    /// (Genizah.csproj). The graph itself is too large to ship with every update: it is in the
    /// model store (%LOCALAPPDATA%\Genizah\models), downloaded from its GitHub release when it
    /// isn't there yet. A development build has it beside Genizah.dll instead.
    /// </summary>
    internal static class ModelLoader
    {
        /// <summary>Where every model graph's GitHub release is (<see cref="ModelStore.ReleaseUrl"/>).</summary>
        public const string ModelReleases = "https://github.com/Pituchey-Hotam/Genizah/releases/download/";

        public static readonly ModelStore Store = new ModelStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Genizah", "models"));

        /// <summary>Redirects are left to <see cref="ModelStore.DownloadAsync"/>, so a resumed download's range reaches the final server.</summary>
        private static readonly HttpClient http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        private static readonly object gate = new object();
        private static Task<NameClassifier> classifier;
        private static int downloading;

        /// <summary>
        /// The session's classifier. A load that failed is tried again the next time it's asked for,
        /// since a download may fail only for now.
        /// </summary>
        public static Task<NameClassifier> Classifier
        {
            get
            {
                lock (gate)
                {
                    if (classifier == null || classifier.IsFaulted || classifier.IsCanceled)
                        classifier = Task.Run(Load);
                    return classifier;
                }
            }
        }

        public static void StartLoading() => _ = Classifier;

        /// <summary>True while the classifier waits for its graph to download.</summary>
        public static bool Downloading => Volatile.Read(ref downloading) != 0;

        /// <summary>How far the classifier's graph download is, in bytes: raised off the UI thread.</summary>
        public static event Action<long, long> DownloadProgress;

        // CodeBase, not Location: VSTO shadow-copies the add-in's assemblies elsewhere, and
        // only the add-in's own folder has the model files and the native DLL.
        public static string AddInDir => Path.GetDirectoryName(new Uri(typeof(ModelLoader).Assembly.CodeBase).LocalPath);

        /// <summary>The model files: the graph's vocabulary, thresholds and manifest.</summary>
        public static string ModelDir => Path.Combine(AddInDir, "model");

        /// <summary>The graph this version of the add-in needs (model/manifest.json).</summary>
        public static ReleaseInfo Manifest => ReleaseInfo.Load(Path.Combine(ModelDir, "manifest.json"));

        private static async Task<NameClassifier> Load()
        {
            PlaceNativeRuntime(AddInDir);
            ReleaseInfo manifest = Manifest;
            string graph = Path.Combine(ModelDir, manifest.ModelFile);
            if (!File.Exists(graph))
                graph = Store.Find(manifest);
            if (graph == null)
            {
                Interlocked.Exchange(ref downloading, 1);
                try
                {
                    graph = await Download(manifest, new Reporter((done, total) => DownloadProgress?.Invoke(done, total)));
                }
                finally
                {
                    Interlocked.Exchange(ref downloading, 0);
                }
            }
            return new NameClassifier(graph, Path.Combine(ModelDir, "vocab.txt"));
        }

        /// <summary>Puts a graph in the model store, if it isn't there already, and returns its path.</summary>
        public static Task<string> Download(ReleaseInfo model, IProgress<(long Done, long Total)> progress = null,
                                            CancellationToken cancel = default) =>
            Store.DownloadAsync(http, ModelStore.ReleaseUrl(ModelReleases, model), model, progress, cancel);

        /// <summary>Reports on the downloading thread, unlike Progress&lt;T&gt;, which posts to whatever context created it.</summary>
        private sealed class Reporter : IProgress<(long Done, long Total)>
        {
            private readonly Action<long, long> report;
            public Reporter(Action<long, long> report) => this.report = report;
            public void Report((long Done, long Total) value) => report(value.Done, value.Total);
        }

        /// <summary>
        /// Puts our onnxruntime.dll beside Microsoft.ML.OnnxRuntime.dll wherever that was loaded
        /// from, before any ONNX Runtime call. .NET Framework binds DllImport("onnxruntime") by
        /// full path to the copy in that folder, so Office's own copies (1.24, which Word may
        /// already have loaded) don't interfere. But VSTO's shadow copy (AppData\Local\assembly\dl3)
        /// takes only the managed assemblies. Without the file there, the binding falls back to a
        /// search by name, which finds Office's copy or System32's older one, and the latter
        /// crashes Word with an access violation no catch can stop.
        /// </summary>
        private static void PlaceNativeRuntime(string addInDir)
        {
            string source = Path.Combine(addInDir, "onnxruntime.dll");
            if (!File.Exists(source))
                throw new FileNotFoundException("onnxruntime.dll is not in the add-in's folder", source);
            string target = Path.Combine(Path.GetDirectoryName(typeof(InferenceSession).Assembly.Location), "onnxruntime.dll");
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                return;
            if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length)
                return;
            try
            {
                File.Copy(source, target, overwrite: true);
            }
            catch (IOException) when (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length)
            {
                // Another Word process put it there first.
            }
        }
    }
}
