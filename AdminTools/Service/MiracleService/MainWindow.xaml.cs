using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MiracleAdmin
{
    namespace Service
    {
        public partial class MainWindow : Window
        {
            ServiceManager service;

            Properties properties;

            public MainWindow()
            {
                InitializeComponent();

                try
                {
                    service = new ServiceManager();

                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                    {
                        key.SetValue("Mggt Service", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" -autorun");
                    }

                    service.OnStart();
                    service.UpdateFirewallRules(true);

                    NotifyIcon.DataContext = service;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }

            void NotifyIcon_MouseClick(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    if (Visibility != Visibility.Visible)
                    {
                        Visibility = Visibility.Visible;
                        Show();
                        Activate();
                    }
                    else
                    {
                        Visibility = Visibility.Hidden;
                    }
                }
            }

            private void MenuItem_Click(object sender, RoutedEventArgs e)
            {
                Visibility = Visibility.Visible;
                Show();
                Activate();
            }

            private void MenuItem_Click_Close(object sender, RoutedEventArgs e)
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    key.DeleteValue("Mggt Service", false);
                }

                Application.Current.Shutdown();
            }

            private void Window_Closed(object sender, EventArgs e)
            {
                service.OnStop();
                service.Close();
            }

            private void MenuItem_Properties(object sender, RoutedEventArgs e)
            {
                try
                {
                    if (properties != null)
                    {
                        properties.Activate();
                        return;
                    }

                    properties = new Properties();
                    properties.DataContext = service;

                    foreach (var plug in service.Extensions)
                    {
                        TabItem item = new TabItem()
                        {
                            Margin = new Thickness(0, 0, 0, 0),
                            Header = plug.Name,
                            Content = plug.Content
                        };
                        properties.tabProperties.Items.Add(item);
                    }

                    properties.Closed += (obj, args) =>
                    {
                        service.OnStop();
                        service.OnStart();

                        //service.SaveConfig();

                        properties = null;
                    };

                    properties.ShowDialog();

                    foreach (var plug in service.Extensions)
                    {
                        plug.OnSaveProperties();
                    }
                }
                catch (Exception)
                {
                    //
                }
            }
        }
    }
}
