using System.Linq;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    public class NameFinderTests
    {
        private static string[] Find(string raw) =>
            NameFinder.Find(TextNormalizer.Normalize(raw)).Select(c => c.NameId + " " + c.Text).ToArray();

        [Theory]
        // The spelled-out letter name אל"ף is never the divine name, under any proclitic el takes.
        [InlineData("האות אל\"ף")]
        [InlineData("האל\"ף-בי\"ת")]
        [InlineData("ואל\"ף")]
        [InlineData("מהאל\"ף")]
        [InlineData("וכשבאל\"ף")]
        [InlineData("אל'ף")]
        public void LetterNameAlefIsNotACandidate(string raw)
        {
            Assert.Empty(Find(raw));
        }

        [Fact]
        public void LetterNameAlefIsNotACandidateUnderAnyGershayimSpelling()
        {
            // Hebrew gershayim and geresh, curly and prime quotes: the normalizer folds them all.
            foreach (int q in new[] { 0x05F4, 0x05F3, 0x201C, 0x201D, 0x201E, 0x201F, 0x2033,
                                      0x2018, 0x2019, 0x201A, 0x201B, 0x2032, 0x0060, 0x00B4 })
                Assert.True(Find("לאל" + (char)q + "ף").Length == 0, q.ToString("X4"));
        }

        [Theory]
        // Only gershayim followed by ף: other quotes and words after אל leave it a candidate.
        [InlineData("\"אל\"", "el אל")]
        [InlineData("אל \"ף", "el אל")]
        [InlineData("מתנות שמאל ניתנו", "el שמאל")]
        public void OtherElFormsAreUnchanged(string raw, string expected)
        {
            Assert.Equal(new[] { expected }, Find(raw));
        }

        [Theory]
        [InlineData("")]
        [InlineData("הילד הלך לחנות וקנה לחם וחלב.")]
        [InlineData("האות אל\"ף")]
        [InlineData("הילד הלך אל החנות")]
        [InlineData("p יהוה tail")]
        public void HasCandidateAgreesWithFind(string raw)
        {
            string normalized = TextNormalizer.Normalize(raw);
            Assert.Equal(NameFinder.Find(normalized).Count > 0, NameFinder.HasCandidate(normalized));
        }

        [Theory]
        [MemberData(nameof(PipelineTests.Cases), MemberType = typeof(PipelineTests))]
        public void HasCandidateAgreesWithFindOnEveryVector(string name)
        {
            using (var doc = TestData.Json(TestData.TestDataPath("pipeline.json")))
            {
                string raw = doc.RootElement.GetProperty("cases").EnumerateArray()
                    .Single(c => c.GetProperty("case").GetString() == name).GetProperty("text").GetString();
                string normalized = TextNormalizer.Normalize(raw);
                Assert.Equal(NameFinder.Find(normalized).Count > 0, NameFinder.HasCandidate(normalized));
            }
        }
    }
}
