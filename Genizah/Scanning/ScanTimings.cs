using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Genizah.Scanning
{
    /// <summary>
    /// Where one scan's time went, phase by phase. A phase measured several times (once per
    /// candidate, say) accumulates. Read by tools/benchmark.
    /// </summary>
    public sealed class ScanTimings
    {
        private readonly List<string> order = new List<string>();
        private readonly Dictionary<string, TimeSpan> phases = new Dictionary<string, TimeSpan>();
        private readonly Stopwatch total = Stopwatch.StartNew();

        public int TextLength { get; set; }
        public int Candidates { get; set; }
        /// <summary>Candidates whose Word range reads differently from the span that was scored: a mapping bug.</summary>
        public int RangeMismatches { get; set; }
        /// <summary>"ok", or why the scan stopped.</summary>
        public string Outcome { get; set; } = "ok";

        public void Add(string phase, TimeSpan elapsed)
        {
            if (!phases.ContainsKey(phase))
            {
                order.Add(phase);
                phases[phase] = TimeSpan.Zero;
            }
            phases[phase] += elapsed;
        }

        /// <summary>Times the block of a using statement as <paramref name="phase"/>.</summary>
        public IDisposable Measure(string phase) => new Measurement(this, phase);

        /// <summary>One line of JSON: the counts, the outcome, and each phase in milliseconds.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"text_length\":").Append(TextLength)
              .Append(",\"candidates\":").Append(Candidates)
              .Append(",\"range_mismatches\":").Append(RangeMismatches)
              .Append(",\"outcome\":\"").Append(Outcome.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ")).Append('"')
              .Append(",\"total_ms\":").Append(Ms(total.Elapsed))
              .Append(",\"phases_ms\":{");
            sb.Append(string.Join(",", order.Select(p => "\"" + p + "\":" + Ms(phases[p]))));
            return sb.Append("}}").ToString();
        }

        private static string Ms(TimeSpan t) => t.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);

        private sealed class Measurement : IDisposable
        {
            private readonly ScanTimings timings;
            private readonly string phase;
            private readonly Stopwatch watch = Stopwatch.StartNew();

            public Measurement(ScanTimings timings, string phase)
            {
                this.timings = timings;
                this.phase = phase;
            }

            public void Dispose() => timings.Add(phase, watch.Elapsed);
        }
    }
}
