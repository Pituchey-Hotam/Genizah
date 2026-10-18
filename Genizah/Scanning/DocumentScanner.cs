using System;
using System.Collections.Generic;
using Genizah.Core;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah.Scanning
{
    /// <summary>
    /// Between a Word document and the classifier: the text it scores, and the Word range of each
    /// span it returns. Everything that touches Word runs on the UI thread; only
    /// <see cref="Score"/> may run off it.
    /// </summary>
    internal static class DocumentScanner
    {
        /// <summary>
        /// The main story's text, with each offset i matching Characters[i + 1]. Word hides its
        /// formatting and field characters from Range.Text, so Range.Start/End offsets don't match
        /// it, but Characters does. The one exception is the end-of-cell mark: one character,
        /// two UTF-16 units ("\r\a").
        /// </summary>
        public static string ReadText(Word.Document doc)
        {
            return doc.Content.Text.Replace("\r\a", "\r");
        }

        /// <summary>Every candidate in <paramref name="text"/>, scored. Runs off the UI thread.</summary>
        public static List<ScoredCandidate> Score(NameClassifier classifier, string text, IProgress<(int Done, int Total)> progress)
        {
            var scored = new List<ScoredCandidate>();
            var all = classifier.Scan(text, out int total);
            progress?.Report((0, total));
            foreach (var s in all)
            {
                scored.Add(s);
                progress?.Report((scored.Count, total));
            }
            return scored;
        }

        /// <summary>The Word range of [rawStart, rawEnd) in <see cref="ReadText"/>'s offsets.</summary>
        public static Word.Range ToRange(Word.Document doc, Word.Characters chars, int rawStart, int rawEnd)
        {
            return doc.Range(chars[rawStart + 1].Start, chars[rawEnd].End);
        }

        /// <summary>
        /// Up to <paramref name="width"/> letters of the span's context on each side, cut at word
        /// boundaries (<see cref="Quote"/>), on one line.
        /// </summary>
        public static void Context(string text, int rawStart, int rawEnd, int width, out string before, out string after)
        {
            before = OneLine(Quote.Before(text, rawStart, width));
            after = OneLine(Quote.After(text, rawEnd, width));
        }

        private static string OneLine(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (char.IsControl(chars[i])) chars[i] = ' ';
            return new string(chars);
        }
    }
}
