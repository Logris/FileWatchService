using Command;
using System;
using System.ComponentModel;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;

namespace FilePolling
{
    public class TaskItem
    {
        public int Id { get; set; }
        public string Path { get; set; }
        public TaskStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public enum TaskStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Deleted
    }

    public class TaskQueueDatabase
    {
        private readonly string _connectionString;
        private readonly CultureInfo _cultureInfo = CultureInfo.InvariantCulture;

        public TaskQueueDatabase(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task AddTaskAsync(string path)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                INSERT INTO Tasks (Path, Status, StatusCode, CreatedAt)
                VALUES (@path, @status, @status_code, @createdAt)";

                command.Parameters.AddWithValue("@path", path);
                command.Parameters.AddWithValue("@status", TaskStatus.Pending.ToString());
                command.Parameters.AddWithValue("@status_code", (int)TaskStatus.Pending);
                command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", _cultureInfo));

                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task RemoveTaskAsync(int taskId)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                DELETE FROM Tasks 
                WHERE Id = @id";

                command.Parameters.AddWithValue("@id", taskId);
                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task<TaskItem> GetNextTaskAsync()
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                SELECT * FROM Tasks 
                WHERE Status = @pending 
                ORDER BY CreatedAt ASC 
                LIMIT 1";

                command.Parameters.AddWithValue("@pending", (int)TaskStatus.Pending);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new TaskItem
                        {
                            Id = reader.GetInt32(0),
                            Path = reader.GetString(1),
                            Status = (TaskStatus)reader.GetInt32(2),
                            CreatedAt = DateTime.Parse(reader.GetString(3), _cultureInfo),
                            CompletedAt = await reader.IsDBNullAsync(4) ? null : DateTime.Parse(reader.GetString(4), _cultureInfo)
                        };
                    }
                    return null;
                }
            }
        }

        public async Task UpdateTaskStatusAsync(int taskId, TaskStatus status)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                UPDATE Tasks 
                SET Status = @status,
                    CompletedAt = @completedAt
                WHERE Id = @id";

                command.Parameters.AddWithValue("@status", (int)status);
                command.Parameters.AddWithValue("@id", taskId);
                command.Parameters.AddWithValue("@completedAt",
                    status == TaskStatus.Completed ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", _cultureInfo) : null);

                await command.ExecuteNonQueryAsync();
            }
        }

        public async Task UpdateTaskStatusAsync(string path, TaskStatus status)
        {
            using (var connection = new SQLiteConnection(_connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                UPDATE Tasks 
                SET Status = @status,
                    CompletedAt = @completedAt
                WHERE Path = @path";

                command.Parameters.AddWithValue("@status", (int)status);
                command.Parameters.AddWithValue("@path", path);
                command.Parameters.AddWithValue("@completedAt",
                    status == TaskStatus.Completed ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", _cultureInfo) : null);

                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public class TaskQueueProcessor : IDisposable // Добавлен интерфейс IDisposable
    {
        private readonly TaskQueueDatabase _db;
        private readonly CancellationTokenSource _cts;
        private bool _isRunning;
        private bool _disposed;

        public TaskQueueProcessor(TaskQueueDatabase db)
        {
            _db = db;
            _cts = new CancellationTokenSource();
            _isRunning = false;
            _disposed = false;
        }

        public void Start()
        {
            if (_isRunning || _disposed) return;
            _isRunning = true;

            Task.Run(async () =>
            {
                try
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var task = await _db.GetNextTaskAsync();

                        if (task != null)
                        {
                            Console.WriteLine($"[{DateTime.Now}] Начало: {task.Path}");
                            await _db.UpdateTaskStatusAsync(task.Id, TaskStatus.InProgress);
                            var result = RunFME("sdf", task.Path, "");
                            if (result.ExitCode == 0)
                            {
                                await _db.UpdateTaskStatusAsync(task.Id, TaskStatus.Completed);
                            }
                            else
                            {
                                await _db.UpdateTaskStatusAsync(task.Id, TaskStatus.Failed);
                            }
                            Console.WriteLine($"[{DateTime.Now}] Завершено: {task.Path}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now}] Очередь пуста, ожидание...");
                            await Task.Delay(2000, _cts.Token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Обработка остановлена");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
                finally
                {
                    _isRunning = false;
                }
            }, _cts.Token);
        }

        public void Stop()
        {
            if (!_isRunning || _disposed) return;
            _cts.Cancel();
        }

        static (string Output, string Error, int ExitCode) RunFME(string workspacePath, string sourceDataset, string destDataset)
        {
            // Путь к исполняемому файлу FME
            string fmePath = @"C:\Program Files\FME\fme.exe";

            // Аргументы командной строки
            string arguments = $"--quiet --NoUI \"{workspacePath}\" --SourceDataset \"{sourceDataset}\" --DestDataset \"{destDataset}\"";

            // Настройка процесса
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fmePath,
                Arguments = arguments,
                RedirectStandardOutput = true, // Перенаправление вывода
                RedirectStandardError = true,  // Перенаправление ошибок
                UseShellExecute = false,      // Не использовать оболочку системы
                CreateNoWindow = true,        // Не создавать окно
                StandardOutputEncoding = Encoding.UTF8, // Кодировка вывода
                StandardErrorEncoding = Encoding.UTF8  // Кодировка ошибок
            };

            // Буферы для вывода и ошибок
            StringBuilder outputBuffer = new StringBuilder();
            StringBuilder errorBuffer = new StringBuilder();

            // Запуск процесса
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;

                // Обработка вывода
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuffer.AppendLine(e.Data); // Добавляем строку в буфер
                    }
                };

                // Обработка ошибок
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuffer.AppendLine(e.Data); // Добавляем строку в буфер
                    }
                };

                // Запуск процесса
                process.Start();

                // Начать асинхронное чтение вывода и ошибок
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Ожидание завершения процесса
                process.WaitForExit();

                // Возвращаем результат
                return (outputBuffer.ToString(), errorBuffer.ToString(), process.ExitCode);
            }
        }

        #region Dispose
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                }
                _disposed = true;
            }
        }

        ~TaskQueueProcessor()
        {
            Dispose(false);
        }
        #endregion
    }

    public class NetworkFolderPollerViewModel : INotifyPropertyChanged
    {
        private readonly TaskQueueDatabase queue;
        private readonly TaskQueueProcessor processor;
        private readonly NetworkFolderPoller poller;
        private readonly Dispatcher dispatcher;
        private ICommand pick_folder_command;
        private readonly string connection_string;

        public NetworkFolderPollerViewModel(string connectionString)
        {
            connection_string = connectionString;
            queue = new TaskQueueDatabase(connectionString);
            processor = new TaskQueueProcessor(queue);
            poller = new NetworkFolderPoller(connectionString);
            dispatcher = Dispatcher.CurrentDispatcher;

            poller.OnNewFileDetected += OnNewFile;
            poller.OnFileDeleted += OnDeleteFile;
            poller.OnFileModified += OnModifyFile;

            LoadConfig();
        }

        #region dataBase Tasks
        public async Task AddTaskAsync(string path)
        {
            using (var connection = new SQLiteConnection(connection_string))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                INSERT INTO Tasks (Path, Status, CreatedAt)
                VALUES (@path, @status, @createdAt)";

                command.Parameters.AddWithValue("@path", path);
                command.Parameters.AddWithValue("@status", (int)TaskStatus.Pending);
                command.Parameters.AddWithValue("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                await command.ExecuteNonQueryAsync();
            }
        }
        #endregion

        #region UI Properties
        public ICommand PickFolderCommand
        {
            get
            {
                return pick_folder_command ?? (pick_folder_command = new RelayCommand(
                   x =>
                   {
                       PickFolderCommandExecute();
                   }));
            }

        }
        public void PickFolderCommandExecute()
        {
            var dlg = new FolderPicker();
            dlg.InputPath = Directory.Exists(NetworkPath) ? NetworkPath :
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (dlg.ShowDialog() == true)
            {
                if (NetworkPath.Length > 0)
                {
                    Trace.WriteLine($"STOP Watch directory: ===================== \"{NetworkPath}\" =========================", "INFO");
                }

                NetworkPath = dlg.ResultPath;
                Trace.WriteLine($"START Watch directory: ===================== \"{NetworkPath}\" =========================", "INFO");

                //SaveConfig();
                //StartWatching();
            }
        }

        // Свойство для привязки NetworkPath
        private string network_path = "";
        public string NetworkPath
        {
            get => network_path;
            set
            {
                if (network_path != value)
                {
                    network_path = value;
                    OnPropertyChanged();
                    poller.NetworkPath = value;
                }
            }
        }

        // Свойство для привязки PollingInterval
        private int polling_interval = 30;
        public int PollingInterval
        {
            get => polling_interval;
            set
            {
                if (polling_interval != value)
                {
                    polling_interval = value;
                    OnPropertyChanged();
                    poller.PollingInterval = value;
                }
            }
        }

        // Свойство для привязки NetworkPath
        private string filter_file_watch = "*.*";
        public string FilterWatch
        {
            get => filter_file_watch;
            set
            {
                if (filter_file_watch != value)
                {
                    filter_file_watch = value;
                    OnPropertyChanged();
                    poller.FilterWatch = value;
                }
            }
        }

        private string command_args = "/t fme.exe /e";
        public string CommandArgs
        {
            get => command_args;
            set
            {
                if (command_args != value)
                {
                    command_args = value;
                    OnPropertyChanged();
                }
            }
        }
        #endregion

        #region Save/Load
        public void LoadConfig()
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(AppDomain.CurrentDomain.BaseDirectory + "/Extensions/FilePolling/FilePolling.config");

                XmlNodeList folder = doc.GetElementsByTagName("WatchFolder");
                if (folder.Count > 0)
                {
                    NetworkPath = folder[0].InnerText;
                }

                XmlNodeList interval = doc.GetElementsByTagName("Interval");
                if (interval.Count > 0)
                {
                    PollingInterval = int.Parse(interval[0].InnerText);
                }

                XmlNodeList filter = doc.GetElementsByTagName("Filter");
                if (filter.Count > 0)
                {
                    FilterWatch = filter[0].InnerText;
                }

                XmlNodeList command = doc.GetElementsByTagName("CommandArgs");
                if (command.Count > 0)
                {
                    CommandArgs = command[0].InnerText;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"LoadConfig: {ex.Message}", "ERROR");
            }
        }

        public void SaveConfig()
        {
            try
            {
                var name = AppDomain.CurrentDomain.BaseDirectory + "/Extensions/FilePolling/FilePolling.config";
                StreamWriter file = new StreamWriter(new FileStream(name, FileMode.Create), Encoding.UTF8);

                string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
                xml += "<Configuration>\n";
                xml += "\t<WatchFolder>" + NetworkPath + "</WatchFolder>\n";
                xml += "\t<Interval>" + PollingInterval + "</Interval>\n";
                xml += "\t<Filter>" + FilterWatch + "</Filter>\n";
                xml += "\t<CommandArgs>" + CommandArgs + "</CommandArgs>\n";
                xml += "</Configuration>\n";

                file.Write(xml);
                file.Flush();
                file.Close();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"SaveConfig: {ex.Message}", "ERROR");
            }
        }
        #endregion

        // Метод для запуска опроса
        public void Start()
        {
            poller.NetworkPath = NetworkPath;
            poller.PollingInterval = PollingInterval;
            poller.Start();

            processor.Start();
        }

        // Метод для остановки опроса
        public void Stop()
        {
            poller.Stop();
            processor.Stop();
        }

        private async void OnNewFile(PollingAction action, string message)
        {
            await queue.AddTaskAsync(message);
            await dispatcher.InvokeAsync(() => Trace.WriteLine(message, "INFO"));
        }

        private async void OnModifyFile(PollingAction action, string message)
        {
            await queue.UpdateTaskStatusAsync(message, TaskStatus.Pending);
            await dispatcher.InvokeAsync(() => Trace.WriteLine(message, "INFO"));
        }

        private async void OnDeleteFile(PollingAction action, string message)
        {
            await queue.UpdateTaskStatusAsync(message, TaskStatus.Deleted);
            await dispatcher.InvokeAsync(() => Trace.WriteLine(message, "INFO"));
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string property_name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
        }
        #endregion
    }
}
