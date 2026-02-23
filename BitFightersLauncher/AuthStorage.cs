using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BitFightersLauncher
{
    public class SavedLoginData
    {
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = false;
        public int UserId { get; set; } = 0;
        public string UserCreatedAt { get; set; } = string.Empty;
        public string ProfilePicture { get; set; } = string.Empty;
    }

    public static class AuthStorage
    {
        private const string EncryptionKey = "BitFighters_Secure_2024_Key_v1";

        public static string GetStorageDirectory()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string launcherDataPath = Path.Combine(appDataPath, "BitFightersLauncher");
            Directory.CreateDirectory(launcherDataPath);
            return launcherDataPath;
        }

        public static string GetSavedLoginPath() => Path.Combine(GetStorageDirectory(), "login.dat");

        public static SavedLoginData? Load()
        {
            try
            {
                string path = GetSavedLoginPath();
                if (!File.Exists(path)) return null;
                string encryptedData = File.ReadAllText(path);
                string decryptedJson = DecryptString(encryptedData);
                var data = JsonSerializer.Deserialize<SavedLoginData>(decryptedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return data;
            }
            catch
            {
                try { File.Delete(GetSavedLoginPath()); } catch { }
                return null;
            }
        }

        public static void Save(string username, string password, bool remember, int userId, string userCreatedAt, string profilePicture = "")
        {
            try
            {
                var data = new SavedLoginData
                {
                    Username = remember ? username : string.Empty,
                    PasswordHash = remember ? HashForStorage(password) : string.Empty,
                    RememberMe = remember,
                    UserId = remember ? userId : 0,
                    UserCreatedAt = remember ? userCreatedAt : string.Empty,
                    ProfilePicture = remember ? profilePicture : string.Empty
                };
                string json = JsonSerializer.Serialize(data);
                string encrypted = EncryptString(json);
                File.WriteAllText(GetSavedLoginPath(), encrypted);
            }
            catch
            {
                // ignore
            }
        }

        public static void Clear()
        {
            try { File.Delete(GetSavedLoginPath()); } catch { }
        }

        private static string EncryptString(string plainText)
        {
            byte[] iv = new byte[16];
            byte[] array;

            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(EncryptionKey.PadRight(32).Substring(0, 32));
                aes.IV = iv;

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                using (MemoryStream memoryStream = new MemoryStream())
                using (CryptoStream cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                using (StreamWriter streamWriter = new StreamWriter(cryptoStream))
                {
                    streamWriter.Write(plainText);
                    streamWriter.Flush();
                    cryptoStream.FlushFinalBlock();
                    array = memoryStream.ToArray();
                }
            }

            return Convert.ToBase64String(array);
        }

        private static string DecryptString(string cipherText)
        {
            byte[] iv = new byte[16];
            byte[] buffer = Convert.FromBase64String(cipherText);

            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(EncryptionKey.PadRight(32).Substring(0, 32));
                aes.IV = iv;
                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                using (MemoryStream memoryStream = new MemoryStream(buffer))
                using (CryptoStream cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                using (StreamReader streamReader = new StreamReader(cryptoStream))
                {
                    return streamReader.ReadToEnd();
                }
            }
        }

        private static string HashForStorage(string input)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input + "BitFighters_Salt_2024"));
                return Convert.ToBase64String(bytes);
            }
        }
    }
}
