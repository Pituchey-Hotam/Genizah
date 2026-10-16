using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace Genizah.Core
{
    /// <summary>
    /// A file a version is made of, as its application manifest (Genizah.dll.manifest) lists it.
    /// </summary>
    public sealed class ManifestFile
    {
        /// <summary>Relative to the version's folder, with '\' between folders: model\vocab.txt.</summary>
        public string Path { get; }
        public long Size { get; }
        /// <summary>The sha256 of the file's bytes.</summary>
        public byte[] Sha256 { get; }

        public ManifestFile(string path, long size, byte[] sha256)
        {
            Path = path;
            Size = size;
            Sha256 = sha256;
        }

        /// <summary>
        /// The file's name as a GitHub release asset, which can't hold folders: the path with '\'
        /// replaced by '.'. tools/dist/package.ps1 names the assets the same way.
        /// </summary>
        public string AssetName => Path.Replace('\\', '.');
    }

    /// <summary>
    /// The deployment manifest (Genizah.vsto): the version, and the application manifest it pins.
    /// </summary>
    public sealed class DeploymentManifest
    {
        /// <summary>MAJOR.MINOR.PATCH: the manifest's four-part version without its last part, which is always 0.</summary>
        public Version Version { get; }
        /// <summary>The application manifest, pinned by size and sha256.</summary>
        public ManifestFile ApplicationManifest { get; }

        private DeploymentManifest(Version version, ManifestFile applicationManifest)
        {
            Version = version;
            ApplicationManifest = applicationManifest;
        }

        /// <summary>
        /// Reads a deployment manifest, after checking that <paramref name="publicKeyXml"/> signed
        /// it. Throws <see cref="InvalidDataException"/> if it isn't signed by that key, or isn't
        /// what a build writes.
        /// </summary>
        public static DeploymentManifest Read(byte[] bytes, string publicKeyXml)
        {
            XmlDocument doc = ManifestSignature.LoadSigned(bytes, publicKeyXml);
            var ns = Manifests.Namespaces(doc);
            var identity = Manifests.Single(doc, "/asmv1:assembly/asmv1:assemblyIdentity", ns);
            if (!Version.TryParse(identity.GetAttribute("version"), out Version four) || four.Revision != 0)
                throw new InvalidDataException($"the deployment manifest's version '{identity.GetAttribute("version")}' isn't MAJOR.MINOR.PATCH.0");
            var pinned = doc.SelectNodes("/asmv1:assembly/asmv2:dependency/asmv2:dependentAssembly[@codebase]", ns)
                .Cast<XmlElement>().ToList();
            if (pinned.Count != 1)
                throw new InvalidDataException($"the deployment manifest pins {pinned.Count} application manifests, not one");
            return new DeploymentManifest(new Version(four.Major, four.Minor, four.Build),
                                          Manifests.ReadFile(pinned[0], pinned[0].GetAttribute("codebase"), ns));
        }
    }

    /// <summary>
    /// The application manifest (Genizah.dll.manifest): every file of a version.
    /// </summary>
    public static class ApplicationManifest
    {
        /// <summary>
        /// Reads the files an application manifest lists, after checking that it is the one
        /// <paramref name="pinnedBy"/> pins and that <paramref name="publicKeyXml"/> signed it.
        /// Throws <see cref="InvalidDataException"/> otherwise, or if a path could leave the
        /// version's folder, or two files share a path or an asset name.
        /// </summary>
        public static IReadOnlyList<ManifestFile> Read(byte[] bytes, DeploymentManifest pinnedBy, string publicKeyXml)
        {
            ManifestFile pin = pinnedBy.ApplicationManifest;
            if (bytes.Length != pin.Size || !Manifests.Sha256(bytes).SequenceEqual(pin.Sha256))
                throw new InvalidDataException("the application manifest isn't the one the deployment manifest pins");
            XmlDocument doc = ManifestSignature.LoadSigned(bytes, publicKeyXml);
            var ns = Manifests.Namespaces(doc);

            var files = new List<ManifestFile>();
            foreach (XmlElement e in doc.SelectNodes("/asmv1:assembly/asmv2:dependency/asmv2:dependentAssembly[@codebase]", ns))
                files.Add(Manifests.ReadFile(e, e.GetAttribute("codebase"), ns));
            foreach (XmlElement e in doc.SelectNodes("/asmv1:assembly/asmv2:file", ns))
                files.Add(Manifests.ReadFile(e, e.GetAttribute("name"), ns));

            // Windows paths and GitHub asset names both ignore case.
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Genizah.vsto", pin.AssetName };
            foreach (ManifestFile f in files)
            {
                if (!paths.Add(f.Path)) throw new InvalidDataException($"the application manifest lists {f.Path} twice");
                if (!assets.Add(f.AssetName)) throw new InvalidDataException($"two files in the application manifest are both asset {f.AssetName}");
            }
            return files;
        }
    }

    internal static class Manifests
    {
        public static XmlNamespaceManager Namespaces(XmlDocument doc)
        {
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("asmv1", "urn:schemas-microsoft-com:asm.v1");
            ns.AddNamespace("asmv2", "urn:schemas-microsoft-com:asm.v2");
            ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            return ns;
        }

        public static XmlElement Single(XmlNode under, string xpath, XmlNamespaceManager ns)
        {
            XmlNodeList found = under.SelectNodes(xpath, ns);
            if (found.Count != 1) throw new InvalidDataException($"the manifest has {found.Count} of {xpath}, not one");
            return (XmlElement)found[0];
        }

        public static ManifestFile ReadFile(XmlElement e, string path, XmlNamespaceManager ns)
        {
            CheckPath(path);
            if (!long.TryParse(e.GetAttribute("size"), out long size) || size < 0)
                throw new InvalidDataException($"{path} has no size");
            XmlElement hash = Single(e, "asmv2:hash", ns);
            string method = Single(hash, "ds:DigestMethod", ns).GetAttribute("Algorithm");
            if (method != ManifestSignature.ClickOnceSha256)
                throw new InvalidDataException($"{path} is hashed with {method}, not sha256");
            byte[] sha256;
            try { sha256 = Convert.FromBase64String(Single(hash, "ds:DigestValue", ns).InnerText); }
            catch (FormatException) { throw new InvalidDataException($"{path}'s sha256 isn't base64"); }
            if (sha256.Length != 32) throw new InvalidDataException($"{path}'s sha256 isn't 32 bytes");
            return new ManifestFile(path, size, sha256);
        }

        /// <summary>
        /// A path stays inside the version's folder, and its asset name is one GitHub keeps as it
        /// is: segments of letters, digits, '.', '-' and '_', none of them empty, "." or "..".
        /// </summary>
        private static void CheckPath(string path)
        {
            string[] segments = path.Split('\\');
            bool ok = path.Length > 0 && segments.All(s =>
                s.Length > 0 && s != "." && s != ".." && !s.EndsWith(".") && !s.StartsWith(".")
                && s.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_'));
            if (!ok) throw new InvalidDataException($"the manifest lists a file at '{path}', which isn't a plain relative path");
        }

        public static byte[] Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(bytes);
        }
    }

    /// <summary>
    /// Checks a ClickOnce manifest's signature: the StrongNameSignature that signing with
    /// ManifestKeyFile puts at the end of the document. (Its nested Authenticode signature is part
    /// of it, and VSTO checks that one; this only needs to know which key signed the manifest.)
    /// </summary>
    public static class ManifestSignature
    {
        /// <summary>ClickOnce's names for RSA with sha256, and for sha256; not the W3C's.</summary>
        internal const string ClickOnceRsaSha256 = "http://www.w3.org/2000/09/xmldsig#rsa-sha256";
        internal const string ClickOnceSha256 = "http://www.w3.org/2000/09/xmldsig#sha256";

        static ManifestSignature()
        {
            CryptoConfig.AddAlgorithm(typeof(RsaSha256SignatureDescription), ClickOnceRsaSha256);
            CryptoConfig.AddAlgorithm(typeof(Sha256Digest), ClickOnceSha256);
        }

        /// <summary>
        /// Parses a manifest and checks that <paramref name="publicKeyXml"/> (an RSAKeyValue) signed
        /// all of it. Throws <see cref="InvalidDataException"/> otherwise.
        /// </summary>
        public static XmlDocument LoadSigned(byte[] bytes, string publicKeyXml)
        {
            var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            try
            {
                using (var reader = XmlReader.Create(new MemoryStream(bytes), settings))
                    doc.Load(reader);
            }
            catch (XmlException e)
            {
                throw new InvalidDataException("the manifest isn't XML: " + e.Message, e);
            }

            var ns = Manifests.Namespaces(doc);
            // The one signature, directly under the root, and nowhere else at that level.
            XmlNodeList signatures = doc.SelectNodes("/*/ds:Signature", ns);
            if (signatures.Count != 1 || ((XmlElement)signatures[0]).GetAttribute("Id") != "StrongNameSignature")
                throw new InvalidDataException("the manifest doesn't have exactly one StrongNameSignature under its root");

            var signed = new SignedXml(doc);
            try
            {
                signed.LoadXml((XmlElement)signatures[0]);
            }
            catch (CryptographicException e)
            {
                throw new InvalidDataException("the manifest's signature is malformed: " + e.Message, e);
            }

            // A signature over part of the document would leave the rest free to change: it must
            // reference the whole document, minus the signature itself, and nothing else.
            if (signed.SignedInfo.References.Count != 1)
                throw new InvalidDataException("the manifest's signature has more than one reference");
            var reference = (Reference)signed.SignedInfo.References[0];
            TransformChain chain = reference.TransformChain;
            string[] transforms = Enumerable.Range(0, chain.Count).Select(i => chain[i].Algorithm).ToArray();
            if (reference.Uri != ""
                || !transforms.SequenceEqual(new[] { SignedXml.XmlDsigEnvelopedSignatureTransformUrl, SignedXml.XmlDsigExcC14NTransformUrl })
                || reference.DigestMethod != ClickOnceSha256
                || signed.SignedInfo.SignatureMethod != ClickOnceRsaSha256)
                throw new InvalidDataException("the manifest's signature doesn't sign the whole document with RSA and sha256");

            using (RSA key = RSA.Create())
            {
                key.FromXmlString(publicKeyXml);
                bool valid;
                try { valid = signed.CheckSignature(key); }
                catch (CryptographicException e) { throw new InvalidDataException("the manifest's signature can't be checked: " + e.Message, e); }
                if (!valid) throw new InvalidDataException("the manifest isn't signed by the expected key, or was changed after signing");
            }
            return doc;
        }

        /// <summary>PKCS#1 v1.5 RSA signatures over sha256, under ClickOnce's algorithm name.</summary>
        public sealed class RsaSha256SignatureDescription : SignatureDescription
        {
            // Assembly-qualified: .NET resolves these by name, and RSA isn't in its core library.
            public RsaSha256SignatureDescription()
            {
                KeyAlgorithm = typeof(RSA).AssemblyQualifiedName;
                DigestAlgorithm = typeof(Sha256Digest).AssemblyQualifiedName;
                FormatterAlgorithm = typeof(RSAPKCS1SignatureFormatter).AssemblyQualifiedName;
                DeformatterAlgorithm = typeof(RSAPKCS1SignatureDeformatter).AssemblyQualifiedName;
            }

            public override HashAlgorithm CreateDigest() => SHA256.Create();

            public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key)
            {
                var deformatter = new RSAPKCS1SignatureDeformatter(key);
                deformatter.SetHashAlgorithm("SHA256");
                return deformatter;
            }

            public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key)
            {
                var formatter = new RSAPKCS1SignatureFormatter(key);
                formatter.SetHashAlgorithm("SHA256");
                return formatter;
            }
        }

        /// <summary>sha256, constructible by name, for reference digests under ClickOnce's algorithm name.</summary>
        public sealed class Sha256Digest : HashAlgorithm
        {
            private readonly SHA256 inner = SHA256.Create();

            public Sha256Digest() => HashSizeValue = 256;
            public override void Initialize() => inner.Initialize();
            protected override void HashCore(byte[] array, int ibStart, int cbSize) => inner.TransformBlock(array, ibStart, cbSize, null, 0);
            protected override byte[] HashFinal()
            {
                inner.TransformFinalBlock(new byte[0], 0, 0);
                byte[] hash = inner.Hash;
                inner.Initialize();
                return hash;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
