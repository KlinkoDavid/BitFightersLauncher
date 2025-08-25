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

            try
            {
                // Most újra az eredeti LoginWindow-t próbáljuk
                var loginWindow = new LoginWindow();
                
                System.Diagnostics.Debug.WriteLine("LoginWindow létrehozva");
                
                // Beállítjuk főablakként
                this.MainWindow = loginWindow;
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                
                // Megjelenítjük
                loginWindow.Show();
                
                System.Diagnostics.Debug.WriteLine("LoginWindow.Show() meghívva");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hiba az ablak létrehozásakor: {ex.Message}");
                MessageBox.Show($"Hiba az ablak létrehozásakor: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
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
