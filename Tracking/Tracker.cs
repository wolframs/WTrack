#nullable enable

using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Windows;
using System.Drawing;

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
                await WriteOutput(dbFilePath);
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

        private async Task WriteOutput(string dbFilePath)
        {

            using (var conn = new SqliteConnection($"Data Source={dbFilePath};"))
            {
                conn.Open();

                // Query the database for the sum of durations by Program
                var cmdProgram = new SqliteCommand("SELECT program, SUM(duration) FROM window_log GROUP BY program", conn);
                var readerProgram = cmdProgram.ExecuteReader();
                var durationByProgram = new Dictionary<string, double>();

                while (readerProgram.Read())
                {
                    string title = readerProgram.IsDBNull(0) ? "NULL" : readerProgram.GetString(0);
                    double duration = readerProgram.IsDBNull(1) ? 0.0 : readerProgram.GetDouble(1);
                    durationByProgram.Add(title, duration);
                }

                // Query the database for the sum of durations by Window Title
                var cmdTitle = new SqliteCommand("SELECT title, SUM(duration) FROM window_log GROUP BY title", conn);
                var readerTitle = cmdTitle.ExecuteReader();
                var durationByWindowTitle = new Dictionary<string, double>();

                while (readerTitle.Read())
                {
                    string title = readerTitle.IsDBNull(0) ? "NULL" : readerTitle.GetString(0);
                    double duration = readerTitle.IsDBNull(1) ? 0.0 : readerTitle.GetDouble(1);
                    durationByWindowTitle.Add(title, duration);
                }

                await GenerateHtmlOutput(dbFilePath, GetHTMLOutputFilePath(), durationByProgram, durationByWindowTitle);
            }
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
            NativeMethods.GUITHREADINFO guiInfo = new NativeMethods.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
            return NativeMethods.GetGUIThreadInfo(0, ref guiInfo) ? guiInfo.hwndActive : IntPtr.Zero;
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            StringBuilder titleBuilder = new StringBuilder(256);
            int titleLength = NativeMethods.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString(0, titleLength);

            return title.Contains(',') ? $"\"{title}\"" : title;
        }

        private static string GetProgramName(IntPtr hWnd)
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
            Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }

        private static byte[]? GetWindowIconAsByteArray(IntPtr hWnd)
        {
            try
            {
                uint processId;
                NativeMethods.GetWindowThreadProcessId(hWnd, out processId);
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
                IntPtr hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL2, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, NativeMethods.ICON_BIG, IntPtr.Zero);
                if (hIcon == IntPtr.Zero)
                    hIcon = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCL_HICON);
                if (hIcon == IntPtr.Zero)
                    hIcon = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCL_HICONSM);

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


        async Task GenerateHtmlOutput(string dbFilePath, string htmlFilePath, Dictionary<string, double> durationByProgram, Dictionary<string, double> durationByWindowTitle)
        {
            UpdateStatusText("Erstelle HTML-Ausgabe...");

            using (StreamWriter htmlWriter = new(htmlFilePath, false))
            {
                htmlWriter.WriteLine("<!DOCTYPE html>");
                htmlWriter.WriteLine("<html>");
                htmlWriter.WriteLine("<head>");
                htmlWriter.WriteLine("<title>Window Log</title>");

                // Add DataTables CSS
                htmlWriter.WriteLine("<link rel=\"stylesheet\" type=\"text/css\" href=\"https://cdn.datatables.net/v/dt/jq-3.3.1/dt-1.11.5/datatables.min.css\"/>");

                // Add Bootstrap CSS
                htmlWriter.WriteLine("<link rel=\"stylesheet\" href=\"https://maxcdn.bootstrapcdn.com/bootstrap/4.5.2/css/bootstrap.min.css\"/>");
                htmlWriter.WriteLine("<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/bootstrap-dark@1.1.1/bootstrap-dark.min.css\"/>");

                htmlWriter.WriteLine("<style>");
                htmlWriter.WriteLine(".table-dark.table-striped>tbody>tr:nth-of-type(even) { background-color: rgba(255,255,255,.025); }");
                htmlWriter.WriteLine("body { margin: 20px; }");
                htmlWriter.WriteLine("</style>");
                htmlWriter.WriteLine("</head>");
                htmlWriter.WriteLine("<body>");

                using (var connection = new SqliteConnection($"Data Source={dbFilePath}"))
                {
                    connection.Open();

                    string selectQuery = "SELECT date, time, duration, program, title FROM window_log";
                    using (var command = new SqliteCommand(selectQuery, connection))
                    {
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            int row = 0;

                            htmlWriter.WriteLine("<table id=\"windowLogTable\" class=\"table-dark table-striped table-bordered table-hover\">");

                            AddHeaderRow(htmlWriter);

                            htmlWriter.WriteLine("<tbody>");

                            while (reader.Read())
                            {

                                // Data row
                                htmlWriter.Write("<tr>");
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    string? columnValue = reader.GetValue(i).ToString();
                                    string? encodedColumn = HttpUtility.HtmlEncode(columnValue);

                                    if (reader.GetName(i) == "duration")
                                    {
                                        if (Double.TryParse(columnValue, out double duration))
                                        {
                                            TimeSpan durationTimeSpan = TimeSpan.FromSeconds(duration);
                                            string formattedDuration = string.Format("{0}:{1:D2}", (int)durationTimeSpan.TotalMinutes, durationTimeSpan.Seconds);
                                            encodedColumn = HttpUtility.HtmlEncode(formattedDuration);
                                        }
                                    }

                                    htmlWriter.Write("<td>{0}</td>", encodedColumn);
                                }
                                htmlWriter.Write("</tr>");

                                row++;
                            }

                            htmlWriter.WriteLine("</tbody>");
                            htmlWriter.WriteLine("</table>");
                        }
                    }
                }

                // Add summary table container
                htmlWriter.WriteLine("<div id=\"summaryTableContainer\"></div>");

                htmlWriter.WriteLine("</body>");

                // Add DataTables JS and jQuery
                htmlWriter.WriteLine("<script type=\"text/javascript\" src=\"https://code.jquery.com/jquery-3.6.0.min.js\"></script>");
                htmlWriter.WriteLine("<script type=\"text/javascript\" src=\"https://cdn.datatables.net/v/dt/jq-3.3.1/dt-1.11.5/datatables.min.js\"></script>");

                // Data Tables Script
                AddDataTablesScript(htmlWriter);

                // Summary Tables
                CreateSummaryTables(durationByProgram, durationByWindowTitle, htmlWriter);

                // Bootstrap and Popper JS
                htmlWriter.WriteLine("<script src=\"https://cdnjs.cloudflare.com/ajax/libs/popper.js/1.16.0/umd/popper.min.js\"></script>");
                htmlWriter.WriteLine("<script src=\"https://maxcdn.bootstrapcdn.com/bootstrap/4.5.2/js/bootstrap.min.js\"></script>");

                htmlWriter.WriteLine("</html>");

                htmlWriter.Flush();
            }

            UpdateStatusText($"HTML-Ausgabe wurde generiert: {htmlFilePath}");

            await Task.CompletedTask;
        }

        private void CreateSummaryTables(Dictionary<string, double> durationByProgram, Dictionary<string, double> durationByWindowTitle, StreamWriter htmlWriter)
        {
            // Create summary tables
            htmlWriter.WriteLine("<h3>Summary by Program</h3>");
            htmlWriter.WriteLine("<table>");
            htmlWriter.WriteLine("<thead><tr><th>Program</th><th>Duration (s)</th></tr></thead>");
            htmlWriter.WriteLine("<tbody>");

            foreach (var entry in durationByProgram)
            {
                htmlWriter.WriteLine($"<tr><td>{entry.Key}</td><td>{entry.Value:.##}</td></tr>");
            }

            htmlWriter.WriteLine("</tbody>");
            htmlWriter.WriteLine("</table>");

            htmlWriter.WriteLine("<h3>Summary by Window Title</h3>");
            htmlWriter.WriteLine("<table>");
            htmlWriter.WriteLine("<thead><tr><th>Window Title</th><th>Duration (s)</th></tr></thead>");
            htmlWriter.WriteLine("<tbody>");

            foreach (var entry in durationByWindowTitle)
            {
                htmlWriter.WriteLine($"<tr><td>{entry.Key}</td><td>{entry.Value:.##}</td></tr>");
            }

            htmlWriter.WriteLine("</tbody>");
            htmlWriter.WriteLine("</table>");
        }

        private void AddDataTablesScript(StreamWriter htmlWriter)
        {
            // Initialize DataTables
            htmlWriter.WriteLine("<script>");
            htmlWriter.WriteLine("$(document).ready(function () {");
            htmlWriter.WriteLine("    $('#windowLogTable').DataTable();");
            htmlWriter.WriteLine("});");
            htmlWriter.WriteLine("</script>");
        }

        private void AddHeaderRow(StreamWriter htmlWriter)
        {
            // Add header row
            htmlWriter.WriteLine("<thead>");
            htmlWriter.WriteLine("<tr>");
            htmlWriter.WriteLine("<th>Date</th>");
            htmlWriter.WriteLine("<th>Time</th>");
            htmlWriter.WriteLine("<th>Duration (m:s)</th>");
            htmlWriter.WriteLine("<th>Program</th>");
            htmlWriter.WriteLine("<th>Window Title</th>");
            htmlWriter.WriteLine("</tr>");
            htmlWriter.WriteLine("</thead>");
        }

        internal class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpWindowText, int nMaxCount);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

            [StructLayout(LayoutKind.Sequential)]
            public struct GUITHREADINFO
            {
                public uint cbSize;
                public uint flags;
                public IntPtr hwndActive;
                public IntPtr hwndFocus;
                public IntPtr hwndCapture;
                public IntPtr hwndMenuOwner;
                public IntPtr hwndMoveSize;
                public IntPtr hwndCaret;
                public System.Drawing.Rectangle rcCaret;
            }

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, IntPtr lParam);

            [DllImport("user32.dll", EntryPoint = "GetClassLong")]
            public static extern uint GetClassLongPtr32(IntPtr hWnd, int nIndex);

            [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
            public static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

            public const int GCL_HICONSM = -34;
            public const int GCL_HICON = -14;
            public const uint WM_GETICON = 0x7F;
            public const int ICON_SMALL = 0;
            public const int ICON_BIG = 1;
            public const int ICON_SMALL2 = 2;

            public static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
            {
                if (IntPtr.Size == 4)
                    return new IntPtr(GetClassLongPtr32(hWnd, nIndex));
                else
                    return GetClassLongPtr64(hWnd, nIndex);
            }
        }
    }
}
