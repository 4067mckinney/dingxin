using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace StudyFocus
{
    internal static class SystemProxy
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private static string BackupPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudyFocus");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "proxy-backup.txt");
            }
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int length);

        private static void Refresh()
        {
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }

        internal static bool Held { get { return File.Exists(BackupPath); } }

        internal static void Enable(int port)
        {
            try
            {
                string target = "127.0.0.1:" + port;
                if (Held)
                {
                    using (RegistryKey current = Registry.CurrentUser.OpenSubKey(Key))
                    {
                        object enabled = current == null ? null : current.GetValue("ProxyEnable");
                        object server = current == null ? null : current.GetValue("ProxyServer");
                        if (enabled != null && Convert.ToInt32(enabled) == 1 && string.Equals(server as string, target, StringComparison.OrdinalIgnoreCase)) return;
                    }
                }
                if (!Held)
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key))
                    {
                        object enabled = key == null ? null : key.GetValue("ProxyEnable");
                        object server = key == null ? null : key.GetValue("ProxyServer");
                        object over = key == null ? null : key.GetValue("ProxyOverride");
                        object auto = key == null ? null : key.GetValue("AutoConfigURL");
                        var text = new StringBuilder();
                        text.AppendLine(Pack(enabled == null ? "" : enabled.ToString()));
                        text.AppendLine(Pack(server == null ? "" : server.ToString()));
                        text.AppendLine(Pack(over == null ? "" : over.ToString()));
                        text.AppendLine(Pack(auto == null ? "" : auto.ToString()));
                        File.WriteAllText(BackupPath, text.ToString(), Encoding.UTF8);
                    }
                }
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
                {
                    key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", "127.0.0.1:" + port, RegistryValueKind.String);
                    key.SetValue("ProxyOverride", "<-loopback>", RegistryValueKind.String);
                    key.DeleteValue("AutoConfigURL", false);
                }
                Refresh();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }

        internal static void Disable()
        {
            if (!File.Exists(BackupPath)) return;
            try
            {
                string[] lines = File.ReadAllLines(BackupPath, Encoding.UTF8);
                if (lines.Length >= 4)
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
                    {
                        string enabled = Unpack(lines[0]);
                        if (enabled.Length == 0) key.DeleteValue("ProxyEnable", false);
                        else key.SetValue("ProxyEnable", int.Parse(enabled), RegistryValueKind.DWord);

                        string server = Unpack(lines[1]);
                        if (server.Length == 0) key.DeleteValue("ProxyServer", false);
                        else key.SetValue("ProxyServer", server, RegistryValueKind.String);

                        string over = Unpack(lines[2]);
                        if (over.Length == 0) key.DeleteValue("ProxyOverride", false);
                        else key.SetValue("ProxyOverride", over, RegistryValueKind.String);

                        string auto = Unpack(lines[3]);
                        if (auto.Length == 0) key.DeleteValue("AutoConfigURL", false);
                        else key.SetValue("AutoConfigURL", auto, RegistryValueKind.String);
                    }
                }
                File.Delete(BackupPath);
                Refresh();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (FormatException) { }
            catch (System.Security.SecurityException) { }
        }

        internal static void Recover()
        {
            if (File.Exists(BackupPath)) Disable();
        }

        private static string Pack(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Unpack(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch (FormatException) { return ""; }
        }
    }
}
