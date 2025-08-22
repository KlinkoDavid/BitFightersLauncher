using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace BitFightersLauncher
{
    public partial class MainWindow : Window
    {
        private const string GameDownloadUrl = "https://bitfighters.eu/BitFighters/BitFighters.zip";
        private const string GameExecutableName = "BitFighters.exe";
        private string gameInstallPath = string.Empty;

        // A beállításfájl helye (pl. C:\Users\Felhasználó\AppData\Local\BitFightersLauncher\settings.txt)
        private readonly string settingsFilePath;

        public MainWindow()
        {
            InitializeComponent();

            // Beállításfájl útvonalának meghatározása
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            settingsFilePath = Path.Combine(appDataPath, "BitFightersLauncher", "settings.txt");

            // Az ablak betöltődésekor betöltjük a beállításokat és ellenőrizzük a játék állapotát
            Loaded += (s, e) => CheckGameInstallStatus();
        }

        /// <summary>
        /// Betölti a telepítési útvonalat a beállításfájlból.
        /// </summary>
        private void LoadInstallPath()
        {
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string savedPath = File.ReadAllText(settingsFilePath).Trim();
                    // Csak akkor fogadjuk el a mentett útvonalat, ha az egy létező könyvtár
                    if (Directory.Exists(savedPath))
                    {
                        gameInstallPath = savedPath;
                    }
                }
            }
            catch (Exception ex)
            {
                // Hiba esetén (pl. nincs olvasási jog) a program továbbra is működőképes marad
                Debug.WriteLine($"Hiba a beállítások betöltésekor: {ex.Message}");
            }
        }

        /// <summary>
        /// Elmenti a telepítési útvonalat a beállításfájlba.
        /// </summary>
        private void SaveInstallPath()
        {
            try
            {
                // Létrehozzuk a könyvtárat, ha nem létezik
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
                File.WriteAllText(settingsFilePath, gameInstallPath);
            }
            catch (Exception ex)
            {
                // Nem kritikus hiba, ha a mentés nem sikerül, ezért csak jelezzük
                MessageBox.Show($"A telepítési útvonal mentése nem sikerült. A launcher következő indításkor újra kérni fogja.\nHiba: {ex.Message}", "Mentési Hiba", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            // Először megpróbáljuk betölteni a mentett útvonalat
            LoadInstallPath();

            // Ha a betöltés után sincs érvényes útvonal, akkor használjuk az alapértelmezettet
            if (string.IsNullOrEmpty(gameInstallPath))
            {
                gameInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BitFighters");
            }

            string? executablePath = FindExecutable(gameInstallPath);
            if (!string.IsNullOrEmpty(executablePath))
            {
                gameInstallPath = Path.GetDirectoryName(executablePath);
                ButtonText.Text = "PLAY";
            }
            else
            {
                ButtonText.Text = "LETÖLTÉS";
            }
        }

        private async void HandleActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ButtonText.Text == "LETÖLTÉS") await DownloadAndInstallGameAsync();
            else if (ButtonText.Text == "PLAY") await StartGame();
        }

        private async Task DownloadAndInstallGameAsync()
        {
            var dialog = new CommonOpenFileDialog { IsFolderPicker = true, Title = "Válassza ki a telepítési mappát" };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                gameInstallPath = dialog.FileName;
                ButtonText.Visibility = Visibility.Collapsed;
                DownloadProgressBar.Visibility = Visibility.Visible;
                DownloadProgressBar.Value = 0;

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
                            while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                receivedBytes += bytesRead;
                                if (totalBytes > 0)
                                {
                                    int progressPercentage = (int)((double)receivedBytes / totalBytes * 100);
                                    Dispatcher.Invoke(() => DownloadProgressBar.Value = progressPercentage);
                                }
                            }
                        }
                    }
                    ButtonText.Text = "TELEPÍTÉS";
                    await InstallGameAsync(tempDownloadPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Hiba a letöltés során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ButtonText.Visibility = Visibility.Visible;
                    DownloadProgressBar.Visibility = Visibility.Collapsed;
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
                    Dispatcher.Invoke(() => MessageBox.Show($"Hiba a telepítés során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                finally
                {
                    if (File.Exists(downloadedFilePath)) File.Delete(downloadedFilePath);
                }
            });

            if (!string.IsNullOrEmpty(FindExecutable(gameInstallPath)))
            {
                MessageBox.Show("A játék telepítése sikeresen befejeződött!", "Siker", MessageBoxButton.OK, MessageBoxImage.Information);
                // Sikeres telepítés után elmentjük az útvonalat
                SaveInstallPath();
            }
            else
            {
                MessageBox.Show("A futtatható fájl nem található a telepítési mappában.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
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
                            WorkingDirectory = Path.GetDirectoryName(gameExecutablePath)
                        }
                    };
                    process.Start();
                    this.Hide();
                    await process.WaitForExitAsync();
                    this.Show();
                }
                else
                {
                    MessageBox.Show("A játék nem található. Lehet, hogy törölted, vagy áthelyezted a mappát.", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
                    // Töröljük a hibás beállítást, hogy a felhasználó újat választhasson
                    if (File.Exists(settingsFilePath)) File.Delete(settingsFilePath);
                    gameInstallPath = string.Empty; // Töröljük a memóriából is
                    CheckGameInstallStatus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hiba a játék indítása során: {ex.Message}", "Hiba", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}