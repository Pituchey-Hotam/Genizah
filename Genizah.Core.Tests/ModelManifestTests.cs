using System;
using System.IO;
using System.Linq;
using System.Text;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>ModelManifest: model/manifest.json's graph file name.</summary>
    public class ModelManifestTests : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "genizah-manifest-" + Guid.NewGuid().ToString("N") + ".json");

        public void Dispose() => File.Delete(path);

        [Fact]
        public void ReadsTheShippedManifest()
        {
            Assert.EndsWith(".onnx", ModelManifest.Load(TestData.Path("model", "manifest.json")).ModelFile);
        }

        [Fact]
        public void ReadsAManifestWithAByteOrderMark()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"model_file\": \"model.onnx\", \"other\": 1}");
            File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(json).ToArray());
            Assert.Equal("model.onnx", ModelManifest.Load(path).ModelFile);
        }

        [Theory]
        [InlineData("../evil.onnx")]
        [InlineData("..\\\\evil.onnx")]
        [InlineData("dir/model.onnx")]
        [InlineData("C:\\\\model.onnx")]
        [InlineData("..")]
        [InlineData("")]
        public void RejectsAModelFileThatIsNotAPlainName(string file)
        {
            File.WriteAllText(path, "{\"model_file\": \"" + file + "\"}");
            Assert.Throws<InvalidDataException>(() => ModelManifest.Load(path));
        }
    }
}
