using System.Linq;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    public class WordTextTests
    {
        private const char NonBreakingHyphen = (char)0x1E;
        private const char OptionalHyphen = (char)0x1F;
        private const char NoteReference = (char)0x02;
        private const char ColumnBreak = (char)0x0E;

        [Fact]
        public void EachWordCharacterBecomesOneUnicodeCharacter()
        {
            string word = "א" + NonBreakingHyphen + "ב" + OptionalHyphen + "ג" + NoteReference + "ד" + ColumnBreak + "ה\r";
            string expected = "א" + (char)0x2011 + "ב" + (char)0x00AD + "ג" + (char)0x2060 + "ד" + "\n" + "ה\r";
            Assert.Equal(expected, WordText.ToUnicode(word));
        }

        [Fact]
        public void TextWithoutWordCharactersIsUnchanged()
        {
            const string text = "ויאמר אלהים - לו\r";
            Assert.Same(text, WordText.ToUnicode(text));
        }

        [Fact]
        public void AnOptionalHyphenInsideANameKeepsTheName()
        {
            string word = "ויאמר אל" + OptionalHyphen + "הים לו";
            var candidate = Assert.Single(NameFinder.Find(TextNormalizer.Normalize(WordText.ToUnicode(word))));
            Assert.Equal("elohim", candidate.NameId);
        }

        [Fact]
        public void TheModelSeesWordCharactersAsTheirUnicode()
        {
            Assert.Equal(TextNormalizer.Normalize("ויאמר אלהים לו"),
                         TextNormalizer.Normalize(WordText.ToUnicode("ויאמר אלהים" + NoteReference + " לו")));
            Assert.Equal(TextNormalizer.Normalize("א-להים"),
                         TextNormalizer.Normalize(WordText.ToUnicode("א" + NonBreakingHyphen + "להים")));
            Assert.Equal(TextNormalizer.Normalize("אלהים לו"),
                         TextNormalizer.Normalize(WordText.ToUnicode("אלהים" + ColumnBreak + "לו")));
        }

        [Theory]
        [InlineData("א-להים")]
        [InlineData("-להים")]
        [InlineData("צב-אות")]
        [InlineData("ה'")]
        public void ForWordUsesWordsNonBreakingHyphen(string replacement)
        {
            string word = WordText.ForWord(replacement);
            Assert.DoesNotContain('-', word);
            Assert.Equal(replacement.Count(c => c == '-'), word.Count(c => c == NonBreakingHyphen));
            Assert.Equal(replacement.Replace('-', (char)0x2011), WordText.ToUnicode(word));
        }

        [Fact]
        public void AnEditFoundOnTheUnicodeIsMadeOnWordsText()
        {
            string word = "ואל" + OptionalHyphen + "הים";
            NameCensor.Edit(WordText.ToUnicode(word), "אלהים", "א-להים", out int start, out int end, out string insert);
            string censored = word.Substring(0, start) + WordText.ForWord(insert) + word.Substring(end);
            Assert.Equal("וא" + NonBreakingHyphen + "ל" + OptionalHyphen + "הים", censored);
        }
    }
}
