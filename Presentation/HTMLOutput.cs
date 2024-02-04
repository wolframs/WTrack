using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace WTrack.Presentation
{
    class HTMLOutput
    {
        public HTMLOutput(Action<string> updateStatusText)
        {
            this.UpdateStatusText = updateStatusText;
        }

        private readonly Action<string> UpdateStatusText;

        internal async Task WriteOutput(string dbFilePath, string htmlOutputFilePath)
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

                await GenerateHtmlOutput(dbFilePath, htmlOutputFilePath, durationByProgram, durationByWindowTitle);
            }
        }

        private async Task GenerateHtmlOutput(string dbFilePath, string htmlFilePath, Dictionary<string, double> durationByProgram, Dictionary<string, double> durationByWindowTitle)
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
    }
}
