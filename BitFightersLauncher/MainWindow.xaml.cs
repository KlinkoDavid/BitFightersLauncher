using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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

        // Ha nincs content, akkor a title-t használjuk rövidített verzióként
        public string ShortContent => !string.IsNullOrEmpty(Content) && Content.Length > 100
            ? Content.Substring(0, 100) + "..."
            : Title;

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

    public class UserScoreApiResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public UserScoreData? user { get; set; }
    }

    public class UserScoreData
    {
        public int id { get; set; }
        public string username { get; set; } = string.Empty;
        public int highest_score { get; set; }
    }

    public class UserRankApiResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public UserRankData? user { get; set; }
    }

    public class UserRankData
    {
        public int rank { get; set; }
        public int total_users { get; set; }
    }

    // ÚJ: Ranglista adatstruktúrák
    public class LeaderboardEntry
    {
        public int rank { get; set; }
        public int id { get; set; }
        public string username { get; set; } = string.Empty;
        public int highest_score { get; set; }
    }

    public class LeaderboardApiResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public List<LeaderboardEntry>? leaderboard { get; set; }
    }

    public partial class MainWindow : Window
    {
        private const string GameDownloadUrl = "https://bitfighters.eu/BitFighters/BitFighters.zip";
        private const string VersionCheckUrl = "https://bitfighters.eu/BitFighters/version.txt";
        private const string GameExecutableName = "BitFighters.exe";
        private const string ApiUrl = "https://bitfighters.eu/api/Launcher/main_proxy.php";

        private string gameInstallPath = string.Empty;
        private string serverGameVersion = "0.0.0";
        private readonly string settingsFilePath;

        private double _targetVerticalOffset;
        private bool _isScrolling;
        private bool _renderingHooked;
        private readonly bool _reducedMotion;

        // Cached HttpClient for better performance
        private static readonly HttpClient _httpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { { "User-Agent", "BitFighters-Launcher/1.0" } }
        };

        // User info
        private string loggedInUsername = string.Empty;
        private int loggedInUserId = 0;
        private string loggedInUserCreatedAt = string.Empty;
        private int loggedInUserHighestScore = 0;
        private int loggedInUserRanking = 0;

        // Navigation state
        private string currentView = "home";
        private double _targetNavIndicatorY;
        private bool _isNavigating;

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
                ShowHomeView();
                AttachNavButtonHoverEvents(); // Hover effectek engedélyezése újra
                if (NavIndicatorTransform != null)
                {
                    NavIndicatorTransform.Y = 0;
                }

                // Biztosítjuk, hogy a fő gomb látható és működőképes legyen
                if (ActionButton != null)
                {
                    ActionButton.Visibility = Visibility.Visible;
                    ActionButton.IsEnabled = true;
                    Debug.WriteLine($"ActionButton beállítva: Visible={ActionButton.Visibility}, Enabled={ActionButton.IsEnabled}");
                }
                if (MainContentGrid != null)
                {
                    MainContentGrid.Visibility = Visibility.Visible;
                    Debug.WriteLine($"MainContentGrid beállítva: Visible={MainContentGrid.Visibility}");
                }
            };
        }

        public void SetUserInfo(string username, int userId, string createdAt = "")
        {
            loggedInUsername = username;
            loggedInUserId = userId;
            loggedInUserCreatedAt = createdAt;
            this.Title = $"BitFighters Launcher - {username}";
            if (UsernameText != null)
            {
                UsernameText.Text = username;
            }
            Debug.WriteLine($"Bejelentkezett felhasználó: {username} (ID: {userId})");
            LoadUserProfile();
        }

        private async void LoadUserProfile()
        {
            try
            {
                int userScore = await GetUserScoreFromServerAsync();
                loggedInUserHighestScore = userScore >= 0 ? userScore : 0;

                int userRank = await GetUserRankFromServerAsync();
                loggedInUserRanking = userRank >= 0 ? userRank : 0;

                if (currentView == "profile")
                {
                    UpdateProfileView();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a profil betöltésekor: {ex.Message}");
                loggedInUserHighestScore = 0;
                loggedInUserRanking = 0;
            }
        }

        private async Task<int> GetUserScoreFromServerAsync()
        {
            try
            {
                var requestData = new { action = "get_user_score", username = loggedInUsername };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(ApiUrl, content);
                string responseText = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<UserScoreApiResponse>(responseText);
                return apiResponse?.success == true && apiResponse.user != null ? apiResponse.user.highest_score : -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a pontszám lekérdezésekor: {ex.Message}");
                return -1;
            }
        }

        private async Task<int> GetUserRankFromServerAsync()
        {
            try
            {
                var requestData = new { action = "get_user_rank", username = loggedInUsername };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(ApiUrl, content);
                string responseText = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<UserRankApiResponse>(responseText);
                return apiResponse?.success == true && apiResponse.user != null ? apiResponse.user.rank : -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a rangsor lekérdezésekor: {ex.Message}");
                return -1;
            }
        }

        public async Task<bool> UpdateUserScoreAsync(int newScore)
        {
            try
            {
                var requestData = new { action = "update_user_score", user_id = loggedInUserId, new_score = newScore };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(ApiUrl, content);
                string responseText = await response.Content.ReadAsStringAsync();
                var apiResponse = JsonSerializer.Deserialize<UserScoreApiResponse>(responseText);
                if (apiResponse?.success == true)
                {
                    loggedInUserHighestScore = newScore;
                    if (currentView == "profile") UpdateProfileView();
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a pontszám frissítésekor: {ex.Message}");
                return false;
            }
        }

        public async Task RefreshUserScoreAsync()
        {
            try
            {
                int currentScore = await GetUserScoreFromServerAsync();
                if (currentScore >= 0)
                {
                    loggedInUserHighestScore = currentScore;
                }

                int currentRank = await GetUserRankFromServerAsync();
                if (currentRank >= 0)
                {
                    loggedInUserRanking = currentRank;
                }

                if (currentView == "profile") UpdateProfileView();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a pontszám frissítésekor: {ex.Message}");
            }
        }

        public void Logout()
        {
            try
            {
                var originalShutdownMode = Application.Current.ShutdownMode;
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var loginWindow = new LoginWindow();
                loginWindow.LoginSucceeded += LoginWindow_LoginSucceeded;
                loginWindow.LoginCancelled += LoginWindow_LoginCancelled;
                Application.Current.MainWindow = loginWindow;
                loginWindow.Show();
                Application.Current.ShutdownMode = originalShutdownMode;
                var fadeOutAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                fadeOutAnimation.Completed += (s, a) => { this.Close(); };
                this.BeginAnimation(Window.OpacityProperty, fadeOutAnimation);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hiba a kijelentkezés során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoginWindow_LoginSucceeded(object? sender, LoginEventArgs e)
        {
            try
            {
                var mainWindow = new MainWindow();
                mainWindow.SetUserInfo(e.Username, e.UserId, e.UserCreatedAt);
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hiba az újra bejelentkezésnél: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void LoginWindow_LoginCancelled(object? sender, EventArgs e)
        {
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

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            bool stillAnimating = false;

            if (_isScrolling && NewsScrollViewer != null)
            {
                double currentOffset = NewsScrollViewer.VerticalOffset;
                double difference = _targetVerticalOffset - currentOffset;
                if (Math.Abs(difference) < 0.5)
                {
                    NewsScrollViewer.ScrollToVerticalOffset(_targetVerticalOffset);
                    _isScrolling = false;
                }
                else
                {
                    double step = Math.Max(Math.Abs(difference) * 0.15, 1.0);
                    NewsScrollViewer.ScrollToVerticalOffset(currentOffset + Math.Sign(difference) * step);
                    stillAnimating = true;
                }
            }

            if (_isNavigating && NavIndicatorTransform != null)
            {
                double currentY = NavIndicatorTransform.Y;
                double difference = _targetNavIndicatorY - currentY;
                if (Math.Abs(difference) < 0.5)
                {
                    NavIndicatorTransform.Y = _targetNavIndicatorY;
                    _isNavigating = false;
                }
                else
                {
                    double step = Math.Max(Math.Abs(difference) * 0.20, 0.5);
                    NavIndicatorTransform.Y = currentY + Math.Sign(difference) * step;
                    stillAnimating = true;
                }
            }

            if (!stillAnimating) UnhookRendering();
        }

        private void ApplyPerformanceModeIfNeeded()
        {
            if (!_reducedMotion) return;
            if (TopScrollIndicator != null) TopScrollIndicator.Visibility = Visibility.Collapsed;
            if (BottomScrollIndicator != null) BottomScrollIndicator.Visibility = Visibility.Collapsed;
            if (NavIndicator?.Effect is DropShadowEffect navGlow)
            {
                navGlow.BlurRadius = 6;
                navGlow.Opacity = 0.8;
            }
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
                    if (Directory.Exists(savedPath)) gameInstallPath = savedPath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a beállítások betöltésekor: {ex.Message}");
            }
        }

        private async Task LoadServerVersionAsync()
        {
            try
            {
                string url = $"{VersionCheckUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                string versionString = await _httpClient.GetStringAsync(url);
                serverGameVersion = versionString.Trim();
                UpdateVersionDisplay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a szerver verzió betöltésekor: {ex.Message}");
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
                    VersionStatusText.Text = "Telepítve";
                    VersionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    UpdateIndicator.Visibility = Visibility.Collapsed;
                }
                else
                {
                    VersionStatusText.Text = "Nincs telepítve";
                    VersionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204));
                    UpdateIndicator.Visibility = Visibility.Visible;
                    UpdateIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0));
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
                ShowNotification($"Hiba a mentés során: {ex.Message}");
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.ContextMenu != null)
            {
                fe.ContextMenu.PlacementTarget = fe;
                fe.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
                fe.ContextMenu.HorizontalOffset = -6;
                fe.ContextMenu.VerticalOffset = 4;
                fe.ContextMenu.IsOpen = true;
            }
        }

        private void ExitContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (DimOverlay != null) DimOverlay.Visibility = Visibility.Visible;
        }

        private void ExitContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            if (DimOverlay != null) DimOverlay.Visibility = Visibility.Collapsed;
        }

        private void ExitOnlyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void LogoutExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AuthStorage.Clear();
                Logout();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hiba a kijelentkezés során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CheckGameInstallStatus()
        {
            if (string.IsNullOrEmpty(gameInstallPath))
            {
                gameInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BitFighters");
            }

            string? executablePath = FindExecutable(gameInstallPath);
            bool gameInstalled = !string.IsNullOrEmpty(executablePath);

            if (ButtonText != null)
            {
                if (gameInstalled)
                {
                    gameInstallPath = Path.GetDirectoryName(executablePath)!;
                    ButtonText.Text = "JÁTÉK";
                    Debug.WriteLine($"Játék telepítve: {executablePath}");
                }
                else
                {
                    ButtonText.Text = "LETÖLTÉS";
                    Debug.WriteLine("Játék nincs telepítve, letöltés szükséges");
                }
            }

            // ActionButton láthatóságának biztosítása
            if (ActionButton != null && currentView == "home")
            {
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.IsEnabled = true;
                Debug.WriteLine($"CheckGameInstallStatus - ActionButton: {ActionButton.Visibility}, Enabled: {ActionButton.IsEnabled}");
            }

            UpdateVersionDisplay();
        }

        private async void HandleActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentView != "home") return;
            switch (ButtonText.Text)
            {
                case "LETÖLTÉS":
                    await DownloadAndInstallGameAsync();
                    break;
                case "JÁTÉK":
                    await StartGame();
                    break;
            }
        }

        private async Task DownloadAndInstallGameAsync()
        {
            if (currentView != "home") return;

            var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = "Válassza ki a telepítési mappát" };
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                gameInstallPath = dialog.FileName;

                // Letöltés indítása - UI frissítés
                if (ActionButton != null)
                {
                    ActionButton.IsEnabled = false;
                    Debug.WriteLine("Letöltés indítása - ActionButton letiltva");
                }
                if (ButtonText != null)
                {
                    ButtonText.Text = "LETÖLTÉS...";
                    Debug.WriteLine("ButtonText frissítve: LETÖLTÉS...");
                }

                string tempDownloadPath = Path.Combine(Path.GetTempPath(), "BitFighters_game.zip");

                try
                {
                    ShowNotification("Játék letöltése elkezdődött...");
                    Debug.WriteLine($"Letöltés indul: {GameDownloadUrl} -> {tempDownloadPath}");

                    // Progress tracking
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromMinutes(10); // 10 perc timeout

                        var response = await client.GetAsync(GameDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        Debug.WriteLine($"Fájl mérete: {totalBytes} bytes");

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = File.Create(tempDownloadPath))
                        {
                            var buffer = new byte[8192];
                            long totalBytesRead = 0;
                            int bytesRead;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                totalBytesRead += bytesRead;

                                if (totalBytes > 0)
                                {
                                    var progress = (int)((totalBytesRead * 100) / totalBytes);
                                    if (ButtonText != null)
                                    {
                                        ButtonText.Text = $"LETÖLTÉS {progress}%";
                                    }
                                    Debug.WriteLine($"Letöltési progress: {progress}% ({totalBytesRead}/{totalBytes})");
                                }
                            }
                        }
                    }

                    Debug.WriteLine("Letöltés befejezve, telepítés indítása...");
                    if (ButtonText != null) ButtonText.Text = "TELEPÍTÉS...";
                    ShowNotification("Letöltés befejezve, telepítés...");

                    await InstallGameAsync(tempDownloadPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hiba a letöltés során: {ex.Message}");
                    ShowNotification($"Hiba a letöltés során: {ex.Message}");
                }
                finally
                {
                    // UI visszaállítása
                    if (ActionButton != null)
                    {
                        ActionButton.IsEnabled = true;
                        Debug.WriteLine("ActionButton újra engedélyezve");
                    }
                    CheckGameInstallStatus();
                    Debug.WriteLine("Letöltési folyamat befejezve");
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
                    Dispatcher.Invoke(() => ShowNotification($"Hiba a telepítés során: {ex.Message}"));
                }
                finally
                {
                    if (File.Exists(downloadedFilePath)) File.Delete(downloadedFilePath);
                }
            });

            if (!string.IsNullOrEmpty(FindExecutable(gameInstallPath)))
            {
                SaveInstallPath();
                ShowNotification($"A játék sikeresen telepítve! Verzió: v{serverGameVersion}");
            }
            else
            {
                ShowNotification("Hiba: A futtatható fájl nem található a mappában.");
                gameInstallPath = string.Empty;
            }
            CheckGameInstallStatus();
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
                            Arguments = $"-username \"{loggedInUsername}\" -userid {loggedInUserId} -created \"{loggedInUserCreatedAt}\""
                        },
                        EnableRaisingEvents = true
                    };
                    process.Exited += (sender, e) => Dispatcher.Invoke(() => this.Show());
                    process.Start();
                    this.Hide();
                }
                else
                {
                    ShowNotification("Hiba: A játékfájl nem található.");
                    gameInstallPath = string.Empty;
                    CheckGameInstallStatus();
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Hiba a játék indítása során: {ex.Message}");
            }
        }

        private async Task LoadNewsUpdatesAsync()
        {
            try
            {
                Debug.WriteLine($"Loading news from: {ApiUrl}");

                var requestData = new { action = "get_news", limit = 20 };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(ApiUrl, content);
                string responseText = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"Response status: {response.StatusCode}");
                Debug.WriteLine($"Response content: {responseText}");

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var updates = JsonSerializer.Deserialize<List<NewsUpdate>>(responseText, options);

                if (updates == null || updates.Count == 0)
                {
                    updates = new List<NewsUpdate>
                    {
                        new NewsUpdate
                        {
                            Title = "Üdvözöljük a BitFighters Launcher-ben!",
                            Content = "A launcher sikeresen betöltött. Itt fognak megjelenni a legfrissebb hírek és frissítések a játékról.",
                            CreatedAt = DateTime.Now
                        }
                    };
                }

                NewsItemsControl.ItemsSource = updates;
                Debug.WriteLine($"News loaded successfully: {updates.Count} items");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadNewsUpdatesAsync failed: {ex}");
                NewsItemsControl.ItemsSource = new List<NewsUpdate>
                {
                    new NewsUpdate
                    {
                        Title = "Hiba a hírek betöltésekor",
                        Content = $"Nem sikerült elérni a szervert: {ex.Message}",
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
            if (_targetVerticalOffset < 0) _targetVerticalOffset = 0;
            if (_targetVerticalOffset > NewsScrollViewer.ScrollableHeight) _targetVerticalOffset = NewsScrollViewer.ScrollableHeight;
            _isScrolling = true;
            HookRendering();
            e.Handled = true;
        }

        private void NewsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            TopScrollIndicator.Opacity = (e.VerticalOffset > 0) ? 1 : 0;
            BottomScrollIndicator.Opacity = (e.VerticalOffset < NewsScrollViewer.ScrollableHeight - 1) ? 1 : 0;
        }

        private void ResetNavButtonStates()
        {
            // Minden gomb visszaállítása alapértelmezett állapotra
            if (HomeNavButton != null)
            {
                HomeNavButton.Tag = null;
                HomeNavButton.ClearValue(Button.RenderTransformProperty); // Transform törlése
            }
            if (SettingsNavButton != null)
            {
                SettingsNavButton.Tag = null;
                SettingsNavButton.ClearValue(Button.RenderTransformProperty);
            }
            if (StarNavButton != null)
            {
                StarNavButton.Tag = null;
                StarNavButton.ClearValue(Button.RenderTransformProperty);
            }
            if (ProfileNavButton != null)
            {
                ProfileNavButton.Tag = null;
                ProfileNavButton.ClearValue(Button.RenderTransformProperty);
            }
            if (DownloadNavButton != null)
            {
                DownloadNavButton.Tag = null;
                DownloadNavButton.ClearValue(Button.RenderTransformProperty);
            }
        }

        private void ShowHomeView()
        {
            currentView = "home";
            ResetNavButtonStates();
            if (HomeNavButton != null) HomeNavButton.Tag = "Active";

            // Biztosítjuk, hogy a fő tartalom látható legyen
            if (ActionButton != null)
            {
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.IsEnabled = true;
                Debug.WriteLine($"ShowHomeView - ActionButton: Visible={ActionButton.Visibility}, Enabled={ActionButton.IsEnabled}");
            }
            if (MainContentGrid != null)
            {
                MainContentGrid.Visibility = Visibility.Visible;
                Debug.WriteLine($"ShowHomeView - MainContentGrid: Visible={MainContentGrid.Visibility}");
            }
            if (NewsPanelBorder != null) NewsPanelBorder.Visibility = Visibility.Visible;
            if (ProfileViewGrid != null) ProfileViewGrid.Visibility = Visibility.Collapsed;
            if (LeaderboardViewGrid != null) LeaderboardViewGrid.Visibility = Visibility.Collapsed; // Ranglista elrejtése

            AnimateNavIndicator(0);
        }

        private void ShowProfileView()
        {
            currentView = "profile";
            ResetNavButtonStates();
            if (ProfileNavButton != null) ProfileNavButton.Tag = "Active";
            if (ActionButton != null) ActionButton.Visibility = Visibility.Collapsed;
            if (MainContentGrid != null) MainContentGrid.Visibility = Visibility.Collapsed; // Fő tartalom elrejtése
            if (NewsPanelBorder != null) NewsPanelBorder.Visibility = Visibility.Collapsed;
            if (LeaderboardViewGrid != null) LeaderboardViewGrid.Visibility = Visibility.Collapsed; // Ranglista elrejtése
            if (ProfileViewGrid != null)
            {
                ProfileViewGrid.Visibility = Visibility.Visible;
                UpdateProfileView();
            }
            AnimateNavIndicator(3);
        }

        // ÚJ: Ranglista nézet megjelenítése
        private void ShowLeaderboardView()
        {
            currentView = "leaderboard";
            ResetNavButtonStates();
            if (StarNavButton != null) StarNavButton.Tag = "Active";

            if (MainContentGrid != null) MainContentGrid.Visibility = Visibility.Collapsed;
            if (NewsPanelBorder != null) NewsPanelBorder.Visibility = Visibility.Collapsed;
            if (ProfileViewGrid != null) ProfileViewGrid.Visibility = Visibility.Collapsed;
            if (LeaderboardViewGrid != null) LeaderboardViewGrid.Visibility = Visibility.Visible;

            AnimateNavIndicator(2); // Indikátor a Star gombra (index 2)
            LoadLeaderboardAsync();
        }

        // ÚJ: Ranglista adatainak aszinkron betöltése
        private async Task LoadLeaderboardAsync()
        {
            try
            {
                var requestData = new { action = "get_leaderboard", limit = 15 };
                string jsonContent = JsonSerializer.Serialize(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(ApiUrl, content);
                string responseText = await response.Content.ReadAsStringAsync();

                var apiResponse = JsonSerializer.Deserialize<LeaderboardApiResponse>(responseText);

                if (apiResponse?.success == true && apiResponse.leaderboard != null)
                {
                    if (LeaderboardItemsControl != null)
                    {
                        LeaderboardItemsControl.ItemsSource = apiResponse.leaderboard;
                    }
                }
                else
                {
                    Debug.WriteLine($"Sikertelen ranglista betöltés: {apiResponse?.message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a ranglista betöltésekor: {ex.Message}");
            }
        }


        private void AnimateNavIndicator(int buttonIndex)
        {
            if (NavIndicatorTransform == null) return;
            double targetY = buttonIndex * 124;
            if (_reducedMotion)
            {
                NavIndicatorTransform.Y = targetY;
                return;
            }
            if (Math.Abs(NavIndicatorTransform.Y - targetY) < 1.0) return;
            _targetNavIndicatorY = targetY;
            _isNavigating = true;
            HookRendering();
        }

        private void UpdateProfileView()
        {
            if (ProfileViewGrid?.Visibility != Visibility.Visible) return;
            if (ProfileUsernameText != null) ProfileUsernameText.Text = loggedInUsername;
            if (ProfileHighestScoreText != null) ProfileHighestScoreText.Text = loggedInUserHighestScore.ToString();
            if (ProfileUserRankText != null)
            {
                if (loggedInUserRanking > 0)
                    ProfileUserRankText.Text = $"#{loggedInUserRanking}";
                else
                    ProfileUserRankText.Text = "N/A";
            }
            if (ProfileRankText != null) ProfileRankText.Text = GetUserRank(loggedInUserHighestScore);
        }

        private string GetUserRank(int score)
        {
            if (score >= 1000) return "💎 Diamond";
            if (score >= 700) return "🔷 Platinum";
            if (score >= 500) return "🥇 Gold";
            if (score >= 300) return "🥈 Silver";
            if (score >= 100) return "🥉 Bronz";
            return "🎮 Kezdő";
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ShowProfileView();
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHomeView();
        }

        // ÚJ: Star gomb eseménykezelője
        private void StarButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLeaderboardView();
        }

        // Navigációs gombok hover effect kezelése - egyszerûsített verzió
        private void NavButton_MouseEnter(object sender, MouseEventArgs e)
        {
            // XAML kezeli alapból a hover effectet minden gombra
        }

        private void NavButton_MouseLeave(object sender, MouseEventArgs e)
        {
            // XAML kezeli alapból a hover effectet minden gombra
        }

        // Event handler hozzárendelés - most bekapcsoljuk a hover effecteket
        private void AttachNavButtonHoverEvents()
        {
            // Hover effectek hozzáadása minden navigációs gombhoz - XAML-ben is működni fog
            if (HomeNavButton != null)
            {
                HomeNavButton.MouseEnter += (s, e) => { /* XAML kezeli */ };
                HomeNavButton.MouseLeave += (s, e) => { /* XAML kezeli */ };
            }
            if (SettingsNavButton != null)
            {
                SettingsNavButton.MouseEnter += (s, e) => { /* XAML kezeli */ };
                SettingsNavButton.MouseLeave += (s, e) => { /* XAML kezeli */ };
            }
            if (StarNavButton != null)
            {
                StarNavButton.MouseEnter += (s, e) => { /* XAML kezeli */ };
                StarNavButton.MouseLeave += (s, e) => { /* XAML kezeli */ };
            }
            if (ProfileNavButton != null)
            {
                ProfileNavButton.MouseEnter += (s, e) => { /* XAML kezeli */ };
                ProfileNavButton.MouseLeave += (s, e) => { /* XAML kezeli */ };
            }
            if (DownloadNavButton != null)
            {
                DownloadNavButton.MouseEnter += (s, e) => { /* XAML kezeli */ };
                DownloadNavButton.MouseLeave += (s, e) => { /* XAML kezeli */ };
            }
        }

        // Dispose static HttpClient when app closes
        protected override void OnClosed(EventArgs e)
        {
            _httpClient?.Dispose();
            base.OnClosed(e);
        }
    }
}