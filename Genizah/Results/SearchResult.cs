using System;
using Genizah.Core;
using Genizah.Scanning;
using Microsoft.Office.Interop.Word;

namespace Genizah.Results
{
    /// <summary>Where a result stands in the document.</summary>
    public enum ResultState
    {
        /// <summary>The model asked, and the user hasn't answered yet.</summary>
        Pending,
        Censored,
        Left,
    }

    /// <summary>
    /// One candidate the classifier scored, and what has been done with it in the document.
    /// </summary>
    public class SearchResult
    {
        public NameInfo Name { get; }
        public float Logit { get; }

        /// <summary>The calibrated probability that this is a divine name, or null if the thresholds file has no temperature.</summary>
        public double? Probability { get; }

        /// <summary>What the model's score said to do. Fixed; <see cref="State"/> is what was done.</summary>
        public Verdict Verdict { get; }
        public ResultState State { get; private set; }

        public string OriginalText { get; }
        public string CensoredText { get; }

        /// <summary>A little of the text around the name, for deciding without looking at the document.</summary>
        public string ContextBefore { get; }
        public string ContextAfter { get; }

        // Live COM ranges follow edits without adding bookmarks to the document or undo history.
        public Range Range { get; }

        public SearchResult(Document doc, Range range, NameInfo name, float logit, double? probability, Verdict verdict, string contextBefore, string contextAfter)
        {
            Range = range.Duplicate;
            Name = name;
            Logit = logit;
            Probability = probability;
            Verdict = verdict;
            State = verdict == Verdict.Leave ? ResultState.Left : ResultState.Pending;
            OriginalText = range.Text;
            CensoredText = NameCensor.Apply(OriginalText, name.BaseForm, name.getSelectedReplacement());
            ContextBefore = contextBefore;
            ContextAfter = contextAfter;
        }

        public void Censor()
        {
            if (State == ResultState.Censored && Range.Text == CensoredText) return;
            Replace(CensoredText);
            State = ResultState.Censored;
        }

        public void Leave()
        {
            if (State == ResultState.Censored || Range.Text == CensoredText)
                Replace(OriginalText);
            State = ResultState.Left;
        }

        private void Replace(string text)
        {
            // Refuse stale results after editing or undo, rather than overwrite unrelated text.
            string current = Range.Text;
            if (current != OriginalText && current != CensoredText)
                throw new System.Runtime.InteropServices.COMException("הטקסט השתנה. יש לסרוק שוב.");
            if (current == text) return;
            int start = Range.Start;
            Range.Text = text;
            Range.SetRange(start, start + text.Length);
        }
    }
}
