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

namespace WTrack.Tracking
{
    class Tracker
    {
        public Tracker(StatusState statusLogger, int pollingInterval)
        {
            this._statusLogger = statusLogger;
            this.PollingInterval = pollingInterval;
        }

        private readonly StatusState _statusLogger;

        public bool KeepRunning = false;
        public int PollingInterval = 500;

        public async Task Log()
        {
            try
            {
                string dbFilePath = GetDbFilePath();

                string? lastWindowTitle = null;
                DateTime? lastActiveWindowTimestamp = null;

                InitializeDatabase(dbFilePath);

                _statusLogger.SetOutputHtmlFilePath(GetHTMLOutputFilePath());

                UpdateStatusText("Erfassung gestartet...");

                while (KeepRunning)
                {
                    IntPtr hWnd = GetForegroundWindowHandle();
                    string title = GetWindowTitle(hWnd);
                    if (title != lastWindowTitle)
                    {
                        DateTime now = DateTime.Now;
                        string program = GetProgramName(hWnd);

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
            _statusLogger.UpdateStatusText(statusTextString);
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

        private string GetAppFolderPathSection() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"WindowTracker\");

        private string GetDbFilePath() => Path.Combine(GetAppFolderPathSection(), "WindowLog.db");

        private string GetHTMLOutputFilePath() => Path.Combine(GetAppFolderPathSection(), "WindowLog.html");

        private void InitializeDatabase(string dbFilePath)
        {
            CreateWindowTrackerFolder();

            using (var connection = new SqliteConnection($"Data Source={dbFilePath}"))
            {
                connection.Open();

                string createTableQuery = "CREATE TABLE IF NOT EXISTS window_log (id INTEGER PRIMARY KEY, date TEXT, time TEXT, program TEXT, title TEXT, duration REAL)";
                using (var command = new SqliteCommand(createTableQuery, connection))
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

        private string GetProgramName(IntPtr hWnd)
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
            Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
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
        }
    }
}
