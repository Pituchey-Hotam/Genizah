using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Genizah.Results;

namespace Genizah
{
    public partial class ResultsControl : UserControl
    {
        public ResultsControl()
        {
            InitializeComponent();
            this.flowLayoutPanel1.Dock = DockStyle.Fill;
            this.flowLayoutPanel1.AutoScroll = true;
        }

        public void UpdateSearchResults(List<SearchResult> results)
        {
            foreach (var result in results.OrderBy(x => x.rangeStart))
            {
                ResultControl resultControl = new ResultControl(result);
                resultControl.RemoveResultControlHandler += (sender, e) =>
                {
                    flowLayoutPanel1.Controls.Remove(resultControl);
                };
                flowLayoutPanel1.Controls.Add(resultControl);
            }
        }
    }
}
