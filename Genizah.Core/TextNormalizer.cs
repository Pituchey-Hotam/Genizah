using System;
using System.Collections.Generic;
using System.Text;

namespace Genizah.Core
{
    /// <summary>
    /// Text normalization: a port of the training code's normalize(), the contract that
    /// testdata/normalization.json pins. Every mapping is an explicit table, never a call into
    /// .NET's Unicode support, so the result can't depend on the runtime.
    ///
    /// Code points are written as numbers: the tables must survive editors and tools that would
    /// silently turn an escape into an invisible character or back.
    /// </summary>
    public static class TextNormalizer
    {
        /// <summary>Every character outside <see cref="Alphabet"/> becomes this.</summary>
        public const char Unknown = (char)0xFFFD;

        /// <summary>space, א..ת (finals included), 0, a, and the kept punctuation.</summary>
        public static readonly string Alphabet = " " + Range(0x05D0, 0x05EA) + "0a" + ".,:;!?()[]\"'-/";

        private static readonly Dictionary<int, string> CharMap = BuildCharMap();
        private static readonly bool[] InAlphabet = BuildAlphabetTable();

        /// <summary>The training code's normalize(text).</summary>
        public static string Normalize(string text)
        {
            return NormalizeWithOffsets(text).Text;
        }

        /// <summary>
        /// Normalizes <paramref name="raw"/> and records, for each normalized character, the
        /// span of <paramref name="raw"/> it came from.
        /// </summary>
        public static NormalizedText NormalizeWithOffsets(string raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            var text = new StringBuilder(raw.Length);
            var starts = new List<int>(raw.Length);
            var ends = new List<int>(raw.Length);

            int i = 0;
            while (i < raw.Length)
            {
                // One code point at a time, as Python iterates a str. A lone surrogate is a code
                // point of its own there too.
                int width = char.IsHighSurrogate(raw[i]) && i + 1 < raw.Length && char.IsLowSurrogate(raw[i + 1]) ? 2 : 1;
                int cp = width == 2 ? char.ConvertToUtf32(raw[i], raw[i + 1]) : raw[i];

                string mapped;
                if (!CharMap.TryGetValue(cp, out mapped))
                    mapped = null;  // unmapped: kept as it is, which step 3 may turn into Unknown

                if (mapped == null)
                {
                    char c = width == 1 && IsInAlphabet(raw[i]) ? raw[i] : Unknown;
                    Emit(text, starts, ends, c, i, i + width);
                }
                else
                {
                    foreach (char c in mapped)
                        Emit(text, starts, ends, IsInAlphabet(c) ? c : Unknown, i, i + width);
                }
                i += width;
            }
            return new NormalizedText(raw, text.ToString(), starts.ToArray(), ends.ToArray());
        }

        private static void Emit(StringBuilder text, List<int> starts, List<int> ends, char c, int rawStart, int rawEnd)
        {
            // Step 2: a run of spaces collapses to its first. Deleted characters were never
            // emitted, so spaces they separated are adjacent here, as in the Python.
            if (c == ' ' && text.Length > 0 && text[text.Length - 1] == ' ')
                return;
            text.Append(c);
            starts.Add(rawStart);
            ends.Add(rawEnd);
        }

        public static bool IsInAlphabet(char c)
        {
            return c < InAlphabet.Length && InAlphabet[c];
        }

        /// <summary>
        /// True for the characters normalization deletes that attach to the letter before them:
        /// nikud, cantillation and the other marks of U+0591-U+05C7 (but not maqaf, paseq or
        /// sof pasuq, which separate words), and the varika.
        /// </summary>
        public static bool IsHebrewMark(char c)
        {
            return (c >= 0x0591 && c <= 0x05C7 && c != 0x05BE && c != 0x05C0 && c != 0x05C3) || c == 0xFB1E;
        }

        private static bool[] BuildAlphabetTable()
        {
            var table = new bool[0x05EB];
            foreach (char c in Alphabet) table[c] = true;
            return table;
        }

        private static string Range(int first, int last)
        {
            var sb = new StringBuilder();
            for (int cp = first; cp <= last; cp++) sb.Append((char)cp);
            return sb.ToString();
        }

        private static string S(params int[] cps)
        {
            var sb = new StringBuilder();
            foreach (int cp in cps) sb.Append((char)cp);
            return sb.ToString();
        }

        private static IEnumerable<int> Cps(int first, int last)
        {
            for (int cp = first; cp <= last; cp++) yield return cp;
        }

        // Hebrew letters by name, for the presentation-form table.
        private const int Alef = 0x05D0, Bet = 0x05D1, Gimel = 0x05D2, Dalet = 0x05D3, He = 0x05D4,
            Vav = 0x05D5, Zayin = 0x05D6, Tet = 0x05D8, Yod = 0x05D9, FinalKaf = 0x05DA, Kaf = 0x05DB,
            Lamed = 0x05DC, FinalMem = 0x05DD, Mem = 0x05DE, Nun = 0x05E0, Samekh = 0x05E1,
            Ayin = 0x05E2, FinalPe = 0x05E3, Pe = 0x05E4, Tsadi = 0x05E6, Qof = 0x05E7,
            Resh = 0x05E8, Shin = 0x05E9, Tav = 0x05EA;

        /// <summary>The training code's CHAR_MAP, in the same order, put by put.</summary>
        private static Dictionary<int, string> BuildCharMap()
        {
            var m = new Dictionary<int, string>();
            Action<IEnumerable<int>, string> put = (cps, output) =>
            {
                foreach (int cp in cps)
                {
                    if (m.ContainsKey(cp)) throw new InvalidOperationException($"U+{cp:X4} mapped twice");
                    m[cp] = output;
                }
            };

            // Hebrew punctuation inside the mark range: word separators, not marks.
            put(new[] { 0x05BE, 0x05C0 }, " ");  // maqaf, paseq
            put(new[] { 0x05C3 }, ":");  // sof pasuq
            // Nikud, cantillation and the other marks of U+0591-U+05C7; the Judeo-Spanish varika.
            var marks = new List<int>();
            foreach (int cp in Cps(0x0591, 0x05C7))
                if (cp != 0x05BE && cp != 0x05C0 && cp != 0x05C3) marks.Add(cp);
            put(marks, "");
            put(new[] { 0xFB1E }, "");
            // Hebrew presentation forms -> base letters (their NFKD decomposition, marks dropped).
            put(new[] { 0xFB1D, 0xFB39 }, S(Yod));
            put(new[] { 0xFB1F }, S(Yod, Yod));
            put(new[] { 0xFB20 }, S(Ayin));
            put(new[] { 0xFB21, 0xFB2E, 0xFB2F, 0xFB30 }, S(Alef));
            put(new[] { 0xFB22, 0xFB33 }, S(Dalet));
            put(new[] { 0xFB23, 0xFB34 }, S(He));
            put(new[] { 0xFB24, 0xFB3B, 0xFB4D }, S(Kaf));
            put(new[] { 0xFB25, 0xFB3C }, S(Lamed));
            put(new[] { 0xFB26 }, S(FinalMem));
            put(new[] { 0xFB27, 0xFB48 }, S(Resh));
            put(new[] { 0xFB28, 0xFB4A }, S(Tav));
            put(new[] { 0xFB29 }, "+");
            put(new[] { 0xFB2A, 0xFB2B, 0xFB2C, 0xFB2D, 0xFB49 }, S(Shin));
            put(new[] { 0xFB31, 0xFB4C }, S(Bet));
            put(new[] { 0xFB32 }, S(Gimel));
            put(new[] { 0xFB35, 0xFB4B }, S(Vav));
            put(new[] { 0xFB36 }, S(Zayin));
            put(new[] { 0xFB38 }, S(Tet));
            put(new[] { 0xFB3A }, S(FinalKaf));
            put(new[] { 0xFB3E }, S(Mem));
            put(new[] { 0xFB40 }, S(Nun));
            put(new[] { 0xFB41 }, S(Samekh));
            put(new[] { 0xFB43 }, S(FinalPe));
            put(new[] { 0xFB44, 0xFB4E }, S(Pe));
            put(new[] { 0xFB46 }, S(Tsadi));
            put(new[] { 0xFB47 }, S(Qof));
            put(new[] { 0xFB4F }, S(Alef, Lamed));
            // Yiddish ligatures.
            put(new[] { 0x05F0 }, S(Vav, Vav));
            put(new[] { 0x05F1 }, S(Vav, Yod));
            put(new[] { 0x05F2 }, S(Yod, Yod));
            // Quote, geresh and gershayim variants.
            put(new[] { 0x2018, 0x2019, 0x201A, 0x201B, 0x2032, 0x05F3, 0x0060, 0x00B4 }, "'");
            put(new[] { 0x201C, 0x201D, 0x201E, 0x201F, 0x2033, 0x05F4 }, "\"");
            // Dash variants. Maqaf is not one: it became a space above.
            put(new[] { 0x2010, 0x2011, 0x2012, 0x2013, 0x2014, 0x2015, 0x2212, 0xFE58, 0xFE63, 0xFF0D }, "-");
            // Invisible format characters (bidi marks, zero-width, soft hyphen, BOM): deleted.
            put(new[] { 0x00AD, 0x034F, 0x061C, 0x180E, 0xFEFF }, "");
            put(Cps(0x200B, 0x200F), "");
            put(Cps(0x202A, 0x202E), "");
            put(Cps(0x2060, 0x2064), "");
            put(Cps(0x2066, 0x2069), "");
            // Whitespace.
            var spaces = new List<int>(Cps(0x0009, 0x000D)) { 0x0085, 0x00A0, 0x1680 };
            spaces.AddRange(Cps(0x2000, 0x200A));
            spaces.AddRange(new[] { 0x2028, 0x2029, 0x202F, 0x205F, 0x3000 });
            put(spaces, " ");
            // Character classes.
            put(Cps('0', '9'), "0");
            var latin = new List<int>(Cps('A', 'Z'));
            latin.AddRange(Cps('b', 'z'));
            put(latin, "a");
            return m;
        }
    }

    /// <summary>
    /// A normalized string and, for each of its characters, the span of the raw string it came
    /// from. A character that expands (the אל ligature) gives each output character the same
    /// span; a collapsed run of spaces keeps the span of its first space.
    /// </summary>
    public sealed class NormalizedText
    {
        private readonly int[] rawStarts;
        private readonly int[] rawEnds;

        internal NormalizedText(string raw, string text, int[] rawStarts, int[] rawEnds)
        {
            Raw = raw;
            Text = text;
            this.rawStarts = rawStarts;
            this.rawEnds = rawEnds;
        }

        public string Raw { get; }
        public string Text { get; }

        /// <summary>
        /// The raw span [start, end) that normalized [<paramref name="start"/>,
        /// <paramref name="end"/>) came from. It is widened over the Hebrew marks that follow its
        /// last character (the qamats of אֱלֹהֶיךָ), so replacing the raw span leaves no mark
        /// behind to land on the next character.
        /// </summary>
        public void ToRaw(int start, int end, out int rawStart, out int rawEnd)
        {
            if (start < 0 || end > Text.Length || start >= end)
                throw new ArgumentOutOfRangeException(nameof(start), $"[{start}, {end}) is not a non-empty span of {Text.Length} characters");
            rawStart = rawStarts[start];
            rawEnd = rawEnds[end - 1];
            while (rawEnd < Raw.Length && TextNormalizer.IsHebrewMark(Raw[rawEnd]))
                rawEnd++;
        }
    }
}
