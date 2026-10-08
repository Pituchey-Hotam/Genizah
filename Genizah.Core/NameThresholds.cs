using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Genizah.Core
{
    /// <summary>
    /// The logit thresholds per name that turn a score into a decision: censor at or above
    /// <c>censor_at</c>, leave below <c>leave_below</c>, ask in between. They are
    /// model/thresholds.json (tools/make_thresholds.py), read off the shipping graph's
    /// out-of-fold logits by the classifier's phase 4, so they belong to that one graph
    /// (<see cref="ModelSha256"/>). A name in <c>censor_always</c> is censored whatever its score: its
    /// thresholds are both negative infinity. A name with no thresholds is always asked about.
    /// </summary>
    public sealed class NameThresholds
    {
        private readonly Dictionary<string, (double CensorAt, double LeaveBelow)> byName;

        private NameThresholds(string modelSha256, Dictionary<string, (double, double)> byName)
        {
            ModelSha256 = modelSha256;
            this.byName = byName;
        }

        /// <summary>The sha256 of the graph these thresholds were read off.</summary>
        public string ModelSha256 { get; }

        /// <summary>The names that have thresholds; every other name is always asked about.</summary>
        public IEnumerable<string> Names => byName.Keys;

        /// <summary>
        /// The name's thresholds, or false if it has none. Doubles, compared against the float
        /// logit as a double: each threshold sits between two logits, and rounding it to a float
        /// could land it on one of them.
        /// </summary>
        public bool TryGet(string nameId, out double censorAt, out double leaveBelow)
        {
            bool found = byName.TryGetValue(nameId, out var t);
            censorAt = t.CensorAt;
            leaveBelow = t.LeaveBelow;
            return found;
        }

        /// <summary>Reads model/thresholds.json. Throws if a name is unknown, repeated, or its pair is out of order.</summary>
        public static NameThresholds Load(string path)
        {
            File file;
            using (var stream = System.IO.File.OpenRead(path))
                file = (File)new DataContractJsonSerializer(typeof(File)).ReadObject(stream);
            var byName = new Dictionary<string, (double, double)>();
            foreach (var e in file.Names ?? new Entry[0])
            {
                if (!NameFinder.NameOrder.Contains(e.Name))
                    throw new InvalidDataException($"{path}: unknown name {e.Name}");
                if (byName.ContainsKey(e.Name))
                    throw new InvalidDataException($"{path}: {e.Name} is listed twice");
                if (double.IsNaN(e.CensorAt) || double.IsNaN(e.LeaveBelow) || e.LeaveBelow > e.CensorAt)
                    throw new InvalidDataException($"{path}: {e.Name} needs leave_below <= censor_at");
                byName[e.Name] = (e.CensorAt, e.LeaveBelow);
            }
            foreach (string name in file.CensorAlways ?? new string[0])
            {
                if (!NameFinder.NameOrder.Contains(name))
                    throw new InvalidDataException($"{path}: unknown name {name}");
                if (byName.ContainsKey(name))
                    throw new InvalidDataException($"{path}: {name} is listed twice");
                byName[name] = (double.NegativeInfinity, double.NegativeInfinity);
            }
            return new NameThresholds(file.ModelSha256, byName);
        }

        [DataContract]
        private sealed class File
        {
            [DataMember(Name = "model_sha256")] public string ModelSha256 { get; set; }
            [DataMember(Name = "names")] public Entry[] Names { get; set; }
            [DataMember(Name = "censor_always")] public string[] CensorAlways { get; set; }
        }

        [DataContract]
        private sealed class Entry
        {
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "censor_at")] public double CensorAt { get; set; }
            [DataMember(Name = "leave_below")] public double LeaveBelow { get; set; }
        }
    }
}
