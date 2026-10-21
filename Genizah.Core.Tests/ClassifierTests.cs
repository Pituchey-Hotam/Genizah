using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>
    /// The model's logits under ONNX Runtime in .NET, against the ones Python's onnxruntime gave
    /// the same windows (testdata/pipeline.json). The .onnx file is
    /// git-ignored, so these skip until it is copied into model/.
    /// </summary>
    public class ClassifierTests
    {
        // The int8 layers pick kernels by instruction set, so AVX2-only and AVX-512 VNNI CPUs give
        // logits a few hundredths apart. Wiring mistakes (masks swapped, padding attended to,
        // results on the wrong candidate) move them by several units.
        private const float Tolerance = 0.1f;

        private static string ModelPath()
        {
            using (var manifest = TestData.Json(TestData.Path("model", "manifest.json")))
            {
                string path = TestData.Path("model", manifest.RootElement.GetProperty("model_file").GetString());
                Skip.IfNot(File.Exists(path), "model/*.onnx is not copied in (see model/manifest.json)");
                return path;
            }
        }

        private static NameClassifier classifier;

        private static NameClassifier Classifier =>
            classifier ?? (classifier = new NameClassifier(ModelPath(), TestData.Path("model", "vocab.txt"), threads: 1));

        [SkippableFact]
        public void ModelIsTheOneTheManifestNames()
        {
            string path = ModelPath();
            using (var manifest = TestData.Json(TestData.Path("model", "manifest.json")))
            using (var vectors = TestData.Json(TestData.TestDataPath("pipeline.json")))
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
            {
                string got = string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
                Assert.Equal(manifest.RootElement.GetProperty("model_sha256").GetString(), got);
                Assert.Equal(got, vectors.RootElement.GetProperty("model_sha256").GetString());
            }
        }

        [SkippableTheory]
        [MemberData(nameof(PipelineTests.Cases), MemberType = typeof(PipelineTests))]
        public void LogitsMatchPython(string name)
        {
            var model = Classifier;
            using (var doc = TestData.Json(TestData.TestDataPath("pipeline.json")))
            {
                var c = doc.RootElement.GetProperty("cases").EnumerateArray()
                    .Single(k => k.GetProperty("case").GetString() == name);
                var want = c.GetProperty("candidates").EnumerateArray().ToList();
                var got = model.Classify(c.GetProperty("text").GetString());
                Assert.Equal(want.Count, got.Count);
                for (int i = 0; i < got.Count; i++)
                {
                    float expected = (float)want[i].GetProperty("logit").GetDouble();
                    Assert.True(Math.Abs(expected - got[i].Logit) <= Tolerance,
                        $"{got[i].Candidate}: logit {got[i].Logit}, Python {expected}");
                }
            }
        }

        // Batching would change logits by less than Tolerance, so this compares on one machine,
        // exactly: each candidate in a whole text scores as its window does alone.
        [SkippableTheory]
        [MemberData(nameof(PipelineTests.Cases), MemberType = typeof(PipelineTests))]
        public void EachWindowIsScoredAlone(string name)
        {
            var model = Classifier;
            using (var doc = TestData.Json(TestData.TestDataPath("pipeline.json")))
            {
                var c = doc.RootElement.GetProperty("cases").EnumerateArray()
                    .Single(k => k.GetProperty("case").GetString() == name);
                var want = c.GetProperty("candidates").EnumerateArray().ToList();
                var got = model.Classify(c.GetProperty("text").GetString());
                Assert.Equal(want.Count, got.Count);
                for (int i = 0; i < got.Count; i++)
                {
                    float alone = model.Score(new ModelInput(
                        TestData.Longs(want[i].GetProperty("input_ids")),
                        TestData.Floats(want[i].GetProperty("target_mask"))));
                    Assert.True(alone == got[i].Logit, $"{got[i].Candidate}: logit {got[i].Logit}, alone {alone}");
                }
            }
        }
    }
}
