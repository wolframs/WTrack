#nullable enable

using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Web;
using System.Windows;
using System.Drawing;
using WTrack.Presentation;

namespace WTrack.Tracking
{
    class Tracker
    {
        private readonly StatusState statusState;

        public bool KeepRunning = false;
        public int PollingInterval = 10;

        private Task? loggingTask = null;

        public Tracker(StatusState statusLogger, int pollingInterval)
        {
            this.statusState = statusLogger;
            this.PollingInterval = pollingInterval;
        }

        private string GetAppFolderPathSection() => statusState.GetAppFolderPathSection();

        private string GetDbFilePath() => statusState.GetDbFilePath();

        private string GetHTMLOutputFilePath() => statusState.GetHTMLOutputFilePath();

        public void StartTracking()
        {
            KeepRunning = true;

            statusState!.IsStartTrackingEnabled = false;
            statusState!.IsEndTrackingEnabled = true;
            
            loggingTask = Task.Run(LogLoop);
        }

        public void EndTracking()
        {
            KeepRunning = false;

            statusState!.IsStartTrackingEnabled = true;
            statusState!.IsEndTrackingEnabled = false;
        }


        public async Task LogLoop()
        {
            try
            {
                string dbFilePath = GetDbFilePath();

                string? lastWindowTitle = null;
                DateTime? lastActiveWindowTimestamp = null;

                InitializeDatabase(dbFilePath);

                statusState.SetOutputHtmlFilePath(GetHTMLOutputFilePath());

                UpdateStatusText("Erfassung gestartet...");

                while (KeepRunning)
                {
                    IntPtr hWnd = GetForegroundWindowHandle();
                    string title = GetWindowTitle(hWnd);
                    if (title != lastWindowTitle)
                    {
                        DateTime now = DateTime.Now;
                        string program = GetProgramName(hWnd);
                        byte[] iconData = GetWindowIconAsByteArray(hWnd);

                        // Check if the icon exists in the database and add it if it doesn't
                        AddIconToDatabaseIfNotExists(dbFilePath, title, iconData);

                        // Calculate duration of the previous window's activity, if applicable
                        TimeSpan? duration = null;
                        if (lastActiveWindowTimestamp.HasValue)
                        {
                            duration = now - lastActiveWindowTimestamp.Value;
                        }

                        // Update last active window timestamp
                        lastActiveWindowTimestamp = now;

                        // Log
                        LogWindowActivity(dbFilePath, now, program, title, duration);
                        lastWindowTitle = title;
                    }

                    await Task.Delay(PollingInterval);
                }

                UpdateStatusText("Verlasse Erfassungsschleife...");

                var htmlOutput = new HTMLOutput(UpdateStatusText);
                await htmlOutput.WriteOutput(dbFilePath, GetHTMLOutputFilePath());
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString(), "Unbehandelte Ausnahme", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusText(e.ToString());
                throw;
            }
        }

        private void UpdateStatusText(string statusTextString)
        {
            statusState.UpdateStatusText(statusTextString);
        }

        private void InitializeDatabase(string dbFilePath)
        {
            CreateWindowTrackerFolder();

            using (var connection = new SqliteConnection($"Data Source={dbFilePath}"))
            {
                connection.Open();

                string createWindowLogTableQuery = "CREATE TABLE IF NOT EXISTS window_log (id INTEGER PRIMARY KEY, date TEXT, time TEXT, program TEXT, title TEXT, duration REAL)";
                using (var command = new SqliteCommand(createWindowLogTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }

                string createIconTableQuery = "CREATE TABLE IF NOT EXISTS icons (id INTEGER PRIMARY KEY, title TEXT, icon_data BLOB)";
                using (var command = new SqliteCommand(createIconTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void CreateWindowTrackerFolder()
        {
            if (!Directory.Exists(GetAppFolderPathSection()))
            {
                Directory.CreateDirectory(GetAppFolderPathSection());
            }
        }

        private void LogWindowActivity(string dbFilePath, DateTime timestamp, string program, string title, TimeSpan? duration)
        {
            using (var connection = new SqliteConnection($"Data Source={dbFilePath}"))
            {
                connection.Open();

                // Save the duration in the database
                if (duration.HasValue)
                {
                    using (var cmd = new SqliteCommand("INSERT INTO window_log (date, time, program, title, duration) VALUES (@date, @time, @program, @title, COALESCE(@duration, 0))", connection))
                    {
                        cmd.Parameters.AddWithValue("@date", timestamp.ToShortDateString());
                        cmd.Parameters.AddWithValue("@time", timestamp.ToLongTimeString());
                        cmd.Parameters.AddWithValue("@program", program);
                        cmd.Parameters.AddWithValue("@title", title);
                        cmd.Parameters.AddWithValue("@duration", duration?.TotalSeconds); // Store duration in seconds
                        cmd.ExecuteNonQuery();
                    }
                }
                else
                {
                    // For the first recorded window, no duration is available
                    using (var cmd = new SqliteCommand("INSERT INTO window_log (date, time, program, title) VALUES (@date, @time, @program, @title)", connection))
                    {
                        cmd.Parameters.AddWithValue("@date", timestamp.ToShortDateString());
                        cmd.Parameters.AddWithValue("@time", timestamp.ToLongTimeString());
                        cmd.Parameters.AddWithValue("@program", program);
                        cmd.Parameters.AddWithValue("@title", title);
                        cmd.ExecuteNonQuery();
                    }
                }
            }

            UpdateStatusText($"Line logged: {timestamp.ToShortDateString()}, {timestamp.ToLongTimeString()}, {program}, {title}");
        }

        private IntPtr GetForegroundWindowHandle()
        {
            WinNative.GUITHREADINFO guiInfo = new WinNative.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<WinNative.GUITHREADINFO>() };
            return WinNative.GetGUIThreadInfo(0, ref guiInfo) ? guiInfo.hwndActive : IntPtr.Zero;
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            StringBuilder titleBuilder = new StringBuilder(256);
            int titleLength = WinNative.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString(0, titleLength);

            return title.Contains(',') ? $"\"{title}\"" : title;
        }

        private static string GetProgramName(IntPtr hWnd)
        {
            WinNative.GetWindowThreadProcessId(hWnd, out uint processId);
            Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }

        private static byte[]? GetWindowIconAsByteArray(IntPtr hWnd)
        {
            try
            {
                uint processId;
                WinNative.GetWindowThreadProcessId(hWnd, out processId);
                var process = Process.GetProcessById((int)processId);

                string exePath = process.MainModule.FileName;

                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    using (Icon icon = Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (icon != null)
                        {
                            using MemoryStream ms = new();
                            icon.Save(ms);
                            return ms.ToArray();
                        }
                    }
                }
            }
            catch { }

            try
            {
                IntPtr hIcon = WinNative.SendMessage(hWnd, WinNative.WM_GETICON, WinNative.ICON_SMALL2, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = WinNative.SendMessage(hWnd, WinNative.WM_GETICON, WinNative.ICON_SMALL, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = WinNative.SendMessage(hWnd, WinNative.WM_GETICON, WinNative.ICON_BIG, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = WinNative.GetClassLongPtr(hWnd, WinNative.GCL_HICON);
                if (hIcon == IntPtr.Zero)
                    hIcon = WinNative.GetClassLongPtr(hWnd, WinNative.GCL_HICONSM);

                if (hIcon != IntPtr.Zero)
                {
                    using (Icon icon = Icon.FromHandle(hIcon))
                    {
                        using MemoryStream ms = new();
                        icon.Save(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch { }

            return null;
        }

        private static void AddIconToDatabaseIfNotExists(string dbFilePath, string title, byte[] iconData)
        {
            if (iconData == null)
            {
                return;
            }

            using SqliteConnection connection = new SqliteConnection($"Data Source={dbFilePath};");
            connection.Open();

            // Check if the icon exists in the database
            using (SqliteCommand cmdExists = new SqliteCommand("SELECT COUNT(*) FROM icons WHERE title = @title;", connection))
            {
                cmdExists.Parameters.AddWithValue("@title", title);
                long count = (long)cmdExists.ExecuteScalar();

                if (count == 0)
                {
                    // Insert the icon into the database
                    using (SqliteCommand cmdInsert = new SqliteCommand("INSERT INTO icons (title, icon_data) VALUES (@title, @icon_data);", connection))
                    {
                        cmdInsert.Parameters.AddWithValue("@title", title);
                        cmdInsert.Parameters.AddWithValue("@icon_data", iconData);
                        cmdInsert.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>
        /// Generates sample data for testing, if enabled.
        /// </summary>
        /// <param name="enabled">Fills the window_log table with sample data.</param>
        /// <param name="flushLogTable">Flushes the window_log table. The flush is executed before filling with new data, but can also be used independently.</param>
        /// <returns></returns>
        public async Task PopulateSampleDataAsync(bool enabled, bool flushLogTable)
        {
            if (!enabled && !flushLogTable)
                return;

            Random random = new Random();
            string[] programs = { "Program A", "Program B", "Program C", "Program D", "Program E" };
            string[] titles = { "Title 1", "Title 2", "Title 3", "Title 4", "Title 5" };

            // Prepare the start date and time
            DateTime startDate = DateTime.Today.AddDays(-2);
            DateTime startTime = startDate.AddHours(8);

            var connection = new SqliteConnection($"Data Source={GetDbFilePath()}");

            await connection.OpenAsync();
            using (var transaction = await connection.BeginTransactionAsync())
            {
                if (flushLogTable) {
                    // Clear the existing data in the table
                    using (var deleteCommand = new SqliteCommand("DELETE FROM window_log;", connection))
                    {
                        deleteCommand.Transaction = (SqliteTransaction?)transaction;
                        await deleteCommand.ExecuteNonQueryAsync();
                    }
                }
                
                if (enabled)
                {
                    int entryId = 1;

                    for (int day = 0; day < 3; day++)
                    {
                        double totalDuration = 0;
                        DateTime currentTime = startTime;

                        while (totalDuration < 8 * 60 * 60) // 8 hours in seconds
                        {
                            string program = programs[random.Next(programs.Length)];
                            string title = titles[random.Next(titles.Length)];

                            double power = 7; // Adjust this value to control how much the durations skew towards smaller numbers
                            double randomValue = Math.Pow(random.NextDouble(), power);
                            double duration = randomValue * (1200 - 0.01) + 0.01; // Random duration between 0.01 and 1200 seconds

                            // Add the entry to the table
                            using (var insertCommand = new SqliteCommand("INSERT INTO window_log (id, date, time, program, title, duration) VALUES (@id, @date, @time, @program, @title, @duration);", connection))
                            {
                                insertCommand.Transaction = (SqliteTransaction?)transaction;
                                insertCommand.Parameters.AddWithValue("@id", entryId++);
                                insertCommand.Parameters.AddWithValue("@date", currentTime.ToString("yyyy-MM-dd"));
                                insertCommand.Parameters.AddWithValue("@time", currentTime.ToString("HH:mm:ss"));
                                insertCommand.Parameters.AddWithValue("@program", program);
                                insertCommand.Parameters.AddWithValue("@title", title);
                                insertCommand.Parameters.AddWithValue("@duration", duration);

                                await insertCommand.ExecuteNonQueryAsync();
                            }

                            currentTime = currentTime.AddSeconds(duration);
                            totalDuration += duration;
                        }

                        // Increment the start time by a day for the next iteration
                        startTime = startTime.AddDays(1);
                    }
                }

                await transaction.CommitAsync();
            }

            connection.Close();
        }
    }
}
