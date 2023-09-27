using Microsoft.Office.Interop.Word;

namespace Genizah.Results
{
    public class SearchResult
    {
        public string OriginalText { get; set; }
        public string ReplacementText { get; set; }
        public Bookmark Bookmark { get; set; }
        public WdColorIndex OriginalHighlight { get; set; }
    }
}
