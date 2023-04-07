using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WTrack
{
    public class StatusLogEntry : IComparable<StatusLogEntry>
    {
        public int Index { get; set; }
        public DateTime TimeStamp { get; set; }
        public string Message { get; set; }

        public int CompareTo(StatusLogEntry other)
        {
            if (other == null) return 1;
            return Index.CompareTo(other.Index);
        }
    }
}
