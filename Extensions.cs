using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace WTrack
{
    public static class Extensions
    {
        public static void SortByColumnIndex(this DataGrid dataGrid, int columnIndex, ListSortDirection direction)
        {
            var column = dataGrid.Columns[columnIndex];
            if (column != null)
            {
                dataGrid.Items.SortDescriptions.Clear();
                dataGrid.Items.SortDescriptions.Add(new SortDescription(column.SortMemberPath, direction));
                column.SortDirection = direction;
            }
        }
    }
}
