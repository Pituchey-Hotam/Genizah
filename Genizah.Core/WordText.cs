namespace Genizah.Core
{
    /// <summary>
    /// Between Word's text and Unicode. Range.Text reads some of Word's own elements as ASCII
    /// control characters that mean something only to Word. <see cref="ToUnicode"/> maps each to
    /// its Unicode counterpart before the model's pipeline sees the text, one character for one,
    /// so offsets still match Word's Characters. Not part of the model's pipeline.
    /// </summary>
    public static class WordText
    {
        /// <summary>Word's non-breaking hyphen (Ctrl+Shift+-).</summary>
        public const char NonBreakingHyphen = (char)0x1E;

        /// <summary>Word's optional hyphen (Ctrl+-).</summary>
        private const char OptionalHyphen = (char)0x1F;

        /// <summary>A footnote or endnote reference mark.</summary>
        private const char NoteReference = (char)0x02;

        private const char ColumnBreak = (char)0x0E;

        /// <summary><paramref name="text"/>, as read from Word, with Word's own characters in Unicode.</summary>
        public static string ToUnicode(string text)
        {
            char[] chars = null;
            for (int i = 0; i < text.Length; i++)
            {
                char mapped = ToUnicode(text[i]);
                if (mapped == text[i]) continue;
                if (chars == null) chars = text.ToCharArray();
                chars[i] = mapped;
            }
            return chars == null ? text : new string(chars);
        }

        private static char ToUnicode(char c)
        {
            switch (c)
            {
                case NonBreakingHyphen: return (char)0x2011;
                // The soft hyphen, which normalization deletes.
                case OptionalHyphen: return (char)0x00AD;
                // The mark has no letters of its own: a word joiner, which normalization deletes.
                case NoteReference: return (char)0x2060;
                case ColumnBreak: return '\n';
                default: return c;
            }
        }

        /// <summary>
        /// <paramref name="text"/> with each hyphen as Word's non-breaking hyphen, so a censored
        /// name never breaks across lines. Not U+2011: most Hebrew fonts have no glyph for it, and
        /// Word writes it in another font.
        /// </summary>
        public static string ForWord(string text)
        {
            return text.Replace('-', NonBreakingHyphen).Replace((char)0x2011, NonBreakingHyphen);
        }
    }
}
