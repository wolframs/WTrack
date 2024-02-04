using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace WTrack.IO
{
    public class OutputDataItem
    {
        public string CombinedData { get; set; }
        public double Duration { get; set; }

        public long Id { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public string Program { get; set; }
        public string Title { get; set; }
        public BitmapImage Icon { get; set; }

        /// <summary>
        /// Used for deriving a row height, that scales according to an item's duration.
        /// </summary>
        public double ScaledDuration
        {
            get
            {
                if (Duration == 0 || Duration <= DayOutputWindow.DurationCutOff)
                {
                    return 0;
                }

                return 22 + (Duration - DayOutputWindow.MinRowHeight) * (500 - 22) / (DayOutputWindow.MaxRowHeight - DayOutputWindow.MinRowHeight);
            }
        }
    }
}
