using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Genizah.Core;

namespace Genizah.Core.Tests
{
    /// <summary>
    /// Writes and signs manifests shaped like the ones a build writes, with a throwaway key, so tests
    /// can make versions, and break them, at will.
    /// </summary>
    internal sealed class TestManifests : IDisposable
    {
        public const string Asmv1 = "urn:schemas-microsoft-com:asm.v1";
        public const string Asmv2 = "urn:schemas-microsoft-com:asm.v2";
        private const string Sha256Uri = "http://www.w3.org/2000/09/xmldsig#sha256";
        private const string RsaSha256Uri = "http://www.w3.org/2000/09/xmldsig#rsa-sha256";

        public readonly RSA Key = RSA.Create(2048);
        public string PublicKey => Key.ToXmlString(false);

        static TestManifests()
        {
            CryptoConfig.AddAlgorithm(typeof(ManifestSignature.RsaSha256SignatureDescription), RsaSha256Uri);
            CryptoConfig.AddAlgorithm(typeof(ManifestSignature.Sha256Digest), Sha256Uri);
        }

        public void Dispose() => Key.Dispose();

        /// <summary>An application manifest listing <paramref name="files"/> (DLLs as assemblies, the rest as files), signed.</summary>
        public byte[] Application(IEnumerable<KeyValuePair<string, byte[]>> files) =>
            Sign(Document("Genizah.dll", "1.0.0.0",
                string.Concat(files.Select(f => f.Key.EndsWith(".dll")
                    ? $"<dependency><dependentAssembly dependencyType=\"install\" codebase=\"{f.Key}\" size=\"{f.Value.Length}\">{Hash(f.Value)}</dependentAssembly></dependency>"
                    : $"<file name=\"{f.Key}\" size=\"{f.Value.Length}\">{Hash(f.Value)}</file>"))));

        /// <summary>A deployment manifest of version <paramref name="version"/>, pinning <paramref name="application"/>, signed.</summary>
        public byte[] Deployment(string version, byte[] application) =>
            Sign(Document("Genizah.vsto", version,
                $"<dependency><dependentAssembly dependencyType=\"install\" codebase=\"Genizah.dll.manifest\" size=\"{application.Length}\">{Hash(application)}</dependentAssembly></dependency>"));

        public static XmlDocument Document(string name, string version, string body)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml($"<asmv1:assembly xmlns:asmv1=\"{Asmv1}\" xmlns=\"{Asmv2}\" xmlns:dsig=\"{SignedXml.XmlDsigNamespaceUrl}\">"
                        + $"<asmv1:assemblyIdentity name=\"{name}\" version=\"{version}\" />{body}</asmv1:assembly>");
            return doc;
        }

        public static string Hash(byte[] bytes, string method = Sha256Uri) =>
            $"<hash><dsig:DigestMethod Algorithm=\"{method}\" /><dsig:DigestValue>{Convert.ToBase64String(Sha256(bytes))}</dsig:DigestValue></hash>";

        public static byte[] Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(bytes);
        }

        /// <summary>
        /// Signs as ClickOnce does: one reference to the whole document, enveloped, exc-c14n. A
        /// <paramref name="referenceUri"/> other than "" signs only the element with that Id.
        /// </summary>
        public byte[] Sign(XmlDocument doc, string referenceUri = "", RSA key = null)
        {
            var signed = new SignedXml(doc) { SigningKey = key ?? Key };
            signed.SignedInfo.SignatureMethod = RsaSha256Uri;
            signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            var reference = new Reference(referenceUri) { DigestMethod = Sha256Uri };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigExcC14NTransform());
            signed.AddReference(reference);
            signed.ComputeSignature();
            XmlElement signature = signed.GetXml();
            signature.SetAttribute("Id", "StrongNameSignature");
            doc.DocumentElement.AppendChild(doc.ImportNode(signature, true));
            return Bytes(doc);
        }

        /// <summary>The manifest with its signature removed and a new one by <paramref name="key"/>.</summary>
        public byte[] Resign(byte[] manifest, RSA key = null)
        {
            XmlDocument doc = Load(manifest);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            foreach (XmlNode s in doc.SelectNodes("/*/ds:Signature", ns).Cast<XmlNode>().ToList())
                s.ParentNode.RemoveChild(s);
            return Sign(doc, "", key);
        }

        public static XmlDocument Load(byte[] bytes)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(new MemoryStream(bytes));
            return doc;
        }

        public static byte[] Bytes(XmlDocument doc)
        {
            var output = new MemoryStream();
            using (var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                doc.Save(writer);
            return output.ToArray();
        }
    }
}
