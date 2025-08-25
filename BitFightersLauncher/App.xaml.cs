using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace BitFightersLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Add global exception handler for unhandled exceptions
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            
            base.OnStartup(e);
            
            // Disable automatic window creation
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Ha van Remember Me és érvényes mentés, lépjünk be automatikusan
            var saved = AuthStorage.Load();
            if (saved != null && saved.RememberMe && !string.IsNullOrEmpty(saved.Username))
            {
                var mainWindow = new MainWindow();
                mainWindow.SetUserInfo(saved.Username, saved.UserId, saved.UserCreatedAt);
                this.MainWindow = mainWindow;
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
                return;
            }
            
            // Egyébként login ablak
            var loginWindow = new LoginWindow();
            bool? loginResult = loginWindow.ShowDialog();
            
            if (loginResult == true)
            {
                var mainWindow = new MainWindow();
                mainWindow.SetUserInfo(loginWindow.LoggedInUsername, loginWindow.UserId, loginWindow.UserCreatedAt);
                this.MainWindow = mainWindow;
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            else
            {
                Shutdown();
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Handle the specific InvalidOperationException related to dependency objects
            if (e.Exception is InvalidOperationException invalidOpEx && 
                invalidOpEx.Message.Contains("property does not point to a DependencyObject"))
            {
                Debug.WriteLine($"Caught InvalidOperationException in animation: {invalidOpEx.Message}");
                e.Handled = true; // Prevent application crash
                return;
            }
            
            // Log other exceptions but don't crash
            Debug.WriteLine($"Unhandled exception: {e.Exception}");
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Debug.WriteLine($"Unhandled domain exception: {e.ExceptionObject}");
        }
    }
}
