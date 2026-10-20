using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Genizah.Core
{
    /// <summary>One occurrence of a divine-name spelling, in normalized-text offsets.</summary>
    public sealed class NameCandidate
    {
        public NameCandidate(int start, int end, string nameId, string text)
        {
            Start = start;
            End = end;
            NameId = nameId;
            Text = text;
        }

        public int Start { get; }
        public int End { get; }

        /// <summary>One of <see cref="NameFinder.NameOrder"/>; the same ids as NameInfo.cs.</summary>
        public string NameId { get; }

        /// <summary>The matched text, proclitics included: the model's target.</summary>
        public string Text { get; }

        public override string ToString() => $"[{Start}, {End}) {NameId} {Text}";
    }

    /// <summary>
    /// Finds the candidates the classifier decides on. A port of the training code's candidate
    /// finder, so the add-in sees exactly the occurrences the training data was built from:
    /// the patterns are its UNDASHED_PATTERNS, and the scan is its _undashed_pass (longest span
    /// wins, NameOrder breaks ties, spans never overlap). Built piece by piece as the training
    /// code builds them, so the resulting pattern strings can be compared with Python's exactly
    /// (Genizah.Core.Tests).
    ///
    /// Run it on normalized text (<see cref="TextNormalizer"/>): nikud is gone there, so a mark
    /// can't hide a word boundary (the אל inside a vocalized ישראל), and maqaf is a space.
    /// </summary>
    public static class NameFinder
    {
        /// <summary>The training code's NAME_ORDER: the tie-break between equal spans.</summary>
        public static readonly IReadOnlyList<string> NameOrder = new[]
        {
            "havaya", "elohim", "eloha", "adnut", "tsvaot", "ehye", "shaday", "el",
        };

        // Hebrew letters, written once so the patterns below read like the training code's.
        private const string Alef = "א", Bet = "ב", Dalet = "ד", He = "ה", Vav = "ו",
            Yod = "י", FinalKaf = "ך", Kaf = "כ", Lamed = "ל", FinalMem = "ם",
            Mem = "מ", FinalNun = "ן", Nun = "נ", FinalPe = "ף", Tsadi = "צ", Shin = "ש", Tav = "ת";

        private static readonly string LetterClass = "[" + Alef + "-" + Tav + "]";

        /// <summary>The training code's MARK.</summary>
        private static readonly string Mark = "[" + (char)0x0591 + "-" + (char)0x05C7 + "]";

        /// <summary>
        /// The training code's PROCLITIC_UNDASHED: ו, then ש/כש only if another proclitic follows (a bare
        /// relative ש makes שאל, שאלה, שאהיה), then ב/ל/מ/כ, then ה.
        /// </summary>
        private static readonly string ProcliticUndashed =
            Vav + "?(?:(?:" + Kaf + Shin + "|" + Shin + ")(?=[" + Bet + Lamed + Mem + Kaf + He + "]))?["
            + Bet + Lamed + Mem + Kaf + "]?" + He + "?";

        /// <summary>
        /// The training code's PROCLITIC_UNDASHED_BARE_SH, for יהוה and אלהים only (BARE_SH_NAMES): a bare
        /// ש is allowed there (שיהוה), since no ordinary word is שיהוה or שאלהים.
        /// </summary>
        private static readonly string ProcliticUndashedBareSh =
            Vav + "?(?:" + Kaf + Shin + "(?=[" + Bet + Lamed + Mem + Kaf + He + "])|" + Shin + ")?["
            + Bet + Lamed + Mem + Kaf + "]?" + He + "?";

        private static readonly HashSet<string> BareShNames = new HashSet<string> { "havaya", "elohim" };

        private static readonly string ElohaSuffixAlt = "(?:" + Vav + Tav + "|" + Yod + Tav + "|" + Yod + Vav + Tav + ")?";

        private static readonly string ElohimTailAlt = "(?:(?:" + Yod + Yod + "|" + Yod + ")?" + FinalMem
            + "|" + Yod + Kaf + FinalMem + "|" + Yod + He + FinalMem + "|" + Yod + Nun + Vav
            + "|" + Yod + Vav + "|" + Yod + He + "|" + Yod + FinalKaf + "|" + Yod + ")";

        private static readonly string ElTailAlt = "(?:" + Yod + ")?";

        /// <summary>
        /// The training code's _ELOHIM_ARAMAIC_TAIL_ALT: Aramaic אלה's endings (אלהא, אלהין, אלהיא, אלהך,
        /// אלהה, אלהכם, אלהכון, אלההם, אלההון, אלהנא), an alternative to elohim's own tail.
        /// </summary>
        private static readonly string ElohimAramaicTailAlt = "(?:" + Alef + "|" + Yod + FinalNun
            + "|" + Yod + Alef + "|" + FinalKaf + "|" + He + "|" + Kaf + FinalMem + "|" + Kaf + Vav + FinalNun
            + "|" + He + FinalMem + "|" + He + Vav + FinalNun + "|" + Nun + Alef + ")";

        /// <summary>
        /// The training code's GERSHAYIM_CHARS: every quote character the normalizer leaves as " or ',
        /// plus the ones it maps there, in the training code's order.
        /// </summary>
        private static readonly string GershayimChars = "\"'" + new string(new[]
        {
            (char)0x05F4, (char)0x05F3, (char)0x201C, (char)0x201D, (char)0x201E, (char)0x201F, (char)0x2033,
            (char)0x2018, (char)0x2019, (char)0x201A, (char)0x201B, (char)0x2032, (char)0x0060, (char)0x00B4,
        });

        /// <summary>
        /// The training code's _EL_NOT_ALEF: an אל followed by gershayim and ף is the letter name אל"ף
        /// (האות אל"ף, האל"ף-בי"ת), never the divine name.
        /// </summary>
        private static readonly string ElNotAlef = "(?!" + Mark + "*[" + GershayimChars + "]" + Mark + "*" + FinalPe + ")";

        /// <summary>The training code's SKELETONS: each name's letters, and which are optional.</summary>
        private static readonly Dictionary<string, (string Letter, bool Optional)[]> Skeletons =
            new Dictionary<string, (string, bool)[]>
            {
                ["havaya"] = new[] { (Yod, false), (He, false), (Vav, false), (He, false) },
                ["adnut"] = new[] { (Alef, false), (Dalet, false), (Nun, false), (Yod, false) },
                ["el"] = new[] { (Alef, false), (Lamed, false) },
                ["eloha"] = new[] { (Alef, false), (Lamed, false), (Vav, true), (He, false) },
                ["elohim"] = new[] { (Alef, false), (Lamed, false), (Vav, true), (He, false), (Yod, true), (FinalMem, false) },
                ["ehye"] = new[] { (Alef, false), (He, false), (Yod, false), (He, false) },
                ["shaday"] = new[] { (Shin, false), (Dalet, false), (Yod, false) },
                ["tsvaot"] = new[] { (Tsadi, false), (Bet, false), (Alef, false), (Vav, true), (Tav, false) },
            };

        /// <summary>Each name's pattern source, exactly as Python's UNDASHED_PATTERNS[name].pattern.</summary>
        public static readonly IReadOnlyDictionary<string, string> PatternSources =
            NameOrder.ToDictionary(n => n, n => BuildPattern(n, Skeletons[n]));

        private static readonly Regex[] Patterns =
            NameOrder.Select(n => new Regex(PatternSources[n], RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToArray();

        /// <summary>Every candidate in <paramref name="normalized"/>, in text order.</summary>
        public static List<NameCandidate> Find(string normalized)
        {
            if (normalized == null) throw new ArgumentNullException(nameof(normalized));
            var hits = new List<(int Start, int End, int Order)>();
            for (int n = 0; n < Patterns.Length; n++)
                foreach (Match m in Patterns[n].Matches(normalized))
                    hits.Add((m.Index, m.Index + m.Length, n));

            // Longest span wins; NameOrder breaks ties, so an overlap can never demote a name to a
            // shorter one.
            hits.Sort((x, y) =>
                x.Start != y.Start ? x.Start.CompareTo(y.Start)
                : x.End - x.Start != y.End - y.Start ? (y.End - y.Start).CompareTo(x.End - x.Start)
                : x.Order.CompareTo(y.Order));

            var taken = new List<NameCandidate>();
            foreach (var h in hits)
            {
                if (taken.Any(t => h.Start < t.End && t.Start < h.End)) continue;
                taken.Add(new NameCandidate(h.Start, h.End, NameOrder[h.Order], normalized.Substring(h.Start, h.End - h.Start)));
            }
            taken.Sort((x, y) => x.Start.CompareTo(y.Start));
            return taken;
        }

        private static string BuildPattern(string name, (string Letter, bool Optional)[] letters)
        {
            // Hebrew letters need no escaping, in Python's re.escape or here.
            string rest = RestFor(name, letters);
            if (name == "elohim")
                rest = "(?:" + rest + "|" + SkeletonBody(letters.Skip(1).Take(3)) + Mark + "*" + ElohimAramaicTailAlt + ")";
            string proclitic = BareShNames.Contains(name) ? ProcliticUndashedBareSh : ProcliticUndashed;
            return "(?<!" + LetterClass + ")" + proclitic + letters[0].Letter + Mark + "*"
                + rest + "(?!" + LetterClass + ")" + (name == "el" ? ElNotAlef : "");
        }

        /// <summary>The training code's _skeleton_body.</summary>
        private static string SkeletonBody(IEnumerable<(string Letter, bool Optional)> letters)
        {
            var parts = new List<string>();
            int i = 0;
            foreach (var (letter, optional) in letters)
            {
                if (i++ > 0) parts.Add(Mark + "*");
                parts.Add(optional ? "(?:" + letter + ")?" : letter);
            }
            return string.Concat(parts);
        }

        /// <summary>The training code's _rest_for: everything after a name's first letter.</summary>
        private static string RestFor(string name, (string Letter, bool Optional)[] letters)
        {
            switch (name)
            {
                case "elohim":
                    return SkeletonBody(letters.Skip(1).Take(3)) + Mark + "*" + ElohimTailAlt;
                case "eloha":
                    return SkeletonBody(letters.Skip(1)) + Mark + "*" + ElohaSuffixAlt;
                case "el":
                    return SkeletonBody(letters.Skip(1)) + Mark + "*" + ElTailAlt;
                default:
                    return SkeletonBody(letters.Skip(1));
            }
        }
    }
}
