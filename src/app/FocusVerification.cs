using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using StudyFocus;

internal static class FocusVerification
{
    [STAThread]
    private static int Main(string[] args)
    {
        string data = Path.Combine(Path.GetTempPath(), "StudyFocusVerify-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (args.Length >= 4) Upgrade(data, args[2], args[3]);
            Engine(data);
            Console.WriteLine("PASS timer, settings lock, pause minutes, monthly stop counts, and state reload");
            Schedules(data);
            Console.WriteLine("PASS multiple independent schedules, disabled slots, and no duplicate triggers");
            ScheduledWindows(data);
            Console.WriteLine("PASS fixed end times, late starts, overnight sessions, overlaps, and restart");
            ScheduleSafety(data);
            Console.WriteLine("PASS skip survives restart and emergency release works with zero allowance or a failed state write");
            LegacySettings(data);
            Console.WriteLine("PASS original installation settings and history load unchanged");
            Credentials(data);
            History(data);
            Console.WriteLine("PASS foreground time, blocked attempts, inactive gaps, and local history reload");
            Console.WriteLine("PASS password, recovery, reset, deletion, and lock");
            Catalog();
            Console.WriteLine("PASS app picker separates Windows packages and components from third-party applications");
            Proxy();
            Console.WriteLine("PASS website allowlist over loopback HTTP and CONNECT");
            if (args.Contains("--edge")) EdgeBrowser();
            Programs(args[0], args[1]);
            Console.WriteLine("PASS non-allowlisted window minimizes, keeps its process and window, and can be restored");
            Interface(data, args[0], args[1]);
            Console.WriteLine("PASS navigation, allowlists, per-plan display, auto-dismiss, and focus sound triggers");
            SetupDialogs();
            Console.WriteLine("PASS setup themes, version, drive selection, and safe uninstall confirmation");
            TrayExit(data);
            Console.WriteLine("PASS tray exit with an enabled plan, during focus, and near a planned start");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(data))
            {
                File.Delete(Path.Combine(data, "state.dat"));
                File.Delete(Path.Combine(data, "state.dat.backup"));
                File.Delete(Path.Combine(data, "state.dat.new"));
                File.Delete(Path.Combine(data, "edge-port"));
                string history = Path.Combine(data, "FocusHistory");
                if (Directory.Exists(history))
                {
                    foreach (string log in Directory.GetFiles(history, "*.jsonl")) File.Delete(log);
                    Directory.Delete(history);
                }
                Directory.Delete(data);
            }
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("An operation should have been rejected.");
    }

    private static void Upgrade(string data, string oldPackage, string newPackage)
    {
        var folder = Path.Combine(data, "upgrade");
        Directory.CreateDirectory(folder);
        var installed = Path.Combine(folder, "StudyFocus.exe");
        var statePath = Path.Combine(folder, "state.dat");
        try
        {
            var oldVersion = Program.InstalledVersion(oldPackage);
            var newVersion = Program.InstalledVersion(newPackage);
            Require(oldVersion != null && newVersion != null && oldVersion < newVersion,
                "Both actual installer versions must be identifiable.");
            Require(FileVersionInfo.GetVersionInfo(newPackage).ProductName == ProductBrand.Name,
                "The new installer does not have the public product name.");
            File.Copy(oldPackage, installed);
            Program.UpdateShortcut(folder, ProductBrand.LegacyName, installed, null);
            Require(File.Exists(Path.Combine(folder, ProductBrand.LegacyName + ".lnk")), "Legacy shortcut fixture was not created.");
            var file = new StateFile(folder);
            var engine = new FocusEngine(file, file.Load());
            var now = DateTimeOffset.Now;
            var settings = engine.Settings.Copy();
            settings.Dark = true;
            settings.Websites.Add("example.org");
            engine.UpdateSettings(now, settings);
            engine.Start(now, null);
            byte[] before = File.ReadAllBytes(statePath);
            bool prepared = false, published = false;
            Program.InstallFiles(newPackage, installed, installed,
                delegate { prepared = true; },
                delegate { Program.UpdateShortcut(folder, ProductBrand.Name, installed, installed); published = true; },
                delegate { throw new InvalidOperationException("Unexpected rollback."); });
            Require(prepared && published && File.ReadAllBytes(newPackage).SequenceEqual(File.ReadAllBytes(installed)),
                "The installer did not replace the previous program in the existing directory.");
            Require(File.Exists(Path.Combine(folder, ProductBrand.Name + ".lnk")) &&
                !File.Exists(Path.Combine(folder, ProductBrand.LegacyName + ".lnk")),
                "The shortcut was not migrated to the renamed product.");
            Require(before.SequenceEqual(File.ReadAllBytes(statePath)) && file.Load().Session != null &&
                file.Load().Settings.Websites.SequenceEqual(new[] { "example.org" }),
                "An in-place upgrade changed the running session or settings.");

            bool recovered = false;
            Reject(delegate
            {
                Program.InstallFiles(oldPackage, installed, installed, delegate { },
                    delegate { throw new InvalidOperationException("Simulated publication error."); },
                    delegate(bool replaced) { recovered = replaced; });
            });
            Require(recovered && File.ReadAllBytes(newPackage).SequenceEqual(File.ReadAllBytes(installed)),
                "An upgrade publication failure did not restore the original executable.");
            Require(!Directory.GetFiles(folder, "*.bak").Any(), "An upgrade backup was left behind after successful rollback.");
            Console.WriteLine("PASS renamed shortcut is created and the matching legacy link is removed");
            Console.WriteLine("PASS in-place update from " + oldVersion + " to " + newVersion + ", session retention, and rollback after publication failure");
        }
        finally
        {
            foreach (var path in Directory.GetFiles(folder)) File.Delete(path);
            Directory.Delete(folder);
        }
    }

    private static void Engine(string data)
    {
        var file = new StateFile(data);
        var engine = new FocusEngine(file, file.Load());
        var now = new DateTimeOffset(DateTime.Today.AddHours(9));
        var rules = new Settings
        {
            Minutes = 25, PauseCount = 1, PauseMinutes = 5, MonthlyStopCount = 1,
            ScheduleDays = 1 << (int)now.DayOfWeek, ScheduleMinute = 570
        };
        engine.UpdateSettings(now, rules);
        Require(!engine.Locked(now.AddMinutes(27)), "Rules should be unlocked before the two-minute window.");
        Require(engine.Locked(now.AddMinutes(28)), "Rules should lock two minutes before start.");
        Reject(delegate { engine.UpdateSettings(now.AddMinutes(28), new Settings()); });
        DateTimeOffset due;
        Require(engine.TryDue(now.AddMinutes(30), out due) && due == now.AddMinutes(30), "Scheduled start should be due.");
        engine.Start(due, due);
        Require(!engine.Session.CountUp && engine.Session.TotalMinutes == 25, "Session should use configured time and mode.");
        engine.Pause(due.AddMinutes(3));
        Require(FocusEngine.Paused(engine.Session, due.AddMinutes(4)), "Session did not pause.");
        Require(engine.MinutesRemaining(due.AddMinutes(4)) == 21, "The scheduled end time should stay fixed during pause.");
        engine.Resume(due.AddMinutes(5));
        var restarted = new FocusEngine(file, file.Load());
        Require(restarted.PauseRemaining(due.AddMinutes(5)).TotalMinutes == 3 && restarted.MinutesRemaining(due.AddMinutes(5)) == 20,
            "Restart lost the remaining pause budget.");
        Reject(delegate { restarted.Stop(due.AddMinutes(5), " "); });
        restarted.Stop(due.AddMinutes(5), "test");
        Require(restarted.StopsRemaining(due) == 0 && file.Load().Stops[0].Reason == "test" &&
            file.Load().Stops[0].Minutes == 20, "The early stop should cost one use and record the reason and remaining time.");
        restarted.Start(now.AddHours(2), null);
        Reject(delegate { restarted.Stop(now.AddHours(2), "over-budget"); });
        Require(restarted.Tick(now.AddHours(3)) && restarted.Session == null, "Timer should finish automatically.");
        Require(restarted.StopsRemaining(now.AddMonths(1)) == 1, "Monthly stop count should reset.");
        restarted.UpdateSettings(now.AddHours(3), new Settings { PauseBudgetMinutes = 5 });
        restarted.Start(now.AddHours(3), null);
        Require(!restarted.Session.CountUp && restarted.MinutesRemaining(now.AddHours(3)) == 45, "Countdown should begin at its configured duration.");
        restarted.Pause(now.AddHours(3));
        Require(restarted.Tick(now.AddHours(3).AddMinutes(5)) && !FocusEngine.Paused(restarted.Session, now.AddHours(3).AddMinutes(5)), "Pause should finish automatically.");
        Reject(delegate { restarted.Pause(now.AddHours(3).AddMinutes(6)); });
        Require(restarted.Tick(now.AddHours(4)), "Countdown should finish.");
        var open = new Settings { CountUp = true, Minutes = 1, PauseBudgetMinutes = 3 };
        var begin = now.AddHours(5);
        restarted.UpdateSettings(begin, open);
        restarted.Start(begin, null);
        Require(FocusEngine.OpenEnded(restarted.Session) && !restarted.Tick(begin.AddMinutes(2)), "Count-up must have no target duration.");
        restarted.Pause(begin.AddMinutes(3));
        Require(FocusEngine.Elapsed(restarted.Session, begin.AddMinutes(4), open.PauseMinutes).TotalMinutes == 3, "Count-up advanced during a pause.");
        restarted.Resume(begin.AddMinutes(4));
        Require(FocusEngine.Elapsed(restarted.Session, begin.AddMinutes(5), open.PauseMinutes).TotalMinutes == 4, "Count-up resumed at the wrong time.");
        restarted.Pause(begin.AddMinutes(6));
        Require(restarted.Tick(begin.AddMinutes(8)), "Count-up didn't auto-resume.");
        restarted = new FocusEngine(file, file.Load());
        Require(FocusEngine.Elapsed(restarted.Session, begin.AddMinutes(9), open.PauseMinutes).TotalMinutes == 6, "Count-up time did not survive restart.");
        restarted.FinishCountUp();
        Require(restarted.Session == null && restarted.State.Stops.Count == 1, "Completing untimed focus should not consume the early-stop budget.");
    }

    private static void History(string data)
    {
        var from = new DateTimeOffset(DateTime.Today.AddHours(8));
        var log = new FocusHistory(data);
        var round = new Session { StartedUtc = from.UtcTicks };
        var a = Path.Combine(data, "Writing.exe");
        var b = Path.Combine(data, "Reading.exe");
        log.Observe(from, round, a, null);
        log.Observe(from.AddSeconds(1), round, a, null);
        log.Observe(from.AddSeconds(2), round, b, Path.Combine(data, "Blocked.exe"));
        log.Observe(from.AddSeconds(3), round, b, null);
        log.Observe(from.AddMinutes(20), round, a, null);
        log.Observe(from.AddMinutes(20).AddSeconds(1), null, null, null);
        log.Close();
        var saved = new FocusHistory(data).Read(from.Date, from.Date.AddDays(1)).ToList();
        Require(saved.Count == 4 && saved.Single(item => item.Blocked).App == "Blocked" &&
            saved.Where(item => item.App == "Writing").Sum(item => item.To - item.From) == TimeSpan.FromSeconds(3).Ticks &&
            saved.Where(item => item.App == "Reading").Sum(item => item.To - item.From) == TimeSpan.FromSeconds(1).Ticks,
            "History did not retain the exact foreground intervals or excluded time incorrectly.");
    }

    private static void Schedules(string data)
    {
        var now = new DateTimeOffset(DateTime.Today.AddHours(7));
        int today = 1 << (int)now.DayOfWeek;
        var file = new StateFile(data);
        var engine = new FocusEngine(file, new State());
        var rules = engine.Settings.Copy();
        rules.Minutes = 10;
        rules.Schedules = new System.Collections.Generic.List<FocusSchedule>
        {
            new FocusSchedule { Minute = 480, Days = today, Enabled = true, CountUp = true },
            new FocusSchedule { Minute = 540, Days = today, Enabled = false },
            new FocusSchedule { Minute = 600, Days = today, Enabled = true, CountUp = false }
        };
        engine.UpdateSettings(now, rules);
        Require(engine.NextSchedule(now) == now.AddHours(1) && engine.Locked(now.AddMinutes(58)), "First plan wasn't scheduled.");
        DateTimeOffset due;
        Require(engine.TryDue(now.AddHours(1), out due), "First enabled plan did not trigger.");
        engine.Start(due, due);
        Require(engine.Session.CountUp && !FocusEngine.OpenEnded(engine.Session),
            "The first plan should run a timed count-up while manual mode is countdown.");
        engine.Tick(due.AddMinutes(10));
        engine = new FocusEngine(file, file.Load());
        Require(!engine.TryDue(now.AddHours(1).AddMinutes(2), out due), "First plan replayed after loading state.");
        Require(!engine.TryDue(now.AddHours(2), out due), "Disabled plan triggered.");
        Require(engine.NextSchedule(now.AddHours(2)) == now.AddHours(3) &&
            engine.TryDue(now.AddHours(3), out due) && due == now.AddHours(3), "Second enabled plan did not trigger.");
        engine.Start(due, due);
        Require(!engine.Session.CountUp, "The second plan should run as a countdown.");
        engine.Tick(due.AddMinutes(10));
        rules = engine.Settings.Copy();
        rules.Schedules = rules.Schedules.Select(plan => plan.Copy()).ToList();
        rules.Schedules[0].Enabled = false;
        rules.Schedules[2].Enabled = false;
        engine.UpdateSettings(now.AddHours(4), rules);
        Require(!engine.HasEnabledSchedule && engine.NextSchedule(now.AddHours(4)) == null, "Disabled plans still showed a next start.");
    }

    private static void ScheduledWindows(string data)
    {
        var day = new DateTimeOffset(DateTime.Today);
        var start = day.AddHours(23).AddMinutes(50);
        var second = day.AddDays(1).AddMinutes(5);
        var end = day.AddDays(1).AddMinutes(20);
        var file = new StateFile(data);
        var engine = new FocusEngine(file, new State());
        var rules = new Settings
        {
            CountUp = true, PauseMinutes = 5,
            Schedules = new System.Collections.Generic.List<FocusSchedule>
            {
                new FocusSchedule { Minute = 1430, EndMinute = 10, Days = 1 << (int)day.DayOfWeek, Enabled = true },
                new FocusSchedule { Minute = 5, EndMinute = 20, Days = 1 << (int)second.DayOfWeek, Enabled = true }
            }
        };
        engine.UpdateSettings(start.AddMinutes(-5), rules);
        Require(engine.NextSchedule(start.AddMinutes(-3)) == start && engine.Locked(start.AddMinutes(-2)),
            "Explicit schedule should be locked two minutes before the start.");
        Require(engine.DueWindow(start.AddSeconds(-1)) == null, "The plan triggered before its start.");
        var late = start.AddMinutes(3);
        engine = new FocusEngine(file, file.Load());
        var first = engine.DueWindow(late);
        Require(first != null && first.Start == start && first.End == day.AddDays(1).AddMinutes(10),
            "Signing in during a plan should use its original overnight end.");
        engine.StartScheduled(late, first);
        Require(engine.Session.Scheduled && engine.Session.CountUp && !FocusEngine.OpenEnded(engine.Session),
            "Scheduled count-up should still end at the fixed time.");
        engine.Tick(second);
        Require(engine.Session.FinishUtc == end.UtcTicks && engine.Session.TotalMinutes == (int)(end - late).TotalMinutes,
            "The later overlapping window should extend the scheduled session to its end.");
        engine.Pause(end.AddMinutes(-1));
        Require(engine.Session.PauseUntilUtc == end.UtcTicks && engine.MinutesRemaining(end.AddSeconds(-30)) == 1,
            "The last pause must stay inside the plan and still require a reason for stopping early.");
        var pausedElapsed = FocusEngine.Elapsed(engine.Session, end.AddSeconds(-30), rules.PauseMinutes);
        engine = new FocusEngine(file, file.Load());
        Require(FocusEngine.Elapsed(engine.Session, end.AddSeconds(-15), rules.PauseMinutes) == pausedElapsed,
            "Count-up time changed while a shortened pause was active after restart.");
        Require(engine.Tick(end) && engine.Session == null && engine.DueWindow(end) == null && engine.State.Stops.Count == 0,
            "The end of the window should release the restrictions without charging an early stop.");

        var savedMidWindow = new FocusEngine(file, new State());
        savedMidWindow.UpdateSettings(late, rules);
        Require(savedMidWindow.DueWindow(late) != null, "A plan saved inside its range should be eligible that day.");
        var backToBack = new FocusEngine(file, new State());
        var consecutive = rules.Copy();
        consecutive.Schedules = rules.Schedules.Select(plan => plan.Copy()).ToList();
        consecutive.Schedules[0].EndMinute = consecutive.Schedules[1].Minute;
        consecutive.Schedules[0].CountUp = false;
        consecutive.Schedules[1].CountUp = true;
        backToBack.UpdateSettings(start.AddMinutes(-3), consecutive);
        backToBack.StartScheduled(start, backToBack.DueWindow(start));
        Require(!backToBack.Tick(second) && backToBack.Session.FinishUtc == end.UtcTicks &&
            backToBack.Session.CountUp && backToBack.Session.StartedUtc == second.UtcTicks,
            "Adjacent schedules should switch to the new mode without releasing restrictions.");
    }

    private static void Credentials(string data)
    {
        var file = new StateFile(data);
        var engine = new FocusEngine(file, new State());
        var now = DateTimeOffset.Now;
        string code;
        var first = AdvancedCredential.Create("Tester 1", out code);
        engine.SetAdvancedCredential(now, first);
        var loaded = file.Load().AdvancedCredential;
        Require(loaded.MatchesPassword("Tester 1") && !loaded.MatchesPassword("Tester 2"), "Password check failed.");
        Require(loaded.MatchesRecovery(code.ToLowerInvariant().Replace("-", " ")) && !loaded.MatchesRecovery("wrong"), "Recovery check failed.");
        string nextCode;
        var second = AdvancedCredential.Create("Tester 2", out nextCode);
        engine.SetAdvancedCredential(now, second);
        Require(!second.MatchesRecovery(code) && second.MatchesRecovery(nextCode) &&
            !second.MatchesPassword("Tester 1") && second.MatchesPassword("Tester 2"), "Reset did not invalidate the old credentials.");
        engine.Start(now, null);
        Reject(delegate { engine.SetAdvancedCredential(now, null); });
        Require(engine.Tick(now.AddDays(1)) || FocusEngine.OpenEnded(engine.Session), "Unexpected test session.");
        if (FocusEngine.OpenEnded(engine.Session)) engine.FinishCountUp();
        engine.SetAdvancedCredential(now, null);
        Require(file.Load().AdvancedCredential == null, "Password deletion did not persist.");
    }

    private static void ScheduleSafety(string data)
    {
        var file = new StateFile(data);
        var engine = new FocusEngine(file, new State());
        var start = new DateTimeOffset(DateTime.Today.AddHours(10));
        var later = start.AddMinutes(15);
        var rules = engine.Settings.Copy();
        rules.MonthlyStopCount = 0;
        rules.Schedules = new System.Collections.Generic.List<FocusSchedule>
        {
            new FocusSchedule { Days = 1 << (int)start.DayOfWeek, Minute = 600, EndMinute = 640, Enabled = true },
            new FocusSchedule { Days = 1 << (int)start.DayOfWeek, Minute = 615, EndMinute = 628, Enabled = true }
        };
        engine.UpdateSettings(start.AddMinutes(-5), rules);
        Require(!engine.SkippableSchedule(start.AddMinutes(-3)).HasValue && engine.SkippableSchedule(start.AddMinutes(-2)) == start,
            "Skip should be offered during the two-minute lock window.");
        Require(!engine.SkipSchedule(start.AddMinutes(-1), later) && engine.SkipSchedule(start.AddMinutes(-1), start),
            "A stale or unrelated occurrence was skipped instead of the current plan.");
        engine = new FocusEngine(file, file.Load());
        Require(engine.DueWindow(start.AddSeconds(10)) == null && engine.NextSchedule(start.AddSeconds(10)) == later &&
            engine.Settings.EffectiveSchedules().All(plan => plan.Enabled), "Skipping a plan changed a later occurrence or failed to persist.");
        Require(engine.DueWindow(later).End == later.AddMinutes(13), "A skipped plan extended a later overlapping plan.");
        var earlier = rules.Copy();
        earlier.Schedules = rules.Schedules.Select(plan => plan.Copy()).ToList();
        earlier.Schedules.Add(new FocusSchedule
            { Days = 1 << (int)start.DayOfWeek, Minute = 599, EndMinute = 608, Enabled = true });
        engine.UpdateSettings(start.AddSeconds(-90), earlier);
        Require(engine.NextSchedule(start.AddSeconds(-90)) == start.AddMinutes(-1), "Skipping a future plan also consumed a newly scheduled earlier plan.");

        var active = new FocusEngine(file, new State());
        active.UpdateSettings(start.AddMinutes(-5), rules.Copy());
        active.Start(start.AddMinutes(-3), null);
        var engaged = start.AddSeconds(15);
        Reject(delegate { active.Stop(engaged, "No allowance"); });
        byte[] previousState = File.ReadAllBytes(Path.Combine(data, "state.dat"));
        active.EmergencyRelease(engaged);
        var released = new FocusEngine(file, file.Load());
        Require(released.Session == null && released.State.Stops.Count == 0 && released.DueWindow(engaged) == null &&
            released.NextSchedule(engaged) == later, "Emergency release changed the quota, lost the next plan, or restarted a due plan.");

        string blocker = Path.Combine(data, "not-a-directory");
        File.WriteAllBytes(blocker, previousState);
        try
        {
            var constrained = new FocusEngine(new StateFile(blocker), new State { Session = new Session(), Settings = rules });
            bool failed = false;
            try { constrained.EmergencyRelease(engaged); }
            catch (IOException) { failed = true; }
            Require(failed && constrained.Session == null && constrained.DueWindow(engaged) == null,
                "A failed state write left the application restricted.");
        }
        finally { File.Delete(blocker); }
    }

    private static void LegacySettings(string data)
    {
        string folder = Path.Combine(data, "legacy-state");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "state.dat");
        try
        {
            string legacy = "{\"Settings\":{\"CountUp\":true,\"Minutes\":52,\"PauseCount\":3,\"PauseMinutes\":4," +
                "\"MonthlyStopMinutes\":80,\"Startup\":false,\"Dark\":true,\"ScheduleDays\":62,\"ScheduleMinute\":870," +
                "\"Apps\":[],\"Websites\":[\"example.com\"]},\"Session\":null," +
                "\"Stops\":[{\"Month\":\"2026-11\",\"WhenUtc\":639000000000000000,\"Minutes\":12,\"Reason\":\"legacy\"}]}";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(legacy),
                Encoding.UTF8.GetBytes("StudyFocus.Windows.State.v1"), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, bytes);
            var state = new StateFile(folder).Load();
            Require(state.Settings.Dark && state.Settings.CountUp && state.Settings.PauseCount == 3 &&
                state.Settings.Websites.SequenceEqual(new[] { "example.com" }) && state.Stops[0].Minutes == 12 &&
                state.AdvancedCredential == null && state.Settings.EffectiveSchedules().Single().Enabled &&
                state.Settings.EffectiveSchedules().Single().Minute == 870, "Legacy settings did not load intact.");
        Require(state.Settings.ShowFloatingTimer, "Upgraded settings should enable the floating timer by default.");
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(folder);
        }
    }

    private static void Catalog()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Require(!AppCatalog.IsSelectable(Path.Combine(windows, "explorer.exe")), "System executable was offered.");
        Require(!AppCatalog.IsSelectable(@"C:\Example\msedge.exe"), "Edge was listed as an ordinary app.");
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Require(ProcessGuard.IsSystemApplication(Path.Combine(programFiles, @"Windows Media Player\wmplayer.exe")) &&
            ProcessGuard.IsSystemApplication(@"D:\WindowsApps\Microsoft.BingWeather_4.0.0.0_x64__8wekyb3d8bbwe\Weather.exe") &&
            ProcessGuard.IsSystemApplication(@"D:\WindowsApps\Microsoft.XboxGamingOverlay_4.0.0.0_x64__8wekyb3d8bbwe\GameBar.exe"),
            "Built-in applications were offered as third-party software.");
        Require(!ProcessGuard.IsSystemApplication(Path.Combine(programFiles, @"Microsoft Office\root\Office16\WINWORD.EXE")) &&
            !ProcessGuard.IsSystemApplication(@"D:\WindowsApps\Microsoft.PowerToys_1.0.0.0_x64__8wekyb3d8bbwe\PowerToys.exe") &&
            !ProcessGuard.IsSystemApplication(@"D:\WindowsApps\ThirdParty.App_1.0.0.0_x64__abcdefghijklm\App.exe"),
            "Independent Microsoft or Store applications disappeared from third-party software.");
        Require(AppCatalog.IsDiscoveryHelper(Path.Combine(programFiles, @"Microsoft Office\root\Office16\SDXHelper.exe")) &&
            !AppCatalog.IsDiscoveryHelper(Path.Combine(programFiles, @"Microsoft Office\root\Office16\WINWORD.EXE")) &&
            !AppCatalog.IsDiscoveryHelper(@"D:\Portable Apps\SDXHelper.exe"),
            "The catalog should hide installed Office helpers while keeping standalone executables.");
        var apps = AppCatalog.Discover();
        Require(apps.Count > 0 && apps.All(x => AppCatalog.IsSelectable(x.Path) && File.Exists(x.Path)), "App scan returned an invalid list.");
        Require(apps.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == apps.Count, "App scan returned duplicate entries.");
        var duplicate = (from x in apps from y in apps where x != y && x.Rule.SameApp(y.Rule) select x.Name + " / " + y.Name +
            " [" + x.GroupKey + " / " + y.GroupKey + "]").FirstOrDefault();
        Require(duplicate == null, "App scan returned the same group more than once: " + duplicate);
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Adobe");
        var primary = new AppCatalog.Entry(Path.Combine(root, @"Acrobat DC\Acrobat\Acrobat.exe"), "Acrobat");
        var component = new AppCatalog.Entry(Path.Combine(root, @"Acrobat DC\Acrobat\AcroDist.exe"), "Distiller");
        var separate = new AppCatalog.Entry(Path.Combine(root, @"Adobe Photoshop\Photoshop.exe"), "Photoshop");
        Require(primary.Rule.SameApp(component.Rule) && primary.Rule.Contains(component.Path) &&
            !primary.Rule.SameApp(separate.Rule), "App grouping split a component or merged a separate product.");
        Console.WriteLine("PASS app grouping and system filtering (" + apps.Count + " applications)");
    }

    private static string Exchange(int port, string request)
    {
        using (var client = new TcpClient())
        {
            client.ReceiveTimeout = 4000;
            client.Connect(IPAddress.Loopback, port);
            var stream = client.GetStream();
            var bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);
            return new StreamReader(stream, Encoding.ASCII).ReadLine();
        }
    }

    private static void Proxy()
    {
        Require(Website.Parse("https://Example.com/a") == "example.com", "Website normalization failed.");
        Require(Website.Matches("a.example.com", "*.example.com") &&
                !Website.Matches("badexample.com", "*.example.com"), "Wildcard did not honor domain boundaries.");
        using (var proxy = new WebsiteProxy())
        {
            proxy.SetRestriction(new[] { "localhost" });
            Require(Exchange(proxy.Port, "CONNECT denied.example:443 HTTP/1.1\r\n\r\n").Contains("403"), "Unlisted website was accepted.");
            Require(Exchange(proxy.Port, "INVALID\r\n\r\n").Contains("400"), "Malformed request was accepted.");
            var server = new TcpListener(IPAddress.Loopback, 0);
            server.Start();
            try
            {
                string authority = "localhost:" + ((IPEndPoint)server.LocalEndpoint).Port;
                Task origin = Task.Run(delegate
                {
                    using (var client = server.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 4000;
                        var stream = client.GetStream();
                        var reader = new StreamReader(stream, Encoding.ASCII);
                        Require(reader.ReadLine() == "GET /study HTTP/1.1", "Origin request path was altered.");
                        while (!string.IsNullOrEmpty(reader.ReadLine())) { }
                        var reply = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
                        stream.Write(reply, 0, reply.Length);
                    }
                    using (var client = server.AcceptTcpClient()) { }
                });
                Require(Exchange(proxy.Port, "GET http://" + authority + "/study HTTP/1.1\r\nHost: " + authority + "\r\n\r\n").Contains("200"), "Allowed HTTP request did not reach the server.");
                Require(Exchange(proxy.Port, "CONNECT " + authority + " HTTP/1.1\r\n\r\n").Contains("200"), "Allowed CONNECT request was not tunneled.");
                Require(origin.Wait(4500), "Origin task did not finish.");
            }
            finally { server.Stop(); }
        }
    }

    private static void Programs(string allow, string deny)
    {
        var guard = new ProcessGuard();
        guard.TestFolder = Path.GetDirectoryName(allow).TrimEnd('\\') + "\\";
        guard.SetRestriction(new[] { new AppRule { Path = allow } }, new string[0]);
        using (var chosen = new TestWindow(allow))
        using (var blocked = new TestWindow(deny))
        {
            guard.TestForeground = () => chosen.Handle;
            Require(guard.CheckForeground() == null && !IsIconic(chosen.Handle), "An allowed window was minimized.");
            guard.TestForeground = () => blocked.Handle;
            var denied = guard.CheckForeground();
            Require(denied != null && denied.Minimized && SpinWait.SpinUntil(() => IsIconic(blocked.Handle), 3000),
                "The unlisted foreground window wasn't minimized.");
            Require(!blocked.Process.HasExited && IsWindow(blocked.Handle), "The blocked application was terminated or closed.");
            Require(guard.CheckForeground() == null, "A minimized window still triggered a warning.");
            guard.SetRestriction(null, null);
            ShowWindowAsync(blocked.Handle, 9);
            Require(SpinWait.SpinUntil(() => !IsIconic(blocked.Handle), 3000) && guard.CheckForeground() == null,
                "The window couldn't be restored after disabling restrictions.");
            var group = new AppRule { Path = allow, Members = new System.Collections.Generic.List<string> { deny } };
            guard.SetRestriction(new[] { group }, new string[0]);
            Require(guard.CheckForeground() == null, "An allowlisted application's component was restricted.");
        }
    }

    private static void EdgeBrowser()
    {
        string edge = ProcessGuard.FindEdge();
        Require(edge != null, "Microsoft Edge is required for --edge.");
        string token = "SF-" + Guid.NewGuid().ToString("N").Substring(0, 10);
        string profile = Path.GetFullPath("Edge smoke " + token);
        string normal = profile + " regular";
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        int port = ((IPEndPoint)server.LocalEndpoint).Port;
        int deniedRequests = 0;
        Task origin = Task.Run(delegate
        {
            try
            {
                while (true)
                    using (var client = server.AcceptTcpClient())
                    {
                        client.ReceiveTimeout = 4000;
                        var stream = client.GetStream();
                        var reader = new StreamReader(stream, Encoding.ASCII);
                        string request = reader.ReadLine();
                        if (request == null) continue;
                        while (!string.IsNullOrEmpty(reader.ReadLine())) { }
                        if (request.Contains("/denied")) Interlocked.Increment(ref deniedRequests);
                        string title = request.Contains("/first") ? token + "-first" :
                            request.Contains("/again") ? token + "-again" : token + "-regular";
                        byte[] body = Encoding.ASCII.GetBytes("<html><title>" + title + "</title><body>" + title + "</body></html>");
                        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n");
                        stream.Write(header, 0, header.Length);
                        stream.Write(body, 0, body.Length);
                    }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        });
        using (var proxy = new WebsiteProxy())
        {
            var owned = new System.Collections.Generic.List<IntPtr>();
            try
            {
                proxy.SetRestriction(new[] { "127.0.0.1" });
                string local = "http://127.0.0.1:" + port;
                var guard = new ProcessGuard();
                guard.ConfigureFocusEdge(edge, profile, proxy.Port);
                guard.OpenEdge(local + "/first");
                IntPtr first = WaitForEdgeTitle(token + "-first");
                owned.Add(first);
                int firstPid;
                GetWindowThreadProcessId(first, out firstPid);

                var restarted = new ProcessGuard();
                restarted.ConfigureFocusEdge(edge, profile, proxy.Port);
                restarted.TestFolder = Path.GetDirectoryName(edge).TrimEnd('\\') + "\\";
                restarted.SetRestriction(new AppRule[0], new string[0]);
                restarted.TestForeground = () => first;
                Require(restarted.CheckForeground() == null && restarted.ForegroundPath != null && !IsIconic(first),
                    "A fresh guard rejected the already open dedicated Edge.");
                restarted.OpenEdge(local + "/again");
                IntPtr again = WaitForEdgeTitle(token + "-again");
                owned.Add(again);
                int againPid;
                GetWindowThreadProcessId(again, out againPid);
                restarted.TestForeground = () => again;
                Require(firstPid == againPid && restarted.CheckForeground() == null && restarted.ForegroundPath != null,
                    "Reopening the same profile did not authorize its actual window owner.");

                restarted.OpenEdge("http://localhost:" + port + "/denied");
                IntPtr denied = WaitForEdgeTitle("localhost", againPid);
                owned.Add(denied);
                Require(Exchange(proxy.Port, "GET http://localhost:" + port + "/denied HTTP/1.1\r\nHost: localhost\r\n\r\n").Contains("403") &&
                    Interlocked.CompareExchange(ref deniedRequests, 0, 0) == 0, "The unlisted host reached the origin.");

                using (var process = Process.Start(new ProcessStartInfo(edge, "--user-data-dir=\"" + normal +
                    "\" --no-first-run --new-window " + local + "/regular") { UseShellExecute = false })) { }
                IntPtr regular = WaitForEdgeTitle(token + "-regular");
                owned.Add(regular);
                restarted.TestForeground = () => regular;
                var block = restarted.CheckForeground();
                Require(block != null && block.Edge && SpinWait.SpinUntil(() => IsIconic(regular), 3000),
                    "The normal Edge window was incorrectly allowed.");
                Console.WriteLine("PASS real Edge content, existing profile after restart, second window, denied site, and ordinary Edge minimization");
            }
            finally
            {
                foreach (IntPtr window in owned) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                server.Stop();
                origin.Wait(4500);
            }
        }
    }

    private static IntPtr WaitForEdgeTitle(string title, int expectedPid = 0)
    {
        IntPtr found = IntPtr.Zero;
        Require(SpinWait.SpinUntil(delegate
        {
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                var text = new StringBuilder(512);
                GetWindowText(window, text, text.Capacity);
                if (text.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) < 0) return true;
                int pid;
                GetWindowThreadProcessId(window, out pid);
                if (expectedPid != 0 && pid != expectedPid) return true;
                found = window;
                return false;
            }, IntPtr.Zero);
            return found != IntPtr.Zero;
        }, 16000), "Edge window did not display " + title + ".");
        return found;
    }

    private delegate bool WindowEnumerator(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowEnumerator callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);

    private sealed class TestWindow : IDisposable
    {
        public readonly Process Process;
        public IntPtr Handle;
        public TestWindow(string executable)
        {
            string title = "StudyFocusProbe-" + Guid.NewGuid().ToString("N");
            Process = Process.Start(new ProcessStartInfo(executable, title) { UseShellExecute = false, CreateNoWindow = true });
            try
            {
                Require(SpinWait.SpinUntil(() => (Handle = FindWindow(null, title)) != IntPtr.Zero, 6000),
                    "Could not find the probe window.");
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (Handle != IntPtr.Zero) PostMessage(Handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
            if (!Process.WaitForExit(1000)) Process.Kill();
            Process.Dispose();
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string windowClass, string title);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr window);

    private static void TrayExit(string data)
    {
        var file = new StateFile(data);
        var engine = new FocusEngine(file, new State());
        var now = DateTimeOffset.Now;
        var start = now.AddDays(1).Date.AddHours(12);
        var plan = new FocusSchedule { Days = 1 << (int)start.DayOfWeek, Minute = 720, Enabled = true };
        var rules = engine.Settings.Copy();
        rules.Schedules = new System.Collections.Generic.List<FocusSchedule> { plan };
        engine.UpdateSettings(now, rules);
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var uninstall = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var window = new MainWindow(engine, show, uninstall, data, false))
        {
            var handle = window.Handle;
            var quit = Field<ToolStripMenuItem>(window, "quit");
            var render = typeof(MainWindow).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance);
            Require(quit.Enabled, "An enabled future plan disabled exit while idle.");

            engine.Start(now, null);
            render.Invoke(window, new object[] { now, "", engine.NextSchedule(now) });
            Require(!quit.Enabled && quit.Text.Contains("专注中"), "Active focus must prevent exit and show why.");
            Invoke(window, "ExitApp");
            Require(!window.IsDisposed && engine.Session != null, "The tray action bypassed the active focus restriction.");
            engine.State.Session = null;

            var nearStart = new DateTimeOffset(start.AddMinutes(-1), now.Offset);
            render.Invoke(window, new object[] { nearStart, "", engine.NextSchedule(nearStart) });
            Require(!quit.Enabled && quit.Text.Contains("即将自动开始"), "The tray should show the upcoming plan lock.");
            render.Invoke(window, new object[] { now, "", engine.NextSchedule(now) });

            using (var respond = new System.Windows.Forms.Timer { Interval = 50 })
            {
                int button = 7;
                respond.Tick += delegate
                {
                    IntPtr dialog = GetLastActivePopup(handle);
                    if (dialog == IntPtr.Zero || dialog == handle) return;
                    respond.Stop();
                    PostMessage(dialog, 0x0111, new IntPtr(button), IntPtr.Zero);
                };
                respond.Start();
                quit.PerformClick();
                Require(!respond.Enabled && !window.IsDisposed, "Declining confirmation must keep the application open.");
                button = 6;
                respond.Start();
                quit.PerformClick();
                Require(!respond.Enabled && window.IsDisposed, "Confirming exit while idle must close the application.");
            }
            Require(file.Load().Settings.EffectiveSchedules().Single().Enabled,
                "Exiting removed or disabled the saved automatic plan.");
        }
    }

    private static void Interface(string data, string allowProbe, string denyProbe)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var file = new StateFile(data);
        var engine = new FocusEngine(file, new State());
        using (var show = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var uninstall = new EventWaitHandle(false, EventResetMode.AutoReset))
        using (var window = new MainWindow(engine, show, uninstall, data, false))
        {
            window.StartPosition = FormStartPosition.Manual;
            window.Location = new Point(SystemInformation.VirtualScreen.Right + 1000, 0);
            window.ShowInTaskbar = false;
            window.Opacity = 0;
            window.Show();
            Application.DoEvents();
            Require(window.Text.Length > 0 && window.Controls.Count > 0, "Form did not load.");
            var theme = Field<Control>(window, "theme");
            Color light = window.BackColor;
            Require(light.ToArgb() == Color.FromArgb(238, 243, 250).ToArgb(), "The light theme canvas has the wrong color.");
            Choose(theme, 1);
            Require(window.BackColor.ToArgb() == Color.FromArgb(16, 24, 39).ToArgb() &&
                file.Load().Settings.Dark, "The dark theme did not apply and save immediately.");
            Preview(window, "focus-dark-preview.png");
            Choose(theme, 0);
            Require(window.BackColor == light && !file.Load().Settings.Dark, "Light theme did not apply and save immediately.");
            var next = engine.Settings.Copy();
            next.CountUp = true;
            engine.UpdateSettings(DateTimeOffset.Now, next);
            Invoke(window, "LoadRules");
            Invoke(window, "Tick");
            Require(Field<string>(window, "clockText") == "正计时" &&
                Field<Label>(window, "goal").Text.IndexOf("分钟", StringComparison.Ordinal) < 0, "Countdown target appears in count-up mode.");
            int idleRepaints = 0;
            Field<Panel>(window, "dial").Invalidated += delegate { idleRepaints++; };
            Invoke(window, "Tick");
            Invoke(window, "Tick");
            Require(idleRepaints == 0, "Idle ticks repainted the dial.");
            Preview(window, "focus-preview.png");
            var select = typeof(MainWindow).GetMethod("SelectPage", BindingFlags.NonPublic | BindingFlags.Instance);
            var pages = Field<Control[]>(window, "pages");
            var host = Field<Panel>(window, "main");
            var statisticsPlaceholder = pages[3];
            int pageLayoutChanges = 0;
            bool animations = Motion.Enabled;
            try
            {
                Motion.Enabled = true;
                select.Invoke(window, new object[] { 3 });
                Require(statisticsPlaceholder.IsDisposed && pages[3] != statisticsPlaceholder &&
                    !Motion.IsRunning(pages[3]), "The first statistics visit did not mount its page without a transition.");
                foreach (var entry in pages)
                {
                    entry.SizeChanged += delegate { pageLayoutChanges++; };
                    entry.VisibleChanged += delegate { pageLayoutChanges++; };
                }
                select.Invoke(window, new object[] { 1 });
                Require(!Motion.IsRunning(pages[1]), "The first visit to rules started a page transition.");
                select.Invoke(window, new object[] { 0 });
                select.Invoke(window, new object[] { 1 });
                Require(pages.All(control => control.Visible) && host.Controls.GetChildIndex(pages[1]) == 0 &&
                    GetTopWindow(host.Handle) == pages[1].Handle && pageLayoutChanges == 0,
                    "Switching page toggled visibility, resized a page, or displayed the wrong page.");
                Motion.Enabled = false;
                Require(pages[1].Left == 0 && !Motion.Busy, "Disabling motion did not finish the page transition.");
                var tabKey = typeof(MainWindow).GetMethod("ProcessTabKey", BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < 24; i++)
                {
                    tabKey.Invoke(window, new object[] { true });
                    Require(window.ActiveControl == null || !host.Contains(window.ActiveControl) ||
                        pages[1].Contains(window.ActiveControl), "The Tab key focused a covered page.");
                }
            }
            finally { Motion.Enabled = animations; }
            Require(!Field<Control>(window, "duration").Visible, "Duration input should not be visible in count-up mode.");
            Require(pages[4].Contains(Field<CheckBox>(window, "startup")) && !Field<CheckBox>(window, "startup").Checked &&
                pages[4].Contains(theme) && Descendants(pages[4]).Any(control => control.Text == "高级设置"),
                "Preferences were not moved to the Settings page.");
            ScheduleCountdown(window);
            SkipScheduleFromHome(window, file, select);
            select.Invoke(window, new object[] { 4 });
            Preview(window, "focus-settings-preview.png");
            select.Invoke(window, new object[] { 1 });
            Require(!Descendants(pages[1]).Any(control => control.Text == "保存规则" || control.Text == "撤销修改"),
                "The rule page still shows manual save controls.");
            Choose(Field<Control>(window, "mode"), 0);
            Require(!file.Load().Settings.CountUp, "Changing the timer mode was not saved immediately.");
            var number = Field<Control>(window, "duration");
            var text = Descendants(number).OfType<TextBox>().Single();
            Require(number.Visible && text.Text == "45", "Countdown duration is not shown as a themed stepper.");
            Press(number, "+");
            Require(text.Text == "46" && file.Load().Settings.Minutes == 46, "The stepper did not save the increased duration.");
            Press(number, "−");
            Require(text.Text == "45" && file.Load().Settings.Minutes == 45, "The stepper did not save the decreased duration.");
            var keyDown = typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
            text.Text = "54";
            Require(file.Load().Settings.Minutes == 45, "A partial duration was saved before commit.");
            keyDown.Invoke(text, new object[] { new KeyEventArgs(Keys.Enter) });
            Require(file.Load().Settings.Minutes == 54, "Enter did not commit a valid duration.");
            text.Text = "0";
            keyDown.Invoke(text, new object[] { new KeyEventArgs(Keys.Enter) });
            Require(file.Load().Settings.Minutes == 54 && text.Text == "54", "An invalid duration was saved or left in the field.");
            text.Text = "45";
            typeof(Control).GetMethod("OnLeave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(number, new object[] { EventArgs.Empty });
            Invoke(window, "Tick");
            Require(file.Load().Settings.Minutes == 45 && Field<Label>(window, "state").Text == "已保存",
                "Leaving the duration field did not save or show confirmation.");
            var rulesScroll = Field<Control>(window, "editor").Parent as Panel;
            Require(rulesScroll != null && !rulesScroll.VerticalScroll.Visible, "Rules should fit without a scroll bar at the default window size.");
            Choose(theme, 1);
            Preview(window, "focus-dark-rules-preview.png");
            Choose(theme, 2);
            Require(file.Load().Settings.FollowSystemTheme && (window.BackColor.ToArgb() == Color.FromArgb(16, 24, 39).ToArgb()) ==
                Program.UsesDarkTheme(engine.Settings), "The system theme option did not persist or use the Windows setting.");
            Choose(theme, 0);
            select.Invoke(window, new object[] { 0 });
            Invoke(window, "Tick");
            Preview(window, "focus-ready-preview.png");
            Preflight(window);
            select.Invoke(window, new object[] { 3 });
            var period = typeof(MainWindow).GetField("statisticsDate", BindingFlags.NonPublic | BindingFlags.Instance);
            period.SetValue(window, DateTime.Today.AddMonths(-2));
            Invoke(window, "RefreshStatistics");
            Require(Field<Control>(window, "statisticsChart").AccessibleDescription == "暂无专注时长", "Empty trend data was not announced.");
            Preview(window, "focus-statistics-empty-preview.png");
            period.SetValue(window, DateTime.Today);
            Invoke(window, "RefreshStatistics");
            select.Invoke(window, new object[] { 1 });
            Choose(Field<Control>(window, "mode"), 1);
            Invoke(window, "LoadRules");
            var guard = Field<ProcessGuard>(window, "guard");
            guard.TestFolder = Path.GetDirectoryName(allowProbe).TrimEnd('\\') + "\\";
            using (var previous = new TestWindow(denyProbe))
            {
                guard.TestForeground = () => IntPtr.Zero;
                Invoke(window, "StartTrial");
                Require(Field<bool>(window, "enforced") && engine.Session == null, "Trial didn't enable isolated restrictions.");
                guard.TestForeground = () => previous.Handle;
                var deadline = Stopwatch.StartNew();
                Invoke(window, "Tick");
                Require(SpinWait.SpinUntil(() => IsIconic(previous.Handle), 2500) && !previous.Process.HasExited,
                    "Trial didn't minimize the existing app or closed it.");
                var warning = Field<Form>(window, "blockNotice");
                Require(warning != null, "A blocked window did not show the dismissible notification.");
                while (Field<Form>(window, "blockNotice") != null && deadline.Elapsed.TotalSeconds < 6)
                {
                    Application.DoEvents();
                    Thread.Sleep(40);
                }
                Require(Field<Form>(window, "blockNotice") == null && deadline.Elapsed.TotalSeconds >= 3,
                    "The blocked-window notice should close automatically (elapsed " + deadline.ElapsedMilliseconds +
                    " ms, still visible: " + (Field<Form>(window, "blockNotice") != null) + ").");
                while (Field<Stopwatch>(window, "trial") != null && deadline.Elapsed.TotalSeconds < 12)
                {
                    Thread.Sleep(250);
                    Invoke(window, "Tick");
                }
                Require(!Field<bool>(window, "enforced") && engine.Session == null && engine.State.Stops.Count == 0,
                    "Trial did not release restrictions or affected the normal session.");
            }
            var categories = Field<Control>(window, "ruleSections");
            var sections = Field<Control[]>(window, "ruleViews");
            Choose(categories, 1);
            AppAllowlist(window, file);
            Require(sections[1].Visible && sections.Count(section => section.Visible) == 1 &&
                Field<ListView>(window, "ruleApps").Items.Count == 2, "The software page did not show the saved groups in its own view.");
            var search = Field<TextBox>(window, "appSearch");
            search.Text = "no-such-app-probe";
            var softwareList = Field<ListView>(window, "ruleApps");
            Require(softwareList.VirtualMode && softwareList.Items.Count == 0 && file.Load().Settings.AppRules.Count == 2 &&
                Field<Control>(window, "appEmpty").Visible, "Search should show its empty state and leave saved groups alone.");
            Press(sections[1], "清除筛选");
            Require(search.TextLength == 0 && softwareList.Items.Count == 2 && softwareList.Columns.Count == 4 &&
                softwareList.Items[1].SubItems.Count == softwareList.Columns.Count,
                "Clear filters did not restore the virtual rows and their columns.");
            var categoryMenu = Field<ToolStripMenuItem[]>(window, "appCategories");
            categoryMenu[2].PerformClick();
            Require(softwareList.Items.Count == 0 && file.Load().Settings.AppRules.Count == 2 && Field<Control>(window, "appEmpty").Visible,
                "The system category included third-party apps or modified the stored rules.");
            Press(sections[1], "清除筛选");
            Require(softwareList.Items.Count == 2 && (int)typeof(MainWindow).GetField("appCategory", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(window) == 0, "Clear filters did not reset the category.");
            Preview(window, "focus-rules-apps-preview.png");
            Choose(categories, 2);
            WebsiteAllowlist(window, file);
            Require(sections[2].Visible && Field<ListView>(window, "ruleWebsites").Columns.Count == 2,
                "The websites page did not expose its domain and matching scope columns.");
            var domainInput = Field<TextBox>(window, "websiteInput");
            domainInput.Text = "https://inline.example.net/resource";
            Invoke(window, "AddWebsiteInline");
            var webSearch = Field<TextBox>(window, "websiteSearch");
            webSearch.Text = "INLINE.EXAMPLE.NET";
            Require(file.Load().Settings.Websites.Contains("inline.example.net") &&
                Field<ListView>(window, "ruleWebsites").Items.Count == 1 && domainInput.Text.Length == 0,
                "Inline website addition did not normalize, save, and filter the domain.");
            webSearch.Clear();
            Preview(window, "focus-rules-websites-preview.png");
            Choose(categories, 3);
            Preview(window, "focus-rules-checks-preview.png");
            Choose(categories, 0);
            ScheduleEditor(window, file);
            Invoke(window, "Tick");
            Preview(window, "focus-rules-timing-preview.png");
            SessionSounds(window);
            FloatingTimer(window, file);
            typeof(MainWindow).GetMethod("SelectPage", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(window, new object[] { 3 });
            Invoke(window, "Tick");
            Require(Field<Label>(window, "statisticsRounds").Text != "0 次", "The statistics page did not load history.");
            Preview(window, "focus-statistics-preview.png");
            Choose(theme, 1);
            Preview(window, "focus-dark-statistics-preview.png");
            Size previousSize = window.Size;
            window.Size = window.MinimumSize;
            Preview(window, "focus-dark-statistics-compact-preview.png");
            select.Invoke(window, new object[] { 0 });
            Preview(window, "focus-dark-compact-preview.png");
            window.Size = previousSize;
            ReferencePages(window, file, select, categories);
            MeasureNavigation(window, select);
        }
    }

    private static void Choose(Control choice, int index)
    {
        choice.GetType().GetProperty("SelectedIndex").SetValue(choice, index, null);
    }

    private static void Preflight(MainWindow window)
    {
        var engine = Field<FocusEngine>(window, "engine");
        var start = Field<Button>(window, "start");
        foreach (bool confirm in new[] { false, true })
        {
            Exception failure = null;
            using (var response = new System.Windows.Forms.Timer { Interval = 60 })
            {
                response.Tick += delegate
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Text == "开始前检查");
                    if (dialog == null) return;
                    response.Stop();
                    try
                    {
                        Require(engine.Session == null && !Field<bool>(window, "enforced"), "Opening preflight started restrictions.");
                        Require(dialog.AcceptButton == dialog.CancelButton &&
                            Descendants(dialog).Any(control => control.Text == "软件白名单为空") &&
                            Descendants(dialog).Any(control => control.Text == "网站白名单为空"), "Preflight did not explain the empty allowlists.");
                        Preview(dialog, "focus-preflight-preview.png");
                        if (confirm) Press(dialog, "开始这轮专注");
                        else dialog.CancelButton.PerformClick();
                    }
                    catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; }
                };
                response.Start();
                start.PerformClick();
                if (failure != null) throw failure;
                Require(!response.Enabled && (engine.Session != null) == confirm && Field<bool>(window, "enforced") == confirm,
                    "The preflight decision did not control whether focus started.");
            }
        }
        Preview(window, "focus-active-preview.png");
        engine.EmergencyRelease(DateTimeOffset.Now);
        Invoke(window, "Tick");
        Console.WriteLine("PASS preflight cancel has no side effects and confirm starts the configured round");
    }

    private static void SkipScheduleFromHome(MainWindow window, StateFile file, MethodInfo select)
    {
        var engine = Field<FocusEngine>(window, "engine");
        var previous = engine.Settings;
        var rules = previous.Copy();
        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, now.Offset).AddMinutes(2);
        rules.Schedules = new System.Collections.Generic.List<FocusSchedule>
        {
            new FocusSchedule { Days = 1 << (int)start.DayOfWeek, Minute = (int)start.TimeOfDay.TotalMinutes,
                EndMinute = (int)start.AddMinutes(18).TimeOfDay.TotalMinutes, Enabled = true }
        };
        engine.UpdateSettings(now, rules);
        Invoke(window, "LoadRules");
        select.Invoke(window, new object[] { 0 });
        Invoke(window, "Tick");
        var skip = Field<Button>(window, "skipSchedule");
        Require(skip.Visible && !Field<Button>(window, "start").Enabled, "A near-term plan did not expose its skip control.");
        skip.PerformClick();
        Require(engine.Session == null && !skip.Visible && Field<Button>(window, "start").Enabled &&
            file.Load().SkippedScheduleStartsUtc.Contains(start.UtcTicks), "Skipping the plan from the main page did not release the lock.");
        engine.UpdateSettings(DateTimeOffset.Now, previous.Copy());
        Invoke(window, "LoadRules");
        Console.WriteLine("PASS main window skips the next occurrence and immediately restores manual start");
    }

    private static void ReferencePages(MainWindow window, StateFile file, MethodInfo select, Control categories)
    {
        var engine = Field<FocusEngine>(window, "engine");
        var theme = Field<Control>(window, "theme");
        Choose(theme, 1);
        select.Invoke(window, new object[] { 4 });
        VerifySettingsPaint(window, theme);
        Preview(window, "reference-settings.png");
        var settingsSections = Field<Control>(window, "settingsSections");
        Choose(settingsSections, 1);
        var opacity = Field<TrackBar>(window, "opacity");
        Preview(window, "reference-hud-settings-100.png");
        opacity.Value = 87;
        NativeKey(opacity.Handle, 0x0100, (IntPtr)Keys.Right, IntPtr.Zero);
        NativeKey(opacity.Handle, 0x0101, (IntPtr)Keys.Right, IntPtr.Zero);
        Require(file.Load().Settings.EffectiveHudOpacity == 88, "HUD opacity did not persist.");
        Preview(window, "reference-hud-settings.png");
        Choose(settingsSections, 0);
        var notices = Field<CheckBox>(window, "showNotifications");
        notices.Checked = false;
        Require(!file.Load().Settings.ShowNotifications && file.Load().Settings.PlayChimes, "The system notice switch altered the chime or failed to save.");
        notices.Checked = true;

        var now = DateTimeOffset.Now;
        var old = now.AddMonths(-1);
        var previousStop = new StopRecord { Month = old.ToString("yyyy-MM"), WhenUtc = old.UtcTicks, Minutes = 1040, Reason = "临时调整" };
        engine.State.Stops.Add(previousStop);
        select.Invoke(window, new object[] { 2 });
        var list = Field<ListView>(window, "records");
        var scope = Field<Control>(window, "recordScope");
        int all = list.Items.Count;
        Choose(scope, 2);
        Require(list.Items.Count < all && list.Items.Cast<ListViewItem>().All(row => row.Tag != previousStop),
            "Month selection did not filter stops by local date.");
        Choose(scope, 0);
        Require(list.Items.Cast<ListViewItem>().Any(item => item.SubItems[1].Text == "1040 分钟") &&
            list.Columns[1].Width >= TextRenderer.MeasureText("1040 分钟", list.Font).Width + 12,
            "Four-digit remaining minutes were truncated.");
        Preview(window, "reference-stop-records.png");
        engine.State.Stops.Remove(previousStop);

        var rule = engine.Settings.Copy();
        rule.CountUp = false;
        rule.ShowFloatingTimer = false;
        rule.MonthlyStopCount = 3;
        int tomorrow = 1 << (int)now.AddDays(1).DayOfWeek;
        rule.Schedules = new[] { 450, 690, 870, 1140 }.Select((minute, index) => new FocusSchedule
        {
            Days = tomorrow, Minute = minute, EndMinute = minute + 45, Enabled = index != 2, CountUp = index < 2
        }).ToList();
        engine.UpdateSettings(now, rule);
        Invoke(window, "LoadRules");
        select.Invoke(window, new object[] { 1 });
        string[] pages = { "reference-timing.png", "reference-apps.png", "reference-websites.png", "reference-check.png" };
        for (int i = 0; i < pages.Length; i++)
        {
            Choose(categories, i);
            Preview(window, pages[i]);
        }
        select.Invoke(window, new object[] { 0 });
        Invoke(window, "Tick");
        Preview(window, "reference-focus-idle.png");
        engine.Start(now.AddMinutes(-16), null);
        try
        {
            Invoke(window, "Tick");
            Require(!Field<Button>(window, "websiteAdd").Enabled, "Inline website editing remained enabled during focus.");
            bool rejected = false;
            try { Invoke(window, "AddWebsiteInline"); }
            catch (TargetInvocationException ex)
            {
                if (!(ex.InnerException is InvalidOperationException)) throw;
                rejected = true;
            }
            Require(rejected, "Inline website editing ignored the focus lock.");
            Preview(window, "reference-focus-active.png");
        }
        finally
        {
            engine.EmergencyRelease(DateTimeOffset.Now);
            Invoke(window, "Tick");
        }
        select.Invoke(window, new object[] { 3 });
        var focusHistory = Field<FocusHistory>(window, "history");
        var append = typeof(FocusHistory).GetMethod("Append", BindingFlags.Instance | BindingFlags.NonPublic);
        string[] apps = { "Microsoft Edge", "Visual Studio Code", "Microsoft Word", "记事本" };
        for (int i = 0; i < 6; i++)
        {
            var at = new DateTimeOffset(now.Date.AddDays(-i).AddHours(9));
            append.Invoke(focusHistory, new object[] { new FocusUse { From = at.UtcTicks,
                To = at.AddMinutes(102 - 12 * i).UtcTicks, Round = at.UtcTicks, App = apps[i % apps.Length] } });
        }
        Invoke(window, "RefreshStatistics");
        Preview(window, "reference-statistics.png");
        var full = window.Size;
        window.Size = window.MinimumSize;
        Application.DoEvents();
        var distribution = Field<Control>(window, "distributionChart");
        using (var bitmap = new Bitmap(distribution.Width, distribution.Height))
        using (var graphics = Graphics.FromImage(bitmap))
            distribution.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(distribution, new object[] { new PaintEventArgs(graphics, distribution.ClientRectangle) });
        var statsList = Field<ListView>(window, "statisticsList");
        Require(statsList.Columns.Cast<ColumnHeader>().Sum(column => column.Width) <= statsList.ClientSize.Width,
            "Statistics columns exceeded the compact viewport.");
        Require(statsList.Columns[2].Width >= TextRenderer.MeasureText("1小时42分", statsList.Font).Width + 10,
            "A long duration lost a unit or digit in the compact viewport.");
        foreach (string name in new[] { "statisticsTotal", "statisticsAverage" })
        {
            var duration = Field<Label>(window, name);
            Require(duration.Width >= duration.PreferredWidth, "A compact statistics card did not fit " +
                duration.Text + " (" + duration.Width + ", preferred " + duration.PreferredWidth + ").");
        }
        Preview(window, "reference-statistics-compact.png");
        window.Size = full;
        Console.WriteLine("PASS software and website search, locked website editing, record filter, notifications, and HUD opacity");
        Console.WriteLine("PASS unified UI font, opaque settings switches, 100% HUD label, and full four-digit durations");
    }

    private static void VerifySettingsPaint(MainWindow window, Control theme)
    {
        var nativeSize = window.Size;
        var settingsSections = Field<Control>(window, "settingsSections");
        var opacity = Field<TrackBar>(window, "opacity");
        foreach (Size size in new[] { nativeSize, window.MinimumSize })
        {
            window.Size = size;
            foreach (int themeIndex in new[] { 1, 0, 1 })
            {
                Choose(theme, themeIndex);
                Choose(settingsSections, 0);
                Application.DoEvents();
                foreach (string name in new[] { "showFloating", "reduceMotion", "startup", "showNotifications", "playChimes" })
                    VerifySwitchPixels(Field<CheckBox>(window, name));
                Choose(settingsSections, 1);
                opacity.Value = 100;
                Application.DoEvents();
                VerifySwitchPixels(Field<CheckBox>(window, "showFloatingOnPage"));
                var percent = Field<Label>(window, "opacityValue");
                Require(percent.Text == "100%" && percent.Font.Name == "Microsoft YaHei UI" &&
                    percent.Width >= percent.PreferredWidth && percent.Height >= percent.PreferredHeight,
                    "The full HUD opacity value and unit do not fit.");
            }
        }
        window.Size = nativeSize;
        Choose(settingsSections, 0);
    }

    private static void VerifySwitchPixels(Control control)
    {
        Require(control.Width > 40 && control.Height > 22 && control.Font.Name == "Microsoft YaHei UI",
            "The settings switch has the wrong font or failed to lay out.");
        using (var bitmap = new Bitmap(control.Width, control.Height))
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Fuchsia);
            control.DrawToBitmap(bitmap, control.ClientRectangle);
            Require(bitmap.GetPixel(4, 2).ToArgb() == control.BackColor.ToArgb() &&
                bitmap.GetPixel(control.Width / 2, control.Height - 3).ToArgb() == control.BackColor.ToArgb(),
                "The settings switch left pixels from another control or its previous background.");
        }
    }

    private static void MeasureNavigation(MainWindow window, MethodInfo select)
    {
        var samples = new System.Collections.Generic.List<double>();
        bool previous = Motion.Enabled;
        try
        {
            Motion.Enabled = false;
            for (int pass = 0; pass < 5; pass++)
                foreach (int page in new[] { 0, 1, 2, 3, 4 })
                {
                    var sw = Stopwatch.StartNew();
                    select.Invoke(window, new object[] { page });
                    if (pass > 0) samples.Add(sw.Elapsed.TotalMilliseconds);
                }
            samples.Sort();
            double median = samples[samples.Count / 2];
            Require(median < 40, "Warm page navigation exceeded the 40 ms regression threshold.");

            Application.DoEvents();
            Motion.Enabled = true;
            Motion.FrameCount = 0;
            Motion.MaxFrameMs = 0;
            select.Invoke(window, new object[] { 0 });
            var deadline = Stopwatch.StartNew();
            while (Motion.Busy && deadline.ElapsedMilliseconds < 500)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            Require(!Motion.Busy && Motion.FrameCount > 0 && Motion.MaxFrameMs < 50,
                "Page animation stalled or exceeded the 50 ms regression threshold (" + Motion.FrameCount +
                " frames, max " + Motion.MaxFrameMs.ToString("F2") + " ms, enabled " + Motion.Enabled + ").");
            Console.WriteLine("PASS warm navigation median {0:F2} ms, max {1:F2} ms, Motion max callback {2:F2} ms ({3} frames)",
                median, samples.Last(), Motion.MaxFrameMs, Motion.FrameCount);
        }
        finally { Motion.Enabled = previous; }
    }

    private static void FloatingTimer(MainWindow window, StateFile file)
    {
        var engine = Field<FocusEngine>(window, "engine");
        var floatingPreference = Field<CheckBox>(window, "showFloating");
        var previousPopup = Field<Form>(window, "floatingTimer");
        if (previousPopup != null)
        {
            previousPopup.Dispose();
            typeof(MainWindow).GetField("floatingTimer", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, null);
        }
        var now = DateTimeOffset.Now;
        var settings = engine.Settings.Copy();
        settings.CountUp = false;
        settings.Minutes = 45;
        settings.PauseBudgetMinutes = 5;
        settings.MonthlyStopCount = 1;
        settings.HudX = 100000;
        settings.HudY = 100000;
        settings.ScheduleDays = 0;
        settings.Schedules = new System.Collections.Generic.List<FocusSchedule>();
        engine.UpdateSettings(now, settings);
        Invoke(window, "LoadRules");
        engine.Start(now.AddMinutes(-19), null);
        Invoke(window, "Tick");
        var popup = Field<Form>(window, "floatingTimer");
        var themeControl = Field<Control>(window, "theme");
        var animationPreference = Field<CheckBox>(window, "reduceMotion");
        var round = engine.Session;
        Require(popup.Visible && popup.TopMost && !popup.ShowInTaskbar && popup.Owner == null && themeControl.Enabled &&
            !Field<Control>(window, "startup").Enabled && !Field<Control>(window, "advanced").Enabled &&
            Descendants(popup).Any(control => control.Text == Field<string>(window, "clockText")),
            "The floating countdown window or live settings permissions are wrong.");
        Require(Screen.FromRectangle(popup.Bounds).WorkingArea.Contains(popup.Bounds) &&
            popup.ContextMenuStrip.Items.Count == 3 && popup.ContextMenuStrip.Items[0].Text == "显示主窗口",
            "The floating window did not clamp its saved position or expose context actions.");
        var location = popup.Location;
        typeof(MainWindow).GetMethod("SaveHudPosition", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(window, new object[] { location });
        Require(file.Load().Settings.HudX == location.X && file.Load().Settings.HudY == location.Y,
            "The HUD position did not persist while the rules were locked.");
        var digits = Descendants(popup).OfType<Label>().Single(control => control.Text == Field<string>(window, "clockText"));
        Require(digits.Height >= digits.PreferredHeight && digits.Width >= digits.PreferredWidth,
            "The floating timer is too small for the clock text (" + digits.Size + ", preferred " + digits.PreferredSize + ").");
        var modeLabel = Descendants(popup).OfType<Label>().Single(control => control.Text == "倒计时");
        Require(modeLabel.Width >= modeLabel.PreferredWidth && modeLabel.Height >= modeLabel.PreferredHeight,
            "The floating timer mode label is clipped.");
        var beforeStyle = popup.BackColor;
        Choose(themeControl, 1);
        Require(popup.BackColor != beforeStyle && file.Load().Settings.Dark, "Theme did not apply to a live floating timer.");
        Preview(popup, "focus-floating-dark.png");
        animationPreference.Checked = true;
        Require(!Motion.Enabled && file.Load().Settings.ReduceMotion && engine.Session == round &&
            engine.Settings.Minutes == 45 && engine.Settings.MonthlyStopCount == 1,
            "Changing motion preferences altered the session or focus rules.");
        Reject(delegate { engine.UpdateSettings(DateTimeOffset.Now, new Settings()); });
        floatingPreference.Checked = false;
        Require(!popup.Visible && engine.Session == round && !file.Load().Settings.ShowFloatingTimer,
            "Turning off the floating display changed the session or left the window showing.");
        floatingPreference.Checked = true;
        Require(popup.Visible && engine.Session == round, "Enabling the floating display did not restore the live timer.");
        Choose(themeControl, 0);
        window.Hide();
        Require(popup.Visible, "Hiding the main window hid the floating timer.");
        Press(popup, "暂停");
        Require(FocusEngine.Paused(engine.Session, DateTimeOffset.Now) && Descendants(popup).Any(control => control.Text == "暂停剩余"),
            "The floating Pause action did not pause the existing session.");
        Preview(popup, "focus-floating-light.png");
        Press(popup, "继续专注");
        Require(!FocusEngine.Paused(engine.Session, DateTimeOffset.Now), "The floating resume action did not resume the session.");
        Press(popup, "×");
        Invoke(window, "Tick");
        Require(!popup.Visible && engine.Session != null, "Dismissing the floating timer changed the round.");
        Field<ToolStripMenuItem>(window, "floatingMenu").PerformClick();
        Require(popup.Visible, "Tray toggle did not reopen the floating timer.");
        FloatingStop(window, popup, false);
        Require(engine.Session == round && engine.StopsRemaining(DateTimeOffset.Now) == 1, "Canceling the popup stop used the quota.");
        FloatingStop(window, popup, true);
        Require(!popup.Visible && engine.Session == null && engine.StopsRemaining(DateTimeOffset.Now) == 0 &&
            engine.State.Stops.Last().Reason == "验证悬浮窗停止", "The floating Stop bypassed or missed the existing confirmation and quota.");
        engine.Start(DateTimeOffset.Now, null);
        Invoke(window, "Tick");
        Require(!Descendants(popup).OfType<Button>().Single(button => button.AccessibleName == "提前停止").Enabled,
            "The floating timer offered a stop with no monthly allowance.");
        window.Show();
        foreach (bool confirm in new[] { false, true })
        {
            Exception failure = null;
            using (var response = new System.Windows.Forms.Timer { Interval = 50 })
            {
                response.Tick += delegate
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Text == "紧急解除限制");
                    if (dialog == null) return;
                    response.Stop();
                    try
                    {
                        Require(dialog.AcceptButton == dialog.CancelButton && dialog.TopMost, "The emergency prompt is unsafe or obscured by the floating window.");
                        Preview(dialog, "focus-emergency-preview.png");
                        if (confirm) Press(dialog, "解除限制");
                        else dialog.CancelButton.PerformClick();
                    }
                    catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; }
                };
                response.Start();
                Field<ToolStripMenuItem>(window, "emergencyMenu").PerformClick();
                if (failure != null) throw failure;
                Require(!response.Enabled && (engine.Session == null) == confirm && popup.Visible != confirm &&
                    Field<bool>(window, "enforced") != confirm && engine.State.Stops.Count == 1 && (file.Load().Session == null) == confirm,
                    "Emergency release did not respect the choice or clear restrictions with exhausted quota.");
            }
        }
        floatingPreference.Checked = false;
        Require(!file.Load().Settings.ShowFloatingTimer, "The floating timer preference was not saved.");
        settings = engine.Settings.Copy();
        settings.CountUp = true;
        engine.UpdateSettings(DateTimeOffset.Now, settings);
        engine.Start(DateTimeOffset.Now, null);
        Invoke(window, "Tick");
        Require(!popup.Visible, "A preference-disabled floating timer opened automatically.");
        Field<ToolStripMenuItem>(window, "floatingMenu").PerformClick();
        Require(popup.Visible && Descendants(popup).Any(control => control.Text.Contains("正计时")), "Count-up display is missing.");
        Press(popup, "结束");
        Require(engine.Session == null && !popup.Visible, "The floating count-up End action did not finish the session.");
        floatingPreference.Checked = true;
        animationPreference.Checked = false;
        Console.WriteLine("PASS floating pause, resume, stop reason and quota, count-up end, tray, and live appearance settings");
    }

    private static void FloatingStop(MainWindow main, Form owner, bool confirm)
    {
        Exception error = null;
        using (var steps = new System.Windows.Forms.Timer { Interval = 65 })
        {
            steps.Tick += delegate
            {
                var dialog = Field<Form>(main, "stopDialog");
                if (dialog == null) return;
                steps.Stop();
                try
                {
                    Require(dialog.Owner == owner && dialog.TopMost && !main.Visible, "The stop prompt did not open above the floating window.");
                    var ok = Descendants(dialog).OfType<Button>().First(button => button.Text == "确认停止");
                    Require(!ok.Enabled, "The stop prompt accepted an empty reason.");
                    if (confirm)
                    {
                        Descendants(dialog).OfType<TextBox>().Single().Text = "验证悬浮窗停止";
                        ok.PerformClick();
                    }
                    else dialog.CancelButton.PerformClick();
                }
                catch (Exception ex) { error = ex; dialog.DialogResult = DialogResult.Cancel; }
            };
            steps.Start();
            Press(owner, "提前停止");
        }
        if (error != null) throw error;
    }

    private static void SessionSounds(MainWindow window)
    {
        var startChime = (System.Media.SoundPlayer)typeof(FocusSounds).GetField("start", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var endChime = (System.Media.SoundPlayer)typeof(FocusSounds).GetField("end", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        startChime.Load();
        endChime.Load();
        Require(!((MemoryStream)startChime.Stream).ToArray().SequenceEqual(((MemoryStream)endChime.Stream).ToArray()),
            "The two sounds should contain different PCM samples.");

        var engine = Field<FocusEngine>(window, "engine");
        var played = new System.Collections.Generic.List<bool>();
        FocusSounds.ObservePlayback = played.Add;
        try
        {
            Invoke(window, "BeginManualFocus");
            Require(engine.Session != null && FocusEngine.OpenEnded(engine.Session), "Manual count-up did not start.");
            Invoke(window, "PauseOrResume");
            Invoke(window, "PauseOrResume");
            Invoke(window, "StopFocus");
            Require(played.SequenceEqual(new[] { true, false }), "Manual focus should beep once at start and once at end, excluding pauses.");

            var now = DateTimeOffset.Now;
            var start = now.AddMinutes(-1);
            var end = now.AddMinutes(10);
            var rules = engine.Settings.Copy();
            rules.Schedules = new System.Collections.Generic.List<FocusSchedule>
            {
                new FocusSchedule
                {
                    Days = 1 << (int)start.DayOfWeek, Minute = (int)start.TimeOfDay.TotalMinutes,
                    EndMinute = (int)end.TimeOfDay.TotalMinutes, Enabled = true, CountUp = false
                }
            };
            engine.UpdateSettings(now, rules);
            var wait = Stopwatch.StartNew();
            while (engine.Session == null && wait.Elapsed.TotalSeconds < 8)
            {
                Invoke(window, "Tick");
                Application.DoEvents();
                Thread.Sleep(100);
            }
            Require(engine.Session != null && engine.Session.Scheduled && !engine.Session.CountUp,
                "The due plan should start with its own countdown mode.");
            engine.Session.FinishUtc = DateTimeOffset.Now.AddSeconds(-1).UtcTicks;
            Invoke(window, "Tick");
            Invoke(window, "Tick");
            Require(engine.Session == null && played.SequenceEqual(new[] { true, false, true, false }),
                "Scheduled focus should beep once at start and once at expiry.");
        }
        finally { FocusSounds.ObservePlayback = null; }
    }

    private static void ScheduleCountdown(MainWindow window)
    {
        var slot = DateTimeOffset.Now.AddMinutes(1);
        var end = slot.AddMinutes(20);
        var due = new ScheduleWindow(slot, end, false);
        var countdown = typeof(MainWindow).GetMethod("ScheduledCountdown", BindingFlags.NonPublic | BindingFlags.Instance);
        var show = typeof(MainWindow).GetMethod("ShowScheduleCountdown", BindingFlags.NonPublic | BindingFlags.Instance);
        Func<DateTimeOffset, DateTimeOffset?, ScheduleWindow, int> remaining = (now, next, windowDue) =>
            (int)countdown.Invoke(window, new object[] { now, next, windowDue });
        Require(remaining(slot.AddSeconds(-6), slot, null) == 0 && remaining(slot.AddSeconds(-5), slot, null) == 5,
            "Countdown should appear five seconds before the planned start.");
        show.Invoke(window, new object[] { 5 });
        Require(Field<Form>(window, "countdownNotice").Visible && Field<Label>(window, "countdownLabel").Text.Contains("5 秒"),
            "The countdown did not show the correct text.");
        Require(remaining(slot.AddSeconds(-1), slot, null) == 1 && remaining(slot, null, due) == 0,
            "Countdown should reach zero at the planned start.");
        show.Invoke(window, new object[] { 0 });
        Require(Field<Form>(window, "countdownNotice") == null, "The finished countdown window stayed open.");
        Require(remaining(slot.AddMinutes(1), null, null) == 0 &&
            remaining(slot.AddMinutes(2), null, due) == 5 &&
            remaining(slot.AddMinutes(2).AddSeconds(5), null, due) == 0,
            "A late start should count down once before it runs.");
    }

    private static void AppAllowlist(MainWindow window, StateFile file)
    {
        Exception failure = null;
        using (var steps = new System.Windows.Forms.Timer { Interval = 60 })
        {
            steps.Tick += delegate
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Owner == window);
                if (dialog == null) return;
                steps.Stop();
                try
                {
                    var list = Descendants(dialog).OfType<ListView>().Single();
                    Require(dialog.MaximizeBox && list.ClientSize.Height >= 300, "The app list did not get a full-size management window.");
                    var category = Descendants(dialog).Single(control => control.AccessibleName == "软件类别");
                    Choose(category, 1);
                    Require(list.Items.Count >= 5 && list.Items.Cast<ListViewItem>().All(item => item.Tag == null),
                        "Core Windows apps should be visibly separated and permanently allowed.");
                    Preview(dialog, "focus-system-allowlist-preview.png");
                    Choose(category, 0);
                    Picker(dialog);
                    Require(list.Items.Count == 2, "The app picker did not update the editor.");
                    Preview(dialog, "focus-app-allowlist-preview.png");
                    Press(dialog, "保存白名单");
                }
                catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; }
            };
            steps.Start();
            Press(window, "管理软件白名单");
        }
        if (failure != null) throw failure;
        Require(file.Load().Settings.AppRules.Count == 2 && Field<Label>(window, "appSummary").Text.Contains("2"),
            "Saving the app allowlist did not persist its groups and update the rules page.");
    }

    private static void WebsiteAllowlist(MainWindow window, StateFile file)
    {
        Exception failure = null;
        bool discard = false;
        using (var steps = new System.Windows.Forms.Timer { Interval = 60 })
        {
            steps.Tick += delegate
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Owner == window);
                if (dialog == null) return;
                steps.Stop();
                try
                {
                    var list = Descendants(dialog).OfType<ListBox>().Single();
                    Require(dialog.MaximizeBox && list.ClientSize.Height >= 300, "The website list did not get a full-size management window.");
                    if (discard)
                    {
                        Require(list.Items.Count == 17, "Reopening the website list did not load all the saved domains.");
                        list.SetSelected(0, true);
                        list.SetSelected(1, true);
                        Press(dialog, "移除选中");
                        Require(list.Items.Count == 15, "The editor could not remove multiple domains.");
                        Press(dialog, "取消");
                        return;
                    }
                    var input = Descendants(dialog).OfType<TextBox>().Single();
                    input.Text = "HTTPS://Example.com/path";
                    Press(dialog, "添加网站");
                    input.Text = "example.com";
                    Press(dialog, "添加网站");
                    Require(list.Items.Count == 1 && (string)list.Items[0] == "example.com", "Domain normalization or deduplication failed.");
                    input.Text = "*.example.com";
                    Press(dialog, "添加网站");
                    for (int i = 0; i < 15; i++)
                    {
                        input.Text = "learn" + i + ".example.org";
                        Press(dialog, "添加网站");
                    }
                    Preview(dialog, "focus-website-allowlist-preview.png");
                    Press(dialog, "保存白名单");
                }
                catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; }
            };
            steps.Start();
            Press(window, "管理网站白名单");
            if (failure != null) throw failure;
            discard = true;
            steps.Start();
            Press(window, "管理网站白名单");
        }
        if (failure != null) throw failure;
        var settings = file.Load().Settings;
        Require(settings.Websites.Count == 17 && settings.Websites.Contains("*.example.com") &&
            settings.AppRules.Count == 2 && Field<Label>(window, "websiteSummary").Text.Contains("17"),
            "Saving or cancelling the website editor affected the persisted allowlists.");
        var mode = Field<Control>(window, "mode");
        Choose(mode, settings.CountUp ? 0 : 1);
        Require(file.Load().Settings.Websites.SequenceEqual(settings.Websites), "Saving other rules replaced the website allowlist.");
        Choose(mode, settings.CountUp ? 1 : 0);
    }

    private static void Press(Control parent, string text)
    {
        Descendants(parent).OfType<Button>().First(button => button.Text == text || button.AccessibleName == text).PerformClick();
    }

    private static void Picker(Form owner)
    {
        int phase = 0;
        string first = null, second = null;
        Exception failure = null;
        var stopwatch = Stopwatch.StartNew();
        using (var steps = new System.Windows.Forms.Timer { Interval = 60 })
        {
            steps.Tick += delegate
            {
                Form dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Owner == owner);
                if (dialog == null) return;
                try
                {
                    Require(stopwatch.Elapsed.TotalSeconds < 12, "Software picker timed out at stage " + phase);
                    var controls = Descendants(dialog).ToList();
                    var list = controls.OfType<ListView>().First();
                    var search = controls.OfType<TextBox>().First();
                    if (phase == 0 && list.Items.Count > 2 && list.Items[0].ImageIndex > 0)
                    {
                        first = ((AppRule)list.Items[0].Tag).Path;
                        second = ((AppRule)list.Items[1].Tag).Path;
                        Preview(dialog, "focus-picker-preview.png");
                        list.Items[0].Checked = true;
                        search.Text = "nothing-can-match-this-2318";
                        phase = 1;
                    }
                    else if (phase == 1 && list.Items.Count == 0)
                    {
                        search.Text = second;
                        phase = 2;
                    }
                    else if (phase == 2 && list.Items.Count == 1)
                    {
                        list.Items[0].Checked = true;
                        search.Text = "";
                        phase = 3;
                    }
                    else if (phase == 3 && list.Items.Count > 2)
                    {
                        Require(list.CheckedItems.Count == 2 && ((AppRule)list.CheckedItems[0].Tag).Path == first,
                            "Searching dropped the previous checked application.");
                        controls.OfType<Button>().First(button => button.Text == "加入白名单").PerformClick();
                        phase = 4;
                    }
                }
                catch (Exception error)
                {
                    failure = error;
                    dialog.DialogResult = DialogResult.Cancel;
                }
            };
            steps.Start();
            Press(owner, "选择已识别软件");
        }
        if (failure != null) throw failure;
        Require(phase == 4, "The selected apps were not added to the allowlist draft.");
        using (var steps = new System.Windows.Forms.Timer { Interval = 60 })
        {
            var wait = Stopwatch.StartNew();
            steps.Tick += delegate
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Owner == owner);
                if (dialog == null) return;
                var list = Descendants(dialog).OfType<ListView>().Single();
                if (list.Items.Count <= 2 && wait.Elapsed.TotalSeconds < 5) return;
                if (list.Items.Count <= 2 || list.Items.Cast<ListViewItem>().Any(row =>
                    ((AppRule)row.Tag).Path == first || ((AppRule)row.Tag).Path == second))
                    failure = new InvalidOperationException("Saved applications were offered again in the picker.");
                dialog.DialogResult = DialogResult.Cancel;
            };
            steps.Start();
            Press(owner, "选择已识别软件");
        }
        if (failure != null) throw failure;
    }

    private static void ScheduleEditor(MainWindow window, StateFile file)
    {
        var engine = Field<FocusEngine>(window, "engine");
        var now = DateTimeOffset.Now;
        int tomorrow = 1 << (int)now.AddDays(1).DayOfWeek;
        var update = engine.Settings.Copy();
        update.Schedules = new System.Collections.Generic.List<FocusSchedule>
        {
            new FocusSchedule { Enabled = true, Days = tomorrow, Minute = 600, EndMinute = 630 },
            new FocusSchedule { Enabled = false, Days = tomorrow, Minute = 750, EndMinute = 30 }
        };
        engine.UpdateSettings(now, update);
        Invoke(window, "LoadRules");
        Exception failure = null;
        using (var steps = new System.Windows.Forms.Timer { Interval = 60 })
        {
            steps.Tick += delegate
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Owner == window);
                if (dialog == null) return;
                steps.Stop();
                try
                {
                    var list = Descendants(dialog).OfType<ListView>().Single();
                    Require(list.Items.Count == 2 && list.Items[0].Checked && !list.Items[1].Checked,
                        "Schedule editor didn't show each plan's switch.");
                    Require(list.Items[0].SubItems[1].Text == "10:30" && list.Items[1].SubItems[1].Text == "次日 00:30",
                        "The end-time column is missing or does not show overnight plans.");
                    list.Items[1].Selected = true;
                    using (var editSteps = new System.Windows.Forms.Timer { Interval = 60 })
                    {
                        editSteps.Tick += delegate
                        {
                            var edit = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Owner == dialog);
                            if (edit == null) return;
                            editSteps.Stop();
                            try
                            {
                                var endInput = Descendants(edit).OfType<DateTimePicker>().Single(field => field.AccessibleName == "结束时间");
                                endInput.Value = DateTime.Today.AddMinutes(45);
                                Choose(Descendants(edit).Single(field => field.AccessibleName == "计时方式"), 0);
                                Descendants(edit).OfType<Button>().First(button => button.Text == "确定").PerformClick();
                            }
                            catch (Exception ex) { failure = ex; edit.DialogResult = DialogResult.Cancel; }
                        };
                        editSteps.Start();
                        Descendants(dialog).OfType<Button>().First(button => button.Text == "修改选中").PerformClick();
                    }
                    if (failure != null) throw failure;
                    Require(list.Items[1].SubItems[1].Text == "次日 00:45", "Changing a plan's end time did not refresh the list.");
                    Require(list.Items[1].SubItems[2].Text == "倒计时" && list.Items[0].SubItems[2].Text.Contains("跟随设置"),
                        "The list should distinguish an explicit timing mode and one inherited from a legacy plan.");
                    list.Items[0].Checked = false;
                    list.Items[1].Checked = true;
                    Preview(dialog, "focus-schedule-preview.png");
                    Descendants(dialog).OfType<Button>().First(button => button.Text == "保存计划").PerformClick();
                }
                catch (Exception ex) { failure = ex; dialog.DialogResult = DialogResult.Cancel; }
            };
            steps.Start();
            Invoke(window, "EditSchedules");
        }
        if (failure != null) throw failure;
        var saved = file.Load().Settings.Schedules;
        Require(saved.Count == 2 && !saved[0].Enabled && saved[1].Enabled && saved[1].EndMinute == 45 &&
            !saved[0].CountUp.HasValue && saved[1].CountUp == false,
            "Editing the two switches did not persist their independent state and end times.");
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr parent);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr NativeKey(IntPtr window, int message, IntPtr key, IntPtr flags);

    private static void Preview(Form window, string file)
    {
        var deadline = Stopwatch.StartNew();
        while (Motion.Busy && deadline.ElapsedMilliseconds < 300)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        using (var screenshot = new Bitmap(window.Width, window.Height))
        {
            window.DrawToBitmap(screenshot, new Rectangle(Point.Empty, window.Size));
            var app = window as MainWindow;
            if (app != null)
            {
                // DrawToBitmap renders overlapping child controls in reverse z-order.
                var page = Field<Control[]>(app, "pages")[Field<int>(app, "page")];
                Point offset = page.PointToScreen(Point.Empty) - (Size)window.Location;
                using (var content = new Bitmap(page.Width, page.Height))
                using (var graphics = Graphics.FromImage(screenshot))
                {
                    page.DrawToBitmap(content, new Rectangle(Point.Empty, page.Size));
                    graphics.DrawImageUnscaled(content, offset);
                }
            }
            screenshot.Save(Path.Combine(Environment.CurrentDirectory, file), ImageFormat.Png);
        }
    }

    private static void SetupDialogs()
    {
        var version = Program.Version;
        var previous = new Version(1, 19, 10, 0);
        string recommended = @"C:\Users\Example\AppData\Local\Programs\StudyFocus";
        string installed = @"E:\专注守门";
        var singleDrive = Program.InstallLocations(recommended, null, new[] { @"C:\" });
        var upgradeLocations = Program.InstallLocations(recommended, installed, new[] { @"C:\", @"D:\", @"E:\" });
        Require(singleDrive.SequenceEqual(new[] { recommended }) && upgradeLocations.SequenceEqual(new[] { installed, recommended, @"D:\定心" }),
            "Installation locations did not support a system-drive-only PC or preserve the existing location.");
        foreach (bool dark in new[] { true, false })
        {
            string theme = dark ? "dark" : "light";
            using (var fresh = MainWindow.SetupWindow.Install(singleDrive, recommended, null, version, dark, false))
            {
                Require(fresh.Destination == recommended && !fresh.StartupEnabled && fresh.Icon != null,
                    "Fresh setup did not offer a usable path, disabled startup, and an application icon.");
                var startup = Descendants(fresh).OfType<CheckBox>().Single();
                startup.Checked = true;
                Require(fresh.StartupEnabled, "Fresh setup did not expose the selected startup setting.");
                startup.Checked = false;
                Require(((Button)fresh.AcceptButton).Text == "安装", "Fresh setup has the wrong action.");
                Require(fresh.Font.Name == "Microsoft YaHei UI", "Setup uses a different UI font than the main window.");
                var title = Descendants(fresh).OfType<Label>().Single(control => control.Text == "首次安装");
                var section = Descendants(fresh).OfType<Label>().Single(control => control.Text == "选择安装位置");
                Require(title.Parent.Parent == section.Parent.Parent &&
                        title.Parent.Parent is TableLayoutPanel && title.Parent != section.Parent &&
                        title.Font.Size > section.Font.Size && section.Font.Size > fresh.Font.Size,
                    "Setup lost its two-column layout or heading hierarchy.");
                Require(fresh.BackColor.ToArgb() == (dark ? Color.FromArgb(16, 24, 39) : Color.FromArgb(238, 243, 250)).ToArgb(),
                    "The setup did not match the application's color scheme.");
                bool captionApplied = false, startupFits = false, destinationFits = false;
                fresh.Shown += delegate
                {
                    int immersive, result = DwmGetWindowAttribute(fresh.Handle, 20, out immersive, sizeof(int));
                    if (result != 0) result = DwmGetWindowAttribute(fresh.Handle, 19, out immersive, sizeof(int));
                    captionApplied = result != 0 || immersive == (dark ? 1 : 0);
                    var rows = ((TableLayoutPanel)startup.Parent).GetRowHeights();
                    startupFits = rows[4] >= startup.PreferredSize.Height + startup.Margin.Vertical && startup.Width >= startup.PreferredSize.Width;
                    var path = Descendants(fresh).OfType<TextBox>().Single();
                    int lastLineY = path.GetPositionFromCharIndex(path.TextLength - 1).Y;
                    destinationFits = lastLineY >= 0 && lastLineY + path.Font.Height <= path.ClientSize.Height;
                    Preview(fresh, "setup-fresh-" + theme + ".png");
                    fresh.CancelButton.PerformClick();
                };
                Require(fresh.ShowOwned(null) == DialogResult.Cancel, "Setup cancellation returned confirmation.");
                Require(captionApplied, "The native caption did not receive the selected immersive mode.");
                Require(startupFits, "The setup startup choice was clipped.");
                Require(destinationFits, "The setup destination path was clipped.");
            }
            using (var upgrade = MainWindow.SetupWindow.Install(upgradeLocations, recommended, previous, version, dark, true))
            {
                Require(upgrade.Text.Contains(version.ToString(3)) && upgrade.Destination == installed && !Descendants(upgrade).OfType<CheckBox>().Any(),
                    "Upgrade did not show its version, reuse the existing path, and preserve startup preferences.");
                Require(Descendants(upgrade).Any(control => control.Text.Replace('\n', ' ').Contains("v" + previous.ToString(3) + " → v" + version.ToString(3))),
                    "The upgrade does not show both versions.");
                var drives = Descendants(upgrade).OfType<ComboBox>().Single();
                drives.SelectedIndex = 1;
                Require(upgrade.Destination == recommended, "Choosing a different location did not update the preview path.");
                bool upgradeTextFits = false;
                upgrade.Shown += delegate
                {
                    var labels = Descendants(upgrade).OfType<Label>().Where(label => label.Text == "覆盖升级" ||
                        (label.AccessibleName ?? "").StartsWith("程序版本", StringComparison.Ordinal)).ToArray();
                    upgradeTextFits = labels.Length == 2 && labels.All(label =>
                        label.PreferredWidth <= label.Width && label.PreferredHeight <= label.Height);
                    Preview(upgrade, "setup-upgrade-" + theme + ".png");
                    upgrade.AcceptButton.PerformClick();
                };
                Require(upgrade.ShowOwned(null) == DialogResult.OK, "Upgrade confirmation did not return the selected directory.");
                Require(upgradeTextFits, "The upgrade title or version is clipped.");
            }
            using (var uninstall = MainWindow.SetupWindow.Uninstall(installed, dark, version))
            {
                Require(uninstall.AcceptButton == uninstall.CancelButton, "Uninstall must default to cancel.");
                Require(Descendants(uninstall).Any(control => control.Text.Contains("Edge 浏览数据")),
                    "The uninstall did not explain which data is retained.");
                uninstall.Shown += delegate { Preview(uninstall, "setup-uninstall-" + theme + ".png"); uninstall.AcceptButton.PerformClick(); };
                Require(uninstall.ShowOwned(null) == DialogResult.Cancel, "Default uninstall action confirmed removal.");
            }
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    private static T Field<T>(MainWindow window, string name)
    {
        return (T)typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
    }

    private static void Invoke(MainWindow window, string method)
    {
        typeof(MainWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
    }
}
