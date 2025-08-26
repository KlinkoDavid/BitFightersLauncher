using Microsoft.WindowsAPICodePack.Dialogs;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace BitFightersLauncher
{
    public class NewsUpdate
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string ShortContent => Content.Length > 100 ? Content.Substring(0, 100) + "..." : Content;
        
        [JsonPropertyName("created_at")]
        public string? CreatedAtString 
        { 
            set 
            {
                if (!string.IsNullOrEmpty(value))
                {
                    if (DateTime.TryParse(value, out DateTime result))
                    {
                        CreatedAt = result;
                    }
                }
            }
        }
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string FormattedDate => CreatedAt.ToString("yyyy.MM.dd");
    }

    public partial class MainWindow : Window
    {
        private const string GameDownloadUrl = "https://bitfighters.eu/BitFighters/BitFighters.zip";
        private const string VersionCheckUrl = "https://bitfighters.eu/BitFighters/version.txt";
        private const string GameExecutableName = "BitFighters.exe";
        private string gameInstallPath = string.Empty;
        private string serverGameVersion = "0.0.0";

        private readonly string settingsFilePath;

        private double _targetVerticalOffset;
        private bool _isScrolling;
        private bool _renderingHooked;
        private readonly bool _reducedMotion;

        // User info
        private string loggedInUsername = string.Empty;
        private int loggedInUserId = 0;
        private string loggedInUserCreatedAt = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string launcherDataPath = Path.Combine(appDataPath, "BitFightersLauncher");
            settingsFilePath = Path.Combine(launcherDataPath, "settings.txt");

            _reducedMotion = (RenderCapability.Tier >> 16) < 2;

            Loaded += (s, e) => UpdateBorderClip();
            SizeChanged += (s, e) => UpdateBorderClip();

            Loaded += async (s, e) =>
            {
                LoadInstallPath();
                await LoadServerVersionAsync();
                CheckGameInstallStatus();
                await LoadNewsUpdatesAsync();
                ApplyPerformanceModeIfNeeded();
            };
        }

        public void SetUserInfo(string username, int userId, string createdAt = "")
        {
            loggedInUsername = username;
            loggedInUserId = userId;
            loggedInUserCreatedAt = createdAt;
            
            // Update window title with username
            this.Title = $"BitFighters Launcher - {username}";
            
            // Update username display
            if (UsernameText != null)
            {
                UsernameText.Text = username;
            }
            
            Debug.WriteLine($"Bejelentkezett felhaszn�l�: {username} (ID: {userId}, Created: {createdAt})");
        }

        public void Logout()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Logout() met�dus kezdete");
                
                // Disable the shutdown mode temporarily
                var originalShutdownMode = Application.Current.ShutdownMode;
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                
                // Csak kijelentkez�s, NE t�r�lj�k a mentett adatokat
                // A felhaszn�l� d�nthet, hogy szeretn�-e megtartani az "Eml�kezzen r�m" be�ll�t�st
                var loginWindow = new LoginWindow();
                System.Diagnostics.Debug.WriteLine("LoginWindow l�trehozva");
                
                // Subscribe to login events
                loginWindow.LoginSucceeded += LoginWindow_LoginSucceeded;
                loginWindow.LoginCancelled += LoginWindow_LoginCancelled;
                System.Diagnostics.Debug.WriteLine("Login event handlerek hozz�adva");
                
                // Set the login window as the main window BEFORE showing it
                Application.Current.MainWindow = loginWindow;
                System.Diagnostics.Debug.WriteLine("LoginWindow be�ll�tva MainWindow-k�nt");
                
                System.Diagnostics.Debug.WriteLine("LoginWindow.Show() h�v�s...");
                loginWindow.Show();
                
                // Restore shutdown mode
                Application.Current.ShutdownMode = originalShutdownMode;
                
                System.Diagnostics.Debug.WriteLine("MainWindow.Close() h�v�s...");
                this.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hiba a Logout() met�dusban: {ex.Message}");
                MessageBox.Show($"Hiba a kijelentkez�s sor�n: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoginWindow_LoginSucceeded(object? sender, LoginEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("LoginWindow_LoginSucceeded kezdete");
                
                // Create and show new main window
                var mainWindow = new MainWindow();
                mainWindow.SetUserInfo(e.Username, e.UserId, e.UserCreatedAt);
                
                // Set new main window
                Application.Current.MainWindow = mainWindow;
                System.Diagnostics.Debug.WriteLine("�j MainWindow be�ll�tva");
                
                mainWindow.Show();
                System.Diagnostics.Debug.WriteLine("�j MainWindow megjelen�tve");
                
                // Close login window
                if (sender is LoginWindow loginWindow)
                {
                    loginWindow.Close();
                    System.Diagnostics.Debug.WriteLine("LoginWindow bez�rva");
                }
                
                System.Diagnostics.Debug.WriteLine($"�jra bejelentkez�s sikeres: {e.Username}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hiba az �jra bejelentkez�sn�l: {ex.Message}");
                MessageBox.Show($"Hiba az �jra bejelentkez�sn�l: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void LoginWindow_LoginCancelled(object? sender, EventArgs e)
        {
            // Ha a felhaszn�l� m�gsem akar bejelentkezni, l�pjen ki az alkalmaz�sb�l
            Application.Current.Shutdown();
        }

        private void UpdateBorderClip()
        {
            if (RootBorder == null) return;
            double radius = RootBorder.CornerRadius.TopLeft;
            RootBorder.Clip = new RectangleGeometry(new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight), radius, radius);
        }

        private void HookRendering()
        {
            if (_renderingHooked || _reducedMotion) return;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
            _renderingHooked = true;
        }

        private void UnhookRendering()
        {
            if (!_renderingHooked) return;
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _renderingHooked = false;
        }

        private void ApplyPerformanceModeIfNeeded()
        {
            if (!_reducedMotion) return;

            // Performance mode eset�n t�ntess�k el a nyilakat
            if (TopScrollIndicator != null) TopScrollIndicator.Visibility = Visibility.Collapsed;
            if (BottomScrollIndicator != null) BottomScrollIndicator.Visibility = Visibility.Collapsed;

            RemoveDropShadows(this);
        }

        private void RemoveDropShadows(DependencyObject parent)
        {
            if (parent is UIElement element && element.Effect is DropShadowEffect)
            {
                element.Effect = null;
            }

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                RemoveDropShadows(child);
            }
        }

        private async void ShowNotification(string message)
        {
            NotificationText.Text = message;
            var showStoryboard = (Storyboard)this.FindResource("ShowNotification");
            showStoryboard.Begin(NotificationBorder);
            await Task.Delay(4000);
            var hideStoryboard = (Storyboard)this.FindResource("HideNotification");
            hideStoryboard.Begin(NotificationBorder);
        }

        private void LoadInstallPath()
        {
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string savedPath = File.ReadAllText(settingsFilePath).Trim();
                    if (Directory.Exists(savedPath))
                    {
                        gameInstallPath = savedPath;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a be�ll�t�sok bet�lt�sekor: {ex.Message}");
            }
        }

        private async Task LoadServerVersionAsync()
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    string url = $"{VersionCheckUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    string versionString = await httpClient.GetStringAsync(url);
                    serverGameVersion = versionString.Trim();
                    Debug.WriteLine($"Szerver verzi� bet�ltve: {serverGameVersion}");
                    UpdateVersionDisplay();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a szerver verzi� bet�lt�sekor: {ex.Message}");
                serverGameVersion = "Ismeretlen";
                UpdateVersionDisplay();
            }
        }

        private void UpdateVersionDisplay()
        {
            if (VersionCurrentText != null && VersionStatusText != null && UpdateIndicator != null)
            {
                VersionCurrentText.Text = $"v{serverGameVersion}";

                string? executablePath = FindExecutable(gameInstallPath);
                bool gameInstalled = !string.IsNullOrEmpty(executablePath);

                if (gameInstalled)
                {
                    VersionStatusText.Text = "Telep�tve";
                    VersionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Z�ld
                    UpdateIndicator.Visibility = Visibility.Collapsed;
                }
                else
                {
                    VersionStatusText.Text = "Nincs telep�tve";
                    VersionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)); // Sz�rke
                    UpdateIndicator.Visibility = Visibility.Visible;
                    UpdateIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Narancss�rga
                }
            }
        }

        private void SaveInstallPath()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
                File.WriteAllText(settingsFilePath, gameInstallPath);
            }
            catch (Exception ex)
            {
                ShowNotification($"Hiba a ment�s sor�n: {ex.Message}");
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            // Kontextus men� megnyit�sa az Exit gombon (leny�l� a jobb fels� X-n�l, balra ny�lva)
            if (sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                fe.ContextMenu.PlacementTarget = fe;
                fe.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
                fe.ContextMenu.HorizontalOffset = -6;
                fe.ContextMenu.VerticalOffset = 4;
                fe.ContextMenu.IsOpen = true;
                return;
            }
        }

        private void ExitContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            // H�tt�r els�t�t�t�se
            if (DimOverlay != null)
                DimOverlay.Visibility = Visibility.Visible;
        }

        private void ExitContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            // H�tt�r vissza�ll�t�sa
            if (DimOverlay != null)
                DimOverlay.Visibility = Visibility.Collapsed;
        }

        private void ExitOnlyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void LogoutExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Kijelentkez�s gombra nyom�s...");
                
                // Teljes kijelentkez�s: t�rli a mentett adatokat
                AuthStorage.Clear();
                System.Diagnostics.Debug.WriteLine("AuthStorage t�r�lve");
                
                // Visszat�r a LoginWindow-hoz
                System.Diagnostics.Debug.WriteLine("Logout() met�dus h�v�sa...");
                Logout();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hiba a kijelentkez�s sor�n: {ex.Message}");
                MessageBox.Show($"Hiba a kijelentkez�s sor�n: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CheckGameInstallStatus()
        {
            if (string.IsNullOrEmpty(gameInstallPath))
            {
                gameInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BitFighters");
            }

            string? executablePath = FindExecutable(gameInstallPath);
            if (!string.IsNullOrEmpty(executablePath))
            {
                gameInstallPath = Path.GetDirectoryName(executablePath)!;
                ButtonText.Text = "J�T�K";
            }
            else
            {
                ButtonText.Text = "LET�LT�S";
            }

            UpdateVersionDisplay();
        }

        private async void HandleActionButton_Click(object sender, RoutedEventArgs e)
        {
            switch (ButtonText.Text)
            {
                case "LET�LT�S":
                    await DownloadAndInstallGameAsync();
                    break;
                case "J�T�K":
                    await StartGame();
                    break;
            }
        }

        private async Task DownloadAndInstallGameAsync()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = "V�lassza ki a telep�t�si mapp�t" };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                gameInstallPath = dialog.FileName;
                ActionButton.IsEnabled = false;
                ButtonText.Text = "FOLYAMATBAN";
                DownloadStatusGrid.Visibility = Visibility.Visible;
                DownloadProgressBar.IsIndeterminate = false;
                DownloadStatusText.Text = "Let�lt�s el�k�sz�t�se...";
                ProgressPercentageText.Text = "0%";
                ProgressDetailsText.Text = "";
                string tempDownloadPath = Path.Combine(Path.GetTempPath(), "game.zip");

                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        var response = await httpClient.GetAsync(GameDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                        using (var downloadStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            long receivedBytes = 0;
                            int bytesRead;
                            var stopwatch = Stopwatch.StartNew();
                            long lastReceivedBytes = 0;
                            DateTime lastUiUpdate = DateTime.Now;
                            const int uiUpdateIntervalMs = 100;

                            while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                receivedBytes += bytesRead;

                                if (totalBytes > 0)
                                {
                                    int progressPercentage = (int)((double)receivedBytes / totalBytes * 100);
                                    string detailsText = $"{(double)receivedBytes / (1024 * 1024):F1} MB / {(double)totalBytes / (1024 * 1024):F1} MB";
                                    string speedText = "";

                                    if (stopwatch.ElapsedMilliseconds > 500)
                                    {
                                        double speed = (receivedBytes - lastReceivedBytes) / stopwatch.Elapsed.TotalSeconds;
                                        speedText = $"({speed / (1024 * 1024):F2} MB/s)";
                                        lastReceivedBytes = receivedBytes;
                                        stopwatch.Restart();
                                    }

                                    if ((DateTime.Now - lastUiUpdate).TotalMilliseconds > uiUpdateIntervalMs || receivedBytes == totalBytes)
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            DownloadProgressBar.Value = progressPercentage;
                                            ProgressPercentageText.Text = $"{progressPercentage}%";
                                            ProgressDetailsText.Text = detailsText;
                                            DownloadStatusText.Text = $"Let�lt�s... {speedText}";
                                        });
                                        lastUiUpdate = DateTime.Now;
                                    }
                                }
                            }
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        DownloadStatusText.Text = "Telep�t�s...";
                        ProgressPercentageText.Text = "";
                        ProgressDetailsText.Text = "Kicsomagol�s...";
                        DownloadProgressBar.IsIndeterminate = true;
                    });

                    await InstallGameAsync(tempDownloadPath);
                }
                catch (Exception ex)
                {
                    ShowNotification($"Hiba a let�lt�s sor�n: {ex.Message}");
                }
                finally
                {
                    ActionButton.IsEnabled = true;
                    DownloadStatusGrid.Visibility = Visibility.Collapsed;
                    DownloadProgressBar.IsIndeterminate = false;
                    DownloadProgressBar.Value = 0;
                    CheckGameInstallStatus();
                }
            }
        }

        private async Task InstallGameAsync(string downloadedFilePath)
        {
            await Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(gameInstallPath);
                    ZipFile.ExtractToDirectory(downloadedFilePath, gameInstallPath, true);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => ShowNotification($"Hiba a telep�t�s sor�n: {ex.Message}"));
                }
                finally
                {
                    if (File.Exists(downloadedFilePath)) File.Delete(downloadedFilePath);
                }
            });

            if (!string.IsNullOrEmpty(FindExecutable(gameInstallPath)))
            {
                SaveInstallPath();
                ShowNotification($"A j�t�k sikeresen telep�tve! Verzi�: v{serverGameVersion}");
            }
            else
            {
                ShowNotification("Hiba: A futtathat� f�jl nem tal�lhat� a mapp�ban.");
                if (File.Exists(settingsFilePath)) File.Delete(settingsFilePath);
                gameInstallPath = string.Empty;
            }

            Dispatcher.Invoke(() =>
            {
                CheckGameInstallStatus();
            });
        }

        private string? FindExecutable(string path)
        {
            if (!Directory.Exists(path)) return null;
            try
            {
                return Directory.GetFiles(path, GameExecutableName, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (UnauthorizedAccessException) { return null; }
        }

        private async Task StartGame()
        {
            try
            {
                string? gameExecutablePath = FindExecutable(gameInstallPath);
                if (!string.IsNullOrEmpty(gameExecutablePath))
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo(gameExecutablePath)
                        {
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(gameExecutablePath)!,
                            // Pass user info as command line arguments including creation date
                            Arguments = $"-username \"{loggedInUsername}\" -userid {loggedInUserId} -created \"{loggedInUserCreatedAt}\""
                        },
                        EnableRaisingEvents = true
                    };
                    process.Exited += (sender, e) =>
                    {
                        Dispatcher.Invoke(() => this.Show());
                    };
                    process.Start();
                    this.Hide();
                }
                else
                {
                    ShowNotification("Hiba: A j�t�kf�jl nem tal�lhat�.");
                    if (File.Exists(settingsFilePath)) File.Delete(settingsFilePath);
                    gameInstallPath = string.Empty;
                    CheckGameInstallStatus();
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Hiba a j�t�k ind�t�sa sor�n: {ex.Message}");
            }
        }

        private async Task LoadNewsUpdatesAsync()
        {
            string apiUrl = "https://bitfighters.eu/api/get_news.php";
            try
            {
                using (var httpClient = new HttpClient())
                {
                    string jsonResponse = await httpClient.GetStringAsync(apiUrl);
                    System.Diagnostics.Debug.WriteLine($"News API Response: {jsonResponse}");
                    
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var updates = JsonSerializer.Deserialize<System.Collections.Generic.List<NewsUpdate>>(jsonResponse, options);
                    
                    // Ha nincs adat vagy �res a lista, adjunk hozz� teszt elemeket
                    if (updates == null || updates.Count == 0)
                    {
                        updates = new System.Collections.Generic.List<NewsUpdate>
                        {
                            new NewsUpdate 
                            { 
                                Title = "�dv�z�lj�k a BitFighters Launcher-ben!", 
                                Content = "A launcher sikeresen bet�lt�tt. Itt fognak megjelenni a legfrissebb h�rek �s friss�t�sek a j�t�kr�l.",
                                CreatedAt = DateTime.Now
                            }
                        };
                    }
                    else
                    {
                        // Ensure all items have a valid date
                        foreach (var update in updates)
                        {
                            if (update.CreatedAt == DateTime.MinValue)
                            {
                                update.CreatedAt = DateTime.Now;
                            }
                        }
                    }
                    
                    NewsItemsControl.ItemsSource = updates;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"News loading error: {ex.Message}");
                NewsItemsControl.ItemsSource = new System.Collections.Generic.List<NewsUpdate>
                {
                    new NewsUpdate 
                    { 
                        Title = "Hiba a h�rek bet�lt�sekor", 
                        Content = "Nem siker�lt el�rni a szervert: " + ex.Message,
                        CreatedAt = DateTime.Now
                    }
                };
            }
        }

        private void NewsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_reducedMotion)
            {
                double target = NewsScrollViewer.VerticalOffset - e.Delta;
                if (target < 0) target = 0;
                if (target > NewsScrollViewer.ScrollableHeight) target = NewsScrollViewer.ScrollableHeight;
                NewsScrollViewer.ScrollToVerticalOffset(target);
                e.Handled = true;
                return;
            }
            _targetVerticalOffset -= e.Delta * 0.7;
            if (_targetVerticalOffset < 0)
            {
                _targetVerticalOffset = 0;
            }
            if (_targetVerticalOffset > NewsScrollViewer.ScrollableHeight)
            {
                _targetVerticalOffset = NewsScrollViewer.ScrollableHeight;
            }
            _isScrolling = true;
            HookRendering();
            e.Handled = true;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_isScrolling && NewsScrollViewer != null)
            {
                double currentOffset = NewsScrollViewer.VerticalOffset;
                double difference = _targetVerticalOffset - currentOffset;
                if (Math.Abs(difference) < 0.5)
                {
                    NewsScrollViewer.ScrollToVerticalOffset(_targetVerticalOffset);
                    _isScrolling = false;
                    UnhookRendering();
                    return;
                }
                double step = Math.Max(Math.Abs(difference) * 0.15, 1.0);
                double newOffset = currentOffset + Math.Sign(difference) * step;
                if ((difference > 0 && newOffset > _targetVerticalOffset) || (difference < 0 && newOffset < _targetVerticalOffset))
                    newOffset = _targetVerticalOffset;
                NewsScrollViewer.ScrollToVerticalOffset(newOffset);
            }
            else
            {
                UnhookRendering();
            }
        }

        private void NewsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            TopScrollIndicator.Opacity = (e.VerticalOffset > 0) ? 1 : 0;
            BottomScrollIndicator.Opacity = (e.VerticalOffset < NewsScrollViewer.ScrollableHeight - 1) ? 1 : 0;
        }
    }
}
