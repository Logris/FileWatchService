using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private readonly SqliteCommandManager _dbManager;

        private readonly object lock_last_files = new object();
        private Dictionary<string, DateTime> last_known_files = new Dictionary<string, DateTime>();
        private CancellationTokenSource cancellation_token_source;
        private bool disposed = false;

        public event Action<PollingAction, string> OnNewFileDetected;
        public event Action<PollingAction, string> OnFileDeleted;
        public event Action<PollingAction, string> OnFileModified;

        public NetworkFolderPoller(SqliteCommandManager manager)
        {
            _dbManager = manager;
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

        private bool IsFileReady(string filePath)
        {
            try
            {
                // Пытаемся открыть файл с эксклюзивным доступом
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    // Дополнительная проверка что файл не пустой (опционально)
                    return stream.Length > 0;
                }
            }
            catch (IOException)
            {
                return false; // Файл заблокирован или недоступен
            }
            catch (Exception)
            {
                return false; // Другие ошибки (например, нет прав доступа)
            }
        }

        private async Task<bool> IsFileReadyForProcessing(string filePath)
        {
            // 1. Быстрая проверка доступности файла
            if (!IsFileAvailable(filePath))
                return false;

            // 2. Проверка стабильности файла (если быстро не определили)
            return await IsFileStable(filePath, delayMs: 500, maxAttempts: 4);
        }

        private bool IsFileAvailable(string filePath)
        {
            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return stream.Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsFileStable(string filePath, int delayMs, int maxAttempts)
        {
            var initialSize = new FileInfo(filePath).Length;
            var initialWriteTime = File.GetLastWriteTime(filePath);

            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(delayMs);

                try
                {
                    var currentSize = new FileInfo(filePath).Length;
                    var currentWriteTime = File.GetLastWriteTime(filePath);

                    if (currentSize != initialSize || currentWriteTime != initialWriteTime)
                        return false;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        private async void CheckForChanges()
        {
            var currentFiles = new Dictionary<string, DateTime>();
            var newFiles = new List<string>();
            var modifiedFiles = new List<string>();

            try
            {
                // 1. Собираем информацию о текущих файлах
                foreach (var filePath in Directory.GetFiles(NetworkPath, FilterWatch, SearchOption.AllDirectories))
                {
                    try
                    {
                        var lastWriteTime = File.GetLastWriteTime(filePath);
                        currentFiles[filePath] = lastWriteTime;

                        // Определяем новые файлы
                        if (!last_known_files.ContainsKey(filePath))
                        {
                            newFiles.Add(filePath);
                        }
                        // Определяем измененные файлы
                        else if (last_known_files.TryGetValue(filePath, out var oldWriteTime) &&
                                lastWriteTime != oldWriteTime)
                        {
                            modifiedFiles.Add(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        OnFileModified?.Invoke(PollingAction.Error, $"Error accessing {filePath}: {ex.Message}");
                    }
                }

                // 2. Проверяем только НОВЫЕ файлы на готовность
                var readyNewFiles = new List<string>();
                foreach (var filePath in newFiles)
                {
                    if (await IsFileReadyForProcessing(filePath))
                    {
                        readyNewFiles.Add(filePath);

                        if (!last_known_files.ContainsKey(filePath))
                        {
                            OnNewFileDetected?.Invoke(PollingAction.NewFile, filePath);
                        }
                    }
                }

                // 3. Обрабатываем новые готовые файлы
                //                 foreach (var filePath in readyNewFiles)
                //                 {
                //                     if (last_known_files.ContainsKey(filePath)) continue;
                //                     OnNewFileDetected?.Invoke(PollingAction.NewFile, filePath);
                //                 }

                // 4. Обрабатываем измененные файлы (для них не проверяем готовность)
                foreach (var filePath in modifiedFiles)
                {
                    OnFileModified?.Invoke(PollingAction.ModifyFile, filePath);
                }

                // 5. Определяем удаленные файлы
                foreach (var file in last_known_files)
                {
                    if (!currentFiles.ContainsKey(file.Key))
                    {
                        OnFileDeleted?.Invoke(PollingAction.DeleteFile, file.Key);
                    }
                }

                // 6. Обновляем кеш
                last_known_files = currentFiles;
            }
            catch (Exception ex)
            {
                OnFileModified?.Invoke(PollingAction.Error, $"CheckForChanges error: {ex.Message}");
            }
        }

        public async Task SaveSnapshotAsync()
        {
            try
            {
                // Создаем параметры динамически
                var parameters = new List<(string, object)>();
                var values = new List<string>();

                int i = 0;
                foreach (var file in last_known_files)
                {
                    values.Add($"(@path{i}, @time{i}, @lastChange{i})");

                    parameters.Add(($"@path{i}", file.Key));
                    parameters.Add(($"@time{i}", file.Value.ToString("yyyy-MM-dd HH:mm:ss")));
                    parameters.Add(($"@lastChange{i}", file.Value.Ticks));

                    i++;
                }

                await _dbManager.ExecuteNonQueryAsync(
                    @"DELETE FROM Snapshot;
                      INSERT INTO Snapshot (Path, Time, LastChange)
                      VALUES " + string.Join(",", values) + "; COMMIT; ", parameters.ToArray());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to save snapshot: {ex.Message}", "ERROR");
            }
        }

        public void SaveSnapshot()
        {
            try
            {
                // Создаем параметры динамически
                var parameters = new List<(string, object)>();
                var values = new List<string>();

                int i = 0;
                foreach (var file in last_known_files)
                {
                    values.Add($"(@path{i}, @time{i}, @lastChange{i})");

                    parameters.Add(($"@path{i}", file.Key));
                    parameters.Add(($"@time{i}", file.Value.ToString("yyyy-MM-dd HH:mm:ss")));
                    parameters.Add(($"@lastChange{i}", file.Value.Ticks));

                    i++;
                }

                _dbManager.ExecuteNonQuery(
                    @"DELETE FROM Snapshot;
                      INSERT INTO Snapshot (Path, Time, LastChange)
                      VALUES " + string.Join(",", values) + ";", parameters.ToArray());
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to save snapshot: {ex.Message}", "ERROR");
            }
        }


        public async Task LoadSnapshotAsync()
        {
            try
            {
                var snapshot = new Dictionary<string, DateTime>();

                using (var reader = await _dbManager.ExecuteReaderAsync("SELECT Path, LastChange FROM Snapshot"))
                {
                    while (reader.Read())
                    {
                        string path = reader.GetString(0);
                        long ticks = reader.GetInt64(1);
                        snapshot[path] = new DateTime(ticks, DateTimeKind.Local);
                    }
                }

                lock (lock_last_files)
                {
                    last_known_files = snapshot;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to load snapshot: {ex.Message}", "ERROR");
            }
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
                    cancellation_token_source?.Cancel();
                    //SaveSnapshotAsync().Wait(); // Сохраняем snapshot при завершении
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