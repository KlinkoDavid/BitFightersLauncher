using System.Windows;

namespace BitFightersLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Disable automatic window creation
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            // Először a bejelentkezési ablakot mutatjuk
            var loginWindow = new LoginWindow();
            bool? loginResult = loginWindow.ShowDialog();
            
            if (loginResult == true)
            {
                // Ha sikeres a bejelentkezés, megnyitjuk a főablakot
                var mainWindow = new MainWindow();
                mainWindow.SetUserInfo(loginWindow.LoggedInUsername, loginWindow.UserId, loginWindow.UserCreatedAt);
                
                // Set as main window before showing
                this.MainWindow = mainWindow;
                this.ShutdownMode = ShutdownMode.OnMainWindowClose;
                
                mainWindow.Show();
            }
            else
            {
                // Ha nem jelentkezett be, kilépünk
                Shutdown();
            }
        }
    }
}
