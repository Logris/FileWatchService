
using System;
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

            var db_manager = new SqliteCommandManager(databasePath);
            db_listener = new DatabaseTraceListener(db_manager, "LOG");

            Trace.Listeners.Add(new TextWriterTraceListener(dll_dir + "\\Polling.log"));
            Trace.Listeners.Add(db_listener);
            Trace.AutoFlush = true;

            poller_model = new NetworkFolderPollerViewModel(db_manager);
            base_page.DataContext = poller_model;
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
