using Microsoft.Web.WebView2.Core;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace BitFightersLauncher
{
    public partial class OAuthWindow : Window
    {
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
        private readonly string _authUrl;
        private readonly string _redirectUriPrefix;
        private readonly LocalizedTexts _texts;

        public string? AuthorizationCode { get; private set; }

        public OAuthWindow(string authUrl, string redirectUriPrefix)
        {
            InitializeComponent();
            _authUrl = authUrl;
            _redirectUriPrefix = redirectUriPrefix;
            Loaded += OAuthWindow_Loaded;
            SizeChanged += (_, _) => ApplyRoundedClip();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyRoundedClip();
        }

        private void ApplyRoundedClip()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int w = (int)(ActualWidth * dpiX);
            int h = (int)(ActualHeight * dpiY);
            int diameter = (int)(15 * 2 * dpiX); // CornerRadius="15"

            var hRgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, diameter, diameter);
            int result = SetWindowRgn(hwnd, hRgn, true);
            if (result == 0)
                DeleteObject(hRgn);
        }

        private async void OAuthWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BitFightersLauncher", "WebView2");

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await AuthBrowser.EnsureCoreWebView2Async(env);
                AuthBrowser.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                AuthBrowser.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                AuthBrowser.Source = new Uri(_authUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(_texts.ErrorMessageFormat, ex.Message),
                    _texts.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri.StartsWith(_redirectUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                var uri = new Uri(e.Uri);
                var idToken = ParseQueryParam(uri.Fragment, "id_token") ?? ParseQueryParam(uri.Query, "id_token");
                var authCode = ParseQueryParam(uri.Query, "code") ?? ParseQueryParam(uri.Fragment, "code");

                AuthorizationCode = !string.IsNullOrEmpty(idToken) ? idToken : authCode;
                DialogResult = !string.IsNullOrEmpty(AuthorizationCode);
                Close();
            }
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private static string? ParseQueryParam(string query, string param)
        {
            query = query.TrimStart('?', '#');

            if (string.IsNullOrWhiteSpace(query))
                return null;

            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0] == param)
                    return Uri.UnescapeDataString(parts[1]);
            }
            return null;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }


        private sealed record LocalizedTexts(
            string WindowTitle,
            string HeaderText,
            string LoadingText,
            string ErrorTitle,
            string ErrorMessageFormat);
    }
}
