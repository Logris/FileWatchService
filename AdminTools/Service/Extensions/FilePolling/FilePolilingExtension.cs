
using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace FilePolling
{
    public class FilePollingExtension : MiracleAdmin.IServiceExtension
    {
        private DatabaseTraceListener db_listener;

        public string Name { get => "FilePolling"; }

        private void Init()
        {
            string dll_dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string databasePath = dll_dir + "\\database.db";
            string connectionString = $"Data Source={databasePath}";

            db_listener = new DatabaseTraceListener(connectionString, "LOG");

            Trace.Listeners.Add(new TextWriterTraceListener(dll_dir + "\\Polling.log"));
            Trace.Listeners.Add(db_listener);
            Trace.AutoFlush = true;

            poller_model = new NetworkFolderPollerViewModel(connectionString);

            base_page.DataContext = poller_model;

            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();

                // Создание таблицы LOG
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS LOG (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Time TEXT NOT NULL,
                        Category TEXT NOT NULL,
                        Message TEXT NOT NULL
                    );";
                    command.ExecuteNonQuery();
                }

                // Создание таблицы LAST_FILE_LIST
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS LAST_CHANGES (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Path TEXT NOT NULL,
                        Time TEXT NOT NULL,
                        LastChange INTEGER NOT NULL
                    );";
                    command.ExecuteNonQuery();
                }

                // Создание таблицы EXECUTE_RESULT
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Tasks (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Path TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        StatusCode INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        CompletedAt TEXT
                    )";
                    command.ExecuteNonQuery();
                }
            }

            poller_model.Start();
        }

        private void Reset()
        {
            Init();
        }

        private void OnTimedEvent(object source, EventArgs e)
        {
            //
        }

        public UIElement Content
        {
            get => base_page;
        }

        public FilePollingExtension()
        {
            base_page.DataContext = poller_model;
        }

        public void Load(object manager)
        {
            Init();
        }

        public void ProccessUdpMessage(byte[] message)
        {
            //
        }

        public void Stop()
        {
            db_listener.Stop();
            poller_model.Stop();
        }
        public void OnSaveProperties()
        {
            poller_model.SaveConfig();
            poller_model.Stop();
            poller_model.Start();
        }

        private readonly FilePollingControl base_page = new FilePollingControl();
        private NetworkFolderPollerViewModel poller_model;
    }
}
