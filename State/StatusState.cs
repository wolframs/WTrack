#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

        public void SetOutputHtmlFilePath(string filePath) => this.outputHtmlFilePath = filePath;

        public static ObservableCollection<StatusLogEntry> logEntries = new();

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

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
