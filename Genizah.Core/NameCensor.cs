using System;

namespace Genizah.Core
{
    /// <summary>
    /// Writes a candidate's censored form. A replacement is written for a name's base form
    /// (אלהים becomes אלקים), but a candidate also carries proclitics, suffixes, matres lectionis and
    /// nikud (ואֱלוֹהֵינוּ). So the edit that turns the base form into the replacement is found, and
    /// only that edit is made to the candidate's own letters: ואֱלוֹקינוּ. Not part of the model's
    /// pipeline; it only decides what text goes back into the document.
    /// </summary>
    public static class NameCensor
    {
        /// <summary>The letters <see cref="NameFinder"/> allows before a name.</summary>
        private const string Proclitics = "וכשבלמה";

        /// <summary>Letters a candidate may have that the base form doesn't (אלוהים for אלהים).</summary>
        private const string Matres = "וי";

        /// <param name="raw">The candidate's raw text: <see cref="ScoredCandidate"/>'s span.</param>
        /// <param name="baseForm">The name's letters, unpointed: אלהים.</param>
        /// <param name="replacement">What the base form becomes: אלקים, א-להים, ה'.</param>
        /// <returns>
        /// <paramref name="raw"/> with the edit made. If the candidate's letters don't line up
        /// with the base form as far as the edit reaches (ה' for אלהינו), everything from the
        /// name's first letter on is replaced, and only the proclitics are kept.
        /// </returns>
        public static string Apply(string raw, string baseForm, string replacement)
        {
            Edit(raw, baseForm, replacement, out int start, out int end, out string insert);
            return raw.Substring(0, start) + insert + raw.Substring(end);
        }

        /// <summary>
        /// The edit <see cref="Apply"/> makes: raw[<paramref name="start"/>, <paramref name="end"/>)
        /// becomes <paramref name="insert"/>. Found on one text, it can be made on another whose
        /// characters line up one for one, such as Word's text and its Unicode.
        /// </summary>
        public static void Edit(string raw, string baseForm, string replacement, out int start, out int end, out string insert)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (string.IsNullOrEmpty(baseForm)) throw new ArgumentException("empty base form", nameof(baseForm));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));

            // The edit: baseForm[keep, editEnd) becomes replacement[keep, replacement.Length - tail).
            int keep = 0;
            while (keep < baseForm.Length && keep < replacement.Length && baseForm[keep] == replacement[keep])
                keep++;
            int tail = 0;
            while (tail < baseForm.Length - keep && tail < replacement.Length - keep
                   && baseForm[baseForm.Length - 1 - tail] == replacement[replacement.Length - 1 - tail])
                tail++;
            int editEnd = baseForm.Length - tail;

            var norm = TextNormalizer.NormalizeWithOffsets(raw);
            string text = norm.Text;
            int nameStart = NameStart(text, baseForm);
            if (nameStart < 0)
            {
                start = 0;
                end = raw.Length;
                insert = replacement;
                return;
            }

            int[] at = Align(text, nameStart, baseForm, editEnd);
            if (at == null)
            {
                norm.ToRaw(nameStart, nameStart + 1, out start, out _);
                end = raw.Length;
                insert = replacement;
                return;
            }
            insert = replacement.Substring(keep, replacement.Length - tail - keep);
            if (editEnd > keep)
                norm.ToRaw(at[keep], at[editEnd - 1] + 1, out start, out end);
            else if (keep == 0)
            {
                norm.ToRaw(nameStart, nameStart + 1, out start, out _);
                end = start;
            }
            else
            {
                // A pure insertion goes after the kept letter's nikud (ש-די, not שׁ-ַדי).
                norm.ToRaw(at[keep - 1], at[keep - 1] + 1, out _, out end);
                start = end;
            }
        }

        /// <summary>Where the name's first letter is, past any proclitics; -1 if nowhere.</summary>
        private static int NameStart(string text, string baseForm)
        {
            char first = baseForm[0];
            for (int i = 0; i < text.Length; i++)
            {
                // Shin can begin both a proclitic and Shaday. Validate the whole name
                // before accepting an ambiguous first letter, even for an early edit
                // such as inserting a dash after shin or replacing the first letter.
                if (text[i] == first && (Proclitics.IndexOf(first) < 0
                    || Align(text, i, baseForm, baseForm.Length) != null))
                    return i;
                if (Proclitics.IndexOf(text[i]) < 0)
                    return -1;
            }
            return -1;
        }

        /// <summary>
        /// The position in <paramref name="text"/> of each of baseForm[0, count), matched from
        /// <paramref name="start"/> and skipping extra matres; null if they don't line up.
        /// </summary>
        private static int[] Align(string text, int start, string baseForm, int count)
        {
            var at = new int[count];
            int j = start;
            for (int i = 0; i < count; )
            {
                if (j >= text.Length)
                    return null;
                if (text[j] == baseForm[i])
                    at[i++] = j;
                else if (i == 0 || Matres.IndexOf(text[j]) < 0)
                    return null;
                j++;
            }
            return at;
        }
    }
}
