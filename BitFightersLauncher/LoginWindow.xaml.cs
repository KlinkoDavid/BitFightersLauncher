using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BitFightersLauncher
{
    public class LoginApiResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public UserData? user { get; set; }
    }

    public class UserData
    {
        public int id { get; set; }
        public string username { get; set; } = string.Empty;
        public int highest_score { get; set; }
        public string created_at { get; set; } = string.Empty;
    }

    // Event args for login events
    public class LoginEventArgs : EventArgs
    {
        public string Username { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string UserCreatedAt { get; set; } = string.Empty;
    }

    public partial class LoginWindow : Window
    {
        // MySQL Proxy API - Updated to use the real database
        private const string ApiUrl = "https://bitfighters.eu/backend/Launcher/main_proxy.php";

        // Events for login success/failure
        public event EventHandler<LoginEventArgs>? LoginSucceeded;
        public event EventHandler? LoginCancelled;

        public bool LoginSuccessful { get; private set; } = false;
        public string LoggedInUsername { get; private set; } = string.Empty;
        public int UserId { get; private set; } = 0;
        public string UserCreatedAt { get; private set; } = string.Empty;

        private bool isPasswordVisible = false;

        public LoginWindow()
        {
            InitializeComponent();

            LoadSavedLogin();

            UsernameTextBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) FocusPassword(); };
            PasswordBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) LoginButton_Click(null, null); };
            PasswordTextBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) LoginButton_Click(null, null); };

            PasswordBox.PasswordChanged += (s, e) =>
            {
                if (!isPasswordVisible)
                    PasswordTextBox.Text = PasswordBox.Password;
            };

            PasswordTextBox.TextChanged += (s, e) =>
            {
                if (isPasswordVisible)
                    PasswordBox.Password = PasswordTextBox.Text;
            };
        }

        private void LoadSavedLogin()
        {
            var saved = AuthStorage.Load();
            if (saved != null && saved.RememberMe && !string.IsNullOrEmpty(saved.Username))
            {
                UsernameTextBox.Text = saved.Username;
                RememberMeCheckBox.IsChecked = true;
                PasswordBox.Focus();
            }
        }

        private void SaveLogin(string username, string password, bool remember)
        {
            AuthStorage.Save(username, password, remember, UserId, UserCreatedAt);
        }

        private void FocusPassword()
        {
            if (isPasswordVisible)
                PasswordTextBox.Focus();
            else
                PasswordBox.Focus();
        }

        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            isPasswordVisible = !isPasswordVisible;

            var button = sender as Button;
            var iconText = FindChild<TextBlock>(button, "IconText");

            if (isPasswordVisible)
            {
                PasswordTextBox.Text = PasswordBox.Password;
                PasswordTextBox.Visibility = Visibility.Visible;
                PasswordBox.Visibility = Visibility.Collapsed;

                if (iconText != null)
                    iconText.Text = "🙈";

                PasswordTextBox.Focus();
                PasswordTextBox.CaretIndex = PasswordTextBox.Text.Length;
            }
            else
            {
                PasswordBox.Password = PasswordTextBox.Text;
                PasswordBox.Visibility = Visibility.Visible;
                PasswordTextBox.Visibility = Visibility.Collapsed;

                if (iconText != null)
                    iconText.Text = "👁";

                PasswordBox.Focus();
            }
        }

        private string GetCurrentPassword()
        {
            return isPasswordVisible ? PasswordTextBox.Text : PasswordBox.Password;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            LoginCancelled?.Invoke(this, EventArgs.Empty);
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameTextBox.Text.Trim();
            string password = GetCurrentPassword();
            bool rememberMe = RememberMeCheckBox.IsChecked ?? false;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Kerem toltsd ki az osszes mezot!");
                return;
            }

            LoginButton.IsEnabled = false;
            LoginButtonText.Text = "BEJELENTKEZES...";

            try
            {
                bool success = await LoginWithProxyAsync(username, password);

                if (success)
                {
                    SaveLogin(username, password, rememberMe);

                    LoginSuccessful = true;

                    LoginSucceeded?.Invoke(this, new LoginEventArgs
                    {
                        Username = LoggedInUsername,
                        UserId = UserId,
                        UserCreatedAt = UserCreatedAt
                    });

                    var fadeOutAnimation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                    fadeOutAnimation.Completed += (s, a) => { this.Close(); };
                    this.BeginAnimation(Window.OpacityProperty, fadeOutAnimation);
                }
                else
                {
                    ShowError("Hibás felhasználónév vagy jelszó!");
                    LoginButton.IsEnabled = true;
                    LoginButtonText.Text = "BEJELENTKEZÉS";
                }
            }
            catch (Exception ex)
            {
                ShowError($"Bejelentkezési hiba: {ex.Message}");
                LoginButton.IsEnabled = true;
                LoginButtonText.Text = "BEJELENTKEZÉS";
            }
        }

        private async Task<bool> LoginWithProxyAsync(string username, string password)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(10);

                    var loginData = new
                    {
                        action = "login",
                        username = username,
                        password = password
                    };

                    string jsonContent = JsonSerializer.Serialize(loginData);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await httpClient.PostAsync(ApiUrl, content);
                    string responseText = await response.Content.ReadAsStringAsync();

                    var apiResponse = JsonSerializer.Deserialize<LoginApiResponse>(responseText);

                    if (apiResponse?.success == true && apiResponse.user != null)
                    {
                        UserId = apiResponse.user.id;
                        LoggedInUsername = apiResponse.user.username;
                        UserCreatedAt = apiResponse.user.created_at;

                        return true;
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MySQL Proxy login error: {ex.Message}");
                return false;
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            var showStoryboard = (Storyboard)this.FindResource("ShowError");
            showStoryboard.Begin(ErrorBorder);

            Task.Delay(4000).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    var hideStoryboard = (Storyboard)this.FindResource("HideError");
                    hideStoryboard.Begin(ErrorBorder);
                });
            });
        }

        private T? FindChild<T>(DependencyObject parent, string childName) where T : DependencyObject
        {
            if (parent == null) return null;

            T? foundChild = null;

            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                T? childType = child as T;
                if (childType == null)
                {
                    foundChild = FindChild<T>(child, childName);
                    if (foundChild != null) break;
                }
                else if (!string.IsNullOrEmpty(childName))
                {
                    var frameworkElement = child as FrameworkElement;
                    if (frameworkElement != null && frameworkElement.Name == childName)
                    {
                        foundChild = (T)child;
                        break;
                    }
                    else
                    {
                        foundChild = FindChild<T>(child, childName);
                        if (foundChild != null) break;
                    }
                }
                else
                {
                    foundChild = (T)child;
                    break;
                }
            }
            return foundChild;
        }

        #region Floating Label Event Handlers

        private void UsernameTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var storyboard = (Storyboard)this.FindResource("FloatLabelUp");
            storyboard.Begin();
            UsernameBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFA726"));
        }

        private void UsernameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(UsernameTextBox.Text))
            {
                var storyboard = (Storyboard)this.FindResource("FloatLabelDown");
                storyboard.Begin();
            }
            UsernameBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF"));
        }

        private void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(UsernameTextBox.Text) && UsernameLabel != null)
            {
                var storyboard = (Storyboard)this.FindResource("FloatLabelUp");
                storyboard.Begin();
            }
        }

        private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var storyboard = (Storyboard)this.FindResource("FloatPasswordLabelUp");
            storyboard.Begin();
            PasswordBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFA726"));
        }

        private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(PasswordBox.Password))
            {
                var storyboard = (Storyboard)this.FindResource("FloatPasswordLabelDown");
                storyboard.Begin();
            }
            PasswordBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF"));
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(PasswordBox.Password) && PasswordLabel != null)
            {
                var storyboard = (Storyboard)this.FindResource("FloatPasswordLabelUp");
                storyboard.Begin();
            }
        }

        private void PasswordTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var storyboard = (Storyboard)this.FindResource("FloatPasswordLabelUp");
            storyboard.Begin();
            PasswordBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFA726"));
        }

        private void PasswordTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(PasswordTextBox.Text))
            {
                var storyboard = (Storyboard)this.FindResource("FloatPasswordLabelDown");
                storyboard.Begin();
            }
            PasswordBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF"));
        }

        private void PasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(PasswordTextBox.Text) && PasswordLabel != null)
            {
                var storyboard = (Storyboard)this.FindResource("FloatPasswordLabelUp");
                storyboard.Begin();
            }
        }

        #endregion
    }
}