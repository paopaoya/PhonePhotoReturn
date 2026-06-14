using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PhonePhotoReturn
{
    internal static class SettingsStore
    {
        private const string UploadDirectoryPrefix = "upload_directory=";
        private const string FileUploadDirectoryPrefix = "file_upload_directory=";
        private const string FixedTokenEnabledPrefix = "fixed_token_enabled=";
        private const string FixedTokenPrefix = "fixed_token=";

        public static string LoadUploadDirectory()
        {
            return LoadDirectory(UploadDirectoryPrefix, DefaultUploadDirectory);
        }

        public static string LoadFileUploadDirectory()
        {
            return LoadDirectory(FileUploadDirectoryPrefix, DefaultFileUploadDirectory);
        }

        public static void SaveUploadDirectory(string path)
        {
            SaveDirectory(UploadDirectoryPrefix, path);
        }

        public static void SaveFileUploadDirectory(string path)
        {
            SaveDirectory(FileUploadDirectoryPrefix, path);
        }

        public static bool LoadFixedTokenEnabled()
        {
            var value = LoadValue(FixedTokenEnabledPrefix);
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static string LoadFixedToken()
        {
            var value = LoadValue(FixedTokenPrefix);
            return IsUsableToken(value) ? value : null;
        }

        public static void SaveFixedTokenSettings(bool enabled, string token)
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var values = ReadSettings();
                values[FixedTokenEnabledPrefix] = enabled ? "true" : "false";
                if (IsUsableToken(token))
                {
                    values[FixedTokenPrefix] = token.Trim();
                }
                WriteSettings(values);
            }
            catch
            {
            }
        }

        private static string LoadDirectory(string prefix, string defaultPath)
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return defaultPath;
                }

                var lines = File.ReadAllLines(SettingsFilePath, Encoding.UTF8);
                foreach (var line in lines)
                {
                    if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var path = line.Substring(prefix.Length).Trim();
                    if (IsUsablePath(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
            }

            return defaultPath;
        }

        private static string LoadValue(string prefix)
        {
            try
            {
                var values = ReadSettings();
                string value;
                return values.TryGetValue(prefix, out value) ? value : null;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveDirectory(string prefix, string path)
        {
            if (!IsUsablePath(path))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                var values = ReadSettings();
                values[prefix] = path;
                WriteSettings(values);
            }
            catch
            {
            }
        }

        private static Dictionary<string, string> ReadSettings()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return values;
                }

                var lines = File.ReadAllLines(SettingsFilePath, Encoding.UTF8);
                foreach (var line in lines)
                {
                    if (line.StartsWith(UploadDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        values[UploadDirectoryPrefix] = line.Substring(UploadDirectoryPrefix.Length).Trim();
                    }
                    else if (line.StartsWith(FileUploadDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        values[FileUploadDirectoryPrefix] = line.Substring(FileUploadDirectoryPrefix.Length).Trim();
                    }
                    else if (line.StartsWith(FixedTokenEnabledPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        values[FixedTokenEnabledPrefix] = line.Substring(FixedTokenEnabledPrefix.Length).Trim();
                    }
                    else if (line.StartsWith(FixedTokenPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        values[FixedTokenPrefix] = line.Substring(FixedTokenPrefix.Length).Trim();
                    }
                }
            }
            catch
            {
            }

            return values;
        }

        private static void WriteSettings(Dictionary<string, string> values)
        {
            var builder = new StringBuilder();
            AppendDirectorySetting(builder, UploadDirectoryPrefix, values);
            AppendDirectorySetting(builder, FileUploadDirectoryPrefix, values);
            AppendRawSetting(builder, FixedTokenEnabledPrefix, values);
            AppendTokenSetting(builder, FixedTokenPrefix, values);
            File.WriteAllText(SettingsFilePath, builder.ToString(), Encoding.UTF8);
        }

        private static void AppendDirectorySetting(StringBuilder builder, string prefix, Dictionary<string, string> values)
        {
            string value;
            if (!values.TryGetValue(prefix, out value) || !IsUsablePath(value))
            {
                return;
            }

            builder.Append(prefix);
            builder.AppendLine(value);
        }

        private static void AppendRawSetting(StringBuilder builder, string prefix, Dictionary<string, string> values)
        {
            string value;
            if (!values.TryGetValue(prefix, out value) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            builder.Append(prefix);
            builder.AppendLine(value.Trim());
        }

        private static void AppendTokenSetting(StringBuilder builder, string prefix, Dictionary<string, string> values)
        {
            string value;
            if (!values.TryGetValue(prefix, out value) || !IsUsableToken(value))
            {
                return;
            }

            builder.Append(prefix);
            builder.AppendLine(value.Trim());
        }

        private static bool IsUsablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUsableToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            token = token.Trim();
            if (token.Length > 128)
            {
                return false;
            }

            foreach (var ch in token)
            {
                if ((ch >= 'a' && ch <= 'z')
                    || (ch >= 'A' && ch <= 'Z')
                    || (ch >= '0' && ch <= '9')
                    || ch == '-'
                    || ch == '_')
                {
                    continue;
                }
                return false;
            }

            return true;
        }

        private static string DefaultUploadDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "PhonePhotoReturn");
            }
        }

        private static string DefaultFileUploadDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads",
                    "PhonePhotoReturn");
            }
        }

        private static string SettingsDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PhonePhotoReturn");
            }
        }

        private static string SettingsFilePath
        {
            get { return Path.Combine(SettingsDirectory, "settings.txt"); }
        }
    }
}
