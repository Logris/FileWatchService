using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FilePolling
{
    public enum PollingAction { NewFile, ModifyFile, DeleteFile, Error };

    public class NetworkFolderPoller
    {
        public string NetworkPath { get; set; }
        public int PollingInterval { get; set; } = 5;
        public string FilterWatch { get; set; } = "*.*";

        private readonly string connection_string;

        private Dictionary<string, DateTime> last_known_files = new Dictionary<string, DateTime>();
        private CancellationTokenSource cancellation_token_source;
        private bool disposed = false;

        public event Action<PollingAction, string> OnNewFileDetected;
        public event Action<PollingAction, string> OnFileDeleted;
        public event Action<PollingAction, string> OnFileModified;

        public NetworkFolderPoller(string connectionString)
        {
            connection_string = connectionString;
            cancellation_token_source = new CancellationTokenSource();
        }

        public void Start()
        {
            if (string.IsNullOrEmpty(NetworkPath)) return;

            if (cancellation_token_source.IsCancellationRequested)
            {
                cancellation_token_source.Dispose(); // Освобождаем старый
                cancellation_token_source = new CancellationTokenSource(); // Создаем новый
            }

            Task.Run(() => PollFolderAsync(cancellation_token_source.Token), cancellation_token_source.Token);
        }

        public void Stop()
        {
            cancellation_token_source?.Cancel();
        }

        private async Task PollFolderAsync(CancellationToken cancellation_token)
        {
            while (!cancellation_token.IsCancellationRequested)
            {
                try
                {
                    CheckForChanges();
                    await Task.Delay(PollingInterval * 1000, cancellation_token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnFileModified?.Invoke(PollingAction.Error, $"Error: {ex.Message}");
                }
            }
        }
        private void CheckForChanges()
        {
            var current_files = new Dictionary<string, DateTime>();

            foreach (var file_path in Directory.GetFiles(NetworkPath, FilterWatch, SearchOption.AllDirectories))
            {
                var last_write_time = File.GetLastWriteTime(file_path);
                current_files[file_path] = last_write_time;
            }

            foreach (var file in current_files)
            {
                if (!last_known_files.ContainsKey(file.Key))
                {
                    OnNewFileDetected?.Invoke(PollingAction.NewFile, $"{file.Key}");
                }
            }

            foreach (var file in last_known_files)
            {
                if (!current_files.ContainsKey(file.Key))
                {
                    OnFileDeleted?.Invoke(PollingAction.DeleteFile, $"{file.Key}");
                }
            }

            foreach (var file in current_files)
            {
                if (last_known_files.TryGetValue(file.Key, out var last_write_time))
                {
                    if (file.Value != last_write_time)
                    {
                        OnFileModified?.Invoke(PollingAction.ModifyFile, $"{file.Key}");
                    }
                }
            }

            last_known_files = current_files;
        }

        // Реализация IDisposable
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Освобождаем управляемые ресурсы
                    cancellation_token_source?.Dispose();
                }

                disposed = true;
            }
        }

        ~NetworkFolderPoller()
        {
            cancellation_token_source?.Cancel();
            Dispose(false);
        }
    }
}