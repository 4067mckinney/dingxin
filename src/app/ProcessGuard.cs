using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace StudyFocus
{
    internal sealed class ProcessGuard
    {
        internal sealed class BlockedWindow
        {
            public string Path { get; set; }
            public bool Edge { get; set; }
            public bool Minimized { get; set; }
        }

        private static readonly string windows = Folder(Environment.SpecialFolder.Windows);
        private static readonly string programs = Folder(Environment.SpecialFolder.ProgramFiles);
        private static readonly string programsX86 = Folder(Environment.SpecialFolder.ProgramFilesX86);
        private static readonly string localFiles = Folder(Environment.SpecialFolder.LocalApplicationData);
        private readonly int ownPid;
        private string focusEdgeProfile, focusEdgeProxy, focusEdgeExecutable;
        private int focusEdgePid;
        private long focusEdgeStart;
        private List<AppRule> rules;
        private HashSet<string> exactPaths;
        private IntPtr previousWindow;
        private bool anyEdgeAllowed;

        internal void SetSystemProxyMode(bool enabled) { anyEdgeAllowed = enabled; }
        private int previousPid;
        private string previousPath;
        private bool previousAllowed, previousEdge;
        private bool retriedNormalWindow;
        private DateTime lastAttempt;
        public string ForegroundPath { get { return previousWindow != IntPtr.Zero && previousAllowed ? previousPath : null; } }
#if FOCUS_TEST
        public string TestFolder { get; set; }
        public Func<IntPtr> TestForeground { get; set; }
#endif

        private static readonly HashSet<string> Commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "cmd.exe", "powershell.exe", "powershell_ise.exe", "wscript.exe", "cscript.exe", "mshta.exe", "wsl.exe", "bash.exe", "curl.exe", "ftp.exe", "winget.exe" };
        private static readonly string[] WindowsProgramFolders =
        {
            @"Common Files\Microsoft Shared\Ink\", @"Internet Explorer\", @"Windows Defender\",
            @"Windows Defender Advanced Threat Protection\", @"Windows Mail\", @"Windows Media Player\",
            @"Windows Multimedia Platform\", @"Windows NT\Accessories\", @"Windows Photo Viewer\",
            @"Windows Portable Devices\", @"Windows Security\", @"WindowsPowerShell\",
            @"Microsoft\Copilot\", @"Microsoft\EdgeCore\", @"Microsoft\EdgeUpdate\", @"Microsoft\EdgeWebView\", @"Microsoft OneDrive\"
        };

        public ProcessGuard()
        {
            using (Process process = Process.GetCurrentProcess()) ownPid = process.Id;
        }

        private static string Folder(Environment.SpecialFolder kind)
        {
            return Environment.GetFolderPath(kind).TrimEnd('\\') + "\\";
        }

        public void SetRestriction(IEnumerable<AppRule> allowed, IEnumerable<string> legacyPaths)
        {
            rules = allowed == null ? null : allowed.ToList();
            exactPaths = allowed == null ? null : new HashSet<string>(legacyPaths, StringComparer.OrdinalIgnoreCase);
            previousWindow = IntPtr.Zero;
        }

        public BlockedWindow CheckForeground()
        {
            if (rules == null) return null;
            IntPtr window = GetForegroundWindow();
#if FOCUS_TEST
            if (TestForeground != null) window = TestForeground();
#endif
            if (window == IntPtr.Zero) { previousWindow = window; return null; }
            int pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid == 0 || pid == ownPid)
            {
                previousWindow = IntPtr.Zero;
                return null;
            }
            if (window != previousWindow || pid != previousPid)
            {
                previousWindow = window;
                previousPid = pid;
                previousPath = ImagePath(pid);
                if (string.Equals(Path.GetFileName(previousPath), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                    previousPath = HostedApplication(window, pid) ?? previousPath;
                string name = Path.GetFileName(previousPath);
                previousEdge = string.Equals(name, "msedge.exe", StringComparison.OrdinalIgnoreCase);
                previousAllowed = previousPath != null &&
                    (previousEdge ? (anyEdgeAllowed || IsFocusEdge(pid, previousPath)) : IsSystem(previousPath, name) || IsAllowedApp(previousPath) || AncestorAllowed(pid));
                retriedNormalWindow = false;
            }
            if (previousPath == null) return null;
            if (IsIconic(window)) { retriedNormalWindow = false; return null; }
#if FOCUS_TEST
            if (TestFolder == null || !previousPath.StartsWith(TestFolder, StringComparison.OrdinalIgnoreCase)) return null;
#endif
            if (previousAllowed) return null;

            var now = DateTime.UtcNow;
            if (retriedNormalWindow && now - lastAttempt < TimeSpan.FromSeconds(2)) return null;
            bool firstAttempt = !retriedNormalWindow;
            retriedNormalWindow = true;
            lastAttempt = now;
            IntPtr root = GetAncestor(window, 3);
            if (root != IntPtr.Zero)
            {
                int rootPid;
                GetWindowThreadProcessId(root, out rootPid);
                if (rootPid == pid) window = root;
            }
            bool minimized = ShowWindowAsync(window, 6);
            return firstAttempt ? new BlockedWindow { Path = previousPath, Edge = previousEdge, Minimized = minimized } : null;
        }

        private bool IsAllowedApp(string path)
        {
            string file = string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path);
            if (string.Equals(file, "应急停止.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file, "StudyFocus_EmergencyStop.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file, "卸载.exe", StringComparison.OrdinalIgnoreCase)) return true;
            if (exactPaths.Contains(path) || rules.Any(rule => rule.Contains(path))) return true;
            if (rules.Any(rule => Covers(rule, path))) return true;
            if (AppCatalog.StoreRuntimeContains(path, rules)) return true;
            string folder = Path.GetDirectoryName(path);
            var candidates = rules.Where(rule => !string.IsNullOrEmpty(rule.GroupKey) &&
                rule.GroupKey.IndexOf('|') >= 0 &&
                string.Equals(Path.GetDirectoryName(rule.Path), folder, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0) return false;
            var entry = new AppCatalog.Entry(path, null);
            return candidates.Any(rule => rule.SameApp(entry.Rule));
        }

        private static string WeChatPluginRoot(string company)
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tencent", company);
            return Directory.Exists(root) ? root.TrimEnd('\\') + "\\" : null;
        }

        private bool Covers(AppRule rule, string path)
        {
            if (rule == null) return false;
            string name = string.IsNullOrEmpty(rule.Path) ? "" : Path.GetFileName(rule.Path);
            if (string.Equals(name, "Weixin.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "WeChat.exe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string company in new[] { "xwechat", "WeChat" })
                {
                    string plugin = WeChatPluginRoot(company);
                    if (plugin != null && path.StartsWith(plugin, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            string root = !string.IsNullOrEmpty(rule.Root) ? rule.Root
                : string.IsNullOrEmpty(rule.Path) ? null : Path.GetDirectoryName(rule.Path) + "\\";
            return root != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<int, int> parents = new Dictionary<int, int>();

        private static int ParentOf(int pid)
        {
            lock (parents)
            {
                int cached;
                if (parents.TryGetValue(pid, out cached)) return cached;
            }
            int parent = 0;
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT ParentProcessId FROM Win32_Process WHERE ProcessId=" + pid))
                    foreach (System.Management.ManagementObject item in searcher.Get())
                        parent = Convert.ToInt32(item["ParentProcessId"]);
            }
            catch { }
            lock (parents) { parents[pid] = parent; }
            return parent;
        }

        private bool AncestorAllowed(int pid)
        {
            for (int depth = 0; depth < 4; depth++)
            {
                pid = ParentOf(pid);
                if (pid <= 4) return false;
                string path = ImagePath(pid);
                if (string.IsNullOrEmpty(path)) continue;
                if (IsAllowedApp(path)) return true;
            }
            return false;
        }
        private static string HostedApplication(IntPtr frame, int host)
        {
            string path = null;
            EnumChildWindows(frame, delegate(IntPtr window, IntPtr parameter)
            {
                int pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid == 0 || pid == host) return true;
                var name = new StringBuilder(64);
                GetClassName(window, name, name.Capacity);
                if (!string.Equals(name.ToString(), "Windows.UI.Core.CoreWindow", StringComparison.Ordinal)) return true;
                path = ImagePath(pid);
                return path == null;
            }, IntPtr.Zero);
            return path;
        }

        private static string ImagePath(int pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return null;
            try
            {
                var filename = new StringBuilder(1024);
                int length = filename.Capacity;
                if (QueryFullProcessImageName(process, 0, filename, ref length)) return filename.ToString();
                if (Marshal.GetLastWin32Error() != 122) return null;
                filename = new StringBuilder(32768);
                length = filename.Capacity;
                return QueryFullProcessImageName(process, 0, filename, ref length) ? filename.ToString() : null;
            }
            finally { CloseHandle(process); }
        }

        internal void ConfigureFocusEdge(string edge, string profile, int port)
        {
            var fullProfile = profile == null ? null : Path.GetFullPath(profile);
            var proxy = "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (focusEdgeExecutable == edge && focusEdgeProfile == fullProfile && focusEdgeProxy == proxy) return;
            focusEdgeExecutable = edge;
            focusEdgeProfile = fullProfile;
            focusEdgeProxy = proxy;
            focusEdgePid = 0;
            previousWindow = IntPtr.Zero;
        }

        public void OpenEdge(string website)
        {
            if (focusEdgeExecutable == null) throw new InvalidOperationException("未找到 Microsoft Edge。");
            if (focusEdgeProfile != null) Directory.CreateDirectory(focusEdgeProfile);
            string flags = (focusEdgeProfile == null ? "" : "--user-data-dir=\"" + focusEdgeProfile + "\"") + " --proxy-server=\"" + focusEdgeProxy +
                "\" --proxy-bypass-list=\"<-loopback>\" --disable-quic --no-first-run --no-default-browser-check" +
                " --disable-extensions --disable-background-networking --disable-background-mode --no-proxy-auto-detect" +
                " --force-webrtc-ip-handling-policy=disable_non_proxied_udp";
            using (Process process = Process.Start(new ProcessStartInfo(focusEdgeExecutable, flags + " --new-window \"" + website + "\"") { UseShellExecute = false }))
            {
                if (process == null) throw new InvalidOperationException("无法打开 Edge。");
                previousWindow = IntPtr.Zero;
            }
        }

        private bool IsFocusEdge(int pid, string path)
        {
            if (focusEdgeExecutable == null || !string.Equals(path, focusEdgeExecutable, StringComparison.OrdinalIgnoreCase))
                return false;
            long start = StartedAt(pid);
            if (start == 0) return false;
            if (pid == focusEdgePid && start == focusEdgeStart) return true;

            // A second launch can hand its arguments to the existing browser and exit.
            // Inspect the process that owns the window, even after StudyFocus restarts.
            string[] args = ReadEdgeArguments(pid);
            bool profileOk = focusEdgeProfile == null
                ? !args.Any(arg => arg.StartsWith("--user-data-dir=", StringComparison.OrdinalIgnoreCase))
                : HasUniqueSwitch(args, "--user-data-dir=", focusEdgeProfile);
            if (!profileOk ||
                !HasUniqueSwitch(args, "--proxy-server=", focusEdgeProxy) ||
                !HasUniqueSwitch(args, "--proxy-bypass-list=", "<-loopback>") ||
                args.Any(arg => arg == "--no-proxy-server" || arg == "--proxy-auto-detect" ||
                    arg.StartsWith("--proxy-pac-url=", StringComparison.OrdinalIgnoreCase)) || StartedAt(pid) != start) return false;
            focusEdgePid = pid;
            focusEdgeStart = start;
            return true;
        }

        private static bool HasUniqueSwitch(string[] args, string name, string value)
        {
            var switches = args.Where(arg => arg.Equals(name.TrimEnd('='), StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return switches.Length == 1 && string.Equals(switches[0], name + value, StringComparison.OrdinalIgnoreCase);
        }

        private static string[] ReadEdgeArguments(int pid)
        {
            try
            {
                var query = new ObjectQuery("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " +
                    pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using (var search = new ManagementObjectSearcher(query) { Options = new EnumerationOptions { Timeout = TimeSpan.FromMilliseconds(500) } })
                using (var rows = search.Get())
                    foreach (ManagementObject row in rows)
                    {
                        using (row) return SplitWindowsArguments(row["CommandLine"] as string);
                    }
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            catch (COMException) { }
            return new string[0];
        }

        private static string[] SplitWindowsArguments(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return new string[0];
            int count;
            IntPtr args = CommandLineToArgvW(commandLine, out count);
            if (args == IntPtr.Zero) return new string[0];
            try
            {
                var result = new string[count];
                for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(args, i * IntPtr.Size));
                return result;
            }
            finally { LocalFree(args); }
        }

        private static long StartedAt(int pid)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                    return process.HasExited ? 0 : process.StartTime.ToFileTimeUtc();
            }
            catch (ArgumentException) { return 0; }
            catch (InvalidOperationException) { return 0; }
            catch (Win32Exception) { return 0; }
        }

        public static string ApplicationPath()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().Location;
        }

        public static string FindEdge()
        {
            foreach (string root in new[] { programsX86, programs, localFiles })
            {
                string path = Path.Combine(root, @"Microsoft\Edge\Application\msedge.exe");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        internal static bool ExcludedFromCatalog(string path)
        {
            string name = Path.GetFileName(path);
            return path.StartsWith(windows, StringComparison.OrdinalIgnoreCase) ||
                name.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("StudyFocus.exe", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("StudyFocus_Setup.exe", StringComparison.OrdinalIgnoreCase) ||
                IsSystem(path, name);
        }

        private static bool IsSystem(string path, string name)
        {
            if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
            {
                string relative = path.Substring(windows.Length);
                if (relative.StartsWith(@"Temp\", StringComparison.OrdinalIgnoreCase) || Commands.Contains(name)) return false;
                return relative.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) ||
                    relative.Equals("splwow64.exe", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"SysWOW64\", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"SystemApps\", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"ImmersiveControlPanel\", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"WinSxS\", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"IME\", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"Microsoft.NET\", StringComparison.OrdinalIgnoreCase);
            }
            if (path.StartsWith(programs, StringComparison.OrdinalIgnoreCase))
            {
                string relative = path.Substring(programs.Length);
                return relative.StartsWith(@"Windows Defender\", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"WindowsApps\Microsoft.WindowsNotepad_", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"WindowsApps\Microsoft.WindowsCalculator_", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith(@"WindowsApps\Microsoft.Paint_", StringComparison.OrdinalIgnoreCase);
            }
            string edgeRuntime = @"Microsoft\EdgeWebView\Application\";
            return name.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase) &&
                (path.StartsWith(programsX86 + edgeRuntime, StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith(localFiles + edgeRuntime, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsSystemApplication(string path)
        {
            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (IsSystem(normalized, Path.GetFileName(normalized)) || AppCatalog.IsKnownWindowsPackage(normalized) ||
                normalized.StartsWith(localFiles + @"Microsoft\OneDrive\", StringComparison.OrdinalIgnoreCase)) return true;
            return WindowsProgramFolders.Any(folder => normalized.StartsWith(programs + folder, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(programsX86 + folder, StringComparison.OrdinalIgnoreCase));
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flag);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr window, int show);
        private delegate bool ChildWindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, ChildWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint flags, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int length);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr process);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
