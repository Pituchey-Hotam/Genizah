using System.IO;
using System.Linq;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>model/thresholds.json (tools/make_thresholds.py): it loads, and it belongs to the shipping graph.</summary>
    public class NameThresholdsTests
    {
        private static NameThresholds Shipped() => NameThresholds.Load(TestData.Path("model", "thresholds.json"));

        [Fact]
        public void ThresholdsBelongToTheModelTheManifestNames()
        {
            using (var manifest = TestData.Json(TestData.Path("model", "manifest.json")))
                Assert.Equal(manifest.RootElement.GetProperty("model_sha256").GetString(), Shipped().ModelSha256);
        }

        [Fact]
        public void ShippedThresholdsAreOrderedAndTheRareNamesHaveNone()
        {
            var t = Shipped();
            Assert.Equal(new[] { "el", "eloha", "elohim" }, t.Names.OrderBy(n => n));
            foreach (string name in t.Names)
            {
                Assert.True(t.TryGet(name, out double censorAt, out double leaveBelow));
                Assert.True(leaveBelow <= censorAt, name);
            }
            foreach (string rare in new[] { "havaya", "ehye", "shaday", "adnut", "tsvaot" })
                Assert.False(t.TryGet(rare, out _, out _), rare);
        }

        [Fact]
        public void BadFilesAreRefused()
        {
            string Write(string json)
            {
                string path = System.IO.Path.GetTempFileName();
                File.WriteAllText(path, json);
                return path;
            }
            string unknown = Write("{\"model_sha256\": \"x\", \"names\": [{\"name\": \"zeus\", \"censor_at\": 1, \"leave_below\": 0}]}");
            string reversed = Write("{\"model_sha256\": \"x\", \"names\": [{\"name\": \"el\", \"censor_at\": 0, \"leave_below\": 1}]}");
            string twice = Write("{\"model_sha256\": \"x\", \"names\": [{\"name\": \"el\", \"censor_at\": 1, \"leave_below\": 0}, " +
                                 "{\"name\": \"el\", \"censor_at\": 1, \"leave_below\": 0}]}");
            try
            {
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(unknown));
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(reversed));
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(twice));
            }
            finally
            {
                File.Delete(unknown);
                File.Delete(reversed);
                File.Delete(twice);
            }
        }
    }
}
