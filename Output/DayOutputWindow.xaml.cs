#nullable enable

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WTrack.Output
{
    /// <summary>
    /// Interaktionslogik für DayOutputWindow.xaml
    /// </summary>
    public partial class DayOutputWindow : Window
    {
        private StatusState statusState;
        
        public double RowHeight { get; set; } = 22;

        public static double MinRowHeight { get; set; } = double.MaxValue;
        public static double MaxRowHeight { get; set; } = double.MinValue;

        public static double DurationCutOff { get; set; } = 0;

        public DayOutputWindow(StatusState statusState)
        {
            this.statusState = statusState;

            InitializeComponent();

            DataContext = this;

            LoadSQLiteData(0.0);

            dataGrid.PreviewMouseWheel += DataGrid_PreviewMouseWheel;
        }

        private void LoadSQLiteData(double durationCutoffS)
        {
            // Load data from SQLite database
            var connectionString = $"Data Source={statusState.GetDbFilePath()}";
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var command = new SqliteCommand("SELECT w.*, i.icon_data FROM window_log w LEFT JOIN icons i ON w.title = i.title WHERE w.duration >= @cutoff OR (CASE WHEN @cutoff = 0 THEN w.duration IS NULL ELSE 0 END)", connection);
            command.Parameters.AddWithValue("@cutoff", durationCutoffS);
            using var reader = command.ExecuteReader();

            // Create a list to hold the data
            var data = new List<OutputDataItem>();

            while (reader.Read())
            {
                // Combine the data into a single string
                var combinedData = new StringBuilder();
                combinedData.Append(reader["id"]).Append(", ");
                combinedData.Append(reader["date"]).Append(", ");
                combinedData.Append(reader["time"]).Append(", ");
                combinedData.Append(reader["program"]).Append(", ");
                combinedData.Append(reader["title"]).Append(", ");
                combinedData.Append(reader["duration"]);

                double.TryParse(reader["duration"].ToString(), out double duration);
                if (duration > 0)
                {
                    MinRowHeight = Math.Min(MinRowHeight, duration);
                    MaxRowHeight = Math.Max(MaxRowHeight, duration);
                }

                byte[]? iconData = reader["icon_data"] as byte[];
                BitmapImage? icon = null;
                if (iconData != null)
                {
                    using var stream = new MemoryStream(iconData);
                    icon = new BitmapImage();
                    icon.BeginInit();
                    icon.CacheOption = BitmapCacheOption.OnLoad;
                    icon.StreamSource = stream;
                    icon.EndInit();
                }

                data.Add(new OutputDataItem { 
                    CombinedData = combinedData.ToString(),
                    Duration = duration,
                    Id = (long)reader["id"],
                    Date = reader["date"].ToString(),
                    Time = reader["time"].ToString(),
                    Program = reader["program"].ToString(),
                    Title = reader["title"].ToString(),
                    Icon = icon
                });
            }
            // Set the DataGrid's ItemsSource
            dataGrid.ItemsSource = data;
            dataGrid.Items.Refresh();
        }

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                double scaleFactor = e.Delta > 0 ? 1.25 : 0.75;

                // Change min and max row heights
                MinRowHeight = Math.Clamp(MinRowHeight * scaleFactor, 0, 500);
                MaxRowHeight = Math.Clamp(MaxRowHeight * scaleFactor, 0, 500);

                // Optional: change font size
                dataGrid.FontSize = Math.Clamp(dataGrid.FontSize * scaleFactor, 4, 48);

                // Refresh the DataGrid to update the row heights
                dataGrid.Items.Refresh();

                // Mark the event as handled to prevent the DataGrid from scrolling
                e.Handled = true;
            }
        }

        private void DurationCutOffTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^[0-9]*(?:[,.][0-9]*)?$");
            e.Handled = !regex.IsMatch(e.Text);
        }

        private void ApplyCutOffButton_Click(object sender, RoutedEventArgs e)
        {
            string input = durationCutOffTextBox.Text;

            input = input.Replace(',', '.'); // Replace comma with dot as decimal separator

            if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out double cutOffValue) && cutOffValue >= 0.0)
            {
                LoadSQLiteData(cutOffValue);
            }
            else
            {
                MessageBox.Show("Als Duration Cut Off wird eine Dezimalzahl >= 0 erwartet, zum Beispiel 3,5.");
            }
        }
    }
}
