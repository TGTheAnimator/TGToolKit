using System.Windows;
using System.Threading;
using System.Windows.Threading;
using System.Threading.Tasks;
using ToolKitV.Views;

namespace ToolKitV
{
    public partial class App : Application
    {
        protected Mutex? Mutex;
        private bool _ownsMutex;

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
            {
                Mutex?.ReleaseMutex();
            }
            Mutex?.Dispose();
            base.OnExit(e);
        }

        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            System.IO.File.WriteAllText("crash.log", e.Exception.ToString());
            MessageBox.Show(
                "An unexpected error occurred:\n\n" + e.Exception.ToString()
                + "\n\nCheck log.txt in the application folder for details.",
                "TGToolKit — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }
    }
}
