using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;


namespace laborator12
{
    public partial class Form1 : Form
    {
        private SQLiteConnection _conn;
        private FlowLayoutPanel sessionsPanel;

        private TextBox sessionTitleTextBox;
        private DateTimePicker sessionDatePicker;
        private Button addSessionButton;
        private Button exportPdfButton;

        public Form1()
        {
            InitializeComponent();
            InitDatabase();
            InitializeCustomComponents();
            LoadSessions();
        }

        private void InitDatabase()
        {
            if (!File.Exists("notatnik.db"))
            {
                SQLiteConnection.CreateFile("notatnik.db");
            }

            _conn = new SQLiteConnection("Data Source=notatnik.db;Version=3;");
            _conn.Open();

            string createSessionTable = @"CREATE TABLE IF NOT EXISTS Sessions (
                                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        Title TEXT NOT NULL,
                                        Date TEXT NOT NULL
                                      );";

            string createEntryTable = @"CREATE TABLE IF NOT EXISTS Entries (
                                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                        SessionId INTEGER NOT NULL,
                                        Text TEXT,
                                        FOREIGN KEY(SessionId) REFERENCES Sessions(Id)
                                    );";

            string createAttachmentTable = @"CREATE TABLE IF NOT EXISTS Attachments (
                                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                            EntryId INTEGER NOT NULL,
                                            FilePath TEXT NOT NULL,
                                            FOREIGN KEY(EntryId) REFERENCES Entries(Id)
                                        );";

            using var cmd = new SQLiteCommand(createSessionTable, _conn);
            cmd.ExecuteNonQuery();

            cmd.CommandText = createEntryTable;
            cmd.ExecuteNonQuery();

            cmd.CommandText = createAttachmentTable;
            cmd.ExecuteNonQuery();
        }

        private void InitializeCustomComponents()
        {
            this.Text = "Notatnik Analityczny";
            this.Size = new System.Drawing.Size(900, 600);

            sessionTitleTextBox = new TextBox { Width = 200 };
            sessionTitleTextBox.Top = 10;
            sessionTitleTextBox.Left = 10;
            // Usunięto PlaceholderText dla kompatybilności
            this.Controls.Add(sessionTitleTextBox);

            sessionDatePicker = new DateTimePicker();
            sessionDatePicker.Top = 10;
            sessionDatePicker.Left = sessionTitleTextBox.Right + 10;
            this.Controls.Add(sessionDatePicker);

            addSessionButton = new Button { Text = "Dodaj sesję" };
            addSessionButton.Top = 10;
            addSessionButton.Left = sessionDatePicker.Right + 10;
            addSessionButton.Click += AddSessionButton_Click;
            this.Controls.Add(addSessionButton);

            exportPdfButton = new Button { Text = "Eksportuj do PDF" };
            exportPdfButton.Top = 10;
            exportPdfButton.Left = addSessionButton.Right + 10;
            exportPdfButton.Click += ExportPdfButton_Click;
            this.Controls.Add(exportPdfButton);

            sessionsPanel = new FlowLayoutPanel();
            sessionsPanel.Top = addSessionButton.Bottom + 20;
            sessionsPanel.Left = 10;
            sessionsPanel.Width = this.ClientSize.Width - 20;
            sessionsPanel.Height = this.ClientSize.Height - sessionsPanel.Top - 20;
            sessionsPanel.AutoScroll = true;
            sessionsPanel.FlowDirection = FlowDirection.TopDown;
            sessionsPanel.WrapContents = false;
            sessionsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(sessionsPanel);

            this.Resize += (s, e) =>
            {
                sessionsPanel.Width = this.ClientSize.Width - 20;
                sessionsPanel.Height = this.ClientSize.Height - sessionsPanel.Top - 20;
            };
        }

        private void AddSessionButton_Click(object sender, EventArgs e)
        {
            string title = sessionTitleTextBox.Text.Trim();
            DateTime date = sessionDatePicker.Value;

            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show("Podaj tytuł sesji!");
                return;
            }

            string insertSession = "INSERT INTO Sessions (Title, Date) VALUES (@title, @date);";
            using var cmd = new SQLiteCommand(insertSession, _conn);
            cmd.Parameters.AddWithValue("@title", title);
            cmd.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));
            cmd.ExecuteNonQuery();

            sessionTitleTextBox.Text = "";
            LoadSessions();
        }

        private void LoadSessions()
        {
            sessionsPanel.Controls.Clear();

            string querySessions = "SELECT Id, Title, Date FROM Sessions ORDER BY Date DESC;";
            using var cmd = new SQLiteCommand(querySessions, _conn);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                int sessionId = reader.GetInt32(0);
                string title = reader.GetString(1);
                string date = reader.GetString(2);

                var sessionPanel = CreateSessionPanel(sessionId, title, date);
                sessionsPanel.Controls.Add(sessionPanel);
            }
        }

        private Panel CreateSessionPanel(int sessionId, string title, string date)
        {
            var panel = new Panel();
            panel.Width = sessionsPanel.Width - 25;
            panel.Height = 250;
            panel.BorderStyle = BorderStyle.FixedSingle;
            panel.Margin = new Padding(0, 0, 0, 10);

            var label = new Label();
            label.Text = $"Sesja: {title} ({date})";
            label.Font = new Font("Arial", 10, FontStyle.Bold);
            label.Top = 5;
            label.Left = 5;
            label.Width = panel.Width - 10;
            panel.Controls.Add(label);

            var entries = GetEntries(sessionId);

            int topOffset = label.Bottom + 5;
            foreach (var entry in entries)
            {
                var entryPanel = CreateEntryPanel(entry);
                entryPanel.Top = topOffset;
                entryPanel.Left = 5;
                panel.Controls.Add(entryPanel);
                topOffset += entryPanel.Height + 5;
            }

            var addEntryBtn = new Button();
            addEntryBtn.Text = "Dodaj wpis";
            addEntryBtn.Top = topOffset;
            addEntryBtn.Left = 5;
            addEntryBtn.Click += (s, e) => AddEntry(sessionId);
            panel.Controls.Add(addEntryBtn);

            return panel;
        }

        private class Entry
        {
            public int Id;
            public string Text;
            public List<string> Attachments;
        }

        private List<Entry> GetEntries(int sessionId)
        {
            var list = new List<Entry>();
            string query = "SELECT Id, Text FROM Entries WHERE SessionId = @sid;";
            using var cmd = new SQLiteCommand(query, _conn);
            cmd.Parameters.AddWithValue("@sid", sessionId);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var entry = new Entry
                {
                    Id = reader.GetInt32(0),
                    Text = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Attachments = GetAttachments(reader.GetInt32(0))
                };

                list.Add(entry);
            }

            return list;
        }

        private List<string> GetAttachments(int entryId)
        {
            var list = new List<string>();
            string query = "SELECT FilePath FROM Attachments WHERE EntryId = @eid;";
            using var cmd = new SQLiteCommand(query, _conn);
            cmd.Parameters.AddWithValue("@eid", entryId);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }

            return list;
        }

        private Panel CreateEntryPanel(Entry entry)
        {
            var panel = new Panel();
            panel.Width = 700;
            panel.Height = 120;
            panel.BorderStyle = BorderStyle.FixedSingle;

            var textBox = new TextBox();
            textBox.Multiline = true;
            textBox.Width = panel.Width - 20;
            textBox.Height = 60;
            textBox.Top = 5;
            textBox.Left = 5;
            textBox.Text = entry.Text;
            panel.Controls.Add(textBox);

            var saveBtn = new Button();
            saveBtn.Text = "Zapisz wpis";
            saveBtn.Top = textBox.Bottom + 5;
            saveBtn.Left = 5;
            saveBtn.Click += (s, e) =>
            {
                UpdateEntryText(entry.Id, textBox.Text);
                MessageBox.Show("Wpis zapisany");
            };
            panel.Controls.Add(saveBtn);

            var addAttachBtn = new Button();
            addAttachBtn.Text = "Dodaj załącznik";
            addAttachBtn.Top = textBox.Bottom + 5;
            addAttachBtn.Left = saveBtn.Right + 5;
            addAttachBtn.Click += (s, e) =>
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Filter = "Wszystkie pliki|*.*|FASTA|*.fasta|CSV|*.csv|PNG|*.png";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    SaveAttachment(entry.Id, dlg.FileName);
                    MessageBox.Show($"Dodano załącznik: {dlg.FileName}");
                    LoadSessions(); // odśwież panel
                }
            };
            panel.Controls.Add(addAttachBtn);

            var attachmentsLabel = new Label();
            attachmentsLabel.Top = addAttachBtn.Bottom + 5;
            attachmentsLabel.Left = 5;
            attachmentsLabel.Width = panel.Width - 10;
            attachmentsLabel.Text = "Załączniki: " + (entry.Attachments.Count == 0 ? "Brak" : string.Join(", ", entry.Attachments));
            panel.Controls.Add(attachmentsLabel);

            return panel;
        }

        private void AddEntry(int sessionId)
        {
            string insertEntry = "INSERT INTO Entries (SessionId, Text) VALUES (@sid, '');";
            using var cmd = new SQLiteCommand(insertEntry, _conn);
            cmd.Parameters.AddWithValue("@sid", sessionId);
            cmd.ExecuteNonQuery();
            LoadSessions();
        }

        private void UpdateEntryText(int entryId, string text)
        {
            string update = "UPDATE Entries SET Text = @text WHERE Id = @id;";
            using var cmd = new SQLiteCommand(update, _conn);
            cmd.Parameters.AddWithValue("@text", text);
            cmd.Parameters.AddWithValue("@id", entryId);
            cmd.ExecuteNonQuery();
        }

        private void SaveAttachment(int entryId, string filePath)
        {
            string insert = "INSERT INTO Attachments (EntryId, FilePath) VALUES (@eid, @path);";
            using var cmd = new SQLiteCommand(insert, _conn);
            cmd.Parameters.AddWithValue("@eid", entryId);
            cmd.Parameters.AddWithValue("@path", filePath);
            cmd.ExecuteNonQuery();
        }

        private void ExportPdfButton_Click(object sender, EventArgs e)
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "PDF|*.pdf";
            dlg.FileName = "Raport_Sesji.pdf";

            if (dlg.ShowDialog() != DialogResult.OK) return;

            var sessions = new List<(string Title, string Date, List<(string Text, List<string> Attachments)> Entries)>();

            string querySessions = "SELECT Id, Title, Date FROM Sessions ORDER BY Date DESC;";
            using var cmdSessions = new SQLiteCommand(querySessions, _conn);
            using var readerSessions = cmdSessions.ExecuteReader();

            while (readerSessions.Read())
            {
                int sessionId = readerSessions.GetInt32(0);
                string title = readerSessions.GetString(1);
                string date = readerSessions.GetString(2);

                var entries = new List<(string, List<string>)>();
                string queryEntries = "SELECT Id, Text FROM Entries WHERE SessionId = @sid;";
                using var cmdEntries = new SQLiteCommand(queryEntries, _conn);
                cmdEntries.Parameters.AddWithValue("@sid", sessionId);
                using var readerEntries = cmdEntries.ExecuteReader();

                while (readerEntries.Read())
                {
                    int entryId = readerEntries.GetInt32(0);
                    string text = readerEntries.IsDBNull(1) ? "" : readerEntries.GetString(1);

                    var attachments = new List<string>();
                    string queryAttach = "SELECT FilePath FROM Attachments WHERE EntryId = @eid;";
                    using var cmdAttach = new SQLiteCommand(queryAttach, _conn);
                    cmdAttach.Parameters.AddWithValue("@eid", entryId);
                    using var readerAttach = cmdAttach.ExecuteReader();

                    while (readerAttach.Read())
                    {
                        attachments.Add(readerAttach.GetString(0));
                    }

                    entries.Add((text, attachments));
                }

                sessions.Add((title, date, entries));
            }

            var doc = QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.PageColor(QuestPDF.Helpers.Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header()
                        .Text("Raport sesji analitycznych")
                        .SemiBold().FontSize(20).FontColor(Colors.Blue.Medium);

                    page.Content()
                        .Column(col =>
                        {
                            foreach (var session in sessions)
                            {
                                col.Item().Text($"Sesja: {session.Title} ({session.Date})").Bold().FontSize(16).FontColor(Colors.Black);
                                foreach (var entry in session.Entries)
                                {
                                    col.Item().Text(entry.Text);
                                    if (entry.Attachments.Count > 0)
                                        col.Item().Text("Załączniki: " + string.Join(", ", entry.Attachments));
                                }
                                col.Item().PaddingBottom(10).Element(x => x.LineHorizontal(1));
                            }
                        });
                });
            });

            doc.GeneratePdf(dlg.FileName);
            MessageBox.Show("PDF wygenerowany pomyślnie!");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _conn?.Close();
            base.OnFormClosing(e);
        }
    }
}
