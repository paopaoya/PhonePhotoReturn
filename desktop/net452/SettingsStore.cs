using System;
using System.IO;
using System.Text;

namespace PhonePhotoReturn
{
    internal static class SettingsStore
    {
        private const string UploadDirectoryPrefix = "upload_directory=";

        public static string LoadUploadDirectory()
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    return DefaultUploadDirectory;
                }

                var lines = File.ReadAllLines(SettingsFilePath, Encoding.UTF8);
                foreach (var line in lines)
                {
                    if (!line.StartsWith(UploadDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var path = line.Substring(UploadDirectoryPrefix.Length).Trim();
                    if (IsUsablePath(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
            }

            return DefaultUploadDirectory;
        }

        public static void SaveUploadDirectory(string path)
        {
            if (!IsUsablePath(path))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(SettingsFilePath, UploadDirectoryPrefix + path, Encoding.UTF8);
            }
            catch
            {
            }
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

        private static string DefaultUploadDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
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
