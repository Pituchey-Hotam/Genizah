using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Genizah.Core
{
    /// <summary>A candidate with the model's verdict and where it sits in the raw text.</summary>
    public sealed class ScoredCandidate
    {
        public ScoredCandidate(NameCandidate candidate, int rawStart, int rawEnd, float logit)
        {
            Candidate = candidate;
            RawStart = rawStart;
            RawEnd = rawEnd;
            Logit = logit;
        }

        /// <summary>The occurrence, in normalized-text offsets.</summary>
        public NameCandidate Candidate { get; }

        /// <summary>The raw span [RawStart, RawEnd), widened over trailing nikud (<see cref="NormalizedText.ToRaw"/>).</summary>
        public int RawStart { get; }
        public int RawEnd { get; }

        /// <summary>The model's raw output: positive means censor. Uncalibrated.</summary>
        public float Logit { get; }

        public override string ToString() => $"{Candidate} raw [{RawStart}, {RawEnd}) logit {Logit:0.###}";
    }

    /// <summary>
    /// Runs the shipping ONNX model (model/manifest.json) over every divine-name candidate in a
    /// text: normalize, find, encode each window, score it.
    ///
    /// Each window is scored alone. The int8 layers quantize activations with one scale over the
    /// whole batch, so batching windows together would make a name's score depend on what else
    /// is in the document (the test vectors' logits are scored the same way).
    /// </summary>
    public sealed class NameClassifier : IDisposable
    {
        private readonly InferenceSession session;
        private readonly WindowEncoder encoder;

        /// <param name="modelPath">The .onnx graph the manifest names.</param>
        /// <param name="vocabPath">model/vocab.txt.</param>
        /// <param name="threads">ONNX Runtime's intra-op threads; 0 lets it choose.</param>
        public NameClassifier(string modelPath, string vocabPath, int threads = 0)
        {
            encoder = new WindowEncoder(WordPieceTokenizer.Load(vocabPath));
            using (var options = new SessionOptions { IntraOpNumThreads = threads })
                session = new InferenceSession(modelPath, options);
        }

        /// <summary>The model's logit for one encoded window.</summary>
        public float Score(ModelInput input)
        {
            int n = input.Length;
            var shape = new[] { 1, n };
            var mask = new float[n];
            for (int i = 0; i < n; i++)
                mask[i] = 1f;
            var feed = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(input.InputIds, shape)),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<float>(mask, shape)),
                NamedOnnxValue.CreateFromTensor("target_mask", new DenseTensor<float>(input.TargetMask, shape)),
            };
            using (var results = session.Run(feed))
                foreach (var r in results)
                    return r.AsTensor<float>().GetValue(0);
            throw new InvalidOperationException("the model returned no output");
        }

        /// <summary>Every candidate in <paramref name="raw"/>, scored, in text order.</summary>
        public List<ScoredCandidate> Classify(string raw)
        {
            return new List<ScoredCandidate>(Scan(raw, out _));
        }

        /// <summary>
        /// <see cref="Classify"/>, scoring each candidate only as it is enumerated, so a caller can
        /// report progress or stop early. <paramref name="count"/> is how many candidates there are.
        /// </summary>
        public IEnumerable<ScoredCandidate> Scan(string raw, out int count)
        {
            var norm = TextNormalizer.NormalizeWithOffsets(raw);
            var found = NameFinder.Find(norm.Text);
            count = found.Count;
            return ScoreEach(norm, found);
        }

        private IEnumerable<ScoredCandidate> ScoreEach(NormalizedText norm, List<NameCandidate> found)
        {
            foreach (var c in found)
            {
                norm.ToRaw(c.Start, c.End, out int rawStart, out int rawEnd);
                yield return new ScoredCandidate(c, rawStart, rawEnd, Score(encoder.Encode(norm.Text, c)));
            }
        }

        public void Dispose() => session.Dispose();
    }
}
