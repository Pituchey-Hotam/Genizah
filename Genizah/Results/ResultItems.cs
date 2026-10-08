using System.ComponentModel;
using Genizah.Core;
using Genizah.Scanning;

namespace Genizah.Results
{
    /// <summary>
    /// The heading of one verdict's group in the results list. The list is flat: a group's rows
    /// follow its heading while it's expanded, and are taken out while it's collapsed.
    /// </summary>
    public class GroupHeader : INotifyPropertyChanged
    {
        public Verdict Verdict { get; }
        public string Title { get; }
        public int Count { get; }

        private bool isExpanded;
        public bool IsExpanded
        {
            get => isExpanded;
            set
            {
                if (isExpanded == value) return;
                isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public GroupHeader(Verdict verdict, int count, bool isExpanded)
        {
            Verdict = verdict;
            Count = count;
            this.isExpanded = isExpanded;
            switch (verdict)
            {
                case Verdict.Ask: Title = $"לא בטוח ({count})"; break;
                case Verdict.Censor: Title = $"לצנזר ({count})"; break;
                default: Title = $"להשאיר ({count})"; break;
            }
        }
    }

    /// <summary>One result as the list shows it: a one-line snippet, and the details below the list.</summary>
    public class ResultItem : INotifyPropertyChanged
    {
        /// <summary>How many letters of context a row shows on each side of the name; the details show all of it.</summary>
        private const int SnippetWidth = 14;

        public SearchResult Result { get; }

        public ResultItem(SearchResult result)
        {
            Result = result;
        }

        public string Name => Result.OriginalText;
        public string SnippetBefore => Quote.Before(Result.ContextBefore, Result.ContextBefore.Length, SnippetWidth);
        public string SnippetAfter => Quote.After(Result.ContextAfter, 0, SnippetWidth);
        public string ContextBefore => Result.ContextBefore;
        public string ContextAfter => Result.ContextAfter;

        public ResultState State => Result.State;
        public bool CanCensor => Result.State != ResultState.Censored;
        public bool CanLeave => Result.State == ResultState.Pending;
        public bool CanUndo => Result.State == ResultState.Censored;

        public string StatusText
        {
            get
            {
                switch (Result.State)
                {
                    case ResultState.Pending: return "ממתין להכרעה";
                    case ResultState.Censored: return "צונזר";
                    default: return "הושאר";
                }
            }
        }

        public string VerdictText
        {
            get
            {
                string verdict;
                switch (Result.Verdict)
                {
                    case Verdict.Censor: verdict = "לצנזר"; break;
                    case Verdict.Ask: verdict = "לא בטוח"; break;
                    default: verdict = "להשאיר"; break;
                }
                return $"המודל: {verdict} ({Result.Logit:0.00})";
            }
        }

        public string ReplacementText => "יוחלף ב: " + Result.CensoredText;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Tells the list the result's state changed.</summary>
        public void Refresh()
        {
            // An empty name means every property.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
