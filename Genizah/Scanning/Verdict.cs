using System.Globalization;

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
    /// The two logit thresholds that turn a score into a <see cref="Verdict"/>: censor at or above
    /// <see cref="CensorAt"/>, leave below <see cref="LeaveBelow"/>, ask in between. They are
    /// application settings (Genizah.dll.config) and belong to the shipping model's fit, so they
    /// are set by hand for each model, never defaulted in code.
    /// </summary>
    public sealed class Thresholds
    {
        public Thresholds(float censorAt, float leaveBelow)
        {
            CensorAt = censorAt;
            LeaveBelow = leaveBelow;
        }

        public float CensorAt { get; }
        public float LeaveBelow { get; }

        public Verdict Decide(float logit)
        {
            if (logit >= CensorAt) return Verdict.Censor;
            if (logit < LeaveBelow) return Verdict.Leave;
            return Verdict.Ask;
        }

        /// <summary>
        /// The thresholds in the add-in's settings, or null if they aren't both set to numbers
        /// with LeaveBelow &lt;= CensorAt. <paramref name="problem"/> then says what's wrong.
        /// </summary>
        public static Thresholds FromSettings(out string problem)
        {
            var settings = Properties.Settings.Default;
            string censor = settings.censor_logit_threshold, leave = settings.leave_logit_threshold;
            problem = null;
            if (string.IsNullOrWhiteSpace(censor) || string.IsNullOrWhiteSpace(leave))
            {
                problem = "ספי ההחלטה של המודל לא הוגדרו, ולכן כל השמות מוצגים לבדיקה.";
                return null;
            }
            if (!float.TryParse(censor, NumberStyles.Float, CultureInfo.InvariantCulture, out float censorAt)
                || !float.TryParse(leave, NumberStyles.Float, CultureInfo.InvariantCulture, out float leaveBelow)
                || leaveBelow > censorAt)
            {
                problem = $"ספי ההחלטה של המודל אינם תקינים (censor {censor}, leave {leave}), ולכן כל השמות מוצגים לבדיקה.";
                return null;
            }
            return new Thresholds(censorAt, leaveBelow);
        }
    }
}
