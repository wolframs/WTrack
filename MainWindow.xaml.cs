#nullable enable

using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Windows.UI.Popups;
using WinRT;
using WTrack.Tracking;

namespace WTrack
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Tracker? tracker = null;
        private StatusState? statusState = null;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = new StatusState(FindName("StatusText") as TextBlock);

            statusState = DataContext.As<StatusState>();

            Loaded += OnMainWindowLoaded;
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            statusState!.IsStartTrackingEnabled = true;
            statusState!.IsEndTrackingEnabled = false;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.Hide();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Get the NotifyIcon from the application resources.
            var notifyIcon = (TaskbarIcon)FindResource("NotifyIcon");

            if (notifyIcon != null)
            {
                notifyIcon.Visibility = Visibility.Visible;
            }

            var sortLogGrid = (DataGrid)FindName("StatusLog");
            if (sortLogGrid != null)
            {
                sortLogGrid.SortByColumnIndex(0, System.ComponentModel.ListSortDirection.Descending);
            }
        }

        private void StartTracking(object sender, RoutedEventArgs e)
        {
            if (tracker == null)
            {
                tracker = new(statusState!);
            }

            // Disable the start button and enable the stop button
            statusState!.IsStartTrackingEnabled = false;
            statusState!.IsEndTrackingEnabled = true;

            tracker!.keepRunning = true;

            Task.Run(async () =>
            {
                await tracker.Log();
            });
        }

        private void EndTracking(object sender, EventArgs e)
        {
            if (tracker != null)
            {
                tracker.keepRunning = false;
            }

            // Disable the stop button and enable the start button
            statusState!.IsStartTrackingEnabled = true;
            statusState!.IsEndTrackingEnabled = false;
        }

        /// <summary>
        /// If our sender is a DataGridCell, we check whether to open the explorer to a certain file location.
        /// </summary>
        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dataGridCell = sender as DataGridCell;
            if (dataGridCell != null)
            {
                try
                {
                    string? message = ((TextBlock)dataGridCell.Content).Text;
                    if (message?.StartsWith("HTML-Ausgabe wurde generiert: ") == true)
                    {
                        string filePath = message.Substring("HTML-Ausgabe wurde generiert: ".Length);
                        if (File.Exists(filePath))
                        {
                            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                        }
                    }
                }
                catch (Exception error) { 
                    DataContext.As<StatusState>().UpdateStatusText(error.Message); 
                }

            }
        }

    }
}
