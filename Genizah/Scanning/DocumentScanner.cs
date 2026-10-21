using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Genizah.Core;
using Microsoft.Office.Core;
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
        /// <summary>Between two stories' texts in <see cref="ReadText"/>, so no word runs from one into the next.</summary>
        public const char StoryBreak = '\r';

        /// <summary>
        /// The text of every story Word prints (<see cref="PrintedStories"/>), in that order,
        /// joined by <see cref="StoryBreak"/>.
        /// </summary>
        public static string ReadText(Word.Document doc)
        {
            return string.Join(StoryBreak.ToString(), PrintedStories(doc).Select(StoryText));
        }

        /// <summary>
        /// A story's text, with each offset i matching Characters[i + 1] of a <see cref="Plain"/>
        /// range, and Word's own characters in Unicode (<see cref="WordText"/>). Word hides its
        /// formatting and field characters from Range.Text, so Range.Start/End offsets don't match
        /// it, but Characters does. The exceptions are the end-of-cell mark, one character in two
        /// UTF-16 units ("\r\a"), and a content control's marks, characters with no text.
        /// </summary>
        public static string StoryText(Word.Range story)
        {
            return WordText.ToUnicode(Plain(story).Text?.Replace("\r\a", "\r") ?? "");
        }

        /// <summary>
        /// The stories Word prints: the body, the footnotes and endnotes, each section's own
        /// headers and footers, and the text of each text box and shape, grouped ones included.
        /// Not review comments, which aren't printed.
        /// </summary>
        public static List<Word.Range> PrintedStories(Word.Document doc)
        {
            var stories = new List<Word.Range> { doc.Content };
            foreach (Word.Range story in doc.StoryRanges)
                if (story.StoryType == Word.WdStoryType.wdFootnotesStory || story.StoryType == Word.WdStoryType.wdEndnotesStory)
                    stories.Add(story);
            foreach (Word.Section section in doc.Sections)
                foreach (Word.HeadersFooters headersFooters in new[] { section.Headers, section.Footers })
                    foreach (Word.HeaderFooter headerFooter in headersFooters)
                        // A linked one shows the previous section's, already read.
                        if (headerFooter.Exists && !headerFooter.LinkToPrevious)
                            stories.Add(headerFooter.Range);
            AddTextFrames(stories, doc.Shapes);
            // Any header's Shapes holds the shapes of every header and footer.
            AddTextFrames(stories, doc.Sections[1].Headers[Word.WdHeaderFooterIndex.wdHeaderFooterPrimary].Shapes);
            return stories;
        }

        private static void AddTextFrames(List<Word.Range> stories, IEnumerable shapes)
        {
            foreach (Word.Shape shape in shapes)
            {
                if (shape.Type == MsoShapeType.msoGroup)
                    AddTextFrames(stories, shape.GroupItems);
                else if (shape.Type == MsoShapeType.msoCanvas)
                    AddTextFrames(stories, shape.CanvasItems);
                else
                {
                    Word.TextFrame frame;
                    try { frame = shape.TextFrame; }
                    catch (COMException) { continue; } // A picture, say, has none.
                    // Linked text boxes share one story, read from the first.
                    if (frame.HasText != 0 && frame.Previous == null)
                        stories.Add(frame.ContainingRange);
                }
            }
        }

        /// <summary>
        /// <paramref name="range"/>, set to read fields as their results. By default Range.Text
        /// and Characters follow the view, and read field codes while the user shows them (Alt+F9).
        /// Every range whose text or characters the scan reads needs this, and keeps it in Duplicate.
        /// </summary>
        public static Word.Range Plain(Word.Range range)
        {
            range.TextRetrievalMode.IncludeFieldCodes = false;
            return range;
        }

        /// <summary>[start, end) of <paramref name="story"/>'s story, <see cref="Plain"/>.</summary>
        public static Word.Range Span(Word.Range story, int start, int end)
        {
            Word.Range range = story.Duplicate;
            range.SetRange(start, end);
            return Plain(range);
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

        /// <summary>
        /// Where <paramref name="character"/>'s own text lies in <paramref name="story"/>'s story.
        /// Beside a field, Characters widens a character over the field's hidden marks and code (a
        /// link's first letter starts at the link's opening mark), and a range that wide would
        /// rewrite the whole field. The positions no overlapping field hides are the character's;
        /// if they don't come to its length, the widened range stands.
        /// </summary>
        public static void Bounds(Word.Range story, Word.Range character, out int start, out int end)
        {
            start = character.Start;
            end = character.End;
            int length = character.Text?.Length ?? 0;
            if (end - start <= length)
                return;
            var hidden = new bool[end - start];
            foreach (Word.Field field in Span(story, start, end).Fields)
            {
                // The opening mark, the code and the separator; then the closing mark.
                Hide(hidden, start, field.Code.Start - 1, field.Code.End + 1);
                Hide(hidden, start, field.Result.End, field.Result.End + 1);
            }
            int first = -1, last = -1, shown = 0;
            for (int i = 0; i < hidden.Length; i++)
                if (!hidden[i])
                {
                    if (first < 0) first = i;
                    last = i;
                    shown++;
                }
            if (shown != length || last - first + 1 != length)
                return;
            start += first;
            end = start + length;
        }

        private static void Hide(bool[] hidden, int offset, int from, int to)
        {
            for (int i = Math.Max(from - offset, 0); i < Math.Min(to - offset, hidden.Length); i++)
                hidden[i] = true;
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
