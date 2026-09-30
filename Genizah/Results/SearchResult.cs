using System;
using Genizah.Core;
using Genizah.Scanning;
using Microsoft.Office.Interop.Word;

namespace Genizah.Results
{
    /// <summary>Where a result stands in the document.</summary>
    public enum ResultState
    {
        /// <summary>The model asked, and the user hasn't answered yet.</summary>
        Pending,
        Censored,
        Left,
    }

    /// <summary>
    /// One candidate the classifier scored, and what has been done with it in the document.
    /// </summary>
    public class SearchResult
    {
        public NameInfo Name { get; }
        public float Logit { get; }

        /// <summary>What the model's score said to do. Fixed; <see cref="State"/> is what was done.</summary>
        public Verdict Verdict { get; }
        public ResultState State { get; private set; }

        public string OriginalText { get; }
        public string CensoredText { get; }

        /// <summary>A little of the text around the name, for deciding without looking at the document.</summary>
        public string ContextBefore { get; }
        public string ContextAfter { get; }

        public Bookmark Bookmark { get; private set; }
        public WdColorIndex OriginalHighlight { get; }

        private readonly Document doc;
        private readonly string bookmarkName;

        /// <summary>Marks the candidate with a bookmark; <see cref="Censor"/> is what changes its text.</summary>
        public SearchResult(Document doc, Range range, NameInfo name, float logit, Verdict verdict, string contextBefore, string contextAfter)
        {
            this.doc = doc;
            Name = name;
            Logit = logit;
            Verdict = verdict;
            State = verdict == Verdict.Leave ? ResultState.Left : ResultState.Pending;
            OriginalText = range.Text;
            OriginalHighlight = range.HighlightColorIndex;
            CensoredText = NameCensor.Apply(OriginalText, name.BaseForm, name.getSelectedReplacement());
            ContextBefore = contextBefore;
            ContextAfter = contextAfter;
            // Hidden (a leading underscore), and within Word's 40-character limit.
            bookmarkName = "_g" + Guid.NewGuid().ToString("N");
            Bookmark = doc.Bookmarks.Add(bookmarkName, range);
        }

        public void Censor()
        {
            if (State == ResultState.Censored) return;
            Replace(CensoredText);
            State = ResultState.Censored;
        }

        public void Leave()
        {
            if (State == ResultState.Censored)
                Replace(OriginalText);
            State = ResultState.Left;
        }

        private void Replace(string text)
        {
            // Setting the text of a bookmark's whole range deletes the bookmark; the range itself
            // grows to cover the new text, so the bookmark is put back on it.
            Range range = Bookmark.Range;
            range.Text = text;
            Bookmark = doc.Bookmarks.Add(bookmarkName, range);
        }

        /// <summary>Drops the result: its highlight and its bookmark go; its text stays as it is.</summary>
        public void Release()
        {
            Unhighlight();
            Bookmark.Delete();
        }

        private const WdColorIndex INVALID_COLOR = (WdColorIndex)9999999;

        public void Highlight()
        {
            WdColorIndex color;
            switch (State)
            {
                case ResultState.Pending: color = WdColorIndex.wdYellow; break;
                case ResultState.Censored: color = WdColorIndex.wdBrightGreen; break;
                default: color = WdColorIndex.wdGray25; break;
            }
            this.Bookmark.Range.HighlightColorIndex = color;
        }

        public void Unhighlight()
        {
            // If the original text was partially highlighted (or had multiple highlight colors), INVALID_COLOR will be returned.
            // For now, we just don't keep the highlight information in this case.
            if (this.OriginalHighlight != INVALID_COLOR)
            {
                this.Bookmark.Range.HighlightColorIndex = this.OriginalHighlight;
            }
            else
            {
                this.Bookmark.Range.HighlightColorIndex = WdColorIndex.wdNoHighlight;
            }
        }
    }
}
