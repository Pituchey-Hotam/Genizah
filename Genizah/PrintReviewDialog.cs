using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah
{
    internal enum PrintReviewChoice { Print, Review, Cancel }
    internal enum PrintReviewStatus { Current, Pending, Unreviewed, Scanning }

    /// <summary>An explicit choice; opening this dialog never runs a classifier.</summary>
    internal sealed class PrintReviewDialog : Window
    {
        private PrintReviewChoice choice = PrintReviewChoice.Cancel;

        private PrintReviewDialog(PrintReviewStatus status, int pending)
        {
            Title = "גניזה — הדפסה";
            Width = 500;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FlowDirection = FlowDirection.RightToLeft;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Genizah;component/Theme.xaml", UriKind.Relative) });
            Background = (Brush)FindResource("Surface");
            Foreground = (Brush)FindResource("Text");
            var body = new StackPanel { Margin = new Thickness(20) };
            string message = status == PrintReviewStatus.Pending
                ? $"{pending} שמות עדיין ממתינים להכרעה. אפשר להדפיס בכל זאת, או לחזור לסקירה."
                : status == PrintReviewStatus.Scanning
                    ? "סריקת השמות עדיין פועלת. אפשר להדפיס ללא בדיקה עדכנית, או לחזור לסקירה ולהדפיס בסיומה."
                    : "אין סקירה עדכנית למסמך זה. אפשר להדפיס ללא בדיקה, או לסרוק את השמות ולעבור על התוצאות לפני ההדפסה.";
            body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
            var buttons = new WrapPanel();
            AddButton(buttons, status == PrintReviewStatus.Pending ? "הדפס בכל זאת" : "הדפס ללא בדיקה", PrintReviewChoice.Print, true);
            AddButton(buttons, status == PrintReviewStatus.Unreviewed ? "בדוק תחילה" : "עבור לסקירה", PrintReviewChoice.Review);
            AddButton(buttons, "ביטול", PrintReviewChoice.Cancel);
            body.Children.Add(buttons);
            Content = body;
        }

        private void AddButton(Panel panel, string text, PrintReviewChoice result, bool primary = false)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)FindResource(primary ? "PrimaryButton" : "FlatButton"),
                IsDefault = primary,
                IsCancel = result == PrintReviewChoice.Cancel,
                Margin = new Thickness(0, 0, 8, 4)
            };
            button.Click += (sender, args) => { choice = result; Close(); };
            panel.Children.Add(button);
        }

        internal static PrintReviewChoice ShowOver(Word.Window window, PrintReviewStatus status, int pending)
        {
            var dialog = new PrintReviewDialog(status, pending);
            new WindowInteropHelper(dialog).Owner = new IntPtr(window.Hwnd);
            dialog.ShowDialog();
            return dialog.choice;
        }
    }
}
