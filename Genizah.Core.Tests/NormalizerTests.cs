using System.Collections.Generic;
using System.IO;
using System.Linq;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    public class NormalizerTests
    {
        public static IEnumerable<object[]> NormalizeCases()
        {
            using (var doc = TestData.Json(TestData.TestDataPath("normalization.json")))
                foreach (var c in doc.RootElement.GetProperty("normalize").EnumerateArray())
                    yield return new object[] { c.GetProperty("case").GetString(), c.GetProperty("input").GetString(), c.GetProperty("output").GetString() };
        }

        /// <summary>The shared vectors every implementation must pass. (Their "encode" section
        /// pins the character BiGRU's ids, which the shipping BEREL model doesn't use.)</summary>
        [Theory]
        [MemberData(nameof(NormalizeCases))]
        public void SharedVectors(string name, string input, string output)
        {
            Assert.True(output == TextNormalizer.Normalize(input), name);
        }

        [Theory]
        [MemberData(nameof(NormalizeCases))]
        public void Idempotent(string name, string input, string output)
        {
            Assert.True(output == TextNormalizer.Normalize(TextNormalizer.Normalize(input)), name);
        }

        [Fact]
        public void AstralCharacterIsOneUnknown()
        {
            // Python sees one code point, so one U+FFFD; a UTF-16 port must not see two.
            string emoji = char.ConvertFromUtf32(0x1F600);
            Assert.Equal("a" + TextNormalizer.Unknown + "a", TextNormalizer.Normalize("x" + emoji + "y"));
        }

        [Fact]
        public void OffsetsPointBackToTheRawText()
        {
            const char RightToLeftMark = (char)0x200F;
            string raw = "וַיְדַבֵּ֥ר  אֱלֹהֶ֖יךָ" + RightToLeftMark + " ﭏ";
            var n = TextNormalizer.NormalizeWithOffsets(raw);
            Assert.Equal("וידבר אלהיך אל", n.Text);

            int a = n.Text.IndexOf("אלהיך");
            n.ToRaw(a, a + "אלהיך".Length, out int rs, out int re);
            // The span ends after the qamats under the final kaf, not at the kaf.
            Assert.Equal("אֱלֹהֶ֖יךָ", raw.Substring(rs, re - rs));

            // Both letters of the ligature map to the one raw character.
            int l = n.Text.LastIndexOf("אל");
            n.ToRaw(l, l + 2, out rs, out re);
            Assert.Equal("ﭏ", raw.Substring(rs, re - rs));
        }
    }
}
