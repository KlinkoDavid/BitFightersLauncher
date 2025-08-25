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
using System.Windows.Media.Effects;

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
        private bool _renderingHooked;
        private readonly bool _reducedMotion;

        public MainWindow()
        {
            InitializeComponent();

            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            settingsFilePath = Path.Combine(appDataPath, "BitFightersLauncher", "settings.txt");

            _reducedMotion = (RenderCapability.Tier >> 16) < 2;

            Loaded += (s, e) => UpdateBorderClip();
            SizeChanged += (s, e) => UpdateBorderClip();

            Loaded += async (s, e) =>
            {
                LoadInstallPath();
                CheckGameInstallStatus();
                await LoadNewsUpdatesAsync();

                ApplyPerformanceModeIfNeeded();
            };
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
                ButtonText.Text = "JÁTÉK";
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
            else if (ButtonText.Text == "JÁTÉK")
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
                                            DownloadStatusText.Text = $"Letöltés... {speedText}";
                                        });
                                        lastUiUpdate = DateTime.Now;
                                    }
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