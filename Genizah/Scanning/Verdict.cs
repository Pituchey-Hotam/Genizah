using System;
using System.IO;
using System.Runtime.Serialization;
using Genizah.Core;

namespace Genizah.Scanning
{
    /// <summary>What the classifier's score says to do with a candidate.</summary>
    public enum Verdict
    {
        /// <summary>Censored without asking.</summary>
        Censor,
        /// <summary>The model isn't sure: the user decides.</summary>
        Ask,
        /// <summary>Not a divine name: left as written.</summary>
        Leave,
    }

    /// <summary>
    /// The per-name logit thresholds that turn a score into a <see cref="Verdict"/>: censor at or
    /// above the name's censor_at, leave below its leave_below, ask in between. They ship beside
    /// the model as model/thresholds.json and belong to that graph. A name set to always censor
    /// (censor_always) is censored whatever its score; a name with no thresholds (the rare names)
    /// is always asked about.
    /// </summary>
    public sealed class Thresholds
    {
        private readonly NameThresholds byName;

        private Thresholds(NameThresholds byName)
        {
            this.byName = byName;
        }

        public Verdict Decide(string nameId, float logit)
        {
            if (!byName.TryGet(nameId, out double censorAt, out double leaveBelow)) return Verdict.Ask;
            if (logit >= censorAt) return Verdict.Censor;
            if (logit < leaveBelow) return Verdict.Leave;
            return Verdict.Ask;
        }

        /// <inheritdoc cref="NameThresholds.Probability"/>
        public double? Probability(float logit) => byName.Probability(logit);

        /// <summary>
        /// The thresholds shipped beside the model, or null if they are missing or unreadable;
        /// <paramref name="problem"/> then says what's wrong, and every name is asked about.
        /// </summary>
        public static Thresholds Load(out string problem)
        {
            problem = null;
            string path = Path.Combine(ModelLoader.ModelDir, "thresholds.json");
            try
            {
                return new Thresholds(NameThresholds.Load(path));
            }
            catch (FileNotFoundException)
            {
                problem = "ספי ההחלטה של המודל לא נמצאו, ולכן כל השמות מוצגים לבדיקה.";
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException)
            {
                problem = $"ספי ההחלטה של המודל אינם תקינים ({ex.Message}), ולכן כל השמות מוצגים לבדיקה.";
            }
            return null;
        }
    }
}
