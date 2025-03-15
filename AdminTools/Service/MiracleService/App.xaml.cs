using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace MiracleAdmin
{
    namespace Service
    {
        /// <summary>
        /// Логика взаимодействия для App.xaml
        /// </summary>
        public partial class App : Application
        {
            private const int MINIMUM_SPLASH_TIME = 1500; // Miliseconds
            private const int SPLASH_FADE_TIME = 500;     // Miliseconds

            Mutex myMutex;

            protected override void OnStartup(StartupEventArgs e)
            {
                bool aIsNewInstance = false;
                myMutex = new Mutex(true, "C7193567-6B2F-40D5-BB57-2D6F1F8C04DB", out aIsNewInstance);
                if (!aIsNewInstance)
                {
                    MessageBox.Show("MGGT Service already an instance is running...");
                    App.Current.Shutdown();
                    return;
                }

                bool is_autorun = e.Args.Length > 0 && e.Args[0] == "-autorun";

                SplashScreen splash = new SplashScreen("Resource/Mggt_Kubik.png");
                if (!is_autorun)
                {
                    splash.Show(false, true);
                }

                Stopwatch timer = new Stopwatch();
                timer.Start();


                base.OnStartup(e);
                MainWindow main = new MainWindow();

                timer.Stop();
                if (!is_autorun)
                {
                    int remainingTimeToShowSplash = MINIMUM_SPLASH_TIME - (int)timer.ElapsedMilliseconds;
                    if (remainingTimeToShowSplash > 0)
                        Thread.Sleep(remainingTimeToShowSplash);

                    splash.Close(TimeSpan.FromMilliseconds(SPLASH_FADE_TIME));
                }
            }
        }
    }
}
