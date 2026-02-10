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
using System.Windows.Threading;

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
        private const string GameDownloadUrl = "https://bitfighters.eu/BitFighters.zip";
        private const string VersionCheckUrl = "https://bitfighters.eu/version.txt";
        private const string GameExecutableName = "BitFighters.exe";
        private const string ApiUrl = "https://bitfighters.eu/backend/Launcher/main_proxy.php";

        private string gameInstallPath = string.Empty;
        private string serverGameVersion = "0.0.0";
        private string localGameVersion = "0.0.0";
        private readonly string settingsFilePath;

        // Optimalizált scroll változók - DispatcherTimer használata
        private DispatcherTimer? _scrollTimer;
        private double _targetVerticalOffset;
        private readonly bool _reducedMotion;
        private bool _isCompactHeaderVisible;
        private ScrollViewer? _mainScrollViewer;
        private Border? _compactHeaderBar;

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

        // Navigation state - egyszer?sített
        private string currentView = "home";

        private TaskCompletionSource<bool> _downloadPauseSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _isDownloadPaused;
        private bool _isDownloadInProgress;
        private int _lastDownloadPercentage;
        private string _currentDownloadStatusText = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            _downloadPauseSource.TrySetResult(true);
            SetPauseResumeButtonState(false, false);

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string launcherDataPath = Path.Combine(appDataPath, "BitFightersLauncher");
            settingsFilePath = Path.Combine(launcherDataPath, "settings.txt");

            // Teljesítmény ellen?rzés
            _reducedMotion = (RenderCapability.Tier >> 16) < 2 || SystemParameters.HighContrast;

            // Egyszer?sített inicializálás
            Loaded += MainWindow_Loaded;
            SizeChanged += (s, e) => UpdateBorderClip();

            // Optimalizált timerek inicializálása
            InitializeTimers();
        }

        private void InitializeTimers()
        {
            // Scroll timer optimalizálása
            _scrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_reducedMotion ? 50 : 16) // 60 FPS normál esetben, 20 FPS gyenge gépen
            };
            _scrollTimer.Tick += ScrollTimer_Tick;

        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateBorderClip();
            LoadInstallPath();
            
            // Párhuzamos betöltések optimalizálása
            var tasks = new List<Task>
            {
                LoadServerVersionAsync(),
                LoadNewsUpdatesAsync()
            };
            
            await Task.WhenAll(tasks);
            
            CheckGameInstallStatus();
            
            // Automatikus frissítés ellen?rzése
            _ = CheckForUpdatesAutomatically();
            
            ApplyPerformanceModeIfNeeded();
            ShowHomeView();

            _mainScrollViewer = MainScrollViewer;
            _compactHeaderBar = CompactHeaderBar;

            if (_mainScrollViewer != null)
            {
                _mainScrollViewer.ScrollChanged += MainScrollViewer_ScrollChanged;
            }

            Dispatcher.BeginInvoke(new Action(UpdateCompactHeaderVisibility), DispatcherPriority.Background);
            
            // Biztosítjuk, hogy a f? gomb látható és m?köd?képes legyen
            if (ActionButton != null)
            {
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.IsEnabled = true;
            }
            if (MainContentGrid != null)
            {
                MainContentGrid.Visibility = Visibility.Visible;
            }
        }

        public void SetUserInfo(string username, int userId, string createdAt = "")
        {
            loggedInUsername = username;
            loggedInUserId = userId;
            loggedInUserCreatedAt = createdAt;
            this.Title = $"BitFighters Launcher - {username}";
            
            if (UsernameText != null)
                UsernameText.Text = username;
            
            // Nem blokkoló profil betöltés
            _ = LoadUserProfileAsync();
        }

        private async Task LoadUserProfileAsync()
        {
            try
            {
                var scoreTask = GetUserScoreFromServerAsync();
                var rankTask = GetUserRankFromServerAsync();
                
                await Task.WhenAll(scoreTask, rankTask);
                
                loggedInUserHighestScore = Math.Max(0, await scoreTask);
                loggedInUserRanking = Math.Max(0, await rankTask);

                // UI frissítés csak ha szükséges
                if (currentView == "profile")
                {
                    Dispatcher.Invoke(UpdateProfileView);
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
                var scoreTask = GetUserScoreFromServerAsync();
                var rankTask = GetUserRankFromServerAsync();
                
                await Task.WhenAll(scoreTask, rankTask);
                
                var currentScore = await scoreTask;
                var currentRank = await rankTask;
                
                if (currentScore >= 0) loggedInUserHighestScore = currentScore;
                if (currentRank >= 0) loggedInUserRanking = currentRank;

                if (currentView == "profile") 
                    UpdateProfileView();
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
                
                // Egyszer?sített fade out
                var fadeOutAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
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

        // Optimalizált timer alapú scroll kezelés
        private void ScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (NewsScrollViewer == null) return;
            
            double currentOffset = NewsScrollViewer.HorizontalOffset;
            double difference = _targetVerticalOffset - currentOffset;
            
            if (Math.Abs(difference) < 1.0)
            {
                NewsScrollViewer.ScrollToHorizontalOffset(_targetVerticalOffset);
                _scrollTimer?.Stop();
            }
            else
            {
                double step = _reducedMotion ? Math.Sign(difference) * 5 : Math.Max(Math.Abs(difference) * 0.15, 1.0);
                NewsScrollViewer.ScrollToHorizontalOffset(currentOffset + Math.Sign(difference) * step);
            }
        }

        private void ApplyPerformanceModeIfNeeded()
        {
            if (!_reducedMotion) return;
            
            // Scroll indikátorok eltávolítása gyenge gépeken
            // if (TopScrollIndicator != null) TopScrollIndicator.Visibility = Visibility.Collapsed;
            // if (BottomScrollIndicator != null) BottomScrollIndicator.Visibility = Visibility.Collapsed;
            
            // TextBlock rendering optimalizálás kikapcsolása gyenge gépen
            OptimizeTextRendering(this);
        }

        private void OptimizeTextRendering(DependencyObject parent)
        {
            if (parent is TextBlock textBlock)
            {
                // Egyszer? text rendering gyenge gépeken
                textBlock.ClearValue(TextOptions.TextFormattingModeProperty);
                textBlock.ClearValue(TextOptions.TextRenderingModeProperty);
                textBlock.ClearValue(TextOptions.TextHintingModeProperty);
            }
            
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                OptimizeTextRendering(child);
            }
        }

        private async void ShowNotification(string message)
        {
            if (NotificationText == null || NotificationBorder == null) return;
            
            NotificationText.Text = message;
            var showStoryboard = (Storyboard)this.FindResource("ShowNotification");
            showStoryboard.Begin(NotificationBorder);
            
            // Async delay
            await Task.Delay(3000); // Rövidebb id?tartam
            
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

        // VERZIÓ KEZELÉS - JAVÍTOTT VÁLTOZATOK
        private string GetLocalGameVersion()
        {
            try
            {
                string? executablePath = FindExecutable(gameInstallPath);
                if (!string.IsNullOrEmpty(executablePath))
                {
                    // El?ször megpróbáljuk egy lokális verzió fájlból olvasni
                    string localVersionFile = Path.Combine(gameInstallPath, "version.txt");
                    if (File.Exists(localVersionFile))
                    {
                        string localVersion = File.ReadAllText(localVersionFile).Trim();
                        Debug.WriteLine($"Helyi verzió fájlból: '{localVersion}'");
                        return localVersion;
                    }

                    // Ha nincs lokális verzió fájl, akkor a fájl módosítási dátumát használjuk
                    var fileInfo = new FileInfo(executablePath);
                    string dateVersion = fileInfo.LastWriteTime.ToString("yyyy.MM.dd");
                    Debug.WriteLine($"Helyi verzió dátum alapján: '{dateVersion}' ({executablePath})");
                    return dateVersion;
                }
                else
                {
                    Debug.WriteLine("Játék executable nem található");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a helyi verzió lekérdezésekor: {ex.Message}");
            }
            return "0.0.0";
        }

        private bool IsUpdateAvailable()
        {
            localGameVersion = GetLocalGameVersion();
            
            Debug.WriteLine($"Verzió összehasonlítás: Helyi='{localGameVersion}', Szerver='{serverGameVersion}'");
            
            // Ha nincs telepítve a játék, akkor nem frissítésról van szó
            if (localGameVersion == "0.0.0" || string.IsNullOrEmpty(FindExecutable(gameInstallPath)))
            {
                Debug.WriteLine("Játék nincs telepítve - nem frissítés szükséges");
                return false;
            }
            
            // Ha a szerver verzió ismeretlen vagy hibás
            if (string.IsNullOrEmpty(serverGameVersion) || serverGameVersion == "Ismeretlen")
            {
                Debug.WriteLine("Szerver verzió ismeretlen - nem frissítés szükséges");
                return false;
            }
            
            // Egyszer? string összehasonlítás - ha különböznek, akkor frissítés szükséges
            bool updateNeeded = !string.Equals(localGameVersion, serverGameVersion, StringComparison.OrdinalIgnoreCase);
            Debug.WriteLine($"Verzió összehasonlítás eredménye - Frissítés szükséges: {updateNeeded}");
            return updateNeeded;
        }

        private async Task LoadServerVersionAsync()
        {
            try
            {
                // Cache elkerülése különböz? módszerekkel
                string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                string url = $"{VersionCheckUrl}?t={timestamp}&v={Guid.NewGuid():N}";
                
                Debug.WriteLine($"Szerver verzió lekérdezése: {url}");
                
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue()
                    {
                        NoCache = true,
                        NoStore = true
                    };
                    client.DefaultRequestHeaders.Add("Pragma", "no-cache");
                    
                    string versionString = await client.GetStringAsync(url);
                    serverGameVersion = versionString.Trim();
                    
                    Debug.WriteLine($"Szerver verzió betöltve: '{serverGameVersion}'");
                }
                
                Dispatcher.Invoke(UpdateVersionDisplay);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a szerver verzió betöltésekor: {ex.Message}");
                serverGameVersion = "Ismeretlen";
                Dispatcher.Invoke(UpdateVersionDisplay);
            }
        }

        private void UpdateVersionDisplay()
        {
            if (VersionCurrentText == null || VersionStatusText == null || UpdateIndicator == null)
                return;

            Debug.WriteLine($"UI frissítése - Szerver: '{serverGameVersion}'");

            // Csak a szerver verziót jelenítjük meg
            VersionCurrentText.Text = $"v{serverGameVersion}";
            string? executablePath = FindExecutable(gameInstallPath);
            bool gameInstalled = !string.IsNullOrEmpty(executablePath);

            Debug.WriteLine($"Játék telepítve: {gameInstalled}");

            if (gameInstalled)
            {
                localGameVersion = GetLocalGameVersion();
                bool updateAvailable = IsUpdateAvailable();
                
                Debug.WriteLine($"Frissítés elérhető: {updateAvailable}");
                
                if (updateAvailable)
                {
                    VersionStatusText.Text = "Frissítés elérhető";
                    VersionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 167, 38));
                    UpdateIndicator.Visibility = Visibility.Visible;
                    UpdateIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 167, 38));
                    
                    // Change button text to indicate update
                    if (ButtonText != null)
                    {
                        ButtonText.Text = "FRISSÍTÉS";
                        Debug.WriteLine("Gomb szöveg frissítve: FRISSÍTÉS");
                    }
                    if (CompactActionButton != null)
                    {
                        CompactActionButton.Content = "FRISSÍTÉS";
                    }
                }
                else
                {
                    VersionStatusText.Text = "Naprakész";
                    VersionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                    UpdateIndicator.Visibility = Visibility.Collapsed;
                    
                    if (ButtonText != null)
                    {
                        ButtonText.Text = "JÁTÉK";
                        Debug.WriteLine("Gomb szöveg frissítve: JÁTÉK");
                    }
                    if (CompactActionButton != null)
                    {
                        CompactActionButton.Content = "JÁTÉK";
                    }
                }
            } 
            else
            {
                VersionStatusText.Text = "Nincs telepítve";
                VersionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                UpdateIndicator.Visibility = Visibility.Visible;
                UpdateIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 167, 38));
                
                if (ButtonText != null)
                {
                    ButtonText.Text = "LETÖLTÉS";
                    Debug.WriteLine("Gomb szöveg frissítve: LETÖLTÉS");
                }
                if (CompactActionButton != null)
                {
                    CompactActionButton.Content = "LETÖLTÉS";
                }
            }
            
            UpdateActionButtonIcon();
        }

        private async Task CheckForUpdatesAutomatically()
        {
            try
            {
                Debug.WriteLine("Automatikus frissítés ellenörzés kezdése...");
                
                // El?ször betöltjük a szerver verziót
                await LoadServerVersionAsync();
                
                // Várakozás a UI frissítésre
                await Task.Delay(100);
                
                if (IsUpdateAvailable())
                {
                    Debug.WriteLine("Frissítés elérhető - csak UI frissítés, dialógus nélkül");
                    
                    // Eltávolítottuk a MessageBox dialógust
                    // Csak csendes frissítés történik az UI-ban
                    ShowNotification($"Új verzió elérhető: v{serverGameVersion}");
                }
                else
                {
                    Debug.WriteLine("Nincs elérhető frissítés");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba az automatikus frissítés ellenőrzésekor: {ex.Message}");
            }
        }

        public async Task ManualVersionCheckAsync()
        {
            try
            {
                Debug.WriteLine("Manuális verzió ellenőrzés...");
                ShowNotification("Verzió ellenőrzése...");
                
                await LoadServerVersionAsync();
                
                if (IsUpdateAvailable())
                {
                    ShowNotification($"Új verzió elérhető: v{serverGameVersion}");
                }
                else
                {
                    ShowNotification("A játék naprakész!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a manuális verzió ellenőrzésekor: {ex.Message}");
                ShowNotification("Hiba a verzió ellenőrzése során!");
            }
        }

        private async void VersionDisplay_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Debug.WriteLine("=== MANUÁLIS VERZIÓ ELLEN?RZÉS INDÍTÁSA ===");
                await ManualVersionCheckAsync();
                
                // Debug információk megjelenítése a felhasználónak
                string debugInfo = $"Debug információk:\n\n" +
                                  $"Játék mappa: {gameInstallPath}\n" +
                                  $"Helyi verzió: {localGameVersion}\n" +
                                  $"Szerver verzió: {serverGameVersion}\n" +
                                  $"Frissítés szükséges: {IsUpdateAvailable()}\n" +
                                  $"Executable található: {!string.IsNullOrEmpty(FindExecutable(gameInstallPath))}\n\n" +
                                  $"Helyi verzió fájl: {Path.Combine(gameInstallPath, "version.txt")}\n" +
                                  $"Verzió fájl létezik: {File.Exists(Path.Combine(gameInstallPath, "version.txt"))}";
                
                var result = MessageBox.Show(debugInfo + "\n\nSzeretné kényszeríteni a verzió újra ellenőrzését?", 
                    "Verzió információk", MessageBoxButton.YesNo, MessageBoxImage.Information);
                
                if (result == MessageBoxResult.Yes)
                {
                    // Kényszerítjük az újra ellen?rzést
                    Debug.WriteLine("Kényszerített verzió ellenőrzés...");
                    await LoadServerVersionAsync();
                    CheckGameInstallStatus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hiba: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // JÁTÉK FRISSÍTÉSI LOGIKA
        private async Task PerformGameUpdateAsync()
        {
            if (currentView != "home") return;

            bool isUpdate = ButtonText?.Text == "FRISSÍTÉS" || IsUpdateAvailable();
            
            if (!isUpdate)
            {
                var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = "Válassza ki a telepítési mappát" };
                if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                    return;
                gameInstallPath = dialog.FileName;
            }

            if (ActionButton != null)
            {
                ActionButton.IsEnabled = false;
            }
            if (CompactActionButton != null)
            {
                CompactActionButton.IsEnabled = false;
            }
            
            UpdateActionButtonIcon();

            string tempDownloadPath = Path.Combine(Path.GetTempPath(), "BitFighters_game.zip");

            try
            {
                ShowNotification(isUpdate ? "Játék frissítése elkezdődött..." : "Játék letöltése elkezdődött...");

                if (isUpdate && Directory.Exists(gameInstallPath))
                {
                    await PerformCompleteGameUpdate(tempDownloadPath);
                }
                else
                {
                    await PerformFreshInstall(tempDownloadPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Hiba a {(isUpdate ? "frissítés" : "letöltés")} során: {ex.Message}");
                ShowNotification($"Hiba a {(isUpdate ? "frissítés" : "letöltés")} során: {ex.Message}");
                UpdateProgressUI(0, "Hiba!", "", false);
            }
            finally
            {
                if (ActionButton != null)
                {
                    ActionButton.IsEnabled = true;
                }
                if (CompactActionButton != null)
                {
                    CompactActionButton.IsEnabled = true;
                }
                CheckGameInstallStatus();
            }
        }

        private async Task PerformCompleteGameUpdate(string tempDownloadPath)
        {
            try
            {
                UpdateProgressUI(0, "Előkészítés...", "", true);

                var backupPath = await CreateConfigBackupAsync();
                await DownloadGameFileAsync(tempDownloadPath, true);
                
                UpdateProgressUI(100, "Fájlok kibontása...", "", true);
                await ClearOldGameFilesAsync();
                await ExtractNewGameVersionAsync(tempDownloadPath);
                await RestoreConfigBackupAsync(backupPath);

                UpdateProgressUI(100, "Befejezve!", "", false);
                ShowNotification($"Játék sikeresen frissítve! Új verzió: v{serverGameVersion}");
                localGameVersion = GetLocalGameVersion();
            }
            catch (Exception ex)
            {
                UpdateProgressUI(0, "Hiba!", "", false);
                throw new Exception($"Frissítési hiba: {ex.Message}", ex);
            }
        }

        private async Task PerformFreshInstall(string tempDownloadPath)
        {
            await DownloadGameFileAsync(tempDownloadPath, false);
            
            UpdateProgressUI(100, "Fájlok kibontása...", "", true);
            await ExtractNewGameVersionAsync(tempDownloadPath);
            
            if (!string.IsNullOrEmpty(FindExecutable(gameInstallPath)))
            {
                SaveInstallPath();
                UpdateProgressUI(100, "Befejezve!", "", false);
                ShowNotification($"A játék sikeresen telepítve! Verzió: v{serverGameVersion}");
                localGameVersion = GetLocalGameVersion();
            }
            else
            {
                UpdateProgressUI(0, "Hiba!", "", false);
                ShowNotification("Hiba: A futtatható fájl nem található a mappában.");
                gameInstallPath = string.Empty;
            }
        }
        
        private void UpdateProgressUI(int percentage, string statusText, string speedText, bool visible)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var progressIndicatorBorder = this.FindName("ProgressIndicatorBorder") as Border;
                    var buttonContentPanel = this.FindName("ButtonContentPanel") as StackPanel;
                    var actionButton = this.FindName("ActionButton") as Button;
                    
                    if (progressIndicatorBorder != null)
                    {
                        progressIndicatorBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    }

                    if (actionButton != null)
                    {
                        actionButton.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
                    }
                    
                    // Hide/show normal button content
                    if (buttonContentPanel != null)
                    {
                        buttonContentPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
                    }
                    
                    // Update percentage circle
                    var progressPercentageCircle = this.FindName("ProgressPercentageCircle") as TextBlock;
                    if (progressPercentageCircle != null)
                    {
                        progressPercentageCircle.Text = $"{percentage}";
                    }
                    
                    // Update circular progress ring
                    var progressRingPath = this.FindName("ProgressRingPath") as System.Windows.Shapes.Path;
                    if (progressRingPath != null)
                    {
                        double radius = 15;
                        double circumference = 2 * Math.PI * radius;
                        double thickness = Math.Max(1, progressRingPath.StrokeThickness);
                        double scaledCircumference = circumference / thickness;

                        double dashLength = (scaledCircumference * percentage) / 100.0;
                        double gapLength = scaledCircumference - dashLength;

                        progressRingPath.StrokeDashArray = new DoubleCollection { dashLength, gapLength };
                    }
                    
                    var progressStatusText = this.FindName("ProgressStatusText") as TextBlock;
                    if (progressStatusText != null)
                    {
                        progressStatusText.Text = statusText;
                    }
                    
                    var progressSpeedText = this.FindName("ProgressSpeedText") as TextBlock;
                    if (progressSpeedText != null)
                    {
                        progressSpeedText.Text = speedText;
                    }
                    
                    // Compact header handling
                    var compactProgressIndicator = this.FindName("CompactProgressIndicator") as Border;
                    var compactActionButton = this.FindName("CompactActionButton") as Button;
                    
                    if (compactProgressIndicator != null)
                    {
                        compactProgressIndicator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    }
                    if (compactActionButton != null)
                    {
                        compactActionButton.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
                    }

                    // Update compact texts
                    var compactProgressPercentage = this.FindName("CompactProgressPercentage") as TextBlock;
                    if (compactProgressPercentage != null)
                    {
                         compactProgressPercentage.Text = $"{percentage}";
                    }
                    
                    var compactProgressStatus = this.FindName("CompactProgressStatus") as TextBlock;
                    if (compactProgressStatus != null)
                    {
                         compactProgressStatus.Text = statusText;
                    }

                    var compactProgressSpeed = this.FindName("CompactProgressSpeed") as TextBlock;
                    if (compactProgressSpeed != null)
                    {
                         compactProgressSpeed.Text = speedText;
                    }
                    
                    // Update compact ring
                    var compactProgressRingPath = this.FindName("CompactProgressRingPath") as System.Windows.Shapes.Path;
                    if (compactProgressRingPath != null)
                    {
                        double radius = 14; 
                        double circumference = 2 * Math.PI * radius;
                        double thickness = Math.Max(1, compactProgressRingPath.StrokeThickness);
                        double scaledCircumference = circumference / thickness;
                        
                        double dashLength = (scaledCircumference * percentage) / 100.0;
                        double gapLength = scaledCircumference - dashLength;
                        compactProgressRingPath.StrokeDashArray = new DoubleCollection { dashLength, gapLength };
                    }

                    // Also update button text for fallback
                    if (ButtonText != null && !visible)
                    {
                        // Reset button text when progress is hidden
                        CheckGameInstallStatus();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating progress UI: {ex.Message}");
                }
            });
        }

        private void SetPauseResumeButtonState(bool isEnabled, bool isPaused)
        {
            Dispatcher.Invoke(() =>
            {
                var pauseResumeButton = this.FindName("PauseResumeButton") as Button;
                if (pauseResumeButton != null)
                {
                    pauseResumeButton.IsEnabled = isEnabled;
                }

                var compactPauseResumeButton = this.FindName("CompactPauseResumeButton") as Button;
                if (compactPauseResumeButton != null)
                {
                    compactPauseResumeButton.IsEnabled = isEnabled;
                }

                UpdatePauseResumeUI(isPaused);
            });
        }

        private void UpdatePauseResumeUI(bool isPaused)
        {
            var pauseIcon = this.FindName("PauseIcon") as Canvas;
            var resumeIcon = this.FindName("ResumeIcon") as System.Windows.Shapes.Path;

            if (pauseIcon != null)
            {
                pauseIcon.Visibility = isPaused ? Visibility.Collapsed : Visibility.Visible;
            }

            if (resumeIcon != null)
            {
                resumeIcon.Visibility = isPaused ? Visibility.Visible : Visibility.Collapsed;
            }

            var compactPauseIcon = this.FindName("CompactPauseIcon") as Canvas;
            var compactResumeIcon = this.FindName("CompactResumeIcon") as System.Windows.Shapes.Path;

            if (compactPauseIcon != null)
            {
                compactPauseIcon.Visibility = isPaused ? Visibility.Collapsed : Visibility.Visible;
            }

            if (compactResumeIcon != null)
            {
                compactResumeIcon.Visibility = isPaused ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async Task DownloadGameFileAsync(string downloadPath, bool isUpdate)
        {
            const int maxRetries = 3;
            int attempt = 0;
            Exception? lastException = null;

            _isDownloadInProgress = true;
            _isDownloadPaused = false;
            _downloadPauseSource.TrySetResult(true);
            _lastDownloadPercentage = 0;
            _currentDownloadStatusText = isUpdate ? "Frissítés..." : "Letöltés...";
            SetPauseResumeButtonState(true, false);
            try
            {
                while (attempt < maxRetries)
                {
                    FileStream? fileStream = null;
                    try
                    {
                        attempt++;
                        if (attempt > 1)
                        {
                            Debug.WriteLine($"Letöltési próbálkozás {attempt}/{maxRetries}");
                            UpdateProgressUI(0, "Újrapróbálás...", "0 MB/s", true);
                            await Task.Delay(3000 * attempt); // Increasing wait before retry
                        }

                        // Delete any existing partial download with retries
                        if (File.Exists(downloadPath))
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                try
                                {
                                    File.SetAttributes(downloadPath, FileAttributes.Normal);
                                    File.Delete(downloadPath);
                                    break;
                                }
                                catch
                                {
                                    if (i == 2) throw;
                                    await Task.Delay(500);
                                    GC.Collect();
                                    GC.WaitForPendingFinalizers();
                                }
                            }
                        }

                        UpdateProgressUI(0, isUpdate ? "Frissítés..." : "Letöltés...", "0 MB/s", true);

                        using (var client = new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromMinutes(15); // Longer timeout
                            client.DefaultRequestHeaders.ConnectionClose = false;
                            client.DefaultRequestHeaders.Add("Accept-Encoding", "identity"); // Disable compression

                            var response = await client.GetAsync(GameDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                            response.EnsureSuccessStatusCode();

                            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                            Debug.WriteLine($"Letöltendő fájl mérete: {totalBytes} bytes ({totalBytes / 1024 / 1024} MB)");

                            if (totalBytes <= 0)
                            {
                                throw new InvalidOperationException("A szerver nem küldte el a fájl méretét");
                            }

                            var progressBuffer = new byte[81920]; // 80KB buffer

                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            {
                                fileStream = new FileStream(
                                    downloadPath,
                                    FileMode.Create,
                                    FileAccess.Write,
                                    FileShare.None,
                                    81920,
                                    FileOptions.Asynchronous | FileOptions.WriteThrough);

                                long totalBytesRead = 0;
                                int bytesRead;
                                var lastProgressUpdate = DateTime.Now;
                                var startTime = DateTime.Now;
                                long lastBytesRead = 0;

                                while (true)
                                {
                                    bool shouldWait = _isDownloadPaused;
                                    if (shouldWait)
                                    {
                                        await _downloadPauseSource.Task;
                                        lastProgressUpdate = DateTime.Now;
                                        lastBytesRead = totalBytesRead;
                                    }

                                    bytesRead = await contentStream.ReadAsync(progressBuffer, 0, progressBuffer.Length);
                                    if (bytesRead <= 0)
                                    {
                                        break;
                                    }

                                    await fileStream.WriteAsync(progressBuffer, 0, bytesRead);
                                    totalBytesRead += bytesRead;

                                    if (totalBytes > 0 && DateTime.Now - lastProgressUpdate > TimeSpan.FromMilliseconds(250))
                                    {
                                        var progress = (int)((totalBytesRead * 100) / totalBytes);
                                        _lastDownloadPercentage = progress;
                                        
                                        // Calculate download speed
                                        var elapsed = (DateTime.Now - lastProgressUpdate).TotalSeconds;
                                        var bytesPerSecond = (totalBytesRead - lastBytesRead) / elapsed;
                                        var mbPerSecond = bytesPerSecond / (1024 * 1024);
                                        
                                        string statusText = _currentDownloadStatusText;
                                        string speedText = $"{mbPerSecond:F1} MB/s";
                                        
                                        UpdateProgressUI(progress, statusText, speedText, true);
                                        
                                        lastProgressUpdate = DateTime.Now;
                                        lastBytesRead = totalBytesRead;
                                    }
                                }

                                // Critical: Ensure all data is written
                                await fileStream.FlushAsync();
                                fileStream.Close();
                                fileStream.Dispose();
                                fileStream = null;

                                Debug.WriteLine($"Letöltve {totalBytesRead} bytes");
                                
                                // Show completion
                                UpdateProgressUI(100, "Kész!", "Befejezve", true);
                                await Task.Delay(500);

                                // Verify file size matches exactly
                                if (totalBytesRead != totalBytes)
                                {
                                    throw new InvalidDataException($"Hiányos letöltés: {totalBytesRead}/{totalBytes} bytes");
                                }
                            }
                        }

                        // Aggressive file system sync
                        await Task.Delay(1000);
                        for (int i = 0; i < 3; i++)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            await Task.Delay(200);
                        }

                        // Verify the file exists and has correct size
                        var downloadedFileInfo = new FileInfo(downloadPath);
                        downloadedFileInfo.Refresh();
                        if (!downloadedFileInfo.Exists)
                        {
                            throw new FileNotFoundException("A letöltött fájl nem található a fájlrendszerben");
                        }

                        Debug.WriteLine($"Letöltött fájl ellenőrzése: {downloadedFileInfo.Length} bytes");

                        // Verify the downloaded file is a valid ZIP with retries
                        bool isValid = await ValidateZipWithRetries(downloadPath, 5); // More retry attempts
                        if (!isValid)
                        {
                            // Create diagnostic dump
                            await CreateDiagnosticDump(downloadPath);
                            throw new InvalidDataException("A letöltött fájl sérült vagy nem érvényes ZIP fájl.");
                        }

                        Debug.WriteLine($"Fájl sikeresen letöltve és validálva: {downloadPath}");
                        return; // Success!
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        Debug.WriteLine($"Letöltési hiba (próbálkozás {attempt}/{maxRetries}): {ex.GetType().Name} - {ex.Message}");
                        Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        
                        // Ensure file stream is closed
                        if (fileStream != null)
                        {
                            try
                            {
                                fileStream.Close();
                                fileStream.Dispose();
                            }
                            catch { /* Ignore */ }
                        }

                        if (attempt >= maxRetries)
                        {
                            throw new Exception($"A letöltés {maxRetries} próbálkozás után is sikertelen volt.\n\nUtolsó hiba: {ex.Message}", ex);
                        }
                    }
                    finally
                    {
                        if (fileStream != null)
                        {
                            try
                            {
                                fileStream.Dispose();
                            }
                            catch { /* Ignore */ }
                        }
                    }
                }

                throw new Exception("A letöltés sikertelen volt.", lastException);
            }
            finally
            {
                _isDownloadInProgress = false;
                _isDownloadPaused = false;
                _downloadPauseSource.TrySetResult(true);
                SetPauseResumeButtonState(false, false);
            }
        }

        private async Task CreateDiagnosticDump(string zipPath)
        {
            try
            {
                var fileInfo = new FileInfo(zipPath);
                if (!fileInfo.Exists) return;

                Debug.WriteLine($"\n=== DIAGNOSTIC DUMP ===");
                Debug.WriteLine($"File: {zipPath}");
                Debug.WriteLine($"Size: {fileInfo.Length} bytes");
                Debug.WriteLine($"Created: {fileInfo.CreationTime}");
                Debug.WriteLine($"Modified: {fileInfo.LastWriteTime}");

                // Dump first and last bytes
                await Task.Run(() =>
                {
                    using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] firstBytes = new byte[Math.Min(64, fs.Length)];
                        fs.Read(firstBytes, 0, firstBytes.Length);
                        Debug.WriteLine($"First {firstBytes.Length} bytes (hex): {BitConverter.ToString(firstBytes)}");

                        if (fs.Length > 64)
                        {
                            fs.Seek(-Math.Min(64, fs.Length), SeekOrigin.End);
                            byte[] lastBytes = new byte[Math.Min(64, fs.Length)];
                            fs.Read(lastBytes, 0, lastBytes.Length);
                            Debug.WriteLine($"Last {lastBytes.Length} bytes (hex): {BitConverter.ToString(lastBytes)}");
                        }
                    }
                });

                Debug.WriteLine($"=== END DIAGNOSTIC DUMP ===\n");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Diagnostic dump failed: {ex.Message}");
            }
        }

        private async Task<string> CreateConfigBackupAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var tempConfigPath = Path.Combine(Path.GetTempPath(), $"BitFighters_backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(tempConfigPath);

                    var configExtensions = new[] { "*.config", "*.ini", "*.json", "*.txt", "*.dat" };
                    var configFiles = new List<string>();

                    foreach (var extension in configExtensions)
                    {
                        try
                        {
                            configFiles.AddRange(Directory.GetFiles(gameInstallPath, extension, SearchOption.AllDirectories));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Nem sikerült keresni a {extension} fájlokat: {ex.Message}");
                        }
                    }

                    var configKeywords = new[] { "config", "settings", "save", "profile", "user", "pref" };
                    var filteredConfigFiles = configFiles.Where(file =>
                    {
                        var fileName = Path.GetFileName(file).ToLower();
                        return configKeywords.Any(keyword => fileName.Contains(keyword)) ||
                               fileName.EndsWith(".config") || fileName.EndsWith(".ini");
                    }).ToList();

                    foreach (var configFile in filteredConfigFiles)
                    {
                        try
                        {
                            var relativePath = Path.GetRelativePath(gameInstallPath, configFile);
                            var backupFilePath = Path.Combine(tempConfigPath, relativePath);
                            
                            Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath)!);
                            File.Copy(configFile, backupFilePath, true);
                            
                            Debug.WriteLine($"Konfig fájl mentve: {relativePath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Nem sikerült menteni: {configFile} - {ex.Message}");
                        }
                    }

                    return tempConfigPath;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hiba a backup készítésekor: {ex.Message}");
                    return string.Empty;
                }
            });
        }

        private async Task ClearOldGameFilesAsync()
        {
            if (ButtonText != null)
            {
                ButtonText.Text = "RÉGI FÁJLOK TÖRLÉSE...";
            }

            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(gameInstallPath))
                        return;

                    var files = Directory.GetFiles(gameInstallPath, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Nem sikerült törölni a fájlt: {file} - {ex.Message}");
                        }
                    }

                    var directories = Directory.GetDirectories(gameInstallPath, "*", SearchOption.AllDirectories)
                                              .OrderByDescending(d => d.Length);

                    foreach (var directory in directories)
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(directory).Any())
                            {
                                Directory.Delete(directory);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Nem sikerült törölni a könyvtárat: {directory} - {ex.Message}");
                        }
                    }

                    Debug.WriteLine("Régi fájlok törölve");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hiba a régi fájlok törlése során: {ex.Message}");
                    throw;
                }
            });
        }

        private async Task ExtractNewGameVersionAsync(string zipPath)
        {
            if (ButtonText != null)
            {
                ButtonText.Text = "ÚJ VERZIÓ TELEPÍTÉSE...";
            }
            if (CompactActionButton != null)
            {
                CompactActionButton.Content = "TELEPÍTÉS...";
            }

            await Task.Run(() =>
            {
                bool extractionSucceeded = false;
                try
                {
                    // Verify ZIP file exists and has content
                    var fileInfo = new FileInfo(zipPath);
                    fileInfo.Refresh();
                    
                    if (!fileInfo.Exists)
                    {
                        throw new FileNotFoundException($"A ZIP fájl nem található: {zipPath}");
                    }
                    if (fileInfo.Length == 0)
                    {
                        throw new InvalidDataException($"A ZIP fájl üres: {zipPath}");
                    }

                    Debug.WriteLine($"Kicsomagolás kezdése - ZIP fájl mérete: {fileInfo.Length} bytes");

                    Directory.CreateDirectory(gameInstallPath);
                    
                    // Use FileStream with explicit parameters for better control
                    using (var fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, false))
                    {
                        Debug.WriteLine($"ZIP tartalmaz {archive.Entries.Count} bejegyzést");

                        int extractedCount = 0;
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name))
                                continue;

                            var destinationPath = Path.Combine(gameInstallPath, entry.FullName);
                            var destinationDir = Path.GetDirectoryName(destinationPath);
                            
                            if (!string.IsNullOrEmpty(destinationDir))
                                Directory.CreateDirectory(destinationDir);

                            try
                            {
                                // Remove read-only attribute if exists
                                if (File.Exists(destinationPath))
                                {
                                    File.SetAttributes(destinationPath, FileAttributes.Normal);
                                }

                                entry.ExtractToFile(destinationPath, overwrite: true);
                                extractedCount++;

                                if (extractedCount % 10 == 0)
                                {
                                    Debug.WriteLine($"Kicsomagolva: {extractedCount}/{archive.Entries.Count}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Hiba a fájl kicsomagolásakor ({entry.FullName}): {ex.Message}");
                                throw;
                            }
                        }

                        Debug.WriteLine($"Összesen kicsomagolt fájlok: {extractedCount}");
                    }

                    // Létrehozunk egy lokális verzió fájlt a szerver verzióval
                    string localVersionFile = Path.Combine(gameInstallPath, "version.txt");
                    File.WriteAllText(localVersionFile, serverGameVersion);
                    Debug.WriteLine($"Lokális verzió fájl létrehozva: {serverGameVersion}");

                    Debug.WriteLine("Új verzió sikeresen kicsomagolva");
                    extractionSucceeded = true;
                }
                catch (InvalidDataException ex)
                {
                    Debug.WriteLine($"Érvénytelen ZIP fájl: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    throw new InvalidDataException($"A letöltött fájl sérült vagy nem érvényes ZIP formátum. Kérem próbálja újra a letöltést.\n\nRészletek: {ex.Message}", ex);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hiba az új verzió telepítésekor: {ex.GetType().Name} - {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    throw;
                }
                finally
                {
                    // Small delay to ensure all handles are released
                    System.Threading.Thread.Sleep(100);

                    // Only delete the ZIP file if extraction was successful
                    if (extractionSucceeded)
                    {
                        try
                        {
                            if (File.Exists(zipPath))
                            {
                                File.Delete(zipPath);
                                Debug.WriteLine("Temp ZIP fájl törölve");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Nem sikerült törölni a temp fájlt: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"ZIP fájl megőrizve hibakereséshez: {zipPath}");
                    }
                }
            });
        }

        private async Task RestoreConfigBackupAsync(string backupPath)
        {
            if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
                return;

            await Task.Run(() =>
            {
                try
                {
                    var backupFiles = Directory.GetFiles(backupPath, "*", SearchOption.AllDirectories);
                    
                    foreach (var backupFile in backupFiles)
                    {
                        try
                        {
                            var relativePath = Path.GetRelativePath(backupPath, backupFile);
                            var restorePath = Path.Combine(gameInstallPath, relativePath);
                            
                            Directory.CreateDirectory(Path.GetDirectoryName(restorePath)!);
                            File.Copy(backupFile, restorePath, true);
                            
                            Debug.WriteLine($"Konfig fájl visszaállítva: {relativePath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Nem sikerült visszaállítani: {backupFile} - {ex.Message}");
                        }
                    }

                    Directory.Delete(backupPath, true);
                    Debug.WriteLine("Konfiguráció visszaállítva és backup törölve");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Hiba a konfiguráció visszaállításakor: {ex.Message}");
                }
            });
        }

        // SEGÉDMETÓDUSOK
        private string? FindExecutable(string path)
        {
            if (!Directory.Exists(path)) return null;
            try
            {
                return Directory.GetFiles(path, GameExecutableName, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (UnauthorizedAccessException) { return null; }
        }

        private async Task<bool> IsValidZipFileAsync(string zipPath)
        {
            try
            {
                // Refresh file info
                var fileInfo = new FileInfo(zipPath);
                fileInfo.Refresh();
                
                if (!fileInfo.Exists)
                {
                    Debug.WriteLine($"Érvénytelen ZIP: fájl nem létezik: {zipPath}");
                    return false;
                }
                
                if (fileInfo.Length < 22) // Minimum ZIP file size
                {
                    Debug.WriteLine($"Érvénytelen ZIP: túl kicsi ({fileInfo.Length} bytes)");
                    return false;
                }

                Debug.WriteLine($"ZIP validálás kezdése: {fileInfo.Length} bytes");

                // Read and analyze file header for diagnostics
                await Task.Run(() =>
                {
                    using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                    {
                        // Read first 4 bytes (ZIP signature should be 50 4B 03 04 or 50 4B 05 06)
                        byte[] header = new byte[4];
                        fs.Read(header, 0, 4);
                        string hexHeader = BitConverter.ToString(header).Replace("-", " ");
                        Debug.WriteLine($"ZIP file header (hex): {hexHeader}");
                        
                        // Check for valid ZIP signature
                        if (header[0] != 0x50 || header[1] != 0x4B)
                        {
                            throw new InvalidDataException($"Invalid ZIP signature. Expected 'PK' (50 4B), got: {hexHeader}");
                        }
                        
                        // Read last 22 bytes (End of Central Directory)
                        if (fs.Length >= 22)
                        {
                            fs.Seek(-22, SeekOrigin.End);
                            byte[] eocdMarker = new byte[4];
                            fs.Read(eocdMarker, 0, 4);
                            string hexEOCD = BitConverter.ToString(eocdMarker).Replace("-", " ");
                            Debug.WriteLine($"ZIP EOCD marker (hex): {hexEOCD} (should be 50 4B 05 06)");
                        }
                    }
                });

                // Try to open and read the ZIP file
                await Task.Run(() =>
                {
                    using (var fileStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                    using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, false))
                    {
                        // Try to access entries to verify ZIP integrity
                        var entryCount = archive.Entries.Count;
                        if (entryCount == 0)
                        {
                            throw new InvalidDataException("A ZIP fájl üres");
                        }
                        
                        // Try to read the first entry's properties to ensure it's readable
                        var firstEntry = archive.Entries[0];
                        var testLength = firstEntry.Length;
                        
                        Debug.WriteLine($"ZIP validálás sikeres: {entryCount} bejegyzés, első fájl: {firstEntry.FullName} ({testLength} bytes)");
                    }
                });

                return true;
            }
            catch (InvalidDataException ex)
            {
                Debug.WriteLine($"ZIP validálás sikertelen - érvénytelen formátum: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ZIP validálás hiba: {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ValidateZipWithRetries(string zipPath, int maxRetries)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                if (i > 0)
                {
                    Debug.WriteLine($"ZIP validálási próbálkozás {i + 1}/{maxRetries}");
                    await Task.Delay(300 * (i + 1)); // Increasing delay
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                bool isValid = await IsValidZipFileAsync(zipPath);
                if (isValid)
                {
                    return true;
                }
            }

            Debug.WriteLine($"ZIP validálás véglegesen sikertelen {maxRetries} próbálkozás után");
            return false;
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
                ButtonText.Text = gameInstalled ? "JÁTÉK" : "LETÖLTÉS";
            }

            if (CompactActionButton != null)
            {
                CompactActionButton.Content = gameInstalled ? "JÁTÉK" : "LETÖLTÉS";
            }

            if (gameInstalled)
            {
                gameInstallPath = Path.GetDirectoryName(executablePath)!;
            }

            if (ActionButton != null && currentView == "home")
            {
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.IsEnabled = true;
            }

            UpdateVersionDisplay();
            UpdateActionButtonIcon();
        }

        // ESEMÉNYKEZEL?K
        private async void HandleActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (currentView != "home") return;
            switch (ButtonText?.Text)
            {
                case "LETÖLTÉS":
                case "FRISSÍTÉS":
                    await PerformGameUpdateAsync();
                    break;
                case "JÁTÉK":
                    await StartGame();
                    break;
            }
        }

        private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isDownloadInProgress)
            {
                return;
            }

            if (_isDownloadPaused)
            {
                _isDownloadPaused = false;
                _downloadPauseSource.TrySetResult(true);
                UpdatePauseResumeUI(false);
                UpdateProgressUI(_lastDownloadPercentage, _currentDownloadStatusText, "0 MB/s", true);
            }
            else
            {
                _isDownloadPaused = true;
                _downloadPauseSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                UpdatePauseResumeUI(true);
                UpdateProgressUI(_lastDownloadPercentage, "Szüneteltetve", "0 MB/s", true);
            }
        }
        
        private void UpdateActionButtonIcon()
        {
            try
            {
                // Use FindName to get the grid elements dynamically
                var playIconGrid = this.FindName("PlayIconGrid") as Grid;
                var downloadIconGrid = this.FindName("DownloadIconGrid") as Grid;
                
                if (playIconGrid == null || downloadIconGrid == null) 
                {
                    Debug.WriteLine("Icon grids not found yet");
                    return;
                }
                
                string? buttonTextValue = ButtonText?.Text;
                
                // Switch to download icon if downloading or updating
                if (buttonTextValue == "LETÖLTÉS" || buttonTextValue == "FRISSÍTÉS" || 
                    buttonTextValue?.Contains("LETÖLTÉS") == true || buttonTextValue?.Contains("FRISSÍTÉS") == true)
                {
                    playIconGrid.Visibility = Visibility.Collapsed;
                    downloadIconGrid.Visibility = Visibility.Visible;
                    Debug.WriteLine("Icon changed to download icon");
                }
                else
                {
                    playIconGrid.Visibility = Visibility.Visible;
                    downloadIconGrid.Visibility = Visibility.Collapsed;
                    Debug.WriteLine("Icon changed to play icon");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating action button icon: {ex.Message}");
            }
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

                var requestData = new { action = "get_news", limit = 3 };
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

                Dispatcher.Invoke(() => 
                {
                    if (NewsItemsControl != null)
                        NewsItemsControl.ItemsSource = updates.OrderByDescending(u => u.CreatedAt).Take(3);
                });
                
                Debug.WriteLine($"News loaded successfully: {updates.Count} items");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadNewsUpdatesAsync failed: {ex}");
                Dispatcher.Invoke(() =>
                {
                    if (NewsItemsControl != null)
                    {
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
                });
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    this.DragMove();
                }
                catch (InvalidOperationException)
                {
                    // Ignore if DragMove is called when mouse button is not down
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
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

        private void NewsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (NewsScrollViewer == null) return;
            
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                var parentScrollViewer = FindParentScrollViewer(NewsScrollViewer);
                if (parentScrollViewer != null)
                {
                    double target = parentScrollViewer.VerticalOffset - e.Delta;
                    if (target < 0) target = 0;
                    if (target > parentScrollViewer.ScrollableHeight) target = parentScrollViewer.ScrollableHeight;
                    parentScrollViewer.ScrollToVerticalOffset(target);
                    e.Handled = true;
                }
                return;
            }
            
            if (_reducedMotion)
            {
                double target = NewsScrollViewer.HorizontalOffset - e.Delta;
                if (target < 0) target = 0;
                if (target > NewsScrollViewer.ScrollableWidth) target = NewsScrollViewer.ScrollableWidth;
                NewsScrollViewer.ScrollToHorizontalOffset(target);
                e.Handled = true;
                return;
            }
            
            _targetVerticalOffset = NewsScrollViewer.HorizontalOffset - e.Delta * 0.7;
            if (_targetVerticalOffset < 0) _targetVerticalOffset = 0;
            if (_targetVerticalOffset > NewsScrollViewer.ScrollableWidth) _targetVerticalOffset = NewsScrollViewer.ScrollableWidth;
            
            _scrollTimer?.Start();
            e.Handled = true;
        }

        private void NewsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Scroll indikátorok eltávolítva
        }

        private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateCompactHeaderVisibility();
        }

        private void UpdateCompactHeaderVisibility()
        {
            if (_compactHeaderBar == null || _mainScrollViewer == null)
            {
                return;
            }

            if (currentView != "home")
            {
                SetCompactHeaderVisibility(false);
                return;
            }

            // Using direct VerticalOffset instead of element position calculation for reliability
            // Show compact header when user has scrolled down past the main hero section
            bool shouldShow = _mainScrollViewer.VerticalOffset > 190;

            SetCompactHeaderVisibility(shouldShow);
        }

        private void SetCompactHeaderVisibility(bool show)
        {
            if (_compactHeaderBar == null || _isCompactHeaderVisible == show)
            {
                return;
            }

            _isCompactHeaderVisible = show;
            var translate = _compactHeaderBar.RenderTransform as TranslateTransform ?? new TranslateTransform();
            _compactHeaderBar.RenderTransform = translate;

            if (show)
            {
                _compactHeaderBar.Visibility = Visibility.Visible;
                _compactHeaderBar.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                _compactHeaderBar.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                translate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-120, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
        }

        private ScrollViewer? FindParentScrollViewer(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is ScrollViewer scrollViewer && !ReferenceEquals(scrollViewer, NewsScrollViewer))
                {
                    return scrollViewer;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }

        private void ShowHomeView()
        {
            currentView = "home";

            if (ActionButton != null)
            {
                ActionButton.Visibility = Visibility.Visible;
                ActionButton.IsEnabled = true;
            }
            if (MainContentGrid != null)
            {
                MainContentGrid.Visibility = Visibility.Visible;
            }
            if (NewsPanelBorder != null) NewsPanelBorder.Visibility = Visibility.Visible;
            if (ProfileViewGrid != null) ProfileViewGrid.Visibility = Visibility.Collapsed;
            if (LeaderboardViewGrid != null) LeaderboardViewGrid.Visibility = Visibility.Collapsed;

        }

        private void ShowProfileView()
        {
            currentView = "profile";
            if (ActionButton != null) ActionButton.Visibility = Visibility.Collapsed;
            if (MainContentGrid != null) MainContentGrid.Visibility = Visibility.Collapsed;
            if (NewsPanelBorder != null) NewsPanelBorder.Visibility = Visibility.Collapsed;
            if (LeaderboardViewGrid != null) LeaderboardViewGrid.Visibility = Visibility.Collapsed;
            if (ProfileViewGrid != null)
            {
                ProfileViewGrid.Visibility = Visibility.Visible;
                UpdateProfileView();
            }
        }

        private void ShowLeaderboardView()
        {
            currentView = "leaderboard";

            if (MainContentGrid != null) MainContentGrid.Visibility = Visibility.Collapsed;
            if (NewsPanelBorder != null) NewsPanelBorder.Visibility = Visibility.Collapsed;
            if (ProfileViewGrid != null) ProfileViewGrid.Visibility = Visibility.Collapsed;
            if (LeaderboardViewGrid != null) LeaderboardViewGrid.Visibility = Visibility.Visible;

            _ = LoadLeaderboardAsync();
        }

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
                    Dispatcher.Invoke(() =>
                    {
                        if (LeaderboardItemsControl != null)
                        {
                            LeaderboardItemsControl.ItemsSource = apiResponse.leaderboard;
                        }
                    });
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

        private void UpdateProfileView()
        {
            if (ProfileViewGrid?.Visibility != Visibility.Visible) 
                return;
            
            if (ProfileUsernameText != null) 
                ProfileUsernameText.Text = loggedInUsername;
            
            if (ProfileHighestScoreText != null) 
                ProfileHighestScoreText.Text = loggedInUserHighestScore.ToString();
            
            if (ProfileUserRankText != null)
                ProfileUserRankText.Text = loggedInUserRanking > 0 ? $"#{loggedInUserRanking}" : "N/A";
                
            if (ProfileRankText != null) 
                ProfileRankText.Text = GetUserRank(loggedInUserHighestScore);
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
            if (ProfileMenuButton?.ContextMenu != null)
            {
                ProfileMenuButton.ContextMenu.PlacementTarget = ProfileMenuButton;
                ProfileMenuButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                
                // Calculate offset to keep menu within window bounds
                var buttonPosition = ProfileMenuButton.TransformToAncestor(this).Transform(new Point(0, 0));
                double menuWidth = 300;
                double windowWidth = this.ActualWidth;
                
                // Adjust horizontal offset if menu would go outside window
                if (buttonPosition.X + menuWidth > windowWidth)
                {
                    ProfileMenuButton.ContextMenu.HorizontalOffset = -(menuWidth - ProfileMenuButton.ActualWidth);
                }
                else
                {
                    ProfileMenuButton.ContextMenu.HorizontalOffset = 0;
                }
                
                ProfileMenuButton.ContextMenu.VerticalOffset = 6;
                ProfileMenuButton.ContextMenu.IsOpen = true;
            }
        }

        private void ProfileHomeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowHomeView();
        }

        private void ProfileViewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowProfileView();
        }

        private void ProfileLeaderboardMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ShowLeaderboardView();
        }

        private void ProfileContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (DimOverlay != null) DimOverlay.Visibility = Visibility.Visible;
        }

        private void ProfileContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            if (DimOverlay != null) DimOverlay.Visibility = Visibility.Collapsed;
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHomeView();
        }

        private void StarButton_Click(object sender, RoutedEventArgs e)
        {
            ShowLeaderboardView();
        }

        private void CompactHomeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowHomeView();
        }

        private void CompactProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                
                // Keep menu within window bounds logic similar to main profile button if needed
                // For now standard placement is likely fine as it is on the right
                // But since it is on the far right, we might want to shift it left
                btn.ContextMenu.HorizontalOffset = -(btn.ContextMenu.Width - btn.ActualWidth); // Align right edge
                
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void CompactHeaderBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Forward scroll events to the main ScrollViewer so scrolling works even over the compact header
            if (_mainScrollViewer != null)
            {
                double newOffset = _mainScrollViewer.VerticalOffset - e.Delta;
                if (newOffset < 0) newOffset = 0;
                if (newOffset > _mainScrollViewer.ScrollableHeight) newOffset = _mainScrollViewer.ScrollableHeight;
                _mainScrollViewer.ScrollToVerticalOffset(newOffset);
                e.Handled = true;
            }
        }

        private void RootBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Forward scroll events from anywhere in the window to the main ScrollViewer
            // This allows scrolling even when the compact header is hidden or when clicking on empty areas
            if (_mainScrollViewer != null && !e.Handled)
            {
                double newOffset = _mainScrollViewer.VerticalOffset - e.Delta;
                if (newOffset < 0) newOffset = 0;
                if (newOffset > _mainScrollViewer.ScrollableHeight) newOffset = _mainScrollViewer.ScrollableHeight;
                _mainScrollViewer.ScrollToVerticalOffset(newOffset);
                e.Handled = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _scrollTimer?.Stop();
            // Ne dispose-oljuk a statikus HttpClient-et, mert több ablak között megosztott
            // _httpClient.Dispose();
            base.OnClosed(e);
        }
    }
}