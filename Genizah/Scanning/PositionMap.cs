using System;
using System.Collections.Generic;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah.Scanning
{
    /// <summary>
    /// Maps offsets in <see cref="DocumentScanner.ReadText"/>'s text to Word ranges, a paragraph
    /// at a time. Indexing the whole document's Characters costs more the further in the offset
    /// is (over half a second per name in a 25,000-character document), so the document is read
    /// once as paragraphs and the gaps between them, each with its positions and text. Where a
    /// paragraph's text is as long as its span of positions, which is almost everywhere, offset
    /// and position move together; only a paragraph where they don't (a field, say) indexes
    /// Characters, and only its own.
    /// </summary>
    internal sealed class PositionMap
    {
        private sealed class Chunk
        {
            public int TextStart, TextEnd, Start, End;
            /// <summary>Its text is exactly as long as its positions, one for one.</summary>
            public bool Linear;
            public Word.Characters Characters;
        }

        private readonly Word.Document doc;
        private readonly List<Chunk> chunks;

        private PositionMap(Word.Document doc, List<Chunk> chunks)
        {
            this.doc = doc;
            this.chunks = chunks;
        }

        /// <summary>
        /// The map of <paramref name="doc"/>, whose text is <paramref name="text"/>; null if its
        /// paragraphs' texts, laid end to end, don't make up that text.
        /// </summary>
        public static PositionMap Build(Word.Document doc, string text)
        {
            var chunks = new List<Chunk>();
            int textAt = 0, position = doc.Content.Start;
            foreach (Word.Paragraph paragraph in doc.Paragraphs)
            {
                Word.Range range = paragraph.Range;
                int start = range.Start, end = range.End;
                // Whatever lies between paragraphs, such as a table row's end mark.
                if (start > position && !Add(chunks, doc.Range(position, start), position, start, text, ref textAt))
                    return null;
                if (start < position || !Add(chunks, range, start, end, text, ref textAt))
                    return null;
                position = end;
            }
            int contentEnd = doc.Content.End;
            if (contentEnd > position && !Add(chunks, doc.Range(position, contentEnd), position, contentEnd, text, ref textAt))
                return null;
            return textAt == text.Length ? new PositionMap(doc, chunks) : null;
        }

        private static bool Add(List<Chunk> chunks, Word.Range range, int start, int end, string text, ref int textAt)
        {
            string chunkText = range.Text?.Replace("\r\a", "\r") ?? "";
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
                Linear = chunkText.Length == end - start,
            });
            textAt += chunkText.Length;
            return true;
        }

        /// <summary>The Word range of [rawStart, rawEnd) in the text's offsets.</summary>
        public Word.Range ToRange(int rawStart, int rawEnd)
        {
            CharacterAt(rawStart, out int start, out _);
            CharacterAt(rawEnd - 1, out _, out int end);
            return doc.Range(start, end);
        }

        private void CharacterAt(int offset, out int start, out int end)
        {
            Chunk chunk = Find(offset);
            int local = offset - chunk.TextStart;
            if (chunk.Linear)
            {
                start = chunk.Start + local;
                end = start + 1;
                return;
            }
            if (chunk.Characters == null)
                chunk.Characters = doc.Range(chunk.Start, chunk.End).Characters;
            Word.Range character = chunk.Characters[local + 1];
            start = character.Start;
            end = character.End;
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
