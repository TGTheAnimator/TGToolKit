using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using ToolKitV.Views;

namespace ToolKitV
{
    public partial class App : Application
    {
        protected Mutex? Mutex;
        private bool _ownsMutex;

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Single-instance guard — prevent running TGToolKit more than once at a time.
            Mutex = new Mutex(true, "TGToolKit_SingleInstance", out _ownsMutex);

            if (!_ownsMutex)
            {
                MessageBox.Show(
                    "TGToolKit is already running.",
                    "TGToolKit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }

            // Show manual splash screen for better scaling/stretching control
            var splash = new SplashWindow();
            splash.Show();

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Artificial delay to show splash screen
            await Task.Delay(2500);

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();

            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsMutex)
                Mutex?.ReleaseMutex();
            Mutex?.Dispose();
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                // Structured, appendable crash log — each crash session is clearly separated
                string crashLogPath = Models.AppPaths.CrashLogFilePath;
                string timestamp    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string entry =
                    $"{'=',-50}\n" +
                    $"[{timestamp}] TGToolKit v3.5.0 — UNHANDLED EXCEPTION\n" +
                    $"{'=',-50}\n" +
                    $"MESSAGE:\n  {e.Exception.Message}\n\n" +
                    $"INNER EXCEPTION:\n  {e.Exception.InnerException?.Message ?? "None"}\n\n" +
                    $"STACK TRACE:\n{e.Exception.StackTrace}\n" +
                    $"{new string('-', 50)}\n\n";

                File.AppendAllText(crashLogPath, entry);
            }
            catch
            {
                // If we can't even write the log, don't cascade into a second exception
            }

            MessageBox.Show(
                "TGToolKit encountered an unexpected error and recovered safely.\n\n" +
                $"Error: {e.Exception.Message}\n\n" +
                "Full details have been written to Logs/crash.log inside the AppData folder.\n" +
                "Please report this file to the developer.",
                "TGToolKit — System Exception",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Mark handled — prevents the Windows hard-crash dialog from appearing
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                try
                {
                    string crashLogPath = Models.AppPaths.CrashLogFilePath;
                    string timestamp    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string entry =
                        $"{'=',-50}\n" +
                        $"[{timestamp}] TGToolKit v3.5.0 — UNHANDLED BACKGROUND EXCEPTION\n" +
                        $"{'=',-50}\n" +
                        $"MESSAGE:\n  {ex.Message}\n\n" +
                        $"INNER EXCEPTION:\n  {ex.InnerException?.Message ?? "None"}\n\n" +
                        $"STACK TRACE:\n{ex.StackTrace}\n" +
                        $"{new string('-', 50)}\n\n";

                    File.AppendAllText(crashLogPath, entry);
                }
                catch
                {
                }
            }
        }
    }
}
