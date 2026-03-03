using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BitFightersLauncher
{
    internal static class SecureEnv
    {
        private static readonly Lazy<Dictionary<string, string>> DotEnvValues = new(LoadDotEnv);

        public static string ApiUrl => GetOptional("BF_API_URL", "https://bitfighters.eu/backend/Launcher/main_proxy.php");
        public static string GoogleClientId => GetOptional("BF_GOOGLE_CLIENT_ID", "1094285883096-imblg0jviac4s2h9nkkr4ce77or57t6v.apps.googleusercontent.com");
        public static string GoogleRedirectUri => GetOptional("BF_GOOGLE_REDIRECT_URI", "https://bitfighters.eu");

        // Not sensitive, but configurable from the same .env file
        public static string GameDownloadUrl => GetOptional("BF_GAME_DOWNLOAD_URL", "https://bitfighters.eu/BitFighters.zip");
        public static string VersionCheckUrl => GetOptional("BF_VERSION_CHECK_URL", "https://bitfighters.eu/version.txt");

        private static string GetOptional(string key, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(key);

            if (string.IsNullOrWhiteSpace(value) && DotEnvValues.Value.TryGetValue(key, out var dotEnvValue))
                value = dotEnvValue;

            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return DecodeIfEncrypted(value);
        }

        private static string DecodeIfEncrypted(string raw)
        {
            const string prefix = "enc:";
            if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return raw;

            var cipherBase64 = raw.Substring(prefix.Length).Trim();
            var cipherBytes = Convert.FromBase64String(cipherBase64);
            var plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }

        private static Dictionary<string, string> LoadDotEnv()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var envPath = FindDotEnvPath();

            if (string.IsNullOrWhiteSpace(envPath) || !File.Exists(envPath))
                return result;

            foreach (var line in File.ReadAllLines(envPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim().Trim('"');

                if (!string.IsNullOrWhiteSpace(key))
                    result[key] = value;
            }

            return result;
        }

        private static string? FindDotEnvPath()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && current != null; i++)
            {
                var candidate = Path.Combine(current.FullName, ".env");
                if (File.Exists(candidate))
                    return candidate;

                current = current.Parent;
            }

            return null;
        }
    }
}
