using System.IO;
using System.Linq;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>model/thresholds.json: it loads, and it belongs to the shipping graph.</summary>
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
        public void ShippedThresholdsAreOrderedHavayaIsAlwaysCensoredAndTheRareNamesHaveNone()
        {
            var t = Shipped();
            Assert.Equal(new[] { "el", "eloha", "elohim", "havaya" }, t.Names.OrderBy(n => n));
            foreach (string name in t.Names)
            {
                Assert.True(t.TryGet(name, out double censorAt, out double leaveBelow));
                Assert.True(leaveBelow <= censorAt, name);
            }
            Assert.True(t.TryGet("havaya", out double havayaCensorAt, out double havayaLeaveBelow));
            Assert.Equal(double.NegativeInfinity, havayaCensorAt);
            Assert.Equal(double.NegativeInfinity, havayaLeaveBelow);
            foreach (string rare in new[] { "ehye", "shaday", "adnut", "tsvaot" })
                Assert.False(t.TryGet(rare, out _, out _), rare);
        }

        [Fact]
        public void ProbabilityIsTheLogitCalibratedByTheShippedTemperature()
        {
            var t = Shipped();
            Assert.True(t.Temperature > 1);
            Assert.Equal(0.5, t.Probability(0f));
            // The calibration's own figure: elohim's censor_at +6.68 is 0.980.
            Assert.Equal(0.980, t.Probability(6.676351f).Value, 3);
            Assert.Equal(1 - t.Probability(3f).Value, t.Probability(-3f).Value, 12);
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
            string alwaysTwice = Write("{\"model_sha256\": \"x\", \"names\": [{\"name\": \"el\", \"censor_at\": 1, \"leave_below\": 0}], " +
                                       "\"censor_always\": [\"el\"]}");
            string alwaysUnknown = Write("{\"model_sha256\": \"x\", \"censor_always\": [\"zeus\"]}");
            string coldTemperature = Write("{\"model_sha256\": \"x\", \"temperature\": 0}");
            string noTemperature = Write("{\"model_sha256\": \"x\"}");
            try
            {
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(unknown));
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(reversed));
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(twice));
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(alwaysTwice));
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(alwaysUnknown));
                Assert.Throws<InvalidDataException>(() => NameThresholds.Load(coldTemperature));
                Assert.Null(NameThresholds.Load(noTemperature).Probability(1f));
            }
            finally
            {
                File.Delete(unknown);
                File.Delete(reversed);
                File.Delete(twice);
                File.Delete(alwaysTwice);
                File.Delete(alwaysUnknown);
                File.Delete(coldTemperature);
                File.Delete(noTemperature);
            }
        }
    }
}
