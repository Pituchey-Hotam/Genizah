using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Genizah.Core;

namespace Genizah.Core.Tests
{
    /// <summary>
    /// Locates the vectors tools/make_vectors.py writes. They're read in place rather than
    /// copied to the build output: the local files run to tens of megabytes.
    /// </summary>
    internal static class TestData
    {
        public static readonly string RepoRoot = FindRepoRoot();

        public static string Path(params string[] parts) =>
            System.IO.Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray());

        public static string TestDataPath(string name) => Path("Genizah.Core.Tests", "testdata", name);

        public static string LocalPath(string name) => Path("Genizah.Core.Tests", "testdata", "local", name);

        public static JsonDocument Json(string path) => JsonDocument.Parse(File.ReadAllBytes(path));

        /// <summary>One JSON document per LF-terminated line (the texts hold U+2028 and friends).</summary>
        public static IEnumerable<JsonDocument> JsonLines(string path)
        {
            string all = File.ReadAllText(path, new UTF8Encoding(false));
            foreach (string line in all.Split('\n'))
                if (line.Trim().Length > 0)
                    yield return JsonDocument.Parse(line);
        }

        private static WordPieceTokenizer tokenizer;

        public static WordPieceTokenizer Tokenizer =>
            tokenizer ?? (tokenizer = WordPieceTokenizer.Load(Path("model", "vocab.txt")));

        public static long[] Longs(JsonElement array) => array.EnumerateArray().Select(e => e.GetInt64()).ToArray();

        public static float[] Floats(JsonElement array) => array.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();

        private static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(System.IO.Path.Combine(dir.FullName, "Genizah.sln")))
                    return dir.FullName;
            throw new InvalidOperationException("Genizah.sln not found above " + AppContext.BaseDirectory);
        }
    }
}
