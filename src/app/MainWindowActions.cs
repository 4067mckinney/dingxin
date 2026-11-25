
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private Form stopDialog;
        private Session stopDialogSession;
        private void LoadRules()
        {
            LoadTimingRule();
            plans = engine.Settings.EffectiveSchedules().Select(plan => plan.Copy()).ToList();
            ShowPlanSummary();
            ShowAllowlistSummaries();
        }

        private void LoadTimingRule()
        {
            loadingRules = true;
            try
            {
                mode.SelectedIndex = engine.Settings.CountUp ? 1 : 0;
                duration.Value = engine.Settings.Minutes;
                UpdateTimerFields();
            }
            finally { loadingRules = false; }
        }

        private bool Locked(DateTimeOffset now)
        {
            DateTimeOffset due;
            return trial != null || engine.Locked(now) || engine.TryDue(now, out due);
        }

        private void RequireEditable()
        {
            if (Locked(DateTimeOffset.Now)) throw new InvalidOperationException("规则已锁定，专注结束后可修改。");
        }

        private void SaveTimingRule(bool includeDuration)
        {
            if (loadingRules) return;
            var previous = engine.Settings;
            try
            {
                bool countUp = mode.SelectedIndex == 1;
                int minutes = includeDuration && !countUp ? duration.Value : previous.Minutes;
                if (previous.CountUp == countUp && previous.Minutes == minutes) return;
                RequireEditable();
                var rules = previous.Copy();
                rules.CountUp = countUp;
                rules.Minutes = minutes;
                engine.UpdateSettings(DateTimeOffset.Now, rules);
                ShowPlanSummary();
                ruleError = "";
                ShowSaved();
            }
            catch (Exception ex)
            {
                ruleError = ex.Message;
                savedPage = -1;
                LoadTimingRule();
            }
        }

        private void ShowSaved()
        {
            savedPage = page;
            savedUntil = DateTimeOffset.Now.AddMilliseconds(1500);
        }

        private void Tick()
        {
            if (updateSignal != null && updateSignal.WaitOne(0))
            {
                exitAllowed = true;
                Close();
                return;
            }
            var now = DateTimeOffset.Now;
            string error = "";
            DateTimeOffset? next = null;
            try
            {
                if (trial != null && trial.Elapsed.TotalSeconds >= TrialSeconds)
                {
                    trial = null;
                    notice = "测试结束，软件和网站限制已解除。";
                }
                bool existing = engine.Session != null;
                engine.Tick(now);
                if (existing && engine.Session == null)
                {
                    if (engine.Settings.PlayChimes) FocusSounds.PlayEnd();
                    notice = "本轮专注已完成。";
                    Balloon("专注完成", "本轮软件和网站限制已解除。");
                }

                next = engine.NextSchedule(now);
                bool autoStart = trial == null && engine.Session == null;
                var due = autoStart ? engine.ActiveWindow(now) : null;
                int countdown = autoStart ? ScheduledCountdown(now, next, due) : 0;
                ShowScheduleCountdown(countdown);
                if (due != null && now >= nextRetry && countdown == 0)
                {
                    try
                    {
                        EnsureProxy();
                        engine.StartActiveSchedule(DateTimeOffset.Now);
                        Restrict(now);
                        if (engine.Session != null)
                        {
                            if (engine.Settings.PlayChimes) FocusSounds.PlayStart();
                            SelectPage(0);
                            Reveal();
                            Balloon("已自动开始", "软件和网站白名单已启用。");
                        }
                    }
                    catch
                    {
                        nextRetry = now.AddSeconds(20);
                        throw;
                    }
                }

                Restrict(now);
                next = engine.NextSchedule(now);
                if (trial == null && next.HasValue && next != warned && engine.SkippableSchedule(now) == next)
                {
                    warned = next;
                    Balloon("两分钟内将自动开始", "专注会收起白名单外的窗口。打开主窗口可查看限制或跳过本次。");
                }
                var blocked = guard.CheckForeground();
                bool recording = enforced && trial == null && engine.Session != null;
                history.Observe(now, recording ? engine.Session : null, recording ? guard.ForegroundPath : null,
                    recording && blocked != null ? blocked.Path : null);
                if (blocked != null) ShowBlockedApp(blocked);
                if (page == 2 && (displayedRecordStops != engine.State.Stops.Count || displayedRecordDay != now.Date)) RefreshRecords();
                if (page == 3 && (statisticsRefreshPending || (now >= nextStatisticsRefresh &&
                    (recording || displayedHistoryRevision != history.Revision || displayedStops != engine.State.Stops.Count))))
                    RefreshStatistics();
                if (showSignal.WaitOne(0)) Reveal();
                if (uninstallSignal.WaitOne(0)) Uninstall();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                next = engine.NextSchedule(now);
            }
            if (!IsDisposed) Render(now, error, next);
        }

        private void Render(DateTimeOffset now, string error, DateTimeOffset? next)
        {
            Session round = engine.Session;
            if (stopDialog != null && stopDialogSession != round) stopDialog.DialogResult = DialogResult.Cancel;
            bool active = round != null, paused = FocusEngine.Paused(round, now);
            bool testing = trial != null;
            skippableSchedule = testing ? null : engine.SkippableSchedule(now);
            bool locked = active || testing || skippableSchedule.HasValue;
            bool showSaved = page == savedPage && now < savedUntil;
            state.Text = showSaved ? "已保存" : testing ? "测试中" : active ? (paused ? "暂停中" : "专注中")
                : locked ? "即将自动开始" : "";
            state.ForeColor = !showSaved && paused ? palette.Paused : palette.Signal;
            bool countUp = active ? round.CountUp : engine.Settings.CountUp;
            bool openEnded = active ? FocusEngine.OpenEnded(round) : countUp;
            int minutes = active ? round.TotalMinutes : engine.Settings.Minutes;
            var target = TimeSpan.FromMinutes(minutes);
            var remaining = FocusEngine.Remaining(round, now);
            var elapsed = FocusEngine.Elapsed(round, now, engine.Settings.PauseMinutes);
            bool idle = !testing && !active;
            var displayed = testing ? TimeSpan.FromSeconds(TrialSeconds) - trial.Elapsed
                : paused ? TimeSpan.FromTicks(round.PauseUntilUtc - now.UtcTicks)
                : countUp ? elapsed : active ? remaining : target;
            long seconds = idle ? -2 : Math.Max(0, (long)(testing || paused || !countUp ? Math.Ceiling(displayed.TotalSeconds) : Math.Floor(displayed.TotalSeconds)));
            double elapsedSeconds = Math.Max(0, Math.Floor(elapsed.TotalSeconds));
            double progress = testing ? 1 - seconds / (double)TrialSeconds
                : openEnded ? (elapsedSeconds % 60) / 60
                : active && round.Scheduled ? Math.Max(0, 1 - remaining.TotalSeconds / target.TotalSeconds)
                : Math.Min(1, elapsedSeconds / target.TotalSeconds);
            bool phaseChanged = renderedSession != round || renderedPaused != paused || renderedTrial != testing;
            string shownTime = idle ? (countUp ? "正计时" : minutes + " 分钟")
                : (seconds / 3600).ToString("00") + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            if (seconds != renderedSeconds || shownTime != clockText)
            {
                renderedSeconds = seconds;
                clockText = shownTime;
                dial.AccessibleName = idle ? "本轮计时 " + clockText : "专注计时 " + clockText;
                dial.Invalidate();
            }
            if (progress != targetRatio)
            {
                targetRatio = progress;
                Motion.Stop(dial);
                if (page == 0 && phaseChanged && Math.Abs(progress - ratio) > 0.04)
                {
                    double from = ratio, to = progress;
                    Motion.Run(dial, 200, delegate(float t) { ratio = from + (to - from) * t; dial.Invalidate(); });
                }
                else
                {
                    ratio = progress;
                }
                dial.Invalidate();
            }
            renderedSession = round;
            renderedPaused = paused;
            renderedTrial = testing;
            if (phaseChanged) dial.Invalidate();
            goal.Text = idle ? (allowedAppCount == 0 ? "软件白名单为空，开始前可查看限制" : "白名单将在开始后生效")
                : testing ? "测试结束后自动解除限制"
                : paused ? "暂停结束后自动恢复专注"
                : active && round.Scheduled ? "计划结束 " + new DateTimeOffset(round.FinishUtc, TimeSpan.Zero).ToLocalTime().ToString("HH:mm")
                : openEnded ? (active ? "按「结束」完成本轮专注" : "无固定时长") : "本轮目标 " + minutes + " 分钟";
            modeLabel.Text = testing ? "白名单测试" : paused ? "暂停剩余"
                    : ((round != null && round.Scheduled) ? "自动开始 · " : "") + TimerModeName(countUp) + "专注";
            var displayedSchedule = skippableSchedule ?? next;
            scheduleLabel.Text = displayedSchedule.HasValue ? DateLabel(displayedSchedule.Value, now) : "未设置";
            planNext.Text = next.HasValue ? "下次开始  " + DateLabel(next.Value, now) : "";
            planNext.Visible = next.HasValue;
            var pauseRemaining = engine.PauseRemaining(now);
            pauseLabel.Text = DisplayDuration(pauseRemaining.Ticks, true);
            int available = engine.StopsRemaining(now);
            balance.Text = available + " 次";
            editor.Enabled = managePlans.Enabled = manageApps.Enabled = manageWebsites.Enabled = trialRules.Enabled = !locked;
            appEmptyAction.Enabled = allowedAppCount > 0 || !locked;
            websiteInput.Enabled = websiteAdd.Enabled = preflightStart.Enabled = !locked;
            if (page == 1 && ruleSections.SelectedIndex == 3)
                RenderPreflight(now, locked, displayedSchedule, pauseRemaining, available);
            behavior.Enabled = true;
            advanced.Enabled = !locked;
            settingsHint.Text = locked ? "专注或计划即将开始时，高级设置暂时锁定。" : "";
            UpdateHintRow(settingsHint);
            idleActions.Visible = !active;
            activeActions.Visible = active;
            start.Enabled = testing || !locked;
            start.Text = testing ? "结束测试" : "开始专注";
            previewRules.Enabled = !testing;
            pause.Enabled = active && (paused || pauseRemaining > TimeSpan.Zero);
            pause.Text = paused ? "恢复专注" : "暂停";
            stop.Enabled = active && (openEnded || (remaining > TimeSpan.Zero && available > 0));
            stop.Text = openEnded ? "结束" : "提前停止";
            openEdge.Text = enforced ? "打开专注 Edge" : "打开 Edge";
            skipSchedule.Visible = skippableSchedule.HasValue;
            var scheduleAction = ((TableLayoutPanel)skipSchedule.Parent).RowStyles[2];
            var scheduleActionHeight = skippableSchedule.HasValue ? (float)Math.Round(30 * DeviceDpi / 96.0) : 0;
            if (scheduleAction.Height != scheduleActionHeight) scheduleAction.Height = scheduleActionHeight;
            emergency.Visible = active;
            emergencyMenu.Visible = active;
            quit.Enabled = !locked;
            quit.Text = testing ? "退出应用（测试中）" : active ? "退出应用（专注中）"
                : locked ? "退出应用（即将自动开始）" : "退出应用";
            editHint.Text = testing ? "测试期间规则暂时锁定。"
                : locked ? "专注中或计划即将开始，规则暂时锁定。" : ruleError;
            UpdateHintRow(editHint);
            info.Text = error.Length != 0 ? error
                : active && !openEnded && available == 0 ? "本月提前停止次数已用完。需要时可紧急解除。"
                : testing ? "试着切到白名单外的窗口，20 秒后自动解除限制。"
                : paused ? "暂停期间可正常使用其他软件和网站。"
                : enforced || page != 0 ? "" : notice;
            UpdateHintRow(info, 2);
            string trayText = active || testing ? Program.Name + " · " + (testing ? "测试" : paused ? "暂停" : "专注") + " " + clockText : Program.Name;
            if (tray.Text != trayText) tray.Text = trayText;
            RenderFloatingTimer(round, paused, openEnded, progress, available);
        }

        private void UpdateHintRow(Label hint, int index = 0)
        {
            var row = ((TableLayoutPanel)hint.Parent).RowStyles[index];
            int height = hint.Text.Length == 0 ? 0 : (int)Math.Round(30 * DeviceDpi / 96.0);
            if (row.Height != height) row.Height = height;
        }

        private static string DateLabel(DateTimeOffset next, DateTimeOffset now)
        {
            string day = next.Date == now.Date ? "今天" : next.Date == now.Date.AddDays(1) ? "明天"
                : next.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN"));
            return day + " " + next.ToString("HH:mm");
        }

        private void StartFocus()
        {
            if (engine.Session != null || trial != null) return;
            if (ShowRulePreview(true)) BeginManualFocus();
        }

        private void BeginManualFocus()
        {
            if (engine.Session != null || trial != null) return;
            if (engine.SkippableSchedule(DateTimeOffset.Now).HasValue)
                throw new InvalidOperationException("自动计划即将开始。可跳过本次后开始手动专注。");
            EnsureProxy();
            var now = DateTimeOffset.Now;
            engine.Start(now, null);
            Restrict(now);
            if (engine.Settings.PlayChimes) FocusSounds.PlayStart();
            notice = "";
        }

        private void StartTrial()
        {
            if (trial != null)
            {
                trial = null;
                Restrict(DateTimeOffset.Now);
                notice = "测试已结束，软件和网站限制已解除。";
                return;
            }
            RequireEditable();
            EnsureProxy();
            RequireEditable();
            trial = Stopwatch.StartNew();
            Restrict(DateTimeOffset.Now);
        }

        private void TryRules()
        {
            RequireEditable();
            if (MessageBox.Show(this, "将在 20 秒内应用当前白名单。到时自动解除限制。", "测试白名单",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            StartTrial();
            if (trial != null)
            {
                SelectPage(0);
                ShowTrialNotice();
            }
        }

        private void ShowTrialNotice()
        {
            var form = new Form
            {
                Text = "测试中", FormBorderStyle = FormBorderStyle.FixedToolWindow,
                StartPosition = FormStartPosition.CenterScreen, TopMost = true, ShowInTaskbar = false,
                ClientSize = new Size(360, 130), BackColor = bg, ForeColor = ink, Font = Font
            };
            var label = Label("测试中：20 秒后自动解除限制", 14, "title");
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            form.Controls.Add(label);
            int left = TrialSeconds;
            var timer = new Timer { Interval = 1000 };
            timer.Tick += delegate
            {
                left--;
                label.Text = "测试中：" + Math.Max(0, left) + " 秒后自动解除限制";
                if (left > 0) return;
                timer.Stop();
                timer.Dispose();
                form.Close();
            };
            form.FormClosed += delegate { timer.Stop(); timer.Dispose(); };
            timer.Start();
            form.Show(this);
        }
        private void SkipNextSchedule()
        {
            var target = skippableSchedule;
            if (!target.HasValue || !engine.SkipSchedule(DateTimeOffset.Now, target.Value)) return;
            countdownSlot = null;
            nextRetry = DateTimeOffset.MinValue;
            notice = "已跳过本次，之后的自动计划仍会运行。";
        }

        private void EmergencyRelease()
        {
            if (engine.Session == null || !ConfirmEmergencyRelease() || engine.Session == null) return;
            string saveError = null;
            var emergencySession = engine.Session;
            try
            {
                engine.EmergencyRelease(DateTimeOffset.Now);
                if (emergencySession != null && emergencySession.Scheduled && emergencySession.ScheduleStartUtc != 0)
                    engine.DisableScheduleStart(new DateTimeOffset(emergencySession.ScheduleStartUtc, TimeSpan.Zero).ToLocalTime());
            }
            catch (Exception ex) { saveError = ex.Message; }
            Restrict(DateTimeOffset.Now);
            if (engine.Settings.PlayChimes) FocusSounds.PlayEnd();
            notice = "本轮已结束，限制已解除。";
            if (saveError != null) Warn("本轮限制已解除，但状态保存失败。重启前请检查数据文件。\n" + saveError);
        }

        private bool ConfirmEmergencyRelease()
        {
            TableLayoutPanel layout;
            using (var dialog = SettingsDialog("紧急解除限制", 505, 207, out layout, 48, -1, 44))
            {
                dialog.TopMost = floatingTimer != null && floatingTimer.Visible;
                layout.Controls.Add(Label("结束本轮专注？", 16, "title"), 0, 0);
                layout.Controls.Add(Label("软件和网站限制将立即解除。此操作无需提前停止额度。", 10, "muted"), 0, 1);
                var actions = Flow();
                var release = Button("解除限制", 118, delegate { dialog.DialogResult = DialogResult.OK; });
                release.Tag = "danger";
                actions.Controls.Add(release);
                var cancel = CancelButtonFor(dialog, "继续专注");
                actions.Controls.Add(cancel);
                layout.Controls.Add(actions, 0, 2);
                dialog.AcceptButton = cancel;
                dialog.ActiveControl = cancel;
                PaintTree(dialog, bg);
                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private void PauseOrResume()
        {
            var now = DateTimeOffset.Now;
            if (FocusEngine.Paused(engine.Session, now)) engine.Resume(now);
            else engine.Pause(now);
            Restrict(now);
        }

        private void StopFocus()
        {
            StopFocusWithOwner(this);
        }

        private void StopFocusWithOwner(Form owner)
        {
            var now = DateTimeOffset.Now;
            var session = engine.Session;
            if (session == null || stopDialog != null) return;
            if (FocusEngine.OpenEnded(session))
            {
                engine.FinishCountUp();
                Restrict(now);
                if (engine.Settings.PlayChimes) FocusSounds.PlayEnd();
                notice = "本轮正计时已结束。";
                return;
            }
            if (engine.MinutesRemaining(now) == 0) return;
            int available = engine.StopsRemaining(now);
            if (available == 0) throw new InvalidOperationException("本月提前停止次数已用完。");
            TableLayoutPanel box;
            using (var dialog = SettingsDialog("提前停止专注", 448, 198, out box, 42, -1, 45))
            {
                if (owner == floatingTimer)
                {
                    dialog.TopMost = true;
                    dialog.StartPosition = FormStartPosition.Manual;
                    var area = Screen.FromControl(owner).WorkingArea;
                    var point = new Point(owner.Left + (owner.Width - dialog.Width) / 2, owner.Bottom + 8);
                    if (point.Y + dialog.Height > area.Bottom) point.Y = owner.Top - dialog.Height - 8;
                    dialog.Location = new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - dialog.Width)),
                        Math.Max(area.Top, Math.Min(point.Y, area.Bottom - dialog.Height)));
                }
                box.Controls.Add(Label("将扣除 1 次，本月还剩 " + available + " 次。请输入停止理由。", 10), 0, 0);
                var reason = new TextBox { Multiline = true, MaxLength = 300, Dock = DockStyle.Fill, AccessibleName = "停止理由" };
                box.Controls.Add(reason, 0, 1);
                var controls = Flow();
                controls.Padding = new Padding(0, 8, 0, 0);
                var ok = Button("确认停止", 105, delegate { if (reason.Text.Trim().Length > 0) dialog.DialogResult = DialogResult.OK; }, true);
                ok.Enabled = false;
                reason.TextChanged += delegate { ok.Enabled = reason.Text.Trim().Length > 0; };
                var cancel = Button("继续专注", 105, delegate { dialog.DialogResult = DialogResult.Cancel; });
                dialog.CancelButton = cancel;
                controls.Controls.Add(ok);
                controls.Controls.Add(cancel);
                box.Controls.Add(controls, 0, 2);
                PaintTree(dialog, bg);
                DialogResult answer;
                stopDialog = dialog;
                stopDialogSession = session;
                try { answer = dialog.ShowDialog(owner); }
                finally { stopDialog = null; stopDialogSession = null; }
                if (answer != DialogResult.OK) return;
                if (engine.Session != session)
                    throw new InvalidOperationException("本轮专注已变更，请重新确认是否停止。");
                now = DateTimeOffset.Now;
                var stopping = engine.Session;
                engine.Stop(now, reason.Text);
                bool planDisabled = engine.DisableScheduleCovering(now);
                ReloadPlans();
                Restrict(now);
                if (engine.Settings.PlayChimes) FocusSounds.PlayEnd();
                notice = planDisabled ? "已提前停止，理由已保存；该计划已设为停用。" : "已提前停止，理由已保存。";
                if (page == 2) RefreshRecords();
            }
        }

        private void EnsureProxy()
        {
            if (proxy != null) return;
            Directory.CreateDirectory(data);
            string path = Path.Combine(data, "edge-port");
            int preferred;
            if (!File.Exists(path) || !int.TryParse(File.ReadAllText(path), out preferred) ||
                preferred < 1024 || preferred > 65535) preferred = 0;
            try { proxy = new WebsiteProxy(preferred); }
            catch (System.Net.Sockets.SocketException)
            {
                if (preferred == 0) throw;
                proxy = new WebsiteProxy();
            }
            try
            {
                File.WriteAllText(path, proxy.Port.ToString(CultureInfo.InvariantCulture));
                guard.ConfigureFocusEdge(ProcessGuard.FindEdge(), Path.Combine(data, "EdgeProfile-" + proxy.Port), proxy.Port);
            }
            catch { proxy.Dispose(); proxy = null; throw; }
        }

        private void Restrict(DateTimeOffset now)
        {
            bool testing = trial != null && trial.Elapsed.TotalSeconds < TrialSeconds;
            bool active = testing || (engine.Session != null && !FocusEngine.Paused(engine.Session, now) &&
                (FocusEngine.OpenEnded(engine.Session) || FocusEngine.Remaining(engine.Session, now) > TimeSpan.Zero));
            if (active == enforced && testing == enforcingTest) return;
            if (active) EnsureProxy();
            if (proxy != null) proxy.SetRestriction(active ? engine.Settings.Websites.ToArray() : null);
            var rules = engine.Settings.AppRules ?? engine.Settings.Apps.Where(AppCatalog.IsSelectable)
                .Select(path => new AppCatalog.Entry(path, null).Rule).ToList();
            guard.SetRestriction(active ? rules : null, engine.Settings.Apps);
            if (!active && blockNotice != null) blockNotice.Close();
            enforced = active;
            enforcingTest = testing;
        }

        private void OpenEdge()
        {
            string browser = ProcessGuard.FindEdge();
            if (browser == null) throw new InvalidOperationException("未找到 Microsoft Edge。");
            var now = DateTimeOffset.Now;
            Restrict(now);
            if (!enforced)
            {
                Process.Start(new ProcessStartInfo(browser) { UseShellExecute = false });
                return;
            }
            string domain = engine.Settings.Websites.FirstOrDefault();
            string url = domain == null ? "about:blank"
                : proxy != null ? "http://127.0.0.1:" + proxy.Port + "/start"
                : "https://" + (domain.StartsWith("*.") ? domain.Substring(2) : domain);
            guard.ConfigureFocusEdge(browser, Path.Combine(data, "EdgeProfile-" + proxy.Port), proxy.Port);
            guard.OpenEdge(url);
        }

        internal void Warn(string message)
        {
            MessageBox.Show(this, message, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Act(Action work)
        {
            Act(work, this);
        }

        private void Act(Action work, Form owner)
        {
#if FOCUS_TEST
            work();
            Tick();
#else
            try { work(); Tick(); }
            catch (Exception ex) { MessageBox.Show(owner, ex.Message, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Information); }
#endif
        }

        private void Reveal()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        private void Balloon(string title, string message)
        {
            if (engine.Settings.ShowNotifications) tray.ShowBalloonTip(4300, title, message, ToolTipIcon.Info);
        }

        private void Uninstall()
        {
            Reveal();
            if (Locked(DateTimeOffset.Now))
            {
                Program.ShowUninstallLocked(this, darkTheme);
                return;
            }
            bool deleteData;
            if (!Program.ConfirmUninstallFlow(this, darkTheme, out deleteData) || Locked(DateTimeOffset.Now)) return;
            Program.UninstallDeleteData = deleteData;
            Program.UninstallOnExit = true;
            exitAllowed = true;
            Close();
        }

        private void ExitApp()
        {
            if (Locked(DateTimeOffset.Now)) return;
            if (engine.HasEnabledSchedule)
            {
                if (MessageBox.Show(this, "退出后，自动开始计划会暂停，重新打开应用后恢复。确定退出吗？",
                    Program.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes || Locked(DateTimeOffset.Now)) return;
            }
            exitAllowed = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exitAllowed && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
                Motion.Stop(this);
                Motion.Stop(dial);
                foreach (var section in pages) Motion.Stop(section);
                try { history.Close(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                timer.Dispose();
                appImages.Dispose();
                dialFont.Dispose();
                compactDialFont.Dispose();
                idleDialFont.Dispose();
                tray.Visible = false;
                tray.ContextMenuStrip.Dispose();
                tray.Dispose();
                if (blockNotice != null) blockNotice.Close();
                if (countdownNotice != null) countdownNotice.Close();
                if (floatingTimer != null) floatingTimer.Dispose();
                if (proxy != null) proxy.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}