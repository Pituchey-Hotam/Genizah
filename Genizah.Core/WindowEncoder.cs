using System;
using System.Collections.Generic;

namespace Genizah.Core
{
    /// <summary>One candidate's model input. The attention mask is all ones over these ids.</summary>
    public sealed class ModelInput
    {
        public ModelInput(long[] inputIds, float[] targetMask)
        {
            InputIds = inputIds;
            TargetMask = targetMask;
        }

        public long[] InputIds { get; }

        /// <summary>1 on every word piece that overlaps the target, 0 elsewhere.</summary>
        public float[] TargetMask { get; }

        public int Length => InputIds.Length;
    }

    /// <summary>
    /// Builds the classifier's input for one candidate, as training did (its
    /// encode_rows): the window normalize(left)[-32:] + target + normalize(right)[:32],
    /// tokenized, [CLS] ... [SEP] cut to 128 ids, and a target mask over every piece whose
    /// span overlaps the target.
    /// </summary>
    public sealed class WindowEncoder
    {
        /// <summary>Normalized characters of context kept on each side of the target.</summary>
        public const int Side = 32;

        /// <summary>The model's position table: no input is longer.</summary>
        public const int MaxLength = 128;

        private readonly WordPieceTokenizer tokenizer;

        public WindowEncoder(WordPieceTokenizer tokenizer)
        {
            this.tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        }

        /// <summary>A candidate found by <see cref="NameFinder"/> in <paramref name="normalized"/>.</summary>
        public ModelInput Encode(string normalized, NameCandidate candidate)
        {
            // Normalization is idempotent, so slicing the normalized document gives what
            // normalizing the raw context on each side would.
            int leftStart = Math.Max(0, candidate.Start - Side);
            string left = normalized.Substring(leftStart, candidate.Start - leftStart);
            string right = normalized.Substring(candidate.End, Math.Min(Side, normalized.Length - candidate.End));
            return EncodeWindow(left, candidate.Text, right);
        }

        /// <summary>A training-style row: raw left context, target, raw right context.</summary>
        public ModelInput Encode(string rawLeft, string target, string rawRight)
        {
            string left = TextNormalizer.Normalize(rawLeft);
            string right = TextNormalizer.Normalize(rawRight);
            left = left.Substring(Math.Max(0, left.Length - Side));
            right = right.Substring(0, Math.Min(Side, right.Length));
            return EncodeWindow(left, TextNormalizer.Normalize(target), right);
        }

        private ModelInput EncodeWindow(string left, string target, string right)
        {
            int lo = left.Length, hi = left.Length + target.Length;
            List<Token> tokens = tokenizer.Encode(left + target + right);
            int n = Math.Min(tokens.Count, MaxLength);
            var ids = new long[n];
            var mask = new float[n];
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                ids[i] = tokens[i].Id;
                if (tokens[i].Start < hi && tokens[i].End > lo)
                {
                    mask[i] = 1f;
                    any = true;
                }
            }
            if (!any)
                throw new ArgumentException($"the target \"{target}\" covers no word piece of its window");
            return new ModelInput(ids, mask);
        }
    }
}
