using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>
    /// Raw text -> normalized text -> candidates -> model inputs, against the Python sources of
    /// truth (testdata/pipeline.json, from tools/make_vectors.py).
    /// </summary>
    public class PipelineTests
    {
        public static IEnumerable<object[]> Cases()
        {
            using (var doc = TestData.Json(TestData.TestDataPath("pipeline.json")))
                foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
                    yield return new object[] { c.GetProperty("case").GetString() };
        }

        private static JsonElement Case(JsonDocument doc, string name) =>
            doc.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("case").GetString() == name);

        [Fact]
        public void PatternsAreBuiltAsPythonBuildsThem()
        {
            using (var doc = TestData.Json(TestData.TestDataPath("patterns.json")))
            {
                var order = doc.RootElement.GetProperty("name_order").EnumerateArray().Select(e => e.GetString()).ToList();
                Assert.Equal(order, NameFinder.NameOrder);
                foreach (var p in doc.RootElement.GetProperty("patterns").EnumerateObject())
                    Assert.True(p.Value.GetString() == NameFinder.PatternSources[p.Name], p.Name);
            }
        }

        [Fact]
        public void VectorsWereMadeWithTheShippedTokenizer()
        {
            using (var vectors = TestData.Json(TestData.TestDataPath("pipeline.json")))
            using (var manifest = TestData.Json(TestData.Path("model", "manifest.json")))
                Assert.Equal(manifest.RootElement.GetProperty("tokenizer_sha256").GetString(),
                             vectors.RootElement.GetProperty("tokenizer_sha256").GetString());
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void MatchesPython(string name)
        {
            using (var doc = TestData.Json(TestData.TestDataPath("pipeline.json")))
            {
                var c = Case(doc, name);
                string raw = c.GetProperty("text").GetString();
                var norm = TextNormalizer.NormalizeWithOffsets(raw);
                Assert.Equal(c.GetProperty("normalized").GetString(), norm.Text);

                var found = NameFinder.Find(norm.Text);
                var want = c.GetProperty("candidates").EnumerateArray().ToList();
                Assert.Equal(want.Select(w => $"{w.GetProperty("start").GetInt32()}-{w.GetProperty("end").GetInt32()} {w.GetProperty("name").GetString()}"),
                             found.Select(f => $"{f.Start}-{f.End} {f.NameId}"));

                var encoder = new WindowEncoder(TestData.Tokenizer);
                for (int i = 0; i < found.Count; i++)
                {
                    var input = encoder.Encode(norm.Text, found[i]);
                    Assert.Equal(TestData.Longs(want[i].GetProperty("input_ids")), input.InputIds);
                    Assert.Equal(TestData.Floats(want[i].GetProperty("target_mask")), input.TargetMask);

                    // The raw span normalizes back to the target, and encoding the raw text around
                    // it training's way (each side normalized on its own) gives the same input.
                    norm.ToRaw(found[i].Start, found[i].End, out int rs, out int re);
                    Assert.Equal(found[i].Text, TextNormalizer.Normalize(raw.Substring(rs, re - rs)));
                    var asTrained = encoder.Encode(raw.Substring(0, rs), raw.Substring(rs, re - rs), raw.Substring(re));
                    Assert.Equal(input.InputIds, asTrained.InputIds);
                    Assert.Equal(input.TargetMask, asTrained.TargetMask);
                }
            }
        }
    }

    /// <summary>
    /// The same checks over all the data, from the git-ignored testdata/local/ files: every
    /// training row's input as the training code built it, and every Olah and OSHB text's
    /// candidates. Skipped where those files haven't been generated.
    /// </summary>
    public class LocalParityTests
    {
        [SkippableFact]
        public void EveryTrainingWindow()
        {
            string path = TestData.LocalPath("windows.jsonl");
            Skip.IfNot(File.Exists(path), "run tools/make_vectors.py to generate testdata/local/");
            var encoder = new WindowEncoder(TestData.Tokenizer);
            int rows = 0;
            var bad = new List<string>();
            foreach (var doc in TestData.JsonLines(path))
                using (doc)
                {
                    var r = doc.RootElement;
                    rows++;
                    var input = encoder.Encode(r.GetProperty("left").GetString(), r.GetProperty("target").GetString(), r.GetProperty("right").GetString());
                    if (!input.InputIds.SequenceEqual(TestData.Longs(r.GetProperty("input_ids"))) ||
                        !input.TargetMask.SequenceEqual(TestData.Floats(r.GetProperty("target_mask"))))
                        bad.Add(r.GetProperty("id").GetString());
                }
            Assert.True(rows > 20000, $"only {rows} rows");
            Assert.True(bad.Count == 0, $"{bad.Count} of {rows} windows differ, e.g. {string.Join(", ", bad.Take(5))}");
        }

        [SkippableFact]
        public void EveryCorpusText()
        {
            string path = TestData.LocalPath("scans.jsonl");
            Skip.IfNot(File.Exists(path), "run tools/make_vectors.py to generate testdata/local/");
            int texts = 0, candidates = 0;
            var bad = new List<string>();
            foreach (var doc in TestData.JsonLines(path))
                using (doc)
                {
                    var r = doc.RootElement;
                    texts++;
                    var want = r.GetProperty("candidates").EnumerateArray()
                        .Select(c => $"{c[0].GetInt32()}-{c[1].GetInt32()} {c[2].GetString()}").ToList();
                    var got = NameFinder.Find(TextNormalizer.Normalize(r.GetProperty("text").GetString()))
                        .Select(f => $"{f.Start}-{f.End} {f.NameId}").ToList();
                    candidates += want.Count;
                    if (!want.SequenceEqual(got))
                        bad.Add(r.GetProperty("id").GetString());
                }
            Assert.True(texts > 9000, $"only {texts} texts");
            Assert.True(bad.Count == 0, $"{bad.Count} of {texts} texts ({candidates} candidates) differ, e.g. {string.Join(", ", bad.Take(5))}");
        }
    }
}
