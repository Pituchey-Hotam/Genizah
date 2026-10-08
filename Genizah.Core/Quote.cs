using System.Globalization;

namespace Genizah.Core
{
    /// <summary>
    /// Cuts a quote out of running text at a word boundary, so it never ends in half a word or
    /// an orphaned nikud mark. Width counts letters: a mark (nikud, cantillation) rides on the
    /// letter before it. Where the text goes on past the quote, "…" says so. Not part of the
    /// model's pipeline; it only decides what the results pane shows.
    /// </summary>
    public static class Quote
    {
        public const char Ellipsis = '…';

        private const char Maqaf = (char)0x05BE;

        /// <summary>
        /// Up to <paramref name="width"/> letters of <paramref name="text"/> ending at
        /// <paramref name="end"/>, starting at a word. If not even one whole word fits, just "…".
        /// </summary>
        public static string Before(string text, int end, int width)
        {
            int i = end;
            for (int letters = 0; i > 0 && letters < width; letters++)
            {
                i--;
                while (i > 0 && IsMark(text[i])) i--;
            }
            if (i == 0) return text.Substring(0, end);
            for (int k = i; k < end; k++)
                if (!IsBreak(text[k]) && IsBreak(text[k - 1]))
                    return Ellipsis + text.Substring(k, end - k);
            // Keep the space that separates the quote from what follows it.
            return end > 0 && IsBreak(text[end - 1]) ? Ellipsis + " " : Ellipsis.ToString();
        }

        /// <summary>
        /// Up to <paramref name="width"/> letters of <paramref name="text"/> from
        /// <paramref name="start"/> on, ending at a word. If not even one whole word fits, just "…".
        /// </summary>
        public static string After(string text, int start, int width)
        {
            int i = start;
            for (int letters = 0; i < text.Length && letters < width; letters++)
            {
                i++;
                while (i < text.Length && IsMark(text[i])) i++;
            }
            if (i == text.Length) return text.Substring(start);
            for (int k = i; k > start; k--)
                if (!IsBreak(text[k - 1]) && IsBreak(text[k]))
                    return text.Substring(start, k - start) + Ellipsis;
            // Keep the space that separates the quote from what precedes it.
            return start < text.Length && IsBreak(text[start]) ? " " + Ellipsis : Ellipsis.ToString();
        }

        /// <summary>Whitespace, a control character (Word's cell and paragraph marks), or a maqaf.</summary>
        private static bool IsBreak(char c) => char.IsWhiteSpace(c) || char.IsControl(c) || c == Maqaf;

        private static bool IsMark(char c)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                    return true;
                default:
                    return false;
            }
        }
    }
}
