using System;
using System.Windows;

namespace FileWatcher
{
    public class FileWatcherExtension : MiracleAdmin.IServiceExtension
    {
        public string Name { get => "FileWatcher"; }

        private void Init()
        {
            //
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

        public FileWatcherExtension()
        {
            base_page.DataContext = watcher;
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
            //
        }
        public void OnSaveProperties()
        {
            watcher.SaveConfig();
            watcher.StartWatching();
        }

        private readonly FileWatcherPage base_page = new FileWatcherPage();
        private readonly Watcher watcher = new Watcher();
    }
}
