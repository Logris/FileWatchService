using Command;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Input;
using System.Xml;


namespace FileWatcher
{
    public class PatternMatcher
    {
        public PatternMatcher()
        {
            Type patternMatcherType = typeof(FileSystemWatcher).Assembly.GetType("System.IO.PatternMatcher");
            MethodInfo patternMatchMethod = patternMatcherType.GetMethod("StrictMatchPattern", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            strict_match_pattern = (expression, name) => (bool)patternMatchMethod.Invoke(null, new object[] { expression, name });
        }

        public StrictMatchPatternDelegate StrictMatchPattern => strict_match_pattern;

        private readonly StrictMatchPatternDelegate strict_match_pattern;

        public delegate bool StrictMatchPatternDelegate(string expression, string name);
    }

    public class Watcher : MiracleAdmin.BasePropertyNotify
    {
        public Watcher()
        {
            log = new StreamWriter(new FileStream(AppDomain.CurrentDomain.BaseDirectory + "Watcher.log", FileMode.Append), Encoding.GetEncoding(1251))
            {
                AutoFlush = true
            };

            log.WriteLine("\n=================================================");
            Info("START watcher...");
            log.WriteLine("=================================================");
            log.WriteLine();

            LoadConfig();
            StartWatching();
        }

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
            dlg.InputPath = Directory.Exists(WatchFolder) ? WatchFolder :
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (dlg.ShowDialog() == true)
            {
                if (WatchFolder.Length > 0)
                {
                    Info($"STOP Watch directory: ===================== \"{WatchFolder}\" =========================\n");
                }

                WatchFolder = dlg.ResultPath;
                //SaveConfig();
                //StartWatching();
            }
        }

        public string WatchFolder
        {
            get { return watch_folder; }

            set
            {
                if (value != watch_folder)
                {
                    watch_folder = value;
                    NotifyPropertyChanged();
                }
            }
        }
        public string CreateCommand
        {
            get { return create_command; }

            set
            {
                if (value != create_command)
                {
                    create_command = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public string CreateScriptFilter
        {
            get { return create_script_filter; }

            set
            {
                if (value != create_script_filter)
                {
                    create_script_filter = value;
                    create_script_patterns = create_script_filter.Split(';');
                    NotifyPropertyChanged();
                }
            }
        }

        public double DelayRunScript
        {
            get { return delay_run_script; }

            set
            {
                if (value != delay_run_script)
                {
                    delay_run_script = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public NotifyFilters NotifyFilter
        {
            get { return notify_filters; }

            set
            {
                if (value != notify_filters)
                {
                    notify_filters = value;
                    SetNotifyFlags(notify_filters);
                    NotifyPropertyChanged();
                }
            }
        }

        public bool NotifyFolderName
        {
            get { return notify_dir_name; }

            set
            {
                if (value != notify_dir_name)
                {
                    notify_dir_name = value;
                    NotifyPropertyChanged();
                }
            }
        }
        public bool NotifyFileName
        {
            get { return notify_file_name; }

            set
            {
                if (value != notify_file_name)
                {
                    notify_file_name = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool NotifyLastWrite
        {
            get { return notify_last_write; }

            set
            {
                if (value != notify_last_write)
                {
                    notify_last_write = value;
                    NotifyPropertyChanged();
                }
            }
        }
        public bool NotifyLastAccess
        {
            get { return notify_last_access; }

            set
            {
                if (value != notify_last_access)
                {
                    notify_last_access = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool NotifySize
        {
            get { return notify_size; }

            set
            {
                if (value != notify_size)
                {
                    notify_size = value;
                    NotifyPropertyChanged();
                }
            }
        }
        public bool NotifyAttributes
        {
            get { return notify_attributes; }

            set
            {
                if (value != notify_attributes)
                {
                    notify_attributes = value;
                    NotifyPropertyChanged();
                }
            }
        }
        public bool NotifySecurity
        {
            get { return notify_security; }

            set
            {
                if (value != notify_security)
                {
                    notify_security = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public bool NotifyCreationTime
        {
            get { return notify_creation_time; }

            set
            {
                if (value != notify_creation_time)
                {
                    notify_creation_time = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public NotifyFilters GetNotifyFilters()
        {
            uint filters = 0;

            if (notify_dir_name)
            {
                filters |= (uint)NotifyFilters.DirectoryName;
            }

            if (notify_file_name)
            {
                filters |= (uint)NotifyFilters.FileName;
            }

            if (notify_attributes)
            {
                filters |= (uint)NotifyFilters.Attributes;
            }

            if (notify_last_write)
            {
                filters |= (uint)NotifyFilters.LastWrite;
            }

            if (notify_last_access)
            {
                filters |= (uint)NotifyFilters.LastAccess;
            }

            if (notify_size)
            {
                filters |= (uint)NotifyFilters.Size;
            }

            if (notify_security)
            {
                filters |= (uint)NotifyFilters.Security;
            }

            if (notify_creation_time)
            {
                filters |= (uint)NotifyFilters.CreationTime;
            }

            return (NotifyFilters)filters;
        }

        public void SetNotifyFlags(NotifyFilters filters)
        {
            notify_dir_name = filters.HasFlag(NotifyFilters.DirectoryName);
            notify_file_name = filters.HasFlag(NotifyFilters.FileName);
            notify_attributes = filters.HasFlag(NotifyFilters.Attributes);
            notify_last_write = filters.HasFlag(NotifyFilters.LastWrite);
            notify_last_access = filters.HasFlag(NotifyFilters.LastAccess);
            notify_size = filters.HasFlag(NotifyFilters.Size);
            notify_security = filters.HasFlag(NotifyFilters.Security);

            NotifyCreationTime = filters.HasFlag(NotifyFilters.CreationTime);
        }

        public void StartWatching()
        {
            if (Directory.Exists(WatchFolder))
            {
                watcher = new FileSystemWatcher(WatchFolder);

                watcher.NotifyFilter = GetNotifyFilters();
                watcher.Filter = "*.*";
                watcher.IncludeSubdirectories = true;
                watcher.EnableRaisingEvents = true;

                watcher.Created += OnCreated;
                watcher.Changed += OnChanged;
                watcher.Deleted += OnDeleted;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;

                Info($"START Watch directory: ===================== \"{WatchFolder}\" =========================");
            }
            else
            {
                Info($"Error: Directory \"{WatchFolder}\"not exist.");
            }
        }

        public void ExecuteBatFile(string param)
        {
            var bat_name = AppDomain.CurrentDomain.BaseDirectory + "/Extensions/FileWatcher/OnFileCreated.bat";

            var p = new Process();

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.FileName = bat_name;
            p.StartInfo.Arguments = param;
            p.Start();

            // To avoid deadlocks, always read the output stream first and then wait.  
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();

            log.WriteLine(output);
        }

        public void ExecuteOnCreateCommand(string args)
        {
            var p = new Process();

            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.FileName = @"cmd.exe";
            p.StartInfo.Arguments = args;

            p.EnableRaisingEvents = true;
            p.Exited += OnCmdExited;
            p.Start();
        }

        private void OnCmdExited(object sender, EventArgs e)
        {
            var p = sender as Process;

            if (p != null)
            {
                string output = p.StandardOutput.ReadToEnd();
                output += p.StandardError.ReadToEnd();

                if (output.Length > 0)
                {
                    log.WriteLineAsync(output);
                }
            }
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            string value = $"CREATED [{e.Name}]: {e.FullPath}";
            Info(value);

            //            AddEvent(e.FullPath, WatcherChangeTypes.Created);
            //             if (FilenameFit(e.Name, create_script_patterns))
            //             {
            //                 //ExecuteBatFile($"\"{e.FullPath}\" \"{e.Name}\"");
            //                 ExecuteOnCreateCommand(CreateCommand.Replace("$file_name", e.Name).Replace("$file_path", e.FullPath));
            //             }
        }
        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            Info($"CHANGED [{e.Name}]: {e.FullPath}");

            lock (events)
            {
                if (events.ContainsKey(e.FullPath)) return;

                events.Add(e.FullPath, WatcherChangeTypes.Changed);
            }

            System.Timers.Timer timer = new System.Timers.Timer(delay_run_script * 1000.0) { AutoReset = false };
            timer.Elapsed += (timerElapsedSender, timerElapsedArgs) =>
            {
                lock (events)
                {
                    if (events.ContainsKey(e.FullPath))
                    {
                        if (File.Exists(e.FullPath) && FilenameFit(e.Name, create_script_patterns))
                        {
                            Info($"Execute Script: for \"{e.FullPath}\"");
                            ExecuteOnCreateCommand(CreateCommand.Replace("$file_name", e.Name).Replace("$file_path", e.FullPath));
                        }
                        events.Remove(e.FullPath);
                    }
                    else
                    {
                        //Info($"CHANGED [{e.Name}]: {e.FullPath}");
                    }
                }
            };
            timer.Start();
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            Info($"DELETED [{e.Name}]: {e.FullPath}");
            if (File.Exists(e.FullPath))
            {
                //AddEvent(e.FullPath, WatcherChangeTypes.Deleted);
                Info($"DELETED [{e.Name}]: {e.FullPath}");
            }
        }
        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            //AddEvent(e.FullPath, WatcherChangeTypes.Renamed);

            Info($"RENAMED [{e.Name}]: {e.OldFullPath} -> {e.FullPath}");
        }
        private void OnError(object sender, ErrorEventArgs e)
        {
            string value = $"ERROR: {e.GetException().Message}";
            Info(value);
        }

        public void Info(string message)
        {
            log.WriteLine(DateTime.Now + ": " + message);
        }

        public void LoadConfig()
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(AppDomain.CurrentDomain.BaseDirectory + "/Extensions/FileWatcher/Watcher.config");

                XmlNodeList folder = doc.GetElementsByTagName("WatchFolder");
                if (folder.Count > 0)
                {
                    WatchFolder = folder[0].InnerText;
                }

                XmlNodeList delay = doc.GetElementsByTagName("DelayScript");
                if (delay.Count > 0)
                {
                    DelayRunScript = double.Parse(delay[0].InnerText);
                }

                XmlNodeList create = doc.GetElementsByTagName("CreateScriptFilters");
                if (create.Count > 0)
                {
                    CreateScriptFilter = create[0].InnerText;
                }

                XmlNodeList command = doc.GetElementsByTagName("CreateCommand");
                if (command.Count > 0)
                {
                    CreateCommand = command[0].InnerText;
                }

                XmlNodeList notify = doc.GetElementsByTagName("NotifyFilter");
                if (notify.Count > 0)
                {
                    NotifyFilter = (NotifyFilters)Enum.Parse(typeof(NotifyFilters), notify[0].InnerText);
                }
            }
            catch (Exception ex)
            {
                Info(ex.Message);
            }
        }

        public void SaveConfig()
        {
            try
            {
                var name = AppDomain.CurrentDomain.BaseDirectory + "/Extensions/FileWatcher/Watcher.config";
                StreamWriter file = new StreamWriter(new FileStream(name, FileMode.Create), Encoding.UTF8);

                string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
                xml += "<Configuration>\n";
                xml += "\t<WatchFolder>" + WatchFolder + "</WatchFolder>\n";
                xml += "\t<CreateScriptFilters>" + CreateScriptFilter + "</CreateScriptFilters>\n";
                xml += "\t<DelayScript>" + DelayRunScript + "</DelayScript>\n";
                xml += "\t<CreateCommand>" + CreateCommand + "</CreateCommand>\n";
                xml += "\t<NotifyFilter>" + GetNotifyFilters().ToString() + "</NotifyFilter>\n";
                xml += "</Configuration>\n";

                file.Write(xml);
                file.Flush();
                file.Close();
            }
            catch (Exception ex)
            {
                Info(ex.Message);
            }
        }

        public bool FilenameFit(string filename, string[] mask)
        {
            if (mask == null || mask.Length == 0 || mask[0] == "") return true;

            PatternMatcher patternMatcher = new PatternMatcher();
            foreach (string pattern in mask)
            {
                if (patternMatcher.StrictMatchPattern(pattern.ToLower(), filename.ToLower())) return true;
            }
            return false;
        }

        private void AddEvent(string path, WatcherChangeTypes action)
        {
            WatcherChangeTypes actions;
            if (events.TryGetValue(path, out actions))
            {
                //actions.Add(action);
            }
            else
            {
                events.Add(path, action);
            }
        }

        private readonly Dictionary<string, WatcherChangeTypes> events = new Dictionary<string, WatcherChangeTypes>();
        private string watch_folder;
        private string create_script_filter;
        private string create_command;
        private string[] create_script_patterns;

        private double delay_run_script = 3;

        private NotifyFilters notify_filters = (NotifyFilters)0xff;
        private bool notify_dir_name = true;
        private bool notify_file_name = true;
        private bool notify_creation_time = true;
        private bool notify_size = true;
        private bool notify_last_write = true;
        private bool notify_last_access = true;
        private bool notify_attributes = true;
        private bool notify_security = true;

        private ICommand pick_folder_command;
        private FileSystemWatcher watcher;
        private readonly StreamWriter log;
    }
}
