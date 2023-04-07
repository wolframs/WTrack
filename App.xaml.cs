using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WTrack
{
    /// <summary>
    /// Interaktionslogik für "App.xaml"
    /// </summary>
    public partial class App : Application
    {
        public void ShowMainWindow()
        {
            MainWindow mainWindow = (MainWindow)Current.MainWindow;
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
        }

        private void NotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void MenuItem_Open_Click(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            Current.Shutdown();
        }

        public void ToggleTheme(bool isDarkTheme)
        {
            var themeDictionary = Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Contains("IsDarkTheme") && (bool)d["IsDarkTheme"] == isDarkTheme);

            if (themeDictionary != null)
            {
                // Remove previous theme
                Current.Resources.MergedDictionaries.Clear();

                // Add new theme
                Current.Resources.MergedDictionaries.Add(themeDictionary);
            }
        }


    }

}
