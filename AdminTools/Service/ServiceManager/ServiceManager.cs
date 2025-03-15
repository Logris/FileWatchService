using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Xml;

namespace MiracleAdmin
{
    namespace Service
    {
        public class ServiceManager : BasePropertyNotify
        {
            public string ExecuteCommand
            {
                get => executeEngineCommand;
                set
                {
                    if (value != executeEngineCommand)
                    {
                        executeEngineCommand = value;
                        NotifyPropertyChanged();
                    }
                }
            }

            public string ToolTipString
            {
                get { return _tool_tip_string; }

                set
                {
                    if (value != _tool_tip_string)
                    {
                        _tool_tip_string = value;
                        NotifyPropertyChanged();
                    }
                }
            }

            public int UdpPort
            {
                get => udp_port;
                set
                {
                    if (value != udp_port)
                    {
                        udp_port = value;
                        NotifyPropertyChanged();
                    }
                }
            }

            public void OnStart()
            {
                try
                {
                    Log("Start service.\n");

                    //log.WriteLine("Engine: {0}", executeEngineCommand);
                    //log.WriteLine("Listen port: {0}", udp_port);

                    //GetConfig();

                    string name = System.Net.Dns.GetHostName();

                    string ip = "";
                    IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());
                    foreach (IPAddress addr in localIPs)
                    {
                        if (addr.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ip = addr.ToString();
                        }
                    }

                    ToolTipString = $"Mggt Service\nName: {name}\nIP: {ip}";

                    udp_client = new UdpClient(udp_port, AddressFamily.InterNetwork);

                    bDoWork = true;
                    backgroundThread = new Thread(new ThreadStart(UdpListenerUpdate))
                    {
                        IsBackground = true
                    };
                    backgroundThread.Start();
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show(ex.Message);
                }
            }

            public void OnStop()
            {
                bDoWork = false;
                if (backgroundThread != null)
                {
                    backgroundThread.Abort();
                    backgroundThread.Join();
                }

                if (udp_client != null)
                {
                    udp_client.Close();
                    udp_client = null;
                }

                if (log != null)
                {
                    log.WriteLine("\n");
                    Log("Stop service.");
                }
            }

            public void Log(string message)
            {
                log.WriteLine(DateTime.Now + ": " + message);
            }

            private void LoadConfig()
            {
                //default property
                string EngineDirectory = "";
                String base_dir = AppDomain.CurrentDomain.BaseDirectory;
                int i = base_dir.IndexOf("\\Service\\");
                if (i >= 0)
                {
                    EngineDirectory = base_dir.Substring(0, i);
                }
                else
                {
                    EngineDirectory = base_dir;
                }

                ExecuteCommand = EngineDirectory + "\\Miracle.exe";
                UdpPort = 20001;

                //
                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(AppDomain.CurrentDomain.BaseDirectory + "ServiceConfig.xml");

                    XmlNodeList engine = doc.GetElementsByTagName("Engine");
                    if (engine.Count > 0)
                    {
                        ExecuteCommand = engine[0].InnerText;
                    }

                    XmlNodeList sync_port_nodes = doc.GetElementsByTagName("ListenUdpPort");
                    if (sync_port_nodes.Count > 0)
                    {
                        if (int.TryParse(sync_port_nodes[0].InnerText, out int port))
                        {
                            UdpPort = port;
                        }
                    }

                    XmlNodeList grab_node = doc.GetElementsByTagName("Grabber");
                    if (grab_node.Count > 0)
                    {
                        if (grab_node[0].Attributes["JpgQuality"] is XmlAttribute attr_quality)
                        {
                            if (int.TryParse(attr_quality.Value, out int quality))
                            {
                                //grabber.JPEGQuality = quality;
                            }
                        }

                        if (grab_node[0].Attributes["Framerate"] is XmlAttribute attr_framerate)
                        {
                            if (int.TryParse(attr_framerate.Value, out int framerate))
                            {
                                //grabber.CaptureFrameRate = framerate;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
            }

            public void SaveConfig()
            {
                try
                {
                    string name = AppDomain.CurrentDomain.BaseDirectory + "ServiceConfig.xml";
                    StreamWriter file = new StreamWriter(new FileStream(name, FileMode.Create), Encoding.UTF8);

                    string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
                    xml += "<Configuration>\n";
                    xml += "\t<Engine>" + ExecuteCommand + "</Engine>\n";
                    xml += "\t<ListenUdpPort>" + UdpPort.ToString() + "</ListenUdpPort>\n";
                    xml += "</Configuration>\n";

                    file.Write(xml);
                    file.Flush();
                    file.Close();
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
            }

            private void UdpListenerUpdate()
            {
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                while (bDoWork)
                {
                    try
                    {
                        if (udp_client.Available > 0)
                        {
                            byte[] datagram = udp_client.Receive(ref ep);
                            if (datagram.Length > 21)
                            {
                                String header = Encoding.ASCII.GetString(datagram, 0, 21);
                                if (header == "MiracleAdmin datagram")
                                {
                                    Log(Encoding.ASCII.GetString(datagram));

                                    CommandsGroup group = (CommandsGroup)datagram[21];
                                    switch (group)
                                    {
                                        case CommandsGroup.Service:
                                            {
                                                ExecuteUdpCommand((Shared.ServiceCommands)datagram[22]);
                                            }
                                            break;

                                        case CommandsGroup.Grabber:
                                            {
                                                int len = datagram.Length - 23;
                                                byte[] message = new byte[len + 1];
                                                Array.Copy(datagram, 23, message, 0, len);

                                                GrabberEvent?.Invoke((Shared.GrabberCommand)datagram[22], message);
                                            }
                                            break;
                                    }

                                    if (listenUdpPlugins.ContainsKey(group))
                                    {
                                        int len = datagram.Length - 23;
                                        byte[] message = new byte[len + 1];
                                        Array.Copy(datagram, 23, message, 0, len);

                                        foreach (var plug in listenUdpPlugins[group])
                                        {
                                            plug.ProccessUdpMessage(datagram);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log(ex.Message);
                    }

                    Thread.Sleep(1);
                }
            }

            private void ExecuteUdpCommand(Shared.ServiceCommands command)
            {
                switch (command)
                {
                    case Shared.ServiceCommands.StartEngine:
                        {
                            RunProccess();
                        }
                        break;

                    case Shared.ServiceCommands.StopEngine:
                        {
                            //KillProccess();
                            if (executeProccess != null)
                            {
                                executeProccess.CloseMainWindow();

                                executeProccess = null;
                            }
                        }
                        break;

                    case Shared.ServiceCommands.UpdateSVNEngine:
                        {
                            UpdateSvnEngine();
                        }
                        break;

                    case Shared.ServiceCommands.RestartPC:
                        {
                            // Reboot r = new Reboot();
                            Reboot.Halt(true, false); //(, false) мягкая перезагрузка

                        }
                        break;
                    case Shared.ServiceCommands.ShutdownPC:
                        {
                            //Reboot r = new Reboot();
                            Reboot.Halt(false, false); //(, false) мягкое выключение   
                        }
                        break;
                }
            }

            private void RunProccess()
            {
                executeProccess = Process.Start(executeEngineCommand, "");
            }

            private void KillProccess()
            {
                Process[] killProcess = Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(executeEngineCommand));
                foreach (Process proc in killProcess)
                {
                    proc.CloseMainWindow();
                }
            }

            private void UpdateSvnEngine()
            {
                String param = " /command:update /path:\"" + EngineDirectory + "\" /closeonend:1";

                //LaunchInDiffSess.Execute("c:\\Program Files\\TortoiseSVN\\Bin\\TortoiseProc.exe", param, ref log);
                Process.Start(@"c:\\Program Files\\TortoiseSVN\\Bin\\TortoiseProc.exe", param);
            }

            public void UpdateFirewallRules(bool force = false)
            {
                bool is_update;
                if (force)
                {
                    is_update = true;
                }
                else
                {
                    is_update = path_changed;
                }

                if (is_update)
                {
                    string args = "/C netsh advfirewall firewall delete rule name=\"Miracle(in)\"";
                    args += "&netsh advfirewall firewall delete rule name = \"Miracle(out)\"";
                    args += string.Format("&netsh advfirewall firewall add rule name = \"Miracle(in)\" program = \"{0}\" dir = in protocol = udp action = allow", ExecuteCommand);
                    args += string.Format("&netsh advfirewall firewall add rule name = \"Miracle(out)\" program = \"{0}\" dir = out protocol = udp action = allow", ExecuteCommand);

                    var service_path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    args += "&netsh advfirewall firewall delete rule name=\"MiracleService(in)\"";
                    args += "&netsh advfirewall firewall delete rule name=\"MiracleService(out)\"";
                    args += string.Format("&netsh advfirewall firewall add rule name = \"MiracleService(in)\" program = \"{0}\" dir = in protocol = udp action = allow", service_path);
                    args += string.Format("&netsh advfirewall firewall add rule name = \"MiracleService(out)\" program = \"{0}\" dir = out protocol = udp action = allow", service_path);

                    System.Diagnostics.Process process = new System.Diagnostics.Process();

                    System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo()
                    {
                        UseShellExecute = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                        FileName = "c:\\Windows\\System32\\cmd.exe",
                        Arguments = args
                    };
                    process.StartInfo = startInfo;

                    process.Start();

                    path_changed = false;
                }
            }

            public ServiceManager()
            {
                try
                {
                    log = new StreamWriter(new FileStream(AppDomain.CurrentDomain.BaseDirectory + "MggtService.log", FileMode.Create), Encoding.GetEncoding(1251))
                    {
                        AutoFlush = true
                    };
                    log.WriteLine("\n");

                    //LoadConfig();

                    var pluginsPath = $"{AppDomain.CurrentDomain.BaseDirectory}Extensions";
                    if (Directory.Exists(pluginsPath))
                    {
                        foreach (var dir in Directory.GetDirectories(pluginsPath))
                        {
                            var name = Path.GetFileNameWithoutExtension(dir);
                            var plugin_path = $"{dir}\\{name}.dll";

                            Assembly assembly;
                            try
                            {
                                Log($"Load plugin: {plugin_path}");
                                AssemblyName an = AssemblyName.GetAssemblyName(plugin_path);
                                assembly = Assembly.Load(an);
                            }
                            catch (Exception ex)
                            {
                                Log(ex.Message);
                                continue;
                            }

                            Type pluginType = typeof(IServiceExtension);

                            foreach (Type type in assembly.GetTypes())
                            {
                                if (type.IsInterface || type.IsAbstract) continue;

                                if (type.GetInterface(pluginType.FullName) != null)
                                {
                                    IServiceExtension plugin = (IServiceExtension)Activator.CreateInstance(type);
                                    plugin.Load(this);

                                    extensions.Add(plugin);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            public void Close()
            {
                //SaveConfig();

                foreach (var ext in extensions)
                {
                    ext.Stop();
                }

                if (log != null)
                {
                    log.WriteLine("\n");

                    log.Flush();
                    log.Close();
                    log.Dispose();

                    log = null;
                }
            }

            public void RegisterUdpListenPlugin(CommandsGroup group, IServiceExtension plugin)
            {
                if (listenUdpPlugins.ContainsKey(group))
                {
                    listenUdpPlugins[group].Add(plugin);
                }
                else
                {
                    listenUdpPlugins.Add(group, new List<IServiceExtension>());
                    listenUdpPlugins[group].Add(plugin);
                }
            }

            public delegate void GrabberMessage(Shared.GrabberCommand cmd, byte[] message);

            public event GrabberMessage GrabberEvent;

            private List<IServiceExtension> extensions = new List<IServiceExtension>();
            public List<IServiceExtension> Extensions
            {
                get => extensions; set
                {
                    if (value != extensions) extensions = value;
                }
            }

            private UdpClient udp_client;
            private int udp_port;
            private bool path_changed = false;

            private System.IO.StreamWriter log;
            private Thread backgroundThread;
            private bool bDoWork = true;

            private string executeEngineCommand = "";
            private readonly string EngineDirectory = "";
            private string _tool_tip_string = "";

            private readonly Dictionary<CommandsGroup, List<IServiceExtension>> listenUdpPlugins = new Dictionary<CommandsGroup, List<IServiceExtension>>();

            private Process executeProccess = null;
        }
    }
}