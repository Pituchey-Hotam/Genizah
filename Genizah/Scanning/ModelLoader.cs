using System;
using System.IO;
using System.Threading.Tasks;
using Genizah.Core;
using Microsoft.ML.OnnxRuntime;

namespace Genizah.Scanning
{
    /// <summary>
    /// Loads the classifier once per Word session, off the UI thread. The model files and ONNX
    /// Runtime's native DLL ship beside Genizah.dll (Genizah.csproj).
    /// </summary>
    internal static class ModelLoader
    {
        private static readonly Lazy<Task<NameClassifier>> classifier =
            new Lazy<Task<NameClassifier>>(() => Task.Run(() => Load()));

        /// <summary>The session's classifier. A load that failed stays failed until Word restarts.</summary>
        public static Task<NameClassifier> Classifier => classifier.Value;

        public static void StartLoading() => _ = classifier.Value;

        // CodeBase, not Location: VSTO shadow-copies the add-in's assemblies elsewhere, and
        // only the add-in's own folder has the model and the native DLL.
        private static string AddInDir => Path.GetDirectoryName(new Uri(typeof(ModelLoader).Assembly.CodeBase).LocalPath);

        /// <summary>The model files: the graph, its vocabulary and its thresholds.</summary>
        public static string ModelDir => Path.Combine(AddInDir, "model");

        private static NameClassifier Load()
        {
            PlaceNativeRuntime(AddInDir);
            string modelDir = ModelDir;
            string[] models = Directory.Exists(modelDir) ? Directory.GetFiles(modelDir, "*.onnx") : new string[0];
            if (models.Length != 1)
                throw new FileNotFoundException($"expected one .onnx model in {modelDir}, found {models.Length}");
            return new NameClassifier(models[0], Path.Combine(modelDir, "vocab.txt"));
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
