using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>VersionStore: releases served by a fake GitHub, signed with a throwaway key.</summary>
    public class VersionStoreTests : IDisposable
    {
        private static readonly Uri Releases = new Uri("https://github.com/owner/repo/releases/");
        private static readonly byte[] Graph = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 13)).ToArray();

        private readonly string root = Path.Combine(Path.GetTempPath(), "genizah-versions-" + Guid.NewGuid().ToString("N"));
        private readonly TestManifests sign = new TestManifests();
        private readonly FakeServer server = new FakeServer { Redirect = true };
        private readonly VersionStore store;

        public VersionStoreTests() => store = new VersionStore(Path.Combine(root, "versions"), server.Client, Releases, sign.PublicKey);

        public void Dispose()
        {
            sign.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        [Fact]
        public async Task BuildsTheLatestReleaseFromNothing()
        {
            Publish("1.1.0", Version("code 1.1"), latest: true);
            Release release = await store.LatestAsync();
            Assert.Equal(new Version(1, 1, 0), release.Version);

            BuiltVersion built = await store.BuildAsync(release, runningDir: null);
            Assert.Equal(store.PathFor(release.Version), built.Path);
            Assert.Empty(built.Copied);
            Assert.Equal(3, built.Downloaded.Count);
            AssertFolderIs(built.Path, Version("code 1.1"));
            Assert.Contains("/owner/repo/releases/download/v1.1.0/model.graph.onnx", server.Served);
            Assert.False(Directory.Exists(built.Path + ".partial"));
        }

        [Fact]
        public async Task ACodeOnlyUpdateCopiesTheGraph()
        {
            string running = await Install("1.0.0", Version("code 1.0"));
            Publish("1.1.0", Version("code 1.1"), latest: true);
            server.Requests.Clear();

            BuiltVersion built = await store.BuildAsync(await store.LatestAsync(), running);
            Assert.Equal(new[] { @"model\graph.onnx", @"model\vocab.txt" }, built.Copied.OrderBy(p => p));
            Assert.Equal(new[] { "Genizah.dll" }, built.Downloaded);
            Assert.DoesNotContain(server.Served, p => p.EndsWith("graph.onnx"));
            AssertFolderIs(built.Path, Version("code 1.1"));
        }

        [Fact]
        public async Task ANewGraphIsDownloaded()
        {
            string running = await Install("1.0.0", Version("code 1.0"));
            byte[] newGraph = Graph.Reverse().ToArray();
            Publish("1.1.0", Version("code 1.1", newGraph), latest: true);

            BuiltVersion built = await store.BuildAsync(await store.LatestAsync(), running);
            Assert.Contains(@"model\graph.onnx", built.Downloaded);
            Assert.Equal(newGraph, File.ReadAllBytes(Path.Combine(built.Path, "model", "graph.onnx")));
        }

        [Fact]
        public async Task ATamperedAssetIsRefusedAndNothingIsBuilt()
        {
            Publish("1.1.0", Version("code 1.1"), latest: true);
            server.Files[Asset("1.1.0", "Genizah.dll")] = Encoding.UTF8.GetBytes("evil 1.1");

            Release release = await store.LatestAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => store.BuildAsync(release, null));
            Assert.False(Directory.Exists(store.PathFor(release.Version)));
        }

        [Fact]
        public async Task ARunningFileThatDoesntMatchIsDownloadedInstead()
        {
            string running = await Install("1.0.0", Version("code 1.0"));
            File.WriteAllBytes(Path.Combine(running, "model", "vocab.txt"), Encoding.UTF8.GetBytes("vocAB"));
            Publish("1.1.0", Version("code 1.1"), latest: true);

            BuiltVersion built = await store.BuildAsync(await store.LatestAsync(), running);
            Assert.Contains(@"model\vocab.txt", built.Downloaded);
            AssertFolderIs(built.Path, Version("code 1.1"));
        }

        [Fact]
        public async Task AReleaseSignedWithAnotherKeyIsRefused()
        {
            using (var impostor = new TestManifests())
            {
                byte[] app = impostor.Application(Version("evil"));
                server.Files[Releases + "latest/download/Genizah.vsto"] = impostor.Deployment("9.0.0.0", app);
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LatestAsync());
        }

        [Fact]
        public async Task AnApplicationManifestOtherThanThePinnedOneIsRefused()
        {
            Publish("1.1.0", Version("code 1.1"), latest: true);
            // Validly signed, but not the one the .vsto pins.
            server.Files[Asset("1.1.0", "Genizah.dll.manifest")] = sign.Application(Version("code 1.2"));
            Release release = await store.LatestAsync();
            await Assert.ThrowsAsync<InvalidDataException>(() => store.BuildAsync(release, null));
        }

        [Fact]
        public async Task ABrokenBuildResumesWhereItStopped()
        {
            Publish("1.1.0", Version("code 1.1"), latest: true);
            server.BreakAfter[Asset("1.1.0", "model.graph.onnx")] = 50_000;
            Release release = await store.LatestAsync();
            await Assert.ThrowsAnyAsync<IOException>(() => store.BuildAsync(release, null));
            Assert.False(Directory.Exists(store.PathFor(release.Version)));

            server.Requests.Clear();
            BuiltVersion built = await store.BuildAsync(release, null);
            var graphRequest = server.Requests.Single(r => r.Url.ToString().StartsWith(FakeServer.Cdn) && r.Url.AbsolutePath.EndsWith("graph.onnx"));
            Assert.Equal(50_000, graphRequest.Range?.Ranges.Single().From);
            AssertFolderIs(built.Path, Version("code 1.1"));
        }

        [Fact]
        public async Task AnExistingVersionIsntBuiltAgain()
        {
            Publish("1.1.0", Version("code 1.1"), latest: true);
            Release release = await store.LatestAsync();
            await store.BuildAsync(release, null);
            server.Requests.Clear();

            await store.BuildAsync(release, null);
            Assert.Empty(server.Requests);
        }

        [Fact]
        public async Task StrayFilesInThePartialFolderAreLeftOut()
        {
            Publish("1.1.0", Version("code 1.1"), latest: true);
            Release release = await store.LatestAsync();
            string partial = store.PathFor(release.Version) + ".partial";
            Directory.CreateDirectory(partial);
            File.WriteAllText(Path.Combine(partial, "stray.dll"), "x");

            BuiltVersion built = await store.BuildAsync(release, null);
            AssertFolderIs(built.Path, Version("code 1.1"));
        }

        [Fact]
        public void CleanKeepsOnlyTheNamedFolders()
        {
            foreach (string name in new[] { "1.0.0", "1.1.0", "1.2.0.partial", "1.3.0.partial" })
                Directory.CreateDirectory(Path.Combine(store.Root, name));
            using (store.TryLock())
                store.Clean(new[] { "1.1.0", VersionStore.PartialName(new Version(1, 3, 0)), null });
            Assert.Equal(new[] { "1.1.0", "1.3.0.partial" }, Directory.GetDirectories(store.Root).Select(Path.GetFileName).OrderBy(n => n));
            Assert.True(File.Exists(Path.Combine(store.Root, "update.lock")));
        }

        [Fact]
        public void OnlyOneUpdaterHoldsTheLock()
        {
            using (IDisposable first = store.TryLock())
            {
                Assert.NotNull(first);
                Assert.Null(store.TryLock());
            }
            using (IDisposable again = store.TryLock())
                Assert.NotNull(again);
        }

        /// <summary>A version's files: code that varies, a graph and a vocabulary that may not.</summary>
        private static List<KeyValuePair<string, byte[]>> Version(string code, byte[] graph = null) => new List<KeyValuePair<string, byte[]>>
        {
            new KeyValuePair<string, byte[]>("Genizah.dll", Encoding.UTF8.GetBytes(code)),
            new KeyValuePair<string, byte[]>(@"model\graph.onnx", graph ?? Graph),
            new KeyValuePair<string, byte[]>(@"model\vocab.txt", Encoding.UTF8.GetBytes("vocab")),
        };

        /// <summary>Puts a release on the fake GitHub, laid out as release.yml uploads it.</summary>
        private void Publish(string version, List<KeyValuePair<string, byte[]>> files, bool latest)
        {
            byte[] app = sign.Application(files);
            byte[] vsto = sign.Deployment(version + ".0", app);
            server.Files[Asset(version, "Genizah.vsto")] = vsto;
            server.Files[Asset(version, "Genizah.dll.manifest")] = app;
            foreach (var f in files)
                server.Files[Asset(version, f.Key.Replace('\\', '.'))] = f.Value;
            if (latest) server.Files[Releases + "latest/download/Genizah.vsto"] = vsto;
        }

        /// <summary>A version built into the store, as if installed and running.</summary>
        private async Task<string> Install(string version, List<KeyValuePair<string, byte[]>> files)
        {
            Publish(version, files, latest: true);
            return (await store.BuildAsync(await store.LatestAsync(), null)).Path;
        }

        private static string Asset(string version, string name) => $"{Releases}download/v{version}/{name}";

        private static void AssertFolderIs(string dir, List<KeyValuePair<string, byte[]>> files)
        {
            string[] expected = files.Select(f => f.Key).Concat(new[] { "Genizah.vsto", "Genizah.dll.manifest" }).OrderBy(p => p).ToArray();
            string[] actual = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(dir.Length + 1).Replace(Path.DirectorySeparatorChar, '\\')).OrderBy(p => p).ToArray();
            Assert.Equal(expected, actual);
            foreach (var f in files)
                Assert.Equal(f.Value, File.ReadAllBytes(Path.Combine(dir, f.Key.Replace('\\', Path.DirectorySeparatorChar))));
        }
    }
}
