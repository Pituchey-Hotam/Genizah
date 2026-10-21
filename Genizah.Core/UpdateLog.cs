using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Genizah.Core
{
    public enum UpdateOutcome
    {
        /// <summary>The latest release is the running version, or the one installed for the next start.</summary>
        UpToDate,
        /// <summary>A newer release was installed for the next start.</summary>
        Installed,
        Failed,
        /// <summary>The user turned updates off: nothing was checked.</summary>
        Disabled,
        /// <summary>Another Word process held the lock and checked instead.</summary>
        Busy,
    }

    /// <summary>One update check: when, from which version, and what happened.</summary>
    public sealed class UpdateCheck
    {
        public UpdateCheck(DateTime time, Version running, UpdateOutcome outcome, Version latest, string detail)
        {
            Time = time;
            Running = running;
            Outcome = outcome;
            Latest = latest;
            Detail = detail ?? "";
        }

        public DateTime Time { get; }
        public Version Running { get; }
        public UpdateOutcome Outcome { get; }
        /// <summary>The latest release's version, if the check got that far; the installed one for <see cref="UpdateOutcome.Installed"/>.</summary>
        public Version Latest { get; }
        public string Detail { get; }

        /// <summary>False for a session that didn't check (updates off, or another Word checking).</summary>
        public bool Checked => Outcome == UpdateOutcome.UpToDate || Outcome == UpdateOutcome.Installed || Outcome == UpdateOutcome.Failed;
    }

    /// <summary>
    /// The updater's log, the only trace of a failed update: one line per session, tab-separated
    /// (time, running version, outcome, latest version, detail), the detail kept to one line.
    /// </summary>
    public static class UpdateLog
    {
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";
        private static readonly Regex Breaks = new Regex(@"\s*[\r\n]+\s*");

        /// <summary>Appends <paramref name="check"/>, first deleting the log if it's past <paramref name="maxBytes"/>.</summary>
        public static void Append(string path, UpdateCheck check, long maxBytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var log = new FileInfo(path);
            if (log.Exists && log.Length > maxBytes) log.Delete();
            string line = string.Join("\t",
                check.Time.ToString(TimeFormat, CultureInfo.InvariantCulture),
                check.Running,
                check.Outcome,
                check.Latest?.ToString() ?? "",
                Breaks.Replace(check.Detail, " | ").Replace('\t', ' '));
            File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
        }

        /// <summary>The last session that checked, or null if none did or the log is missing or unreadable.</summary>
        public static UpdateCheck LastCheck(string path)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                UpdateCheck check = Parse(lines[i]);
                if (check != null && check.Checked) return check;
            }
            return null;
        }

        private static UpdateCheck Parse(string line)
        {
            string[] f = line.Split(new[] { '\t' }, 5);
            if (f.Length != 5
                || !DateTime.TryParseExact(f[0], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
                || !Version.TryParse(f[1], out Version running)
                || !Enum.TryParse(f[2], out UpdateOutcome outcome) || !Enum.IsDefined(typeof(UpdateOutcome), outcome))
                return null;
            Version latest = Version.TryParse(f[3], out Version v) ? v : null;
            return new UpdateCheck(time, running, outcome, latest, f[4]);
        }
    }
}
