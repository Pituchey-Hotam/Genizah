using System;
using System.IO;
using Genizah.Core;
using Xunit;

namespace Genizah.Core.Tests
{
    /// <summary>UpdateLog: round trips, which line counts as the last check, and logs it can't read.</summary>
    public class UpdateLogTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "genizah-updatelog-" + Guid.NewGuid().ToString("N"));
        private string Log => Path.Combine(dir, "update.log");
        private static readonly DateTime Noon = new DateTime(2026, 10, 21, 12, 0, 0);

        public void Dispose()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        private void Append(UpdateOutcome outcome, int minutes, Version latest = null, string detail = "") =>
            UpdateLog.Append(Log, new UpdateCheck(Noon.AddMinutes(minutes), new Version(0, 1, 2), outcome, latest, detail), 1 << 20);

        [Fact]
        public void AMissingLogHasNoCheck()
        {
            Assert.Null(UpdateLog.LastCheck(Log));
        }

        [Fact]
        public void ACheckReadsBack()
        {
            Append(UpdateOutcome.Installed, 0, new Version(0, 1, 3), "copied 14 files, downloaded Genizah.dll");
            UpdateCheck last = UpdateLog.LastCheck(Log);
            Assert.Equal(Noon, last.Time);
            Assert.Equal(new Version(0, 1, 2), last.Running);
            Assert.Equal(UpdateOutcome.Installed, last.Outcome);
            Assert.Equal(new Version(0, 1, 3), last.Latest);
            Assert.Equal("copied 14 files, downloaded Genizah.dll", last.Detail);
        }

        [Fact]
        public void SessionsThatDidntCheckAreSkipped()
        {
            Append(UpdateOutcome.UpToDate, 0, new Version(0, 1, 2));
            Append(UpdateOutcome.Disabled, 1);
            Append(UpdateOutcome.Busy, 2);
            UpdateCheck last = UpdateLog.LastCheck(Log);
            Assert.Equal(UpdateOutcome.UpToDate, last.Outcome);
            Assert.Equal(Noon, last.Time);
        }

        [Fact]
        public void AMultilineFailureStaysOneLine()
        {
            Append(UpdateOutcome.Failed, 0, detail: "System.Net.Http.HttpRequestException: refused\r\n   at A()\n\tat B()");
            Append(UpdateOutcome.Busy, 1);
            Assert.Equal(2, File.ReadAllLines(Log).Length);
            UpdateCheck last = UpdateLog.LastCheck(Log);
            Assert.Equal(UpdateOutcome.Failed, last.Outcome);
            Assert.Null(last.Latest);
            Assert.Equal("System.Net.Http.HttpRequestException: refused | at A() | at B()", last.Detail);
        }

        [Fact]
        public void BrokenLinesAreSkipped()
        {
            Append(UpdateOutcome.UpToDate, 0, new Version(0, 1, 2));
            File.AppendAllText(Log, "2026-10-21 03:38:36 0.1.1: an older format\nnot\ta\tcheck\t\t\n2026-10-21 12:05:00\t0.1.2\t7\t\t\n");
            Assert.Equal(Noon, UpdateLog.LastCheck(Log).Time);
        }

        [Fact]
        public void APastLimitLogStartsOver()
        {
            Append(UpdateOutcome.Failed, 0, detail: new string('x', 200));
            UpdateLog.Append(Log, new UpdateCheck(Noon.AddMinutes(1), new Version(0, 1, 2), UpdateOutcome.UpToDate, new Version(0, 1, 2), ""), 100);
            Assert.Single(File.ReadAllLines(Log));
            Assert.Equal(UpdateOutcome.UpToDate, UpdateLog.LastCheck(Log).Outcome);
        }
    }
}
