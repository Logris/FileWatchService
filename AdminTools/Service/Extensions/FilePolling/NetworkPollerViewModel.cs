using Command;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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
        public string Result { get; set; }
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
        private readonly SqliteCommandManager _dbManager;
        private readonly CultureInfo _cultureInfo = CultureInfo.InvariantCulture;

        public TaskQueueDatabase(SqliteCommandManager manager)
        {
            _dbManager = manager;
        }

        public async Task AddTaskAsync(string path)
        {
            await _dbManager.ExecuteNonQueryAsync(@"
                INSERT INTO Tasks (Path, Status, StatusCode, CreatedAt)
                VALUES (@path, @status, @status_code, @createdAt)",
                ("@path", path),
                ("@status", TaskStatus.Pending.ToString()),
                ("@status_code", (int)TaskStatus.Pending),
                ("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", _cultureInfo)));
        }

        public async Task RemoveTaskAsync(int taskId)
        {
            await _dbManager.ExecuteNonQueryAsync(@"DELETE FROM Tasks WHERE Id = @id", ("id", taskId));
        }

        public async Task<TaskItem> GetNextTaskAsync()
        {
            using (var reader = await _dbManager.ExecuteReaderAsync(@"
                        SELECT * FROM Tasks  
                        WHERE StatusCode = @pending 
                        ORDER BY CreatedAt ASC LIMIT 1",
                        ("@pending", (int)TaskStatus.Pending)))
            {
                if (await reader.ReadAsync())
                {
                    return new TaskItem
                    {
                        Id = reader.GetInt32(0),
                        Path = reader.GetString(1),
                        Status = (TaskStatus)reader.GetInt32(3),
                        CreatedAt = DateTime.Parse(reader.GetString(4), _cultureInfo),
                        CompletedAt = await reader.IsDBNullAsync(5) ? null : DateTime.Parse(reader.GetString(5), _cultureInfo)
                    };
                }
                return null;
            }
        }

        public async Task UpdateTaskStatusAsync(int taskId, TaskStatus status, string description = null)
        {
            await _dbManager.ExecuteNonQueryAsync(@"
                    UPDATE Tasks 
                    SET 
                        Status = @status,
                        StatusCode = @status_code,
                        CompletedAt = @completedAt,
                        Description = @description
                    WHERE Id = @id",

                    ("@status", status.ToString()),
                    ("@status_code", (int)status),
                    ("@id", taskId),
                    ("@completedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", _cultureInfo)),
                    ("@description", description));
        }

        public async Task UpdateTaskStatusAsync(string path, TaskStatus status, string description = null)
        {
            await _dbManager.ExecuteNonQueryAsync(@"
                UPDATE Tasks 
                SET Status = @status,
                    StatusCode = @status_code,
                    CompletedAt = @completedAt,
                    Description = @description
                WHERE Path = @path",

                ("@status", status.ToString()),
                ("@status_code", (int)status),
                ("@path", path),
                ("@completedAt", status == TaskStatus.Completed ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", _cultureInfo) : null),
                ("@description", description));
        }
    }

    public class TaskQueueProcessor : IDisposable // Добавлен интерфейс IDisposable
    {
        private readonly NetworkFolderPollerViewModel _owner;
        private readonly TaskQueueDatabase _db;
        private readonly CancellationTokenSource _cts;
        private bool _isRunning;
        private bool _disposed;

        public TaskQueueProcessor(NetworkFolderPollerViewModel owner, TaskQueueDatabase db)
        {
            _owner = owner;
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
                            try
                            {
                                Console.WriteLine($"[{DateTime.Now}] Начало: {task.Path}");
                                await _db.UpdateTaskStatusAsync(task.Id, TaskStatus.InProgress);
                                var result = RunFME(_owner.AppPath, _owner.ReplacedCommandArgs, task.Path);
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
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Ошибка: {ex.Message}");
                                await _db.UpdateTaskStatusAsync(task.Id, TaskStatus.Failed, ex.Message);
                            }
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

        static (string Output, string Error, int ExitCode) RunFME(string appPath, string args, string pathToZip)
        {
            // Путь к исполняемому файлу FME
            string fmePath = appPath;

            // Аргументы командной строки
            string arguments = args;
            arguments += $" --PathToZip {pathToZip}";

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
        private ICommand pick_app_command;
        private readonly SqliteCommandManager _dbManager;

        public NetworkFolderPollerViewModel(SqliteCommandManager manager)
        {
            _dbManager = manager;
            queue = new TaskQueueDatabase(_dbManager);
            processor = new TaskQueueProcessor(this, queue);
            poller = new NetworkFolderPoller(_dbManager);
            dispatcher = Dispatcher.CurrentDispatcher;

            poller.OnNewFileDetected += OnNewFile;
            poller.OnFileDeleted += OnDeleteFile;
            poller.OnFileModified += OnModifyFile;

            LoadConfig();
            poller.LoadSnapshotAsync().GetAwaiter().GetResult();
        }

        #region dataBase Tasks
        public async Task AddTaskAsync(string path)
        {
            await _dbManager.ExecuteNonQueryAsync(@"
                INSERT INTO Tasks (Path, Status, CreatedAt)
                VALUES (@path, @status, @createdAt)",
                ("@path", path),
                ("@status", (int)TaskStatus.Pending),
                ("@createdAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
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
            var picker = new FilePicker();

            picker.PickFolders = true;
            picker.MustExist = true;
            picker.InputPath =
                Directory.Exists(NetworkPath) ? NetworkPath :
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (picker.ShowDialog() == true)
            {
                if (NetworkPath.Length > 0)
                {
                    Trace.WriteLine($"STOP Watch directory: ===================== \"{NetworkPath}\" =========================", "INFO");
                }

                NetworkPath = picker.ResultPath;
                Trace.WriteLine($"START Watch directory: ===================== \"{NetworkPath}\" =========================", "INFO");
            }
        }

        public ICommand PickAppCommand
        {
            get
            {
                return pick_app_command ?? (pick_app_command = new RelayCommand(
                   x =>
                   {
                       var picker = new FilePicker();

                       picker.MustExist = true;
                       picker.InputPath =
                           Directory.Exists(AppPath) ? AppPath : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

                       if (picker.ShowDialog() == true)
                       {
                           AppPath = picker.ResultPath;
                       }
                   }));
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

        private string app_path = "\"c:\\ProgramFiles\\FME\\FME.exe\"";
        public string AppPath
        {
            get => app_path;
            set
            {
                if (app_path != value)
                {
                    app_path = value;
                    OnPropertyChanged();
                }
            }
        }

        private string command_args = "\"workspace.fmw\"";
        public string CommandArgs
        {
            get => command_args;
            set
            {
                if (command_args != value)
                {
                    command_args = value;
                    OnPropertyChanged();
                    ReplacedCommandArgs = Regex.Replace(command_args, @"\r\n?|\n", " ");
                }
            }
        }

        private string replaced_command_args;
        public string ReplacedCommandArgs
        {
            get => replaced_command_args;
            set
            {
                if (replaced_command_args != value)
                {
                    replaced_command_args = value;
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

                XmlNodeList app = doc.GetElementsByTagName("AppPath");
                if (app.Count > 0)
                {
                    AppPath = app[0].InnerText;
                }

                XmlNodeList command = doc.GetElementsByTagName("CommandArgs");
                if (command.Count > 0)
                {
                    if (command[0] is XmlCDataSection cdata)
                    {
                        CommandArgs = cdata.Data;
                    }
                    else
                    {
                        CommandArgs = command[0].InnerText;
                    }
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
                xml += $"<Configuration>\n";
                xml += $"\t<WatchFolder>{NetworkPath}</WatchFolder>\n";
                xml += $"\t<Interval>{PollingInterval}</Interval>\n";
                xml += $"\t<Filter>{FilterWatch}</Filter>\n";
                xml += $"\t<AppPath>{AppPath}</AppPath>\n";
                xml += $"\t<CommandArgs><![CDATA[{CommandArgs}]]></CommandArgs>\n";
                xml += $"</Configuration>\n";

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
            poller.SaveSnapshot();
            poller.Stop();
            processor.Stop();
        }

        private async void OnNewFile(PollingAction action, string message)
        {
            await queue.AddTaskAsync(message);
            await dispatcher.InvokeAsync(() => Trace.WriteLine(message, "NEW"));
        }

        private async void OnModifyFile(PollingAction action, string message)
        {
            await queue.UpdateTaskStatusAsync(message, TaskStatus.Pending);
            await dispatcher.InvokeAsync(() => Trace.WriteLine(message, "MODIFY"));
        }

        private async void OnDeleteFile(PollingAction action, string message)
        {
            await queue.UpdateTaskStatusAsync(message, TaskStatus.Deleted);
            await dispatcher.InvokeAsync(() => Trace.WriteLine(message, "DELETE"));
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
