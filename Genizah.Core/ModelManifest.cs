using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Genizah.Core
{
    /// <summary>model/manifest.json: which graph file goes with the vocabulary and thresholds beside it.</summary>
    [DataContract]
    public sealed class ModelManifest
    {
        /// <summary>The graph's file name, in the same folder.</summary>
        [DataMember(Name = "model_file", IsRequired = true)] public string ModelFile { get; private set; }

        /// <summary>Reads model/manifest.json. Throws if the graph isn't named by a plain file name.</summary>
        public static ModelManifest Load(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            // The serializer rejects a UTF-8 byte order mark, which Windows editors like to add.
            int bom = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
            var manifest = (ModelManifest)new DataContractJsonSerializer(typeof(ModelManifest))
                .ReadObject(new MemoryStream(b, bom, b.Length - bom));
            string file = manifest.ModelFile;
            if (string.IsNullOrEmpty(file) || file != Path.GetFileName(file) || file.StartsWith(".")
                || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"model_file must be a plain file name, not '{file}'");
            return manifest;
        }
    }
}
