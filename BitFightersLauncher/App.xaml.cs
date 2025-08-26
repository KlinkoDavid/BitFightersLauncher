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
                // Ellenőrizzük, hogy van-e mentett bejelentkezési adat "Emlékezzen rám" beállítással
                var savedLogin = AuthStorage.Load();
                
                if (savedLogin != null && savedLogin.RememberMe && 
                    !string.IsNullOrEmpty(savedLogin.Username) && 
                    savedLogin.UserId > 0)
                {
                    // Van mentett adat és "Emlékezzen rám" be van kapcsolva
                    // Közvetlenül a főablakot nyitjuk meg
                    System.Diagnostics.Debug.WriteLine($"Auto-login: {savedLogin.Username}");
                    
                    var mainWindow = new MainWindow();
                    mainWindow.SetUserInfo(savedLogin.Username, savedLogin.UserId, savedLogin.UserCreatedAt);
                    
                    this.MainWindow = mainWindow;
                    this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    mainWindow.Show();
                    
                    System.Diagnostics.Debug.WriteLine("Főablak megnyitva auto-login-nal");
                }
                else
                {
                    // Nincs mentett adat vagy nincs "Emlékezzen rám" bekapcsolva
                    // Normál bejelentkezési folyamat
                    var loginWindow = new LoginWindow();
                    
                    // Subscribe to login events
                    loginWindow.LoginSucceeded += LoginWindow_LoginSucceeded;
                    loginWindow.LoginCancelled += LoginWindow_LoginCancelled;
                    
                    System.Diagnostics.Debug.WriteLine("LoginWindow létrehozva");
                    
                    // Set as main window
                    this.MainWindow = loginWindow;
                    this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    
                    // Show the login window
                    loginWindow.Show();
                    
                    System.Diagnostics.Debug.WriteLine("LoginWindow.Show() meghívva");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hiba az ablak létrehozásakor: {ex.Message}");
                MessageBox.Show($"Hiba az ablak létrehozásakor: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void LoginWindow_LoginSucceeded(object? sender, LoginEventArgs e)
        {
            try
            {
                // Close login window
                if (sender is LoginWindow loginWindow)
                {
                    loginWindow.Hide();
                }
                
                // Create and show main window
                var mainWindow = new MainWindow();
                mainWindow.SetUserInfo(e.Username, e.UserId, e.UserCreatedAt);
                
                // Set new main window
                this.MainWindow = mainWindow;
                mainWindow.Show();
                
                // Close login window
                if (sender is LoginWindow loginWin)
                {
                    loginWin.Close();
                }
                
                System.Diagnostics.Debug.WriteLine($"Sikeres bejelentkezés: {e.Username}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hiba a főablak megnyitásakor: {ex.Message}");
                MessageBox.Show($"Hiba a főablak megnyitásakor: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void LoginWindow_LoginCancelled(object? sender, EventArgs e)
        {
            // User cancelled login, shutdown application
            Shutdown();
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
