using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml;
using Microsoft.Win32;

namespace StudyFocus
{
    internal sealed class AppRule
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Root { get; set; }
        public string GroupKey { get; set; }
        public List<string> Members { get; set; }

        public bool Contains(string path)
        {
            return string.Equals(Path, path, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(Root) && path.StartsWith(Root, StringComparison.OrdinalIgnoreCase) &&
                    !path.Substring(Root.Length).StartsWith(@"steamapps\", StringComparison.OrdinalIgnoreCase)) ||
                (Members != null && Members.Contains(path, StringComparer.OrdinalIgnoreCase));
        }

        public bool SameApp(AppRule other)
        {
            return Contains(other.Path) || other.Contains(Path) ||
                (!string.IsNullOrEmpty(GroupKey) && string.Equals(GroupKey, other.GroupKey, StringComparison.OrdinalIgnoreCase)) ||
                (Members != null && Members.Any(other.Contains)) ||
                (other.Members != null && other.Members.Any(Contains));
        }
    }

    internal sealed class AppCatalog
    {
        public sealed class Entry
        {
            public string Path { get; private set; }
            public string Name { get; private set; }
            public AppRule Rule { get; private set; }
            public bool IsSystem { get; private set; }
            internal string GroupKey { get; private set; }
            internal int Priority { get; private set; }

            public Entry(string path, string name, int source = 0)
            {
                Path = path;
                IsSystem = ProcessGuard.IsSystemApplication(path);
                var details = Details(path);
                string product = details == null ? "" : details.ProductName;
                string company = details == null ? "" : details.CompanyName;
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                Name = string.IsNullOrWhiteSpace(name) ? DisplayName(path, details) : name.Trim();
                string root = InstallRoot(path, product, company);
                string folder = System.IO.Path.GetDirectoryName(path);
                GroupKey = root != null ? root : !string.IsNullOrWhiteSpace(product) && !string.IsNullOrWhiteSpace(company) &&
                    !SharedFolder(folder) ? folder + "|" + company + "|" + product : path;
                Rule = new AppRule { Path = path, Name = Name, Root = root, GroupKey = GroupKey, Members = new List<string>() };
                string fileKey = Normalize(file);
                Priority = source;
                if (fileKey.Length > 2 && Normalize(product).Contains(fileKey)) Priority += 5;
                if (fileKey.Contains("helper") || fileKey.Contains("update") || fileKey.Contains("launcher") ||
                    fileKey.Contains("crash") || fileKey.Contains("report") || fileKey.Contains("dist")) Priority -= 8;
                if (Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) Priority -= 2;
            }

            internal void SetMembers(IEnumerable<Entry> entries)
            {
                Rule.Members = entries.Select(entry => entry.Path)
                    .Where(path => !string.Equals(path, Path, StringComparison.OrdinalIgnoreCase)).ToList();
                Rule.Root = entries.Select(entry => entry.Rule.Root).Where(root => root != null)
                    .OrderBy(root => root.Length).FirstOrDefault(root =>
                        entries.All(entry => entry.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)));
            }
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> WindowsAppNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.Windows.Photos", "照片" },
            { "Microsoft.WindowsCamera", "相机" },
            { "Microsoft.ScreenSketch", "截图工具" },
            { "Microsoft.WindowsAlarms", "时钟" },
            { "Microsoft.WindowsSoundRecorder", "录音机" },
            { "Microsoft.MicrosoftStickyNotes", "便笺" }
        };
        private static readonly HashSet<string> WindowsInboxPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.549981C3F5F10", "Microsoft.BingNews", "Microsoft.BingSearch", "Microsoft.BingWeather",
            "Microsoft.Clipchamp", "Microsoft.Copilot", "Microsoft.DesktopAppInstaller", "Microsoft.GamingApp",
            "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.HEIFImageExtension", "Microsoft.HEVCVideoExtension",
            "Microsoft.Microsoft3DViewer", "Microsoft.MicrosoftOfficeHub", "Microsoft.MicrosoftSolitaireCollection",
            "Microsoft.MicrosoftStickyNotes", "Microsoft.MixedReality.Portal", "Microsoft.MSPaint", "Microsoft.OneConnect",
            "Microsoft.OutlookForWindows", "Microsoft.Paint", "Microsoft.People", "Microsoft.PowerAutomateDesktop", "Microsoft.RawImageExtension",
            "Microsoft.ScreenSketch", "Microsoft.SecHealthUI", "Microsoft.SkypeApp", "Microsoft.StorePurchaseApp",
            "Microsoft.Todos", "Microsoft.VP9VideoExtensions", "Microsoft.WebMediaExtensions", "Microsoft.WebpImageExtension",
            "Microsoft.WindowsTerminal", "Microsoft.WindowsTerminalPreview", "Microsoft.YourPhone", "Microsoft.ZuneMusic",
            "Microsoft.ZuneVideo", "MicrosoftTeams", "microsoft.windowscommunicationsapps", "MicrosoftCorporationII.QuickAssist",
            "MicrosoftCorporationII.WindowsSubsystemForLinux", "MicrosoftCorporationII.WinAppRuntime.Main",
            "MicrosoftCorporationII.WinAppRuntime.Singleton"
        };
        private static readonly HashSet<string> OfficeHelperExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "msoadfsb.exe", "msoasb.exe", "msoxmled.exe", "onenotem.exe", "sdxhelper.exe", "setlang.exe", "skypeserver.exe"
        };

        public static IList<Entry> Discover()
        {
            var catalog = new AppCatalog();
            catalog.ReadShortcuts();
            catalog.ReadRegisteredApplications();
            catalog.ReadWindowsApplications();
            catalog.ReadRunningApplications();
            var groups = new List<List<Entry>>();
            foreach (var entry in catalog.entries.Values)
            {
                var nearby = groups.Where(group => group[0].IsSystem == entry.IsSystem &&
                    group.Any(member => member.Rule.SameApp(entry.Rule))).ToList();
                var merged = new List<Entry> { entry };
                foreach (var group in nearby)
                {
                    merged.AddRange(group);
                    groups.Remove(group);
                }
                groups.Add(merged);
            }
            return groups.Select(group =>
            {
                var primary = group.OrderByDescending(x => x.Priority).ThenBy(x => x.Name.Length).First();
                primary.SetMembers(group);
                return primary;
            }).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Path).ToList();
        }

        public static bool IsSelectable(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && System.IO.Path.IsPathRooted(path) &&
                string.Equals(System.IO.Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) &&
                !ProcessGuard.ExcludedFromCatalog(path);
        }

        internal static bool IsKnownWindowsPackage(string path)
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i + 2 < parts.Length; i++)
            {
                if (!parts[i].Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                string[] identity = parts[i + 1].Split('_');
                if (identity.Length != 5) continue;
                string publisher = identity[4];
                string package = identity[0];
                if (publisher.Equals("yxz26nhyzhsrt", StringComparison.OrdinalIgnoreCase) &&
                    package.Equals("Clipchamp.Clipchamp", StringComparison.OrdinalIgnoreCase)) return true;
                if (!publisher.Equals("8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase) &&
                    !publisher.Equals("cw5n1h2txyewy", StringComparison.OrdinalIgnoreCase)) continue;
                return WindowsAppNames.ContainsKey(package) || WindowsInboxPackages.Contains(package) ||
                    package.StartsWith("Microsoft.Windows", StringComparison.OrdinalIgnoreCase) ||
                    package.StartsWith("Microsoft.Xbox", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private void ReadWindowsApplications()
        {
            const string packages = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            try
            {
                using (var registry = Registry.CurrentUser.OpenSubKey(packages))
                {
                    if (registry == null) return;
                    foreach (string key in registry.GetSubKeyNames())
                    {
                        string name;
                        if (!key.EndsWith("_8wekyb3d8bbwe", StringComparison.OrdinalIgnoreCase) ||
                            !WindowsAppNames.TryGetValue(key.Split('_')[0], out name)) continue;
                        using (var package = registry.OpenSubKey(key))
                        {
                            string root = package == null ? null : package.GetValue("PackageRootFolder") as string;
                            if (string.IsNullOrEmpty(root)) continue;
                            try
                            {
                                var manifest = new XmlDocument { XmlResolver = null };
                                manifest.Load(Path.Combine(root, "AppxManifest.xml"));
                                var exe = manifest.SelectSingleNode("//*[local-name()='Applications']/*[local-name()='Application']/@Executable");
                                if (exe != null && !Path.IsPathRooted(exe.Value)) Add(Path.Combine(root, exe.Value), name, 20);
                            }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                            catch (XmlException) { }
                        }
                    }
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static FileVersionInfo Details(string path)
        {
            try { if (File.Exists(path)) return FileVersionInfo.GetVersionInfo(path); }
            catch (System.ComponentModel.Win32Exception) { }
            return null;
        }

        public static string DisplayName(string path)
        {
            return DisplayName(path, Details(path));
        }

        private static string DisplayName(string path, FileVersionInfo details)
        {
            return details != null && !string.IsNullOrWhiteSpace(details.FileDescription)
                ? details.FileDescription.Trim() : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        private static readonly string[] ProgramRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        };

        private static readonly HashSet<string> SharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "programs", "programfiles", "commonfiles", "shared", "tools", "utilities", "downloads",
            "desktop", "documents", "bin", "apps", "games", "windowsapps", "microsoftoffice", "office16"
        };
        private static readonly HashSet<string> VendorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "adobe", "autodesk", "google", "microsoft", "mozilla", "tencent", "jetbrains", "apple",
            "blackmagicdesign", "videolan", "valve"
        };

        private static string Normalize(string str)
        {
            return new string((str ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static bool SharedFolder(string folder)
        {
            return string.IsNullOrEmpty(folder) || folder.Split('\\').Any(segment =>
            {
                string key = Normalize(segment);
                return key == "commonfiles" || key == "microsoftoffice" || key == "office16";
            });
        }

        private static string InstallRoot(string path, string product, string company)
        {
            string directory = Path.GetDirectoryName(path);
            if (SharedFolder(directory)) return null;
            string companyKey = Normalize(company);
            foreach (string basePath in ProgramRoots)
            {
                if (string.IsNullOrEmpty(basePath) || !directory.StartsWith(basePath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = directory.Substring(basePath.Length + 1).Split('\\');
                string first = Normalize(parts[0]);
                int index = VendorNames.Contains(first) || (first.Length >= 4 && companyKey.StartsWith(first, StringComparison.Ordinal)
                    && !Normalize(product).StartsWith(first, StringComparison.Ordinal)) ? 1 : 0;
                if (parts.Length <= index || SharedNames.Contains(Normalize(parts[index]))) return null;
                return (index == 0 ? Path.Combine(basePath, parts[0]) : Path.Combine(basePath, parts[0], parts[1])) + "\\";
            }
            string productKey = Normalize(product);
            if (productKey.Length < 5) return null;
            for (int i = 0; i < 3 && !string.IsNullOrEmpty(directory); i++, directory = Path.GetDirectoryName(directory))
            {
                string folder = Normalize(Path.GetFileName(directory));
                if (folder.Length >= 5 && !SharedNames.Contains(folder) && !VendorNames.Contains(folder) &&
                    (folder.StartsWith(productKey, StringComparison.Ordinal) || productKey.StartsWith(folder, StringComparison.Ordinal)) &&
                    !companyKey.StartsWith(folder, StringComparison.Ordinal))
                    return directory + "\\";
            }
            return null;
        }

        private void Add(string value, string name = null, int source = 0)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string path;
            try
            {
                path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                if (!Path.IsPathRooted(path)) return;
                path = Path.GetFullPath(path);
                if (!IsSelectable(path) || !File.Exists(path) || entries.ContainsKey(path) || IsDiscoveryHelper(path)) return;
                string filename = Path.GetFileNameWithoutExtension(path);
                if (IsMaintenanceShortcut(filename) || (filename.StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
                    filename.Length > 5 && filename.Skip(5).All(char.IsDigit))) return;
            }
            catch (ArgumentException) { return; }
            catch (NotSupportedException) { return; }
            catch (IOException) { return; }
            entries.Add(path, new Entry(path, name, source));
        }

        internal static bool IsDiscoveryHelper(string path)
        {
            var installerCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Installer\");
            if (path.StartsWith(installerCache, StringComparison.OrdinalIgnoreCase)) return true;
            return OfficeHelperExecutables.Contains(Path.GetFileName(path)) && ProgramRoots.Any(root =>
                path.StartsWith(Path.Combine(root, @"Microsoft Office\"), StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(Path.Combine(root, @"Microsoft Office 15\"), StringComparison.OrdinalIgnoreCase));
        }

        private void ReadShortcuts()
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                foreach (var location in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
                    Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
                {
                    string folder = Environment.GetFolderPath(location);
                    string[] shortcuts;
                    try { shortcuts = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories); }
                    catch (UnauthorizedAccessException) { continue; }
                    catch (IOException) { continue; }
                    foreach (string shortcut in shortcuts)
                    {
                        string name = Path.GetFileNameWithoutExtension(shortcut);
                        if (IsMaintenanceShortcut(name)) continue;
                        object link = null;
                        try
                        {
                            link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcut });
                            string target = (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                            Add(target, name, 10);
                        }
                        catch (TargetInvocationException) { }
                        catch (COMException) { }
                        finally { if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link); }
                    }
                }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        private static bool IsMaintenanceShortcut(string name)
        {
            return name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 || name.Contains("卸载");
        }

        private void ReadRegisteredApplications()
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    try
                    {
                        using (var root = RegistryKey.OpenBaseKey(hive, view))
                        using (var applications = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                        {
                            if (applications == null) continue;
                            foreach (string key in applications.GetSubKeyNames())
                                using (var app = applications.OpenSubKey(key))
                                    if (app != null) Add(app.GetValue(null) as string, null, 5);
                        }
                    }
                    catch (System.Security.SecurityException) { }
                    catch (UnauthorizedAccessException) { }
                }
        }

        private void ReadRunningApplications()
        {
            foreach (Process process in Process.GetProcesses())
                using (process)
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero) Add(process.MainModule.FileName);
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                    catch (NotSupportedException) { }
        }
    }
}
