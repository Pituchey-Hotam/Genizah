using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    public class QuoteTests
    {
        [Theory]
        // Fits: as is, without "…".
        [InlineData("ויאמר משה", 9, "ויאמר משה")]
        [InlineData("ויאמר משה ", 20, "ויאמר משה ")]
        // A cut drops the partial word, and keeps the space before what follows.
        [InlineData("ויאמר משה ", 6, "…משה ")]
        [InlineData("ויאמר משה ", 4, "…משה ")]
        [InlineData("ויאמר משה", 3, "…משה")]
        // Nikud rides on its letter: four letters, not eight characters.
        [InlineData("וַיֹּאמֶר מֹשֶׁה ", 4, "…מֹשֶׁה ")]
        // A maqaf and Word's cell mark are boundaries too.
        [InlineData("כל־הארץ ", 6, "…הארץ ")]
        [InlineData("תא\r\aהארץ ", 6, "…הארץ ")]
        // Not one whole word fits.
        [InlineData("ויאמר ", 3, "… ")]
        [InlineData("ויאמר", 3, "…")]
        public void Before(string text, int width, string expected)
        {
            Assert.Equal(expected, Quote.Before(text, text.Length, width));
        }

        [Theory]
        [InlineData(" ויאמר משה", 10, " ויאמר משה")]
        [InlineData(" ויאמר משה", 7, " ויאמר…")]
        [InlineData(" ויאמר משה", 6, " ויאמר…")]
        [InlineData(" וַיֹּאמֶר מֹשֶׁה", 6, " וַיֹּאמֶר…")]
        [InlineData(" כל־הארץ", 4, " כל…")]
        [InlineData(" ויאמר", 3, " …")]
        [InlineData("ויאמר", 3, "…")]
        public void After(string text, int width, string expected)
        {
            Assert.Equal(expected, Quote.After(text, 0, width));
        }

        [Fact]
        public void BeforeAndAfterASpan()
        {
            string text = "בראשית ברא אלהים את השמים";
            int start = text.IndexOf("אלהים"), end = start + "אלהים".Length;
            Assert.Equal("…ברא ", Quote.Before(text, start, 6));
            Assert.Equal(" את…", Quote.After(text, end, 6));
        }
    }
}
