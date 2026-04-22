using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Reflection;

namespace VideoScreensaver {
    // Manages persistent storage for the screensaver.
    // Can't use IsolatedStorage because of a Windows bug (tries to use 8-char filename when screensaver runs on its own, not in preview mode).
    // Can't use Settings for the same reason.
    // Using the registry directly.
    static class PreferenceManager {

        public const string BASE_KEY = "VideoScreensaver";
        public const string VIDEO_PREFS_FILE = "Media";
        public const string VOLUME_PREFS_FILE = "Volume";
        public const string INTERVAL_PREFS_FILE = "Interval";
        public const string VOLUME_TIMEOUT_PREFS_FILE = "VolumeTimeout";
        public const string ALGORITHM_PREFS_FILE = "Algorithm";

        public const int ALGORITHM_SEQUENTIAL = 0;
        public const int ALGORITHM_RANDOM = 1;
        public const int ALGORITHM_RANDOM_NO_REPEAT = 2;

        public static void RemoveRegistryKeys()
        {
            RegistryKey software = Registry.CurrentUser.CreateSubKey("Software");
            var key = software.OpenSubKey(BASE_KEY);
            if (key != null)
            {
                key.Close();
                software.DeleteSubKeyTree(BASE_KEY);
            }
        }

        public static List<String> ReadVideoSettings() {
            List<String> videos = new List<String>();
            string videoStr = ReadStringValue(VIDEO_PREFS_FILE);
            if (videoStr.Length > 0) {
                videos.AddRange(videoStr.Split('\n'));
            }
            return videos;
        }

        public static void WriteVideoSettings(List<String> videoPaths) {
            WriteStringValue(VIDEO_PREFS_FILE, String.Join<object>("\n", videoPaths));
        }

        public static double ReadVolumeSetting() {
            try {
                return Convert.ToDouble(ReadStringValue(VOLUME_PREFS_FILE));
            }
            catch (System.FormatException) { }
            catch (System.OverflowException) { }
            return 0;
        }

        public static void WriteVolumeSetting(double volume) {
            WriteStringValue(VOLUME_PREFS_FILE, volume.ToString());
        }

        public static int ReadIntervalSetting()
        {
            try
            {
                return Convert.ToInt32(ReadStringValue(INTERVAL_PREFS_FILE));
            }
            catch (System.FormatException) { }
            catch (System.OverflowException) { }
            return 0;
        }

        public static void WriteIntervalSetting(int interval)
        {
            WriteStringValue(INTERVAL_PREFS_FILE, interval.ToString());
        }

        public static int ReadVolumeTimeoutSetting()
        {
            try
            {
                return Convert.ToInt32(ReadStringValue(VOLUME_TIMEOUT_PREFS_FILE));
            }
            catch (System.FormatException) { }
            catch (System.OverflowException) { }
            return 0;
        }

        public static void WriteVolumeTimeoutSetting(int interval)
        {
            WriteStringValue(VOLUME_TIMEOUT_PREFS_FILE, interval.ToString());
        }

        public static int ReadAlgorithmSetting()
        {
            try
            {
                return Convert.ToInt32(ReadStringValue(ALGORITHM_PREFS_FILE));
            }
            catch (System.FormatException) { }
            catch (System.OverflowException) { }
            return 0;
        }

        public static void WriteAlgorithmSetting(int alg)
        {
            WriteStringValue(ALGORITHM_PREFS_FILE, alg.ToString());
        }

        public const string NAS_SERVER_PREFS = "NasServer";
        public const string NAS_USERNAME_PREFS = "NasUsername";
        public const string NAS_PASSWORD_PREFS = "NasPassword";

        public static string ReadNasServer() { return ReadStringValue(NAS_SERVER_PREFS); }
        public static void WriteNasServer(string value) { WriteStringValue(NAS_SERVER_PREFS, value ?? ""); }

        public static string ReadNasUsername() { return ReadStringValue(NAS_USERNAME_PREFS); }
        public static void WriteNasUsername(string value) { WriteStringValue(NAS_USERNAME_PREFS, value ?? ""); }

        public static string ReadNasPassword() {
            string encrypted = ReadStringValue(NAS_PASSWORD_PREFS);
            if (string.IsNullOrEmpty(encrypted)) return "";
            try {
                byte[] bytes = System.Convert.FromBase64String(encrypted);
                byte[] decrypted = System.Security.Cryptography.ProtectedData.Unprotect(bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(decrypted);
            } catch { return ""; }
        }

        public static void WriteNasPassword(string value) {
            if (string.IsNullOrEmpty(value)) { WriteStringValue(NAS_PASSWORD_PREFS, ""); return; }
            try {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
                byte[] encrypted = System.Security.Cryptography.ProtectedData.Protect(bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                WriteStringValue(NAS_PASSWORD_PREFS, System.Convert.ToBase64String(encrypted));
            } catch { WriteStringValue(NAS_PASSWORD_PREFS, ""); }
        }

        private static Tuple<RegistryKey, RegistryKey> OpenRegistryKey(bool forceCreate = false) {
            RegistryKey software = Registry.CurrentUser.CreateSubKey("Software");
            if (forceCreate)
            {
                return new Tuple<RegistryKey, RegistryKey>(software.CreateSubKey(BASE_KEY), software);
            } else
            {
                return new Tuple<RegistryKey, RegistryKey>(software.OpenSubKey(BASE_KEY, true), software);
            }
        }

        private static string ReadStringValue(string valueName) {
            Tuple<RegistryKey, RegistryKey> appKey = OpenRegistryKey();
            try {
                return appKey.Item1 != null ? (appKey.Item1.GetValue(valueName, "") ?? "").ToString() : "";
            }
            finally {
                if (appKey.Item1 != null) appKey.Item1.Close();
                if (appKey.Item2 != null) appKey.Item2.Close();
            }
        }

        private static void WriteStringValue(string valueName, string valueData) {
            Tuple<RegistryKey, RegistryKey> appKey = OpenRegistryKey(true);
            try {
                appKey.Item1.SetValue(valueName, valueData);
            }
            finally {
                appKey.Item1.Close();
                appKey.Item2.Close();
            }
        }
    }
}
