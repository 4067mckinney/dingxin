
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle(StudyFocus.ProductBrand.Name)]
[assembly: AssemblyDescription("Windows 专注计时与白名单")]
[assembly: AssemblyProduct(StudyFocus.ProductBrand.Name)]
[assembly: AssemblyCompany(StudyFocus.ProductBrand.Company)]
[assembly: AssemblyVersion("2.4.3.0")]

namespace StudyFocus
{
    internal static class Program
    {
        internal const string Name = ProductBrand.Name;
        private const string AppName = "StudyFocus";
        private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string UserInstall = Path.Combine(Local, "Programs", AppName);
        private static string install;
        private static string Executable { get { return Path.Combine(install, "StudyFocus.exe"); } }
        internal static readonly string Data = Path.Combine(Local, AppName);
        private static readonly string SessionKey = "Local\\StudyFocus." + WindowsIdentity.GetCurrent().User.Value;
        private static readonly string StartMenu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        private static readonly string DesktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk");
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StudyFocus";
        private const string SettingsKey = @"Software\StudyFocus";
        internal static bool UninstallOnExit;
        internal static bool UninstallDeleteData;
        private const string UninstallerName = "卸载.exe";
        internal static Version Version { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        [STAThread]
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception, "unhandled");
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                string path = LogCrash(e.Exception, "ui");
                MessageBox.Show("程序遇到问题，已写入日志：\n" + path, Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                install = RegisteredDirectory();
                string arg = args.Length == 0 ? "" : args[0];
                if (arg == "--cleanup" && args.Length >= 2)
                {
                    Cleanup(args[1], args.Length > 2 && args[2] == "--delete-data");
                    return;
                }
                string selfPath = ProcessGuard.ApplicationPath();
                bool uninstaller = string.Equals(Path.GetFileName(selfPath), UninstallerName, StringComparison.OrdinalIgnoreCase);
                if (uninstaller && args.Length == 0) arg = "--uninstall";
                bool installed = string.Equals(selfPath, Executable, StringComparison.OrdinalIgnoreCase) ||
                    (uninstaller && string.Equals(Path.GetDirectoryName(selfPath), install, StringComparison.OrdinalIgnoreCase));
                if (!installed)
                {
                    InstallApp();
                    return;
                }
                bool first;
                using (Mutex mutex = new Mutex(true, SessionKey + ".instance", out first))
                {
                    if (!first)
                    {
                        Signal(arg == "--uninstall" ? ".uninstall" : ".show");
                        return;
                    }
                    using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, SessionKey + ".show"))
                    using (EventWaitHandle uninstall = new EventWaitHandle(false, EventResetMode.AutoReset, SessionKey + ".uninstall"))
                    using (EventWaitHandle update = new EventWaitHandle(false, EventResetMode.AutoReset, SessionKey + ".update"))
                    {
                        StateFile file = new StateFile(Data);
                        FocusEngine engine = new FocusEngine(file, file.Load());
                        engine.Tick(DateTimeOffset.Now);
                        if (arg == "--uninstall" && CanUninstall(engine))
                        {
                            bool deleteData;
                            if (ConfirmUninstallFlow(null, UsesDarkTheme(engine.Settings), out deleteData) && CanUninstall(engine))
                                RemoveApp(deleteData);
                            return;
                        }
                        using (MainWindow window = new MainWindow(engine, show, uninstall, Data, arg == "--startup", update))
                        {
                            if (arg == "--uninstall") ShowUninstallLocked(window, UsesDarkTheme(engine.Settings));
                            Application.Run(window);
                        }
                        if (UninstallOnExit) RemoveApp(UninstallDeleteData);
                    }
                }
            }
            catch (Exception ex)
            {
                string path = LogCrash(ex, "startup");
                MessageBox.Show(ex.Message + (string.IsNullOrEmpty(path) ? "" : "\n\n日志：" + path), Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static string LogCrash(Exception error, string source)
        {
            try
            {
                Directory.CreateDirectory(Data);
                string file = Path.Combine(Data, "crash-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                var text = new StringBuilder();
                text.AppendLine("time: " + DateTimeOffset.Now.ToString("o"));
                text.AppendLine("source: " + source);
                text.AppendLine("version: " + Version);
                text.AppendLine("os: " + Environment.OSVersion.VersionString + " (" + (Environment.Is64BitOperatingSystem ? "x64" : "x86") + ")");
                text.AppendLine("runtime: " + Environment.Version);
                text.AppendLine("command: " + Environment.CommandLine);
                text.AppendLine();
                text.AppendLine(error == null ? "(no exception object)" : error.ToString());
                File.WriteAllText(file, text.ToString(), Encoding.UTF8);
                try
                {
                    string[] logs = Directory.GetFiles(Data, "crash-*.log");
                    if (logs.Length > 20)
                        foreach (string old in logs.OrderBy(item => item).Take(logs.Length - 20)) File.Delete(old);
                }
                catch { }
                return file;
            }
            catch { return null; }
        }

        internal static void RestartNow()
        {
            try { Process.Start(Executable); }
            catch { try { Process.Start(Application.ExecutablePath); } catch { return; } }
            Application.Exit();
        }
        internal static bool CanUninstall(FocusEngine engine)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            DateTimeOffset due;
            return !engine.Locked(now) && !engine.TryDue(now, out due);
        }

        internal static bool ConfirmUninstallFlow(IWin32Window owner, bool dark, out bool deleteData)
        {
            deleteData = false;
            if (!ConfirmUninstall(owner, dark)) return false;
            return MainWindow.SetupWindow.ConfirmDeleteData(owner, dark, Version, out deleteData);
        }
        internal static bool ConfirmUninstall(IWin32Window owner, bool dark)
        {
            using (var window = MainWindow.SetupWindow.Uninstall(install, dark, Version))
                return window.ShowOwned(owner) == DialogResult.OK;
        }

        internal static void ShowUninstallLocked(IWin32Window owner, bool dark)
        {
            MainWindow.SetupWindow.Notice(owner, "卸载", "暂时无法卸载",
                "专注期间和自动开始前 2 分钟暂时无法卸载，请等本轮专注结束。", dark, Version);
        }

        internal static bool UsesDarkTheme(Settings settings)
        {
            if (!settings.FollowSystemTheme) return settings.Dark;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key != null && object.Equals(key.GetValue("AppsUseLightTheme"), 0);
            }
            catch (UnauthorizedAccessException) { return settings.Dark; }
            catch (System.Security.SecurityException) { return settings.Dark; }
        }

        private static bool Signal(string name)
        {
            try
            {
                using (EventWaitHandle handle = EventWaitHandle.OpenExisting(SessionKey + name)) return handle.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }

        private static void InstallApp()
        {
            bool first;
            using (var setup = new Mutex(true, SessionKey + ".setup", out first))
                if (first) InstallOrUpdate();
        }

        private static void InstallOrUpdate()
        {
            var version = Version;
            string previousDirectory = install;
            string previousExecutable = Executable;
            Version previousVersion = InstalledVersion(previousExecutable);
            bool upgrading = previousVersion != null;
            if (upgrading && previousVersion == version)
            {
                if (MessageBox.Show("已安装相同版本 " + version.ToString(3) + "。\n要重新覆盖安装吗？", Name,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    LaunchInstalled();
                    return;
                }
            }
            if (upgrading && previousVersion > version)
                throw new InvalidOperationException("请使用相同或更新的安装包迁移现有版本。");

            StateFile file = new StateFile(Data);
            State state = file.Load();
            string target;
            bool startup;
            var locations = InstallLocations(UserInstall, upgrading ? previousDirectory : LastInstallDirectory(), AvailableInstallDrives());
            using (var window = MainWindow.SetupWindow.Install(locations, UserInstall, previousVersion, version,
                upgrading ? UsesDarkTheme(state.Settings) : true, state.Settings.Startup))
            {
                if (window.ShowOwned(null) != DialogResult.OK) return;
                target = window.Destination;
                startup = window.StartupEnabled;
            }
            string targetExecutable = Path.Combine(target, "StudyFocus.exe");
            bool replacesExisting = upgrading && string.Equals(previousExecutable, targetExecutable, StringComparison.OrdinalIgnoreCase);
            if (!replacesExisting && Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                throw new IOException("安装目录已有其他内容。请选择另一安装位置。\n" + target);

            state = file.Load();
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(StartMenu);
            bool stopped = false;
            Settings previousSettings = null;
            Cursor.Current = Cursors.WaitCursor;
            Application.DoEvents();
            InstallFiles(ProcessGuard.ApplicationPath(), upgrading ? previousExecutable : null, targetExecutable,
                delegate
                {
                    if (upgrading)
                    {
                        StopInstalledForUpgrade(previousExecutable, ref stopped);
                        state = file.Load();
                    }
                },
                delegate
                {
                    install = target;
                    RememberInstall(target);
                    InstallEmergencyTool(target);
                    InstallSources(target);
                    try { File.Copy(Executable, Path.Combine(target, UninstallerName), true); } catch (IOException) { }
                    RegisterInstalled(version, upgrading ? previousExecutable : null);
                    SetStartup(upgrading ? state.Settings.Startup : startup);
                    if (!upgrading && state.Settings.Startup != startup)
                    {
                        var originalSettings = state.Settings;
                        state.Settings = originalSettings.Copy();
                        state.Settings.Startup = startup;
                        file.Save(state);
                        previousSettings = originalSettings;
                    }
                    LaunchInstalled();
                },
                delegate(bool published)
                {
                    install = previousDirectory;
                    if (published)
                    {
                        if (upgrading)
                        {
                            RegisterInstalled(previousVersion, targetExecutable);
                            SetStartup(state.Settings.Startup);
                        }
                        else UnregisterInstalled(targetExecutable);
                    }
                    if (previousSettings != null)
                    {
                        state.Settings = previousSettings;
                        file.Save(state);
                    }
                    if (stopped) LaunchInstalled();
                });
        }

        internal static void InstallFiles(string source, string previousExecutable, string targetExecutable,
            Action prepare, Action publish, Action<bool> recover)
        {
            string suffix = Guid.NewGuid().ToString("N");
            string staging = Path.Combine(Path.GetDirectoryName(targetExecutable), "StudyFocus-install-" + suffix + ".tmp");
            string backup = previousExecutable == null ? null : previousExecutable + "." + suffix + ".bak";
            bool published = false;
            bool backedUp = false;
            bool success = false;
            try
            {
                File.Copy(source, staging, false);
                prepare();
                if (backup != null)
                {
                    File.Move(previousExecutable, backup);
                    backedUp = true;
                }
                File.Move(staging, targetExecutable);
                published = true;
                publish();
                success = true;
            }
            catch
            {
                if (published) File.Delete(targetExecutable);
                if (backedUp) File.Move(backup, previousExecutable);
                recover(published);
                throw;
            }
            finally
            {
                DeleteTemporaryFile(staging);
                if (success && backup != null) DeleteTemporaryFile(backup);
            }
        }

        internal static Version InstalledVersion(string executable)
        {
            if (!File.Exists(executable)) return null;
            try
            {
                var candidate = AssemblyName.GetAssemblyName(executable);
                var details = FileVersionInfo.GetVersionInfo(executable);
                return ProductBrand.Matches(details) ? candidate.Version : null;
            }
            catch (BadImageFormatException) { return null; }
        }

        private static string RegisteredDirectory()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                var path = key == null ? null : key.GetValue("InstallLocation") as string;
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
                {
                    path = Path.GetFullPath(path);
                    if (InstalledVersion(Path.Combine(path, "StudyFocus.exe")) != null) return path;
                }
            }
            if (InstalledVersion(Path.Combine(UserInstall, "StudyFocus.exe")) != null) return UserInstall;
            return AvailableInstallDrives().SelectMany(root => new[] { Path.Combine(root, Name), Path.Combine(root, ProductBrand.LegacyName) })
                .FirstOrDefault(folder => InstalledVersion(Path.Combine(folder, "StudyFocus.exe")) != null) ?? UserInstall;
        }

        private static bool IsNonCDrive(string root)
        {
            return !string.IsNullOrEmpty(root) && root.Length == 3 &&
                !string.Equals(root, @"C:\", StringComparison.OrdinalIgnoreCase);
        }

        internal static string[] InstallLocations(string userDirectory, string previousDirectory, System.Collections.Generic.IEnumerable<string> otherDrives)
        {
            return new[] { previousDirectory, userDirectory }.Where(path => !string.IsNullOrEmpty(path))
                .Concat(otherDrives.Where(IsNonCDrive).Where(root => !string.Equals(previousDirectory,
                    Path.Combine(root, ProductBrand.LegacyName), StringComparison.OrdinalIgnoreCase))
                    .Select(root => Path.Combine(root, Name)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string[] AvailableInstallDrives()
        {
            return DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady && IsNonCDrive(drive.Name))
                .Select(drive => drive.Name).OrderBy(drive => drive, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void StopInstalledForUpgrade(string executable, ref bool stopped)
        {
            int session;
            using (var current = Process.GetCurrentProcess()) session = current.SessionId;
            foreach (var process in Process.GetProcessesByName("StudyFocus"))
                using (process)
                    try
                    {
                        if (process.SessionId != session ||
                            !string.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                        if (Signal(".update") && process.WaitForExit(3500))
                        {
                            stopped = true;
                            continue;
                        }
                        process.Kill();
                        stopped = true;
                        if (!process.WaitForExit(5000)) throw new TimeoutException("旧版本仍在运行，请稍后重试更新。");
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        if (!process.HasExited) throw;
                    }
        }

        private static void LaunchInstalled()
        {
            using (Process process = Process.Start(new ProcessStartInfo(Executable) { UseShellExecute = true })) { }
        }

        private static void RegisterInstalled(Version version, string previousExecutable)
        {
            string name = FileVersionInfo.GetVersionInfo(Executable).ProductName;
            UpdateShortcut(StartMenu, name, Executable, previousExecutable);
            CreateDesktopShortcut();
            if (!File.Exists(DesktopLink))
            {
                try
                {
                    string start = Path.Combine(StartMenu, name + ".lnk");
                    if (File.Exists(start)) File.Copy(start, DesktopLink, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                key.SetValue("DisplayName", name);
                key.SetValue("DisplayVersion", version.ToString(3));
                key.SetValue("DisplayIcon", Executable);
                key.SetValue("Publisher", ProductBrand.Company);
                key.SetValue("InstallLocation", install);
                key.SetValue("UninstallString", Quote(Path.Combine(install, UninstallerName)) + " --uninstall");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        internal static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(AppName, Quote(Executable) + " --startup");
                else key.DeleteValue(AppName, false);
            }
        }

        private static void CreateDesktopShortcut()
        {
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell", true);
                object shell = Activator.CreateInstance(type);
                object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { DesktopLink });
                try
                {
                    link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Executable });
                    link.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { install });
                    link.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { Name });
                    link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                }
                finally
                {
                    if (link != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
            }
            catch (Exception) { }
        }
        internal static void UpdateShortcut(string directory, string name, string executable, string previousExecutable)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell", true);
            object shell = Activator.CreateInstance(type);
            object link = null;
            try
            {
                string path = Path.Combine(directory, name + ".lnk");
                if (File.Exists(path) && !ShortcutPointsTo(shell, path, executable, previousExecutable))
                {
                    path = Path.Combine(directory, name + " (" + AppName + ").lnk");
                    if (File.Exists(path) && !ShortcutPointsTo(shell, path, executable, previousExecutable)) return;
                }
                link = OpenShortcut(shell, path);
                link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { executable });
                link.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(executable) });
                link.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { name });
                link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                string obsolete = name == Name ? ProductBrand.LegacyName : Name;
                DeleteBrandedShortcuts(shell, directory, obsolete, executable, previousExecutable);
            }
            finally
            {
                if (link != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }

        private static object OpenShortcut(object shell, string path)
        {
            return shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        }

        private static bool ShortcutPointsTo(object shell, string path, string executable, string previousExecutable)
        {
            object shortcut = null;
            try
            {
                shortcut = OpenShortcut(shell, path);
                string target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null) as string;
                return string.Equals(target, executable, StringComparison.OrdinalIgnoreCase) ||
                    (previousExecutable != null && string.Equals(target, previousExecutable, StringComparison.OrdinalIgnoreCase));
            }
            catch (TargetInvocationException) { return false; }
            catch (System.Runtime.InteropServices.COMException) { return false; }
            finally { if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
        }

        private static void DeleteBrandedShortcuts(object shell, string directory, string name, string executable, string previousExecutable)
        {
            foreach (string file in new[] { name + ".lnk", name + " (" + AppName + ").lnk" })
            {
                string path = Path.Combine(directory, file);
                if (File.Exists(path) && ShortcutPointsTo(shell, path, executable, previousExecutable)) DeleteTemporaryFile(path);
            }
        }

        private static void InstallSources(string directory)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StudyFocus_Sources.zip"))
                {
                    if (stream == null) return;
                    string root = Path.Combine(directory, "源代码");
                    Directory.CreateDirectory(root);
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        memory.Position = 0;
                        using (var archive = new ZipArchive(memory, ZipArchiveMode.Read))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                                string path = Path.Combine(root, relative);
                                if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(path); continue; }
                                Directory.CreateDirectory(Path.GetDirectoryName(path));
                                using (var output = File.Create(path))
                                using (var input = entry.Open()) input.CopyTo(output);
                            }
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void InstallEmergencyTool(string directory)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StudyFocus_EmergencyStop.exe"))
                {
                    if (stream == null) return;
                    using (var file = File.Create(Path.Combine(directory, "应急停止.exe")))
                        stream.CopyTo(file);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private static void RemoveApp(bool deleteData)
        {
            try { if (deleteData && Directory.Exists(Data)) Directory.Delete(Data, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            UnregisterInstalled();
            string script = Path.Combine(install, "uninstall.cmd");
            var text = new StringBuilder();
            text.AppendLine("@echo off");
            text.AppendLine("ping -n 2 127.0.0.1 >nul");
            try { File.Delete(Path.Combine(install, "应急停止.exe")); } catch (IOException) { }
            try { if (Directory.Exists(Path.Combine(install, "源代码"))) Directory.Delete(Path.Combine(install, "源代码"), true); } catch (IOException) { }
            text.AppendLine("del /f /q \"" + Path.Combine(install, "应急停止.exe") + "\"");
            text.AppendLine("rmdir /s /q \"" + Path.Combine(install, "源代码") + "\"");
            text.AppendLine("del /f /q \"" + Path.Combine(install, UninstallerName) + "\"");
            text.AppendLine("del /f /q \"" + Executable + "\"");
            text.AppendLine("del /f /q \"%~f0\"");
            File.WriteAllText(script, text.ToString(), Encoding.Default);
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + script + "\"\"")
                { UseShellExecute = false, CreateNoWindow = true });
            }
            catch (Exception ex) { LogCrash(ex, "uninstall"); }
            Application.Exit();
        }

        private static void Cleanup(string processId, bool deleteData)
        {
            string self = ProcessGuard.ApplicationPath();
            string fileName = Path.GetFileNameWithoutExtension(self);
            Guid cleanupId;
            if (!string.Equals(Path.GetDirectoryName(self).TrimEnd('\\'), Data.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                !fileName.StartsWith("uninstall-", StringComparison.Ordinal) ||
                !Guid.TryParseExact(fileName.Substring("uninstall-".Length), "N", out cleanupId)) return;
            int pid;
            if (!int.TryParse(processId, out pid)) return;
            try
            {
                using (Process process = Process.GetProcessById(pid))
                    if (!string.Equals(process.MainModule.FileName, Executable, StringComparison.OrdinalIgnoreCase) || !process.WaitForExit(12000)) return;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            bool first;
            using (var setup = new Mutex(true, SessionKey + ".setup", out first))
            {
                if (!first || !string.Equals(install, RegisteredDirectory(), StringComparison.OrdinalIgnoreCase) ||
                    InstalledVersion(Executable) != Assembly.GetExecutingAssembly().GetName().Version) return;
                try { File.Delete(Path.Combine(install, UninstallerName)); } catch (IOException) { }
                File.Delete(Executable);
                UnregisterInstalled();
                if (deleteData)
                    try { if (Directory.Exists(Data)) Directory.Delete(Data, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try
                {
                    foreach (string entry in Directory.GetFileSystemEntries(install))
                    {
                        if (Directory.Exists(entry)) Directory.Delete(entry, true);
                        else File.Delete(entry);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            // Open the running temporary image for delete-on-close. Windows completes the deletion when it exits.
            try
            {
                using (FileStream stream = new FileStream(self, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.DeleteOnClose)) { }
            }
            catch (IOException) { }
        }

        private static void UnregisterInstalled(string publishedExecutable = null)
        {
            SetStartup(false);
            object shell = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell", true);
                shell = Activator.CreateInstance(type);
                string target = publishedExecutable ?? Executable;
                DeleteBrandedShortcuts(shell, StartMenu, Name, target, null);
                DeleteBrandedShortcuts(shell, StartMenu, ProductBrand.LegacyName, target, null);
                DeleteBrandedShortcuts(shell, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name, target, null);
                DeleteBrandedShortcuts(shell, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductBrand.LegacyName, target, null);
            }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (TargetInvocationException) { }
            finally { if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey, true))
                if (key != null) key.DeleteValue("LastInstall", false);
        }

        private static string LastInstallDirectory()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                return key == null ? null : key.GetValue("LastInstall") as string;
        }

        private static void RememberInstall(string directory)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue("LastInstall", directory);
        }

        private static void DeleteTemporaryFile(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}