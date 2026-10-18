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
        public bool IsStale { get; private set; }
        private ResultState uncensoredState;

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
            uncensoredState = State;
            OriginalText = range.Text;
            CensoredText = NameCensor.Apply(OriginalText, name.BaseForm, name.getSelectedReplacement());
            ContextBefore = contextBefore;
            ContextAfter = contextAfter;
        }

        public void Censor()
        {
            EnsureCurrent();
            if (State == ResultState.Censored) return;
            Replace(CensoredText);
            State = ResultState.Censored;
        }

        public void Leave()
        {
            EnsureCurrent();
            if (State == ResultState.Censored)
                Replace(OriginalText);
            State = ResultState.Left;
            uncensoredState = ResultState.Left;
        }

        /// <summary>Reads only this occurrence; never searches for another matching name.</summary>
        public bool Reconcile()
        {
            var previous = State;
            bool wasStale = IsStale;
            try
            {
                string current = Range.Text;
                IsStale = current != OriginalText && current != CensoredText;
                if (!IsStale && OriginalText != CensoredText)
                    State = current == CensoredText ? ResultState.Censored : uncensoredState;
            }
            catch (System.Runtime.InteropServices.COMException) { IsStale = true; }
            return previous != State || wasStale != IsStale;
        }

        private void EnsureCurrent()
        {
            Reconcile();
            if (IsStale)
                throw new System.Runtime.InteropServices.COMException("הטקסט השתנה. יש לסרוק שוב.");
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
