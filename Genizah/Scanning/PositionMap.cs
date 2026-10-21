using System;
using System.Collections.Generic;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah.Scanning
{
    /// <summary>
    /// Maps offsets in <see cref="DocumentScanner.ReadText"/>'s text to Word ranges, a paragraph
    /// at a time. Indexing a whole story's Characters costs more the further in the offset is
    /// (over half a second per name in a 25,000-character document), so each story is read once
    /// as paragraphs and the gaps between them, each with its positions and text. Where a
    /// paragraph's text is as long as its span of positions, which is almost everywhere, offset
    /// and position move together; only a paragraph where they don't (a field, say) indexes
    /// Characters, and only its own.
    /// </summary>
    internal sealed class PositionMap
    {
        private sealed class Story
        {
            public Word.Range Range;
            private List<int> marks;

            /// <summary>
            /// How many content control marks lie at positions [from, to]. Characters has a
            /// character for each mark, which Range.Text leaves out.
            /// </summary>
            public int MarksIn(int from, int to)
            {
                if (marks == null)
                {
                    marks = new List<int>();
                    foreach (Word.ContentControl control in Range.ContentControls)
                    {
                        marks.Add(control.Range.Start - 1);
                        marks.Add(control.Range.End);
                    }
                }
                int count = 0;
                foreach (int mark in marks)
                    if (mark >= from && mark <= to) count++;
                return count;
            }
        }

        private sealed class Chunk
        {
            public int TextStart, TextEnd, Start, End;
            public Story Story;
            /// <summary>Its text is exactly as long as its positions, one for one.</summary>
            public bool Linear;
            public Word.Characters Characters;
            /// <summary>
            /// How many of Characters come before the chunk: after a field's closing mark, the
            /// next paragraph's Characters starts with the previous paragraph's mark.
            /// </summary>
            public int Skip;
        }

        private readonly List<Chunk> chunks;

        /// <summary>Stories whose paragraphs didn't add up to their text, mapped through their whole Characters.</summary>
        public int StoriesByCharacters { get; }

        private PositionMap(List<Chunk> chunks, int storiesByCharacters)
        {
            this.chunks = chunks;
            StoriesByCharacters = storiesByCharacters;
        }

        /// <summary>
        /// The map of <paramref name="doc"/>, whose text is <paramref name="text"/>; null if its
        /// stories' texts don't make up that text.
        /// </summary>
        public static PositionMap Build(Word.Document doc, string text)
        {
            var chunks = new List<Chunk>();
            int textAt = 0, byCharacters = 0;
            List<Word.Range> stories = DocumentScanner.PrintedStories(doc);
            for (int i = 0; i < stories.Count; i++)
            {
                if (i > 0)
                {
                    if (textAt >= text.Length || text[textAt] != DocumentScanner.StoryBreak)
                        return null;
                    textAt++;
                }
                var story = new Story { Range = DocumentScanner.Plain(stories[i]) };
                int storyAt = textAt, count = chunks.Count;
                if (!AddParagraphs(chunks, story, text, ref textAt))
                {
                    // Paragraphs that don't add up to the text: the story's whole Characters, which is slow but sure.
                    chunks.RemoveRange(count, chunks.Count - count);
                    textAt = storyAt;
                    if (!Add(chunks, story, story.Range, story.Range.Start, story.Range.End, text, ref textAt, false))
                        return null;
                    byCharacters++;
                }
            }
            return textAt == text.Length ? new PositionMap(chunks, byCharacters) : null;
        }

        private static bool AddParagraphs(List<Chunk> chunks, Story story, string text, ref int textAt)
        {
            Word.Range all = story.Range;
            int position = all.Start;
            foreach (Word.Paragraph paragraph in all.Paragraphs)
            {
                Word.Range range = paragraph.Range;
                int start = range.Start, end = range.End;
                // Whatever lies between paragraphs, such as a table row's end mark.
                if (start > position && !Add(chunks, story, DocumentScanner.Span(all, position, start), position, start, text, ref textAt))
                    return false;
                if (start < position || !Add(chunks, story, range, start, end, text, ref textAt))
                    return false;
                position = end;
            }
            int storyEnd = all.End;
            return storyEnd <= position || Add(chunks, story, DocumentScanner.Span(all, position, storyEnd), position, storyEnd, text, ref textAt);
        }

        private static bool Add(List<Chunk> chunks, Story story, Word.Range range, int start, int end, string text, ref int textAt, bool linear = true)
        {
            string chunkText = DocumentScanner.StoryText(range);
            if (textAt + chunkText.Length > text.Length || string.CompareOrdinal(text, textAt, chunkText, 0, chunkText.Length) != 0)
                return false;
            if (chunkText.Length == 0)
                return true;
            chunks.Add(new Chunk
            {
                TextStart = textAt,
                TextEnd = textAt + chunkText.Length,
                Start = start,
                End = end,
                Story = story,
                Linear = linear && chunkText.Length == end - start,
            });
            textAt += chunkText.Length;
            return true;
        }

        /// <summary>The Word range of [rawStart, rawEnd) in the text's offsets.</summary>
        public Word.Range ToRange(int rawStart, int rawEnd)
        {
            Chunk chunk = Find(rawStart);
            CharacterAt(chunk, rawStart, out int start, out _);
            CharacterAt(Find(rawEnd - 1), rawEnd - 1, out _, out int end);
            return DocumentScanner.Span(chunk.Story.Range, start, end);
        }

        private static void CharacterAt(Chunk chunk, int offset, out int start, out int end)
        {
            int local = offset - chunk.TextStart;
            if (chunk.Linear)
            {
                start = chunk.Start + local;
                end = start + 1;
                return;
            }
            if (chunk.Characters == null)
            {
                chunk.Characters = DocumentScanner.Span(chunk.Story.Range, chunk.Start, chunk.End).Characters;
                while (chunk.Skip < chunk.Characters.Count && chunk.Characters[chunk.Skip + 1].Start < chunk.Start)
                    chunk.Skip++;
            }
            // Past the content control marks before it, which have characters but no text.
            int marks = 0;
            Word.Range character;
            while (true)
            {
                character = chunk.Characters[chunk.Skip + local + marks + 1];
                int before = chunk.Story.MarksIn(chunk.Start, character.Start);
                if (before == marks) break;
                marks = before;
            }
            DocumentScanner.Bounds(chunk.Story.Range, character, out start, out end);
        }

        private Chunk Find(int offset)
        {
            int lo = 0, hi = chunks.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (chunks[mid].TextStart <= offset) lo = mid;
                else hi = mid - 1;
            }
            Chunk chunk = chunks[lo];
            if (offset < chunk.TextStart || offset >= chunk.TextEnd)
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "outside the document's text");
            return chunk;
        }
    }
}
