using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle(StudyFocus.ProductBrand.EmergencyName)]
[assembly: AssemblyDescription("结束当前专注并跳过眼下的自动计划")]
[assembly: AssemblyProduct(StudyFocus.ProductBrand.EmergencyName)]
[assembly: AssemblyCompany(StudyFocus.ProductBrand.Company)]
[assembly: AssemblyVersion("1.6.0.0")]

namespace StudyFocus
{
    internal static class EmergencyStop
    {
        private const string Title = ProductBrand.EmergencyName;
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StudyFocus";
        private static readonly Version Supported = new Version(2, 0, 99, 0);

        [STAThread]
        private static void Main()
        {
            string app = null;
            bool stoppedProcess = false;
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                app = LocateApplication(local);
                var file = new StateFile(Path.Combine(local, "StudyFocus"));
                string sessionKey;
                using (var user = WindowsIdentity.GetCurrent()) sessionKey = "Local\\StudyFocus." + user.User.Value;
                bool changed;
                using (var setup = new Mutex(false, sessionKey + ".setup"))
                {
                    if (!Enter(setup, 0)) throw new InvalidOperationException("安装正在进行，请等安装完成后再试。");
                    try
                    {
                        var preview = file.Load();
                        if (!Apply(new FocusEngine(file, preview), DateTimeOffset.Now))
                        {
                            MessageBox.Show("当前没有正在运行或即将开始的专注。", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                        using (var instance = new Mutex(false, sessionKey + ".instance"))
                        {
                            if (!Enter(instance, 0))
                            {
                                StopExactProcess(app, ref stoppedProcess);
                                if (!Enter(instance, 3000)) throw new InvalidOperationException(ProductBrand.Name + "仍在运行，请重试。");
                            }
                            try
                            {
                                var state = file.Load();
                                changed = Apply(new FocusEngine(file, state), DateTimeOffset.Now);
                                if (changed) file.Save(state);
                            }
                            finally { instance.ReleaseMutex(); }
                        }
                    }
                    finally { setup.ReleaseMutex(); }
                }
                bool restarted = (!changed && !stoppedProcess) || Relaunch(app);
                stoppedProcess = false;
                string message = changed
                    ? "应急停止已执行。此刻的专注限制已解除，后续计划会继续运行。"
                    : "本次专注已经结束。";
                if (!restarted) message = "本次专注已结束。打开" + ProductBrand.Name + "即可恢复后续计划。";
                MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                if (stoppedProcess && app != null) Relaunch(app);
                MessageBox.Show(error.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static bool Apply(FocusEngine engine, DateTimeOffset now)
        {
            var state = engine.State;
            bool hadSession = state.Session != null;
            if (hadSession && (FocusEngine.OpenEnded(state.Session) || state.Session.FinishUtc > now.UtcTicks))
            {
                state.Stops.Add(new StopRecord
                {
                    Month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture), WhenUtc = now.UtcTicks,
                    Minutes = engine.MinutesRemaining(now), Reason = "强制停止（应急）"
                });
            }
            state.Session = null;
            bool skipped = false;
            var due = engine.DueWindow(now);
            if (due != null)
            {
                Skip(state, due.Start);
                skipped = true;
            }
            var cutoff = now.AddMinutes(2);
            for (var next = engine.NextSchedule(now); next.HasValue && next.Value <= cutoff; next = engine.NextSchedule(now))
            {
                Skip(state, next.Value);
                skipped = true;
            }
            return hadSession || skipped;
        }

        private static void Skip(State state, DateTimeOffset start)
        {
            state.LastScheduleUtc = start.UtcTicks;
            state.LastSchedule = start.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        }

        private static bool Enter(Mutex mutex, int wait)
        {
            try { return mutex.WaitOne(wait); }
            catch (AbandonedMutexException) { return true; }
        }

        private static string LocateApplication(string local)
        {
            string folder = null;
            using (var registry = Registry.CurrentUser.OpenSubKey(UninstallKey))
                if (registry != null) folder = registry.GetValue("InstallLocation") as string;
            string fallback = Path.Combine(local, "Programs", "StudyFocus");
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder)) folder = fallback;
            string executable = Path.GetFullPath(Path.Combine(folder, "StudyFocus.exe"));
            if (!File.Exists(executable) && !string.Equals(folder, fallback, StringComparison.OrdinalIgnoreCase))
                executable = Path.Combine(fallback, "StudyFocus.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("没有找到已安装的" + ProductBrand.Name + "。");
            if (!SupportsApplication(executable))
                throw new InvalidOperationException("此应急工具与当前安装的主程序版本不匹配，请使用随主程序发布的应急工具。");
            return executable;
        }

        internal static bool SupportsApplication(string executable)
        {
            var assembly = AssemblyName.GetAssemblyName(executable);
            return ProductBrand.Matches(FileVersionInfo.GetVersionInfo(executable)) && assembly.Version <= Supported;
        }

        private static void StopExactProcess(string executable, ref bool stopped)
        {
            int session;
            using (var current = Process.GetCurrentProcess()) session = current.SessionId;
            foreach (var process in Process.GetProcessesByName("StudyFocus"))
                using (process)
                {
                    try
                    {
                        if (process.SessionId != session ||
                            !string.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                        process.Kill();
                        stopped = true;
                        if (!process.WaitForExit(5000)) throw new TimeoutException(ProductBrand.Name + "尚未退出，请重试。");
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { if (!process.HasExited) throw; }
                }
        }

        private static bool Relaunch(string executable)
        {
            try
            {
                using (var app = Process.Start(new ProcessStartInfo(executable, "--startup") { UseShellExecute = true }))
                    return app != null;
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }
}
