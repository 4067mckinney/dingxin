
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace StudyFocus
{
    internal sealed class Settings
    {
        public bool CountUp { get; set; }
        public int Minutes { get; set; }
        public int PauseCount { get; set; }
        public int PauseMinutes { get; set; }
        public int? PauseBudgetMinutes { get; set; }
        public int? MonthlyStopCount { get; set; }
        public int? MonthlyStopMinutes { get; set; }
        public bool Startup { get; set; }
        public bool EdgeDefaultProfile { get; set; }
        public bool SystemProxy { get; set; }
        public bool Dark { get; set; }
        public bool FollowSystemTheme { get; set; }
        public bool ShowFloatingTimer { get; set; }
        public bool ReduceMotion { get; set; }
        public int Accent { get; set; }
        public int HudAccent { get; set; }
        public bool HudLocked { get; set; }
        public bool HudPinned { get; set; }
        public int? HudX { get; set; }
        public int? HudY { get; set; }
        public int? HudOpacity { get; set; }
        public bool PlayChimes { get; set; }
        public bool ShowNotifications { get; set; }
        public int ScheduleDays { get; set; }
        public int ScheduleMinute { get; set; }
        public long ScheduleChangedUtc { get; set; }
        public List<string> Apps { get; set; }
        public List<AppRule> AppRules { get; set; }
        public List<FocusSchedule> Schedules { get; set; }
        public List<string> Websites { get; set; }

        public Settings()
        {
            Minutes = 45;
            PauseCount = 2;
            PauseMinutes = 5;
            ShowFloatingTimer = true;
            PlayChimes = ShowNotifications = true;
            ScheduleMinute = 14 * 60 + 30;
            Apps = new List<string>();
            Websites = new List<string>();
        }

        public Settings Copy()
        {
            return (Settings)MemberwiseClone();
        }

        internal int EffectiveHudOpacity { get { return Math.Max(65, Math.Min(100, HudOpacity ?? 100)); } }

        public List<FocusSchedule> EffectiveSchedules()
        {
            if (Schedules != null) return Schedules;
            var plans = new List<FocusSchedule>();
            if (ScheduleDays != 0)
                plans.Add(new FocusSchedule { Minute = ScheduleMinute, Days = ScheduleDays, Enabled = true, ChangedUtc = ScheduleChangedUtc });
            return plans;
        }
    }

    internal sealed class FocusSchedule
    {
        public int Minute { get; set; }
        public int? EndMinute { get; set; }
        public bool? CountUp { get; set; }
        public int Days { get; set; }
        public bool Enabled { get; set; }
        public long ChangedUtc { get; set; }

        public FocusSchedule Copy() { return (FocusSchedule)MemberwiseClone(); }

        public int EndOnStartDay(int defaultMinutes)
        {
            if (!EndMinute.HasValue) return Minute + defaultMinutes;
            return EndMinute.Value > Minute ? EndMinute.Value : EndMinute.Value + 1440;
        }
    }

    internal sealed class ScheduleWindow
    {
        public readonly DateTimeOffset Start, End;
        public readonly bool CountUp;

        public ScheduleWindow(DateTimeOffset start, DateTimeOffset end, bool countUp)
        {
            Start = start;
            End = end;
            CountUp = countUp;
        }
    }

    internal sealed class Session
    {
        public Session() { }
        public long FinishUtc { get; set; }
        public long StartedUtc { get; set; }
        public long PausedTicks { get; set; }
        public bool Scheduled { get; set; }
        public long ScheduleStartUtc { get; set; }
        public long PauseUntilUtc { get; set; }
        public long CurrentPauseTicks { get; set; }
        public int PauseUsed { get; set; }
        public long? ReservedPauseTicks { get; set; }
        public bool CountUp { get; set; }
        public int TotalMinutes { get; set; }
    }

    internal sealed class StopRecord
    {
        public StopRecord() { }
        public string Month { get; set; }
        public long WhenUtc { get; set; }
        public int Minutes { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class State
    {
        public Settings Settings { get; set; }
        public Session Session { get; set; }
        public List<StopRecord> Stops { get; set; }
        public string LastSchedule { get; set; }
        public long LastScheduleUtc { get; set; }
        public List<long> SkippedScheduleStartsUtc { get; set; }
        public AdvancedCredential AdvancedCredential { get; set; }

        public State()
        {
            Settings = new Settings();
            Stops = new List<StopRecord>();
        }
    }

    internal sealed class StateFile
    {
        private readonly string path;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private static readonly byte[] Purpose = System.Text.Encoding.UTF8.GetBytes("StudyFocus.Windows.State.v1");

        public StateFile(string directory)
        {
            path = Path.Combine(directory, "state.dat");
        }

        public State Load()
        {
            Exception failure = null;
            foreach (string candidate in new[] { path, path + ".backup" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    byte[] data = ProtectedData.Unprotect(File.ReadAllBytes(candidate), Purpose, DataProtectionScope.CurrentUser);
                    State state = json.Deserialize<State>(System.Text.Encoding.UTF8.GetString(data));
                    if (state == null || state.Settings == null || state.Stops == null)
                        throw new InvalidDataException("专注记录格式不完整。");
                    return state;
                }
                catch (Exception ex) { failure = ex; }
            }
            if (failure != null)
                throw new InvalidDataException("无法读取专注记录。请保留现有数据后再排查。", failure);
            return new State();
        }

        public void Save(State state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] data = ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(json.Serialize(state)), Purpose, DataProtectionScope.CurrentUser);
            string temp = path + ".new";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".backup", true);
            else File.Move(temp, path);
        }
    }

    internal sealed class FocusEngine
    {
        private static readonly TimeSpan ScheduleLockWindow = TimeSpan.FromMinutes(2);
        private readonly StateFile file;
        private Settings calendarSettings;
        private DateTime calendarDate;
        private TimeSpan calendarOffset;
        private readonly List<ScheduleWindow> calendar = new List<ScheduleWindow>();
        public State State { get; private set; }
        public Settings Settings { get { return State.Settings; } }
        public Session Session { get { return State.Session; } }
        public int PauseLimit { get { return Settings.PauseBudgetMinutes ?? Settings.PauseCount * Settings.PauseMinutes; } }

        public FocusEngine(StateFile file, State state)
        {
            this.file = file;
            State = state;
        }

        public bool Locked(DateTimeOffset now)
        {
            DateTimeOffset? next = NextSchedule(now);
            return Session != null || (next.HasValue && next.Value - now <= ScheduleLockWindow);
        }

        public DateTimeOffset? NextSchedule(DateTimeOffset now)
        {
            RefreshCalendar(now);
            foreach (var window in calendar)
                if (window.Start >= now && IsNewSlot(window.Start)) return window.Start;
            return null;
        }

        private void RefreshCalendar(DateTimeOffset now)
        {
            if (calendarSettings == Settings && calendarDate == now.Date && calendarOffset == now.Offset) return;
            calendar.Clear();
            var plans = Settings.EffectiveSchedules();
            for (int i = -1; i <= 7; i++)
            {
                DateTime day = now.Date.AddDays(i);
                foreach (var plan in plans)
                {
                    if (!plan.Enabled || (plan.Days & (1 << (int)day.DayOfWeek)) == 0) continue;
                    var start = LocalTime(day.AddMinutes(plan.Minute));
                    if (!start.HasValue || (!plan.EndMinute.HasValue && start.Value.UtcTicks < plan.ChangedUtc)) continue;
                    var end = LocalTime(day.AddMinutes(plan.EndOnStartDay(Settings.Minutes)));
                    if (end.HasValue && end.Value > start.Value)
                        calendar.Add(new ScheduleWindow(start.Value, end.Value, plan.CountUp ?? Settings.CountUp));
                }
            }
            calendar.Sort((first, second) => first.Start.CompareTo(second.Start));
            calendarSettings = Settings;
            calendarDate = now.Date;
            calendarOffset = now.Offset;
        }

        private static DateTimeOffset? LocalTime(DateTime time)
        {
            return TimeZoneInfo.Local.IsInvalidTime(time) ? (DateTimeOffset?)null
                : new DateTimeOffset(time, TimeZoneInfo.Local.GetUtcOffset(time));
        }

        private static string ScheduleKey(DateTimeOffset date)
        {
            return date.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        }

        private bool IsNewSlot(DateTimeOffset slot)
        {
            return !WasSkipped(slot) && (State.LastScheduleUtc != 0 ? slot.UtcTicks > State.LastScheduleUtc :
                State.LastSchedule == null || string.CompareOrdinal(ScheduleKey(slot), State.LastSchedule) > 0);
        }

        private bool WasSkipped(DateTimeOffset start)
        {
            return State.SkippedScheduleStartsUtc != null && State.SkippedScheduleStartsUtc.Contains(start.UtcTicks);
        }

        public bool HasEnabledSchedule
        {
            get { return Settings.EffectiveSchedules().Any(x => x.Enabled && x.Days != 0); }
        }

        public ScheduleWindow ActiveWindow(DateTimeOffset now)
        {
            RefreshCalendar(now);
            ScheduleWindow found = null;
            foreach (var window in calendar)
            {
                if (window.Start > now) break;
                if (now < window.End && (found == null || window.Start > found.Start)) found = window;
            }
            return found;
        }

        public void StartActiveSchedule(DateTimeOffset now)
        {
            if (Session != null) return;
            var window = ActiveWindow(now);
            if (window == null) return;
            StartSession(now, window);
        }

        private static void ScheduleLog(string text)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudyFocus");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "schedule.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine);
            }
            catch { }
        }

        public bool DisableScheduleCovering(DateTimeOffset now)
        {
            bool changed = false;
            foreach (var plan in Settings.EffectiveSchedules())
            {
                if (!plan.Enabled) continue;
                var start = LocalTime(now.Date.AddMinutes(plan.Minute));
                if (!start.HasValue || start.Value > now) continue;
                var end = LocalTime(now.Date.AddMinutes(plan.EndOnStartDay(Settings.Minutes)));
                if (end.HasValue && now < end.Value) { plan.Enabled = false; changed = true; }
            }
            if (changed) SaveSettings(Settings.Copy());
            ScheduleLog("covering now=" + now.ToString("s") + " changed=" + changed);
            return changed;
        }

        public bool DisableScheduleStart(DateTimeOffset start)
        {
            int minute = start.Hour * 60 + start.Minute;
            bool changed = false;
            foreach (var plan in Settings.EffectiveSchedules())
            {
                if (!plan.Enabled || plan.Minute != minute) continue;
                plan.Enabled = false;
                changed = true;
            }
            if (changed) SaveSettings(Settings.Copy());
            return changed;
        }
        public bool DisableActiveSchedule(DateTimeOffset now)
        {
            var window = ActiveWindow(now);
            if (window == null) return false;
            bool changed = false;
            foreach (var plan in Settings.EffectiveSchedules())
            {
                if (!plan.Enabled) continue;
                var start = LocalTime(now.Date.AddMinutes(plan.Minute));
                if (start.HasValue && start.Value == window.Start)
                {
                    plan.Enabled = false;
                    changed = true;
                }
            }
            if (changed) SaveSettings(Settings.Copy());
            return changed;
        }
        public bool TryDue(DateTimeOffset now, out DateTimeOffset slot)
        {
            var window = DueWindow(now);
            slot = window == null ? default(DateTimeOffset) : window.Start;
            return window != null;
        }

        public ScheduleWindow DueWindow(DateTimeOffset now)
        {
            RefreshCalendar(now);
            ScheduleWindow latest = null;
            DateTimeOffset end = now;
            foreach (var window in calendar)
            {
                if (window.Start > now) break;
                if (window.End <= now || WasSkipped(window.Start)) continue;
                if (window.End > end) end = window.End;
                if (IsNewSlot(window.Start)) latest = window;
            }
            return latest == null ? null : latest.End == end ? latest : new ScheduleWindow(latest.Start, end, latest.CountUp);
        }

        public DateTimeOffset? SkippableSchedule(DateTimeOffset now)
        {
            if (Session != null) return null;
            var due = DueWindow(now);
            if (due != null) return due.Start;
            var next = NextSchedule(now);
            return next.HasValue && next.Value - now <= ScheduleLockWindow ? next : null;
        }

        public bool SkipSchedule(DateTimeOffset now, DateTimeOffset occurrence)
        {
            if (SkippableSchedule(now) != occurrence) return false;
            var previous = State.SkippedScheduleStartsUtc;
            long cutoff = now.AddDays(-2).UtcTicks;
            State.SkippedScheduleStartsUtc = previous == null ? new List<long>() : previous.Where(start => start >= cutoff).ToList();
            State.SkippedScheduleStartsUtc.Add(occurrence.UtcTicks);
            try { file.Save(State); }
            catch
            {
                State.SkippedScheduleStartsUtc = previous;
                throw;
            }
            return true;
        }

        public void EmergencyRelease(DateTimeOffset now)
        {
            if (Session == null) return;
            var due = DueWindow(now);
            if (due != null) RecordSchedule(due.Start);
            State.Session = null;
            // Keep restrictions off for this process even if the state file cannot be saved.
            file.Save(State);
        }

        private void RecordSchedule(DateTimeOffset occurrence)
        {
            State.LastSchedule = ScheduleKey(occurrence);
            State.LastScheduleUtc = occurrence.UtcTicks;
        }

        public void Start(DateTimeOffset now, DateTimeOffset? scheduled)
        {
            var window = scheduled.HasValue ? DueWindow(now) : null;
            if (window != null && window.Start == scheduled.Value) StartScheduled(now, window);
            else if (!scheduled.HasValue && Session == null) StartSession(now, null);
        }

        public void StartScheduled(DateTimeOffset now, ScheduleWindow window)
        {
            if (now >= window.Start && now < window.End && IsNewSlot(window.Start) &&
                (Session == null || Session.Scheduled)) StartSession(now, window);
        }

        private void StartSession(DateTimeOffset now, ScheduleWindow scheduled)
        {
            Session previous = Session;
            long previousFinish = previous == null ? 0 : previous.FinishUtc;
            int previousMinutes = previous == null ? 0 : previous.TotalMinutes;
            string previousSchedule = State.LastSchedule;
            long previousScheduleUtc = State.LastScheduleUtc;
            bool countUp = scheduled == null ? Settings.CountUp : scheduled.CountUp;
            if (scheduled != null) RecordSchedule(scheduled.Start);
            if (previous == null || previous.CountUp != countUp)
            {
                long endsAt = scheduled == null ? (countUp ? 0 : now.AddMinutes(Settings.Minutes).UtcTicks)
                    : Math.Max(previousFinish, scheduled.End.UtcTicks);
                long pauseUntil = previous != null && Paused(previous, now) ? previous.PauseUntilUtc : 0;
                long pauseTicks = pauseUntil == 0 ? 0 : pauseUntil - now.UtcTicks;
                State.Session = new Session
                {
                    CountUp = countUp,
                    Scheduled = scheduled != null,
                    ScheduleStartUtc = scheduled == null ? 0 : scheduled.Start.UtcTicks,
                    TotalMinutes = endsAt == 0 ? 0 : (int)Math.Ceiling(TimeSpan.FromTicks(endsAt - now.UtcTicks).TotalMinutes),
                    StartedUtc = now.UtcTicks,
                    FinishUtc = endsAt,
                    ReservedPauseTicks = previous == null ? 0 : PauseReservation(previous),
                    PauseUntilUtc = pauseUntil,
                    CurrentPauseTicks = pauseTicks,
                    PausedTicks = pauseTicks
                };
            }
            else if (scheduled.End.UtcTicks > previousFinish)
            {
                previous.FinishUtc = scheduled.End.UtcTicks;
                previous.TotalMinutes = (int)Math.Ceiling(TimeSpan.FromTicks(previous.FinishUtc - previous.StartedUtc).TotalMinutes);
            }
            try { file.Save(State); }
            catch
            {
                State.Session = previous;
                if (previous != null)
                {
                    previous.FinishUtc = previousFinish;
                    previous.TotalMinutes = previousMinutes;
                }
                State.LastSchedule = previousSchedule;
                State.LastScheduleUtc = previousScheduleUtc;
                throw;
            }
        }

        public bool Tick(DateTimeOffset now)
        {
            if (Session == null) return false;
            if (Session.Scheduled)
            {
                var due = DueWindow(now);
                if (due != null && due.Start.UtcTicks <= Session.FinishUtc) StartScheduled(now, due);
            }
            if (!OpenEnded(Session) && now.UtcTicks >= Session.FinishUtc)
            {
                Session completed = Session;
                State.Session = null;
                try { file.Save(State); }
                catch { State.Session = completed; throw; }
                return true;
            }
            if (Session.PauseUntilUtc != 0 && now.UtcTicks >= Session.PauseUntilUtc)
            {
                long previous = Session.PauseUntilUtc;
                Session.PauseUntilUtc = 0;
                try { file.Save(State); }
                catch { Session.PauseUntilUtc = previous; throw; }
                return true;
            }
            return false;
        }

        public static bool Paused(Session session, DateTimeOffset now)
        {
            return session != null && session.PauseUntilUtc > now.UtcTicks;
        }

        public static bool OpenEnded(Session session)
        {
            return session != null && session.CountUp && session.FinishUtc == 0;
        }

        public static TimeSpan Elapsed(Session session, DateTimeOffset now, int pauseMinutes)
        {
            if (session == null) return TimeSpan.Zero;
            if (!OpenEnded(session) && !session.Scheduled)
                return TimeSpan.FromMinutes(session.TotalMinutes) - Remaining(session, now);
            bool paused = Paused(session, now);
            long length = session.CurrentPauseTicks == 0 ? TimeSpan.FromMinutes(pauseMinutes).Ticks : session.CurrentPauseTicks;
            long time = paused ? session.PauseUntilUtc - length : now.UtcTicks;
            long totalPaused = paused ? session.PausedTicks - length : session.PausedTicks;
            return TimeSpan.FromTicks(Math.Max(0, time - session.StartedUtc - totalPaused));
        }

        public void Pause(DateTimeOffset now)
        {
            long length = PauseRemaining(now).Ticks;
            if (Session == null || (!OpenEnded(Session) && now.UtcTicks >= Session.FinishUtc) ||
                Paused(Session, now) || length <= 0)
                throw new InvalidOperationException("当前无法暂停。");
            long previousPause = Session.PauseUntilUtc;
            long previousFinish = Session.FinishUtc;
            long previousPausedTicks = Session.PausedTicks;
            if (Session.Scheduled) length = Math.Min(length, Session.FinishUtc - now.UtcTicks);
            long previousCurrentPause = Session.CurrentPauseTicks;
            long? previousReservation = Session.ReservedPauseTicks;
            Session.ReservedPauseTicks = PauseReservation(Session) + length;
            Session.PauseUntilUtc = now.UtcTicks + length;
            Session.CurrentPauseTicks = length;
            if (OpenEnded(Session) || Session.Scheduled) Session.PausedTicks += length;
            else Session.FinishUtc += length;
            try { file.Save(State); }
            catch
            {
                Session.ReservedPauseTicks = previousReservation;
                Session.PauseUntilUtc = previousPause;
                Session.FinishUtc = previousFinish;
                Session.PausedTicks = previousPausedTicks;
                Session.CurrentPauseTicks = previousCurrentPause;
                throw;
            }
        }

        public void Resume(DateTimeOffset now)
        {
            if (!Paused(Session, now)) return;
            long previousPause = Session.PauseUntilUtc;
            long previousFinish = Session.FinishUtc;
            long previousPausedTicks = Session.PausedTicks;
            long? previousReservation = Session.ReservedPauseTicks;
            Session.ReservedPauseTicks = Math.Max(0, PauseReservation(Session) - (previousPause - now.UtcTicks));
            if (OpenEnded(Session) || Session.Scheduled) Session.PausedTicks -= previousPause - now.UtcTicks;
            else Session.FinishUtc -= previousPause - now.UtcTicks;
            Session.PauseUntilUtc = 0;
            try { file.Save(State); }
            catch
            {
                Session.PauseUntilUtc = previousPause;
                Session.FinishUtc = previousFinish;
                Session.PausedTicks = previousPausedTicks;
                Session.ReservedPauseTicks = previousReservation;
                throw;
            }
        }

        private static long PauseReservation(Session session)
        {
            if (session.ReservedPauseTicks.HasValue) return session.ReservedPauseTicks.Value;
            return session.Scheduled || OpenEnded(session) ? session.PausedTicks :
                Math.Max(0, session.FinishUtc - session.StartedUtc - TimeSpan.FromMinutes(session.TotalMinutes).Ticks);
        }

        public TimeSpan PauseRemaining(DateTimeOffset now)
        {
            long used = Session == null ? 0 : Math.Max(0, PauseReservation(Session) -
                Math.Max(0, Session.PauseUntilUtc - now.UtcTicks));
            return TimeSpan.FromTicks(Math.Max(0, TimeSpan.FromMinutes(PauseLimit).Ticks - used));
        }

        public int MonthlyStopLimit
        {
            get { return Settings.MonthlyStopCount ?? (Settings.MonthlyStopMinutes == 0 ? 0 : 3); }
        }

        public int StopsRemaining(DateTimeOffset now)
        {
            string month = Month(now);
            return Math.Max(0, MonthlyStopLimit - State.Stops.Count(record => record.Month == month));
        }

        private static string Month(DateTimeOffset now)
        {
            return now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        public static TimeSpan Remaining(Session session, DateTimeOffset now)
        {
            if (session == null || OpenEnded(session)) return TimeSpan.Zero;
            long from = session.Scheduled ? now.UtcTicks : Math.Max(now.UtcTicks, session.PauseUntilUtc);
            return TimeSpan.FromTicks(Math.Max(0, session.FinishUtc - from));
        }

        public int MinutesRemaining(DateTimeOffset now)
        {
            return (int)Math.Ceiling(Remaining(Session, now).TotalMinutes);
        }

        public void Stop(DateTimeOffset now, string reason)
        {
            int minutes = MinutesRemaining(now);
            if (Session == null || minutes == 0)
                throw new InvalidOperationException("本轮专注已结束。");
            if (StopsRemaining(now) == 0)
                throw new InvalidOperationException("本月提前停止次数已用完。");
            reason = (reason ?? "").Trim();
            if (reason.Length == 0 || reason.Length > 300)
                throw new InvalidOperationException("请输入 1 到 300 字的停止理由。");
            StopRecord record = new StopRecord { Month = Month(now), WhenUtc = now.UtcTicks, Minutes = minutes, Reason = reason };
            Session previous = Session;
            State.Stops.Add(record);
            State.Session = null;
            try { file.Save(State); }
            catch { State.Session = previous; State.Stops.Remove(record); throw; }
        }

        public void FinishCountUp()
        {
            if (!OpenEnded(Session)) throw new InvalidOperationException("当前没有进行中的无时限正计时。");
            Session previous = Session;
            State.Session = null;
            try { file.Save(State); }
            catch { State.Session = previous; throw; }
        }

        public void SetAdvancedCredential(DateTimeOffset now, AdvancedCredential credential)
        {
            EnsureEditable(now);
            var previous = State.AdvancedCredential;
            State.AdvancedCredential = credential;
            try { file.Save(State); }
            catch { State.AdvancedCredential = previous; throw; }
        }

        public void UpdateSettings(DateTimeOffset now, Settings settings)
        {
            EnsureEditable(now);
            Settings previous = Settings;
            settings.ScheduleChangedUtc = settings.ScheduleDays == previous.ScheduleDays &&
                settings.ScheduleMinute == previous.ScheduleMinute && settings.CountUp == previous.CountUp
                ? previous.ScheduleChangedUtc : now.UtcTicks;
            if (settings.Schedules != null && !object.ReferenceEquals(settings.Schedules, previous.Schedules))
            {
                var existing = previous.EffectiveSchedules();
                settings.Schedules = settings.Schedules.Select(plan =>
                {
                    var saved = plan.Copy();
                    var before = existing.FirstOrDefault(x => x.Days == plan.Days && x.Minute == plan.Minute &&
                        x.EndMinute == plan.EndMinute && x.CountUp == plan.CountUp && x.Enabled == plan.Enabled);
                    saved.ChangedUtc = before != null ? before.ChangedUtc : now.UtcTicks;
                    return saved;
                }).ToList();
            }
            SaveSettings(settings);
        }

        public void UpdateDisplayPreferences(bool dark, bool followSystemTheme, bool showFloatingTimer, bool reduceMotion)
        {
            var settings = Settings.Copy();
            settings.Dark = dark;
            settings.FollowSystemTheme = followSystemTheme;
            settings.ShowFloatingTimer = showFloatingTimer;
            settings.ReduceMotion = reduceMotion;
            SaveSettings(settings);
        }

        public void UpdateHudPosition(int x, int y)
        {
            if (Settings.HudX == x && Settings.HudY == y) return;
            var settings = Settings.Copy();
            settings.HudX = x;
            settings.HudY = y;
            SaveSettings(settings);
        }

        public void UpdateSystemProxy(bool enabled)
        {
            if (Settings.SystemProxy == enabled) return;
            var settings = Settings.Copy();
            settings.SystemProxy = enabled;
            SaveSettings(settings);
        }

        public void UpdateEdgeDefaultProfile(bool enabled)
        {
            if (Settings.EdgeDefaultProfile == enabled) return;
            var settings = Settings.Copy();
            settings.EdgeDefaultProfile = enabled;
            SaveSettings(settings);
        }

        public void UpdateStartupPreference(bool enabled)
        {
            if (Settings.Startup == enabled) return;
            var settings = Settings.Copy();
            settings.Startup = enabled;
            SaveSettings(settings);
        }
        public void UpdateAccent(int index)
        {
            if (index < 0 || index > 5) throw new ArgumentOutOfRangeException("index");
            if (Settings.Accent == index) return;
            var settings = Settings.Copy();
            settings.Accent = index;
            SaveSettings(settings);
        }

        public void UpdateHudBehavior(bool locked, bool pinned)
        {
            if (Settings.HudLocked == locked && Settings.HudPinned == pinned) return;
            var settings = Settings.Copy();
            settings.HudLocked = locked;
            settings.HudPinned = pinned;
            SaveSettings(settings);
        }

        public void UpdateHudAccent(int index)
        {
            if (index < 0 || index > 6) throw new ArgumentOutOfRangeException("index");
            if (Settings.HudAccent == index) return;
            var settings = Settings.Copy();
            settings.HudAccent = index;
            SaveSettings(settings);
        }

        public void UpdateHudOpacity(int percent)
        {
            if (percent < 65 || percent > 100) throw new ArgumentOutOfRangeException("percent");
            if (Settings.EffectiveHudOpacity == percent) return;
            var settings = Settings.Copy();
            settings.HudOpacity = percent;
            SaveSettings(settings);
        }

        public void UpdateAlertPreferences(bool playChimes, bool showNotifications)
        {
            if (Settings.PlayChimes == playChimes && Settings.ShowNotifications == showNotifications) return;
            var settings = Settings.Copy();
            settings.PlayChimes = playChimes;
            settings.ShowNotifications = showNotifications;
            SaveSettings(settings);
        }

        private void SaveSettings(Settings settings)
        {
            var previous = Settings;
            State.Settings = settings;
            try { file.Save(State); }
            catch { State.Settings = previous; throw; }
        }

        private void EnsureEditable(DateTimeOffset now)
        {
            DateTimeOffset due;
            if (Locked(now) || TryDue(now, out due))
                throw new InvalidOperationException("专注规则已锁定，结束后可修改。");
        }
    }

    internal static class Website
    {
        public static string Parse(string value)
        {
            string text = (value ?? "").Trim();
            bool subdomains = text.StartsWith("*.", StringComparison.Ordinal);
            if (subdomains) text = text.Substring(2);
            if (text.Length == 0) throw new FormatException("请输入网站域名。");
            if (text.IndexOf("://", StringComparison.Ordinal) < 0) text = "https://" + text;
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                uri.UserInfo.Length != 0 || !uri.IsDefaultPort ||
                (uri.HostNameType != UriHostNameType.Dns || !uri.IdnHost.Contains(".")) ||
                uri.IdnHost.IndexOf('*') >= 0)
                throw new FormatException("请输入有效域名或网页地址，例如 www.example.com。");
            return (subdomains ? "*." : "") + uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        }

        private static readonly string[][] Companions =
        {
            new[] { "bilibili.com", "*.bilibili.com", "*.hdslb.com", "*.bilivideo.com", "*.biliapi.net", "*.biliapi.com" }
        };

        public static bool MatchesSite(string host, string rule)
        {
            if (Matches(host, rule)) return true;
            string root = rule.StartsWith("*.", StringComparison.Ordinal) ? rule.Substring(2) : rule;
            if (root.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) root = root.Substring(4);
            foreach (string[] group in Companions)
            {
                if (!string.Equals(group[0], root, StringComparison.OrdinalIgnoreCase)) continue;
                for (int i = 1; i < group.Length; i++) if (Matches(host, group[i])) return true;
            }
            return false;
        }

        public static bool Matches(string host, string rule)
        {
            host = host.TrimEnd('.');
            if (!rule.StartsWith("*.", StringComparison.Ordinal))
            {
                if (string.Equals(host, rule, StringComparison.OrdinalIgnoreCase)) return true;
                string bareHost = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;
                string bareRule = rule.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? rule.Substring(4) : rule;
                return string.Equals(bareHost, bareRule, StringComparison.OrdinalIgnoreCase);
            }
            string root = rule.Substring(2);
            string baseHost = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;
            return string.Equals(host, root, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseHost, root, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
