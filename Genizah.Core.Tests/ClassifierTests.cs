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
    /// the same windows (testdata/pipeline.json, from tools/make_vectors.py). The .onnx file is
    /// git-ignored, so these skip until it is copied into model/.
    /// </summary>
    public class ClassifierTests
    {
        // classifier/PLAN.md phase 5's bar. Same runtime version, same graph, one window per run.
        private const float Tolerance = 1e-5f;

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
    }
}
