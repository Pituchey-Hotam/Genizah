using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>
    /// Reading and verifying manifests: a pair from a real Release build (testdata/manifests/),
    /// and manifests signed here with a throwaway key.
    /// </summary>
    public class ManifestTests : IDisposable
    {
        /// <summary>The public key that verifies the signed fixtures in testdata/manifests/.</summary>
        private const string BuildKey = "<RSAKeyValue><Modulus>pP2JOUHp4wFx4GRaTvaJa2OyuE8NpPVIkEkuPKyPmRM1MfpOY0uoN0Tbp0zFjX1Qftw3mQyGWlRyHIEbpsPfpwiC/2s6Erkh5Tt+4ot1SGl0bvaxeOJfBNnPGZRccVZZUlfmqyCmXArOymVhMDJJqnfCM4Wtk8P/FSrdlonHr3U=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private static readonly byte[] BuildVsto = File.ReadAllBytes(TestData.TestDataPath(Path.Combine("manifests", "Genizah.vsto")));
        private static readonly byte[] BuildManifest = File.ReadAllBytes(TestData.TestDataPath(Path.Combine("manifests", "Genizah.dll.manifest")));

        private readonly TestManifests sign = new TestManifests();

        public void Dispose() => sign.Dispose();

        [Fact]
        public void ReadsABuildsManifests()
        {
            DeploymentManifest vsto = DeploymentManifest.Read(BuildVsto, BuildKey);
            Assert.Equal(3, vsto.Version.ToString().Split('.').Length);
            Assert.Equal("Genizah.dll.manifest", vsto.ApplicationManifest.Path);
            Assert.Equal(BuildManifest.Length, vsto.ApplicationManifest.Size);

            IReadOnlyList<ManifestFile> files = ApplicationManifest.Read(BuildManifest, vsto, BuildKey);
            string[] paths = files.Select(f => f.Path).ToArray();
            Assert.Contains("Genizah.dll", paths);
            Assert.Contains("onnxruntime.dll", paths);
            Assert.Contains(@"model\vocab.txt", paths);
            Assert.Contains(paths, p => p.StartsWith(@"model\") && p.EndsWith(".onnx"));
            Assert.Equal("model.vocab.txt", files.Single(f => f.Path == @"model\vocab.txt").AssetName);
            Assert.All(files, f => Assert.Equal(32, f.Sha256.Length));
        }

        [Fact]
        public void ABuildsManifestChangedAfterSigningIsRefused()
        {
            byte[] vsto = Replace(BuildVsto, "publisher=\"Genizah\"", "publisher=\"Genizah!\"");
            Assert.Throws<InvalidDataException>(() => DeploymentManifest.Read(vsto, BuildKey));
        }

        [Fact]
        public void AnotherKeyIsRefused()
        {
            Assert.Throws<InvalidDataException>(() => DeploymentManifest.Read(BuildVsto, sign.PublicKey));
        }

        [Fact]
        public void AManifestResignedByAnotherKeyIsRefused()
        {
            byte[] impostor = sign.Resign(BuildVsto);
            Assert.Throws<InvalidDataException>(() => DeploymentManifest.Read(impostor, BuildKey));
            // The same manifest passes with its signer's key: what's refused is only the key.
            DeploymentManifest.Read(impostor, sign.PublicKey);
        }

        [Fact]
        public void AnApplicationManifestOtherThanThePinnedOneIsRefused()
        {
            DeploymentManifest vsto = DeploymentManifest.Read(BuildVsto, BuildKey);
            byte[] other = Replace(BuildManifest, "<description", " <description");
            Assert.Throws<InvalidDataException>(() => ApplicationManifest.Read(other, vsto, BuildKey));
        }

        [Fact]
        public void ReadsWhatItSigned()
        {
            var files = Files((@"model\vocab.txt", "abc"), ("Genizah.dll", "dll"), ("onnxruntime.dll.config", ""));
            byte[] app = sign.Application(files);
            DeploymentManifest vsto = DeploymentManifest.Read(sign.Deployment("1.2.3.0", app), sign.PublicKey);
            Assert.Equal(new Version(1, 2, 3), vsto.Version);

            var read = ApplicationManifest.Read(app, vsto, sign.PublicKey);
            Assert.Equal(files.Select(f => f.Key).OrderBy(p => p), read.Select(f => f.Path).OrderBy(p => p));
            var vocab = read.Single(f => f.Path == @"model\vocab.txt");
            Assert.Equal(3, vocab.Size);
            Assert.Equal(TestManifests.Sha256(Encoding.UTF8.GetBytes("abc")), vocab.Sha256);
        }

        [Fact]
        public void ASignatureOverPartOfTheManifestIsRefused()
        {
            // Signed with the expected key, but only the identity: the version and pin are free to change.
            XmlDocument doc = TestManifests.Document("Genizah.vsto", "9.9.9.0",
                "<dependency><dependentAssembly codebase=\"Genizah.dll.manifest\" size=\"1\">" + TestManifests.Hash(new byte[1]) + "</dependentAssembly></dependency>");
            var identity = (XmlElement)doc.GetElementsByTagName("assemblyIdentity", TestManifests.Asmv1)[0];
            identity.SetAttribute("Id", "part");
            byte[] partial = sign.Sign(doc, "#part");
            var e = Assert.Throws<InvalidDataException>(() => DeploymentManifest.Read(partial, sign.PublicKey));
            Assert.Contains("whole document", e.Message);
        }

        [Fact]
        public void ASecondSignatureIsRefused()
        {
            XmlDocument doc = TestManifests.Load(sign.Deployment("1.0.0.0", new byte[1]));
            XmlDocument other = TestManifests.Load(sign.Deployment("2.0.0.0", new byte[1]));
            XmlNode extra = other.DocumentElement.LastChild;
            doc.DocumentElement.AppendChild(doc.ImportNode(extra, true));
            Assert.Throws<InvalidDataException>(() => DeploymentManifest.Read(TestManifests.Bytes(doc), sign.PublicKey));
        }

        [Fact]
        public void AnUnsignedManifestIsRefused()
        {
            byte[] unsigned = TestManifests.Bytes(TestManifests.Document("Genizah.vsto", "1.0.0.0", ""));
            Assert.Throws<InvalidDataException>(() => DeploymentManifest.Read(unsigned, sign.PublicKey));
        }

        [Fact]
        public void NotXmlIsRefused()
        {
            Assert.Throws<InvalidDataException>(() => DeploymentManifest.Read(Encoding.UTF8.GetBytes("<html>not found"), sign.PublicKey));
        }

        [Fact]
        public void AVersionWithANonZeroFourthPartIsRefused()
        {
            byte[] vsto = sign.Deployment("1.2.3.4", new byte[1]);
            Assert.Throws<InvalidDataException>(() => DeploymentManifest.Read(vsto, sign.PublicKey));
        }

        [Theory]
        [InlineData(@"..\Genizah.dll")]
        [InlineData(@"model\..\..\x.txt")]
        [InlineData(@"C:\Windows\x.dll")]
        [InlineData(@"\x.txt")]
        [InlineData(@"model\\x.txt")]
        [InlineData(@"model/x.txt")]
        [InlineData(@"a b.txt")]
        [InlineData(@".hidden")]
        [InlineData(@"x.")]
        [InlineData(@"שם.txt")]
        public void APathThatIsntPlainIsRefused(string path)
        {
            byte[] app = sign.Application(Files((path, "x")));
            DeploymentManifest vsto = DeploymentManifest.Read(sign.Deployment("1.0.0.0", app), sign.PublicKey);
            Assert.Throws<InvalidDataException>(() => ApplicationManifest.Read(app, vsto, sign.PublicKey));
        }

        [Theory]
        [InlineData(@"model\vocab.txt", "model.vocab.txt")]
        [InlineData(@"model\vocab.txt", @"MODEL\VOCAB.TXT")]
        [InlineData(@"Genizah.vsto", null)]
        [InlineData(@"Genizah.dll.manifest", null)]
        public void FilesThatShareAnAssetNameAreRefused(string first, string second)
        {
            var files = second == null ? Files((first, "1")) : Files((first, "1"), (second, "2"));
            byte[] app = sign.Application(files);
            DeploymentManifest vsto = DeploymentManifest.Read(sign.Deployment("1.0.0.0", app), sign.PublicKey);
            Assert.Throws<InvalidDataException>(() => ApplicationManifest.Read(app, vsto, sign.PublicKey));
        }

        [Fact]
        public void AFileHashedWithSomethingOtherThanSha256IsRefused()
        {
            XmlDocument doc = TestManifests.Document("Genizah.dll", "1.0.0.0",
                "<file name=\"x.txt\" size=\"1\">" + TestManifests.Hash(new byte[1], "http://www.w3.org/2000/09/xmldsig#sha1") + "</file>");
            byte[] app = sign.Sign(doc);
            DeploymentManifest vsto = DeploymentManifest.Read(sign.Deployment("1.0.0.0", app), sign.PublicKey);
            Assert.Throws<InvalidDataException>(() => ApplicationManifest.Read(app, vsto, sign.PublicKey));
        }

        private static List<KeyValuePair<string, byte[]>> Files(params (string Path, string Text)[] files) =>
            files.Select(f => new KeyValuePair<string, byte[]>(f.Path, Encoding.UTF8.GetBytes(f.Text))).ToList();

        private static byte[] Replace(byte[] manifest, string from, string to)
        {
            string text = Encoding.UTF8.GetString(manifest);
            Assert.Contains(from, text);
            return Encoding.UTF8.GetBytes(text.Replace(from, to));
        }
    }
}
