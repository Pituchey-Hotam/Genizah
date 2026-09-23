using Microsoft.Office.Interop.Word;
using System;
using System.Linq;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah.Results
{
    public class SearchResult
    {
        public string OriginalText { get; set; }
        public string ReplacementText { get; set; }
        public Bookmark Bookmark { get; set; }
        public WdColorIndex OriginalHighlight { get; set; }

        public SearchResult(Range matchRange, NameInfo name)
        {
            this.OriginalText = matchRange.Text;
            this.OriginalHighlight = matchRange.HighlightColorIndex;
            this.ReplacementText = name.getSelectedReplacement();
            // Replace text
            matchRange.Text = name.getSelectedReplacement();
            // Keep bookmark
            var bookmarkId = '_' + this.OriginalText + Guid.NewGuid().ToString().Split('-').First();
            this.Bookmark = Globals.ThisAddIn.Application.ActiveDocument.Bookmarks.Add(bookmarkId, matchRange);
        }

        public void UndoReplacement()
        {
            this.Bookmark.Range.Text = this.OriginalText;
            Unhighlight();
        }

        private const WdColorIndex INVALID_COLOR = (WdColorIndex)9999999;

        public void Highlight()
        {
            this.Bookmark.Range.HighlightColorIndex = WdColorIndex.wdYellow;
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
