#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WTrack
{
    public class StatusState: INotifyPropertyChanged
    {
        public StatusState(TextBlock statusTextBlock) {
            this._statusText = statusTextBlock;
        }

        private string? outputHtmlFilePath;

        public string GetAppFolderPathSection() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"WindowTracker\");

        public string GetDbFilePath() => Path.Combine(GetAppFolderPathSection(), "WindowLog.db");

        public string GetHTMLOutputFilePath() => Path.Combine(GetAppFolderPathSection(), "WindowLog.html");

        private TextBlock _statusText;

        private int loggingIndexCounter = 1;

        private bool _isStartTrackingEnabled = true;
        
        private bool _isEndTrackingEnabled = false;

        public bool IsStartTrackingEnabled {
            get { return _isStartTrackingEnabled; }
            set
            {
                if (_isStartTrackingEnabled != value)
                {
                    _isStartTrackingEnabled = value;
                    OnPropertyChanged(nameof(IsStartTrackingEnabled));
                }
            }
        }

        public bool IsEndTrackingEnabled {
            get { return _isEndTrackingEnabled; }
            set
            {
                if (_isEndTrackingEnabled != value)
                {
                    _isEndTrackingEnabled = value;
                    OnPropertyChanged(nameof(IsEndTrackingEnabled));
                }
            }
        }

        private int _pollingInterval = 25;

        public int PollingInterval
        {
            get { return _pollingInterval; }
            set
            {
                _pollingInterval = value;
                OnPropertyChanged(nameof(PollingInterval));
            }
        }

        public static ObservableCollection<StatusLogEntry> logEntries = new();

        public void SetOutputHtmlFilePath(string filePath) => this.outputHtmlFilePath = filePath;

        public async void UpdateStatusText(string statusTextString)
        {
            await _statusText.Dispatcher.InvokeAsync(() =>
            {
                _statusText.Text = statusTextString;
            });

            var newEntry = new StatusLogEntry { Index = loggingIndexCounter++, TimeStamp = DateTime.Now, Message = statusTextString };

            Application.Current.Dispatcher.Invoke(() =>
            {
                logEntries.Add(newEntry);
            });

            OnPropertyChanged("logEntries");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
