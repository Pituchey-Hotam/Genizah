using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Genizah.Core
{
    /// <summary>A word piece and the span of the input it covers. [CLS] and [SEP] cover (0, 0).</summary>
    public struct Token
    {
        public Token(int id, int start, int end)
        {
            Id = id;
            Start = start;
            End = end;
        }

        public int Id { get; }
        public int Start { get; }
        public int End { get; }
    }

    /// <summary>
    /// The shipping model's tokenizer (its tokenizer.json, a Hugging Face BERT WordPiece
    /// pipeline), for <b>normalized</b> input only: text made of
    /// <see cref="TextNormalizer.Alphabet"/> and <see cref="TextNormalizer.Unknown"/>.
    ///
    /// On that input the tokenizer.json normalizer (NFD, lowercase, strip accents) changes
    /// nothing, except that a run of characters outside its allowed ranges, U+FFFD among them,
    /// becomes "[UNK]". The vectors' generator refuses a tokenizer.json whose configuration
    /// differs from what this class implements.
    /// </summary>
    public sealed class WordPieceTokenizer
    {
        public const int UnkId = 0, ClsId = 1, SepId = 2, PadId = 3;
        private const string UnkText = "[UNK]";
        private const string ContinuingPrefix = "##";
        private const int MaxInputCharsPerWord = 100;

        // tokenizer.json's pre_tokenizer, verbatim: Split, behavior Removed, invert, so every
        // match is a word and whatever lies between matches (spaces) is dropped.
        private static readonly Regex PreTokenizer = new Regex(
            @"\[UNK\]|\w*[א-ת](['""][א-ת]\w*)?'?|\w+|\p{P}|[^\w\s]+",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private readonly Dictionary<string, int> vocab;

        public WordPieceTokenizer(IList<string> pieces)
        {
            vocab = new Dictionary<string, int>(pieces.Count, StringComparer.Ordinal);
            for (int i = 0; i < pieces.Count; i++)
                vocab[pieces[i]] = i;
            if (Id(UnkText) != UnkId || Id("[CLS]") != ClsId || Id("[SEP]") != SepId || Id("[PAD]") != PadId)
                throw new ArgumentException("the vocabulary's special pieces are not [UNK] [CLS] [SEP] [PAD] at 0-3");
        }

        /// <summary>
        /// Loads model/vocab.txt: one piece per LF-terminated line, line n being id n. Only LF
        /// ends a line; two pieces are control characters that other line readers split on.
        /// No piece contains CR (the vectors' generator checks), so a CR before the LF, from a
        /// checkout that converted line endings, is dropped.
        /// </summary>
        public static WordPieceTokenizer Load(string vocabPath)
        {
            string all = File.ReadAllText(vocabPath, new UTF8Encoding(false));
            var pieces = new List<string>(all.Split('\n'));
            if (pieces.Count > 0 && pieces[pieces.Count - 1].Length == 0)
                pieces.RemoveAt(pieces.Count - 1);
            for (int i = 0; i < pieces.Count; i++)
                if (pieces[i].EndsWith("\r", StringComparison.Ordinal))
                    pieces[i] = pieces[i].Substring(0, pieces[i].Length - 1);
            return new WordPieceTokenizer(pieces);
        }

        public int VocabSize => vocab.Count;

        private int Id(string piece)
        {
            int id;
            return vocab.TryGetValue(piece, out id) ? id : -1;
        }

        /// <summary>[CLS], the word pieces of <paramref name="normalized"/>, [SEP]. Not truncated.</summary>
        public List<Token> Encode(string normalized)
        {
            // The normalizer's Replace: each run of U+FFFD becomes the five characters of
            // "[UNK]", every one of them aligned to the whole run.
            var text = new StringBuilder(normalized.Length);
            var starts = new List<int>(normalized.Length);
            var ends = new List<int>(normalized.Length);
            for (int i = 0; i < normalized.Length;)
            {
                char c = normalized[i];
                if (c == TextNormalizer.Unknown)
                {
                    int j = i;
                    while (j < normalized.Length && normalized[j] == TextNormalizer.Unknown) j++;
                    foreach (char u in UnkText)
                    {
                        text.Append(u);
                        starts.Add(i);
                        ends.Add(j);
                    }
                    i = j;
                    continue;
                }
                if (!TextNormalizer.IsInAlphabet(c))
                    throw new ArgumentException($"U+{(int)c:X4} at {i} is not normalized text", nameof(normalized));
                text.Append(c);
                starts.Add(i);
                ends.Add(i + 1);
                i++;
            }

            string s = text.ToString();
            var tokens = new List<Token> { new Token(ClsId, 0, 0) };
            foreach (Match word in PreTokenizer.Matches(s))
                AddWordPieces(s, word.Index, word.Length, starts, ends, tokens);
            tokens.Add(new Token(SepId, 0, 0));
            return tokens;
        }

        /// <summary>WordPiece over one word: greedy longest match first, "##" after the first
        /// piece, and the whole word as [UNK] if it is too long or any part of it has no piece.</summary>
        private void AddWordPieces(string s, int at, int length, List<int> starts, List<int> ends, List<Token> tokens)
        {
            if (length > MaxInputCharsPerWord)
            {
                tokens.Add(new Token(UnkId, starts[at], ends[at + length - 1]));
                return;
            }
            var pieces = new List<Token>();
            int start = 0;
            while (start < length)
            {
                int end = length, found = -1;
                while (start < end)
                {
                    string sub = s.Substring(at + start, end - start);
                    found = Id(start > 0 ? ContinuingPrefix + sub : sub);
                    if (found >= 0) break;
                    end--;
                }
                if (found < 0)
                {
                    tokens.Add(new Token(UnkId, starts[at], ends[at + length - 1]));
                    return;
                }
                pieces.Add(new Token(found, starts[at + start], ends[at + end - 1]));
                start = end;
            }
            tokens.AddRange(pieces);
        }
    }
}
