#nullable enable

using Hardcodet.Wpf.TaskbarNotification;
using ModernWpf;
using ModernWpf.Controls;
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
using System.Windows.Threading;
using Windows.UI.Popups;
using WinRT;
using WTrack.IO;
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

        /// <summary> Handles the input in the Polling Rate TextBox. See <see cref="PollingIntervalValidationTimer_Tick(object, EventArgs)">IntervalValidationTimer_Tick()</see></summary>.
        private DispatcherTimer intervalInputValidationTimer = new DispatcherTimer();
        private bool isIntervalTextBoxInputValid = true;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = new StatusState(StatusTextBlock);

            statusState = DataContext.As<StatusState>();

            Loaded += OnMainWindowLoaded;

            intervalInputValidationTimer.Interval = TimeSpan.FromMilliseconds(700);
            intervalInputValidationTimer.Tick += PollingIntervalValidationTimer_Tick;
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
                if (int.TryParse(PollingIntervalTextBox.Text, out int pollingInterval))
                {
                    // Instantiate Tracker
                    tracker = new(statusState!, pollingInterval);
                }
                else
                {
                    MessageBox.Show($"\"{PollingIntervalTextBox.Text}\" ist kein gültiger Wert für einen Abfrageintervall in Millisekunden.", "Korrektur erforderlich", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }
            }

            tracker!.StartTracking();
        }

        private void EndTracking(object sender, EventArgs e)
        {
            if (tracker != null)
            {
                tracker.EndTracking();
            }
        }

        /// <summary>
        /// Can use sample data.
        /// </summary>
        private void OpenDayOutputWindow(object sender, EventArgs e)
        {
            if (tracker == null)
                tracker = new(statusState!, 10);

            Task.Run(async () => await tracker.PopulateSampleDataAsync(false, false))
                .ContinueWith(t =>
                {
                    // Check for any exceptions during the task execution
                    if (t.Exception != null)
                    {
                        // Handle the exception (e.g., log the error, show a message box, etc.)
                        MessageBox.Show(t.Exception.Message);
                    }
                    else
                    {
                        // Since we're updating the UI, we need to make sure we're running on the UI thread
                        Dispatcher.Invoke(new Action(() =>
                        {
                            // Execute the rest of the OpenDayOutputWindow function
                            DayOutputWindow outputWindow = new(statusState!);
                            outputWindow.Show();
                        }));
                    }
                });
        }

        private void IntervalTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!int.TryParse(e.Text, out int value))
            {
                e.Handled = true; // discard the input if it's not an integer
                isIntervalTextBoxInputValid = false;
            }
            else
            {
                isIntervalTextBoxInputValid = true;
            }
        }

        private void PollingIntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isIntervalTextBoxInputValid)
                return;

            intervalInputValidationTimer.Stop();
            intervalInputValidationTimer.Start();
        }

        private void PollingIntervalValidationTimer_Tick(object sender, EventArgs e)
        {
            intervalInputValidationTimer.Stop();

            if (int.TryParse(PollingIntervalTextBox.Text, out int value))
            {
                if (value < 10 || value > 2000)
                {
                    new Action(async () =>
                    {
                        ShowPollingIntervalToolTip();
                        await Task.Delay(TimeSpan.FromSeconds(4));
                        HidePollingIntervalToolTip();
                    })();
                }

                value = Math.Clamp(value, 10, 2000);

                PollingIntervalTextBox.Text = value.ToString();
                statusState!.OnPropertyChanged(nameof(tracker.PollingInterval));    // notify ui of changed value

                if (tracker != null)
                    tracker!.PollingInterval = value;
            }
        }



        private void ShowPollingIntervalToolTip()
        {
            PollingIntervalToolTip.IsOpen = true;
        }

        private void HidePollingIntervalToolTip()
        {
            PollingIntervalToolTip.IsOpen = false;
        }

        private void PollingIntervalTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            HidePollingIntervalToolTip();
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
                catch (Exception error)
                {
                    DataContext.As<StatusState>().UpdateStatusText(error.Message);
                }

            }
        }

        private void ThemeSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            ToggleSwitch? themeSwitch = sender as ToggleSwitch;
            if (themeSwitch != null)
            {
                if (themeSwitch.IsOn == true)
                {
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                }
                else
                {
                    ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                }
            }
        }
    }
}
