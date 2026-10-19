using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    public class NameCensorTests
    {
        [Theory]
        // The base form itself: the replacement, as written.
        [InlineData("אלהים", "אלהים", "אלקים", "אלקים")]
        [InlineData("יהוה", "יהוה", "ה'", "ה'")]
        // Proclitics are kept.
        [InlineData("ביהוה", "יהוה", "ה'", "בה'")]
        [InlineData("והאלהים", "אלהים", "א-להים", "והא-להים")]
        [InlineData("משדי", "שדי", "ש-די", "מש-די")]
        // Suffixes and extra matres too: the edit lands on the candidate's own letters.
        [InlineData("ואלהינו", "אלהים", "אלקים", "ואלקינו")]
        [InlineData("ואלהינו", "אלהים", "-להים", "ו-להינו")]
        [InlineData("אלוהיך", "אלהים", "אלקים", "אלוקיך")]
        [InlineData("צבאת", "צבאות", "צב-ות", "צב-ת")]
        [InlineData("צבאות", "צבאות", "צב-אות", "צב-אות")]
        [InlineData("לאל", "אל", "קל", "לקל")]
        // Nikud outside the edit stays; the edited letter's goes with it.
        [InlineData("וֵאלֹהֵינוּ", "אלהים", "אלקים", "וֵאלֹקינוּ")]
        [InlineData("שַׁדַּי", "שדי", "ש-די", "שַׁ-דַּי")]
        // Letters that don't line up as far as the edit reaches: replace from the name on.
        [InlineData("ואלהינו", "אלהים", "ה'", "וה'")]
        public void Apply(string raw, string baseForm, string replacement, string expected)
        {
            Assert.Equal(expected, NameCensor.Apply(raw, baseForm, replacement));
        }

        [Theory]
        [InlineData("ש-די")]
        [InlineData("-די")]
        [InlineData("ה'")]
        [InlineData("שד-")]
        public void ShadayReplacementPreservesAmbiguousProclitics(string replacement)
        {
            foreach (string prefix in new[] { "", "מ", "שב", "שבה", "כשב", "ושל", "וכשבה" })
            {
                string raw = prefix + "שדי";
                var candidate = Assert.Single(NameFinder.Find(raw));
                Assert.Equal("shaday", candidate.NameId);
                Assert.Equal(raw, candidate.Text);
                Assert.Equal(prefix + replacement, NameCensor.Apply(raw, "שדי", replacement));
            }
        }

        [Theory]
        [InlineData("ש-די", "שֶׁבְּשַׁ-דַּי")]
        [InlineData("-די", "שֶׁבְּ-דַּי")]
        [InlineData("ה'", "שֶׁבְּה'")]
        public void AmbiguousProcliticsKeepTheirNikud(string replacement, string expected)
        {
            const string raw = "שֶׁבְּשַׁדַּי";
            var candidate = Assert.Single(NameFinder.Find(TextNormalizer.NormalizeWithOffsets(raw).Text));
            Assert.Equal("shaday", candidate.NameId);
            Assert.Equal(expected, NameCensor.Apply(raw, "שדי", replacement));
        }
    }
}
