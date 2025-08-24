using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BitFightersLauncher
{
    public partial class MainWindow : Window
    {
        private const string GameDownloadUrl = "https://bitfighters.eu/BitFighters/BitFighters.zip";
        private const string GameExecutableName = "BitFighters.exe";
        private string gameInstallPath = string.Empty;

        private readonly string settingsFilePath;

        private double _targetVerticalOffset;
        private bool _isScrolling;

        public MainWindow()
        {
            InitializeComponent();

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            settingsFilePath = Path.Combine(appDataPath, "BitFightersLauncher", "settings.txt");

            Loaded += async (s, e) =>
            {
                LoadInstallPath();
                CheckGameInstallStatus();
                await LoadNewsUpdatesAsync();

                CompositionTarget.Rendering += CompositionTarget_Rendering;
            };
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
                Debug.WriteLine($"Hiba a beállítások betöltésekor: {ex.Message}");
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
            Application.Current.Shutdown();
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
                ButtonText.Text = "PLAY";
            }
            else
            {
                ButtonText.Text = "LETÖLTÉS";
            }
        }

        private async void HandleActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ButtonText.Text == "LETÖLTÉS")
            {
                await DownloadAndInstallGameAsync();
            }
            else if (ButtonText.Text == "PLAY")
            {
                await StartGame();
            }
        }

        private async Task DownloadAndInstallGameAsync()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = "Válassza ki a telepítési mappát" };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                gameInstallPath = dialog.FileName;

                ActionButton.IsEnabled = false;
                ButtonText.Text = "FOLYAMATBAN";
                DownloadStatusGrid.Visibility = Visibility.Visible;
                DownloadProgressBar.IsIndeterminate = false;
                DownloadStatusText.Text = "Letöltés előkészítése...";
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

                                    Dispatcher.Invoke(() =>
                                    {
                                        DownloadProgressBar.Value = progressPercentage;
                                        ProgressPercentageText.Text = $"{progressPercentage}%";
                                        ProgressDetailsText.Text = detailsText;
                                        DownloadStatusText.Text = $"Letöltés... {speedText}";
                                    });
                                }
                            }
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        DownloadStatusText.Text = "Telepítés...";
                        ProgressPercentageText.Text = "";
                        ProgressDetailsText.Text = "Kicsomagolás...";
                        DownloadProgressBar.IsIndeterminate = true;
                    });

                    await InstallGameAsync(tempDownloadPath);
                }
                catch (Exception ex)
                {
                    ShowNotification($"Hiba a letöltés során: {ex.Message}");
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
                    Dispatcher.Invoke(() => ShowNotification($"Hiba a telepítés során: {ex.Message}"));
                }
                finally
                {
                    if (File.Exists(downloadedFilePath)) File.Delete(downloadedFilePath);
                }
            });

            if (!string.IsNullOrEmpty(FindExecutable(gameInstallPath)))
            {
                ShowNotification("A játék telepítése sikeresen befejeződött!");
                SaveInstallPath();
            }
            else
            {
                ShowNotification("Hiba: A futtatható fájl nem található a mappában.");
                if (File.Exists(settingsFilePath)) File.Delete(settingsFilePath);
                gameInstallPath = string.Empty;
            }

            Dispatcher.Invoke(() => {
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
                            WorkingDirectory = Path.GetDirectoryName(gameExecutablePath)!
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
                    ShowNotification("Hiba: A játékfájl nem található.");
                    if (File.Exists(settingsFilePath)) File.Delete(settingsFilePath);
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
            string apiUrl = "http://bitfighters.eu/api/get_news.php";

            try
            {
                using (var httpClient = new HttpClient())
                {
                    string jsonResponse = await httpClient.GetStringAsync(apiUrl);

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    };
                    var updates = JsonSerializer.Deserialize<System.Collections.Generic.List<NewsUpdate>>(jsonResponse, options);

                    NewsItemsControl.ItemsSource = updates;
                }
            }
            catch (Exception ex)
            {
                NewsItemsControl.ItemsSource = new System.Collections.Generic.List<NewsUpdate>
                {
                    new NewsUpdate { Title = "Hiba a hírek betöltésekor", Content = "Nem sikerült elérni a szervert: " + ex.Message }
                };
            }
        }

        private void NewsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
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

            e.Handled = true;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (_isScrolling && NewsScrollViewer != null)
            {
                double currentOffset = NewsScrollViewer.VerticalOffset;
                double difference = _targetVerticalOffset - currentOffset;

                if (Math.Abs(difference) < 1.0)
                {
                    NewsScrollViewer.ScrollToVerticalOffset(_targetVerticalOffset);
                    _isScrolling = false;
                    return;
                }

                NewsScrollViewer.ScrollToVerticalOffset(currentOffset + difference * 0.2);
            }
        }

        // --- ÚJ ESEMÉNYKEZELŐ A GÖRGETÉSJELZŐKHÖZ ---
        private void NewsScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Felső jelző láthatósága
            TopScrollIndicator.Opacity = (e.VerticalOffset > 0) ? 1 : 0;

            // Alsó jelző láthatósága
            // Akkor látható, ha a jelenlegi pozíció kisebb, mint a maximális görgethető magasság
            BottomScrollIndicator.Opacity = (e.VerticalOffset < NewsScrollViewer.ScrollableHeight - 1) ? 1 : 0;
        }
    }
}