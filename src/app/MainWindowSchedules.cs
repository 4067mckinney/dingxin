using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private static readonly string[] Weeknames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

        private void ShowPlanSummary()
        {
            int enabled = plans.Count(plan => plan.Enabled);
            planSummary.Text = plans.Count == 0 ? "暂无自动开始计划" : enabled + " 条已开启，共 " + plans.Count + " 条";
            SetRuleList(rulePlans, plans.OrderBy(plan => plan.Minute).Select(plan =>
            {
                int end = plan.EndOnStartDay(engine.Settings.Minutes);
                return new[] { PlanTime(plan.Minute), (end >= 1440 ? "次日 " : "") + PlanTime(end % 1440),
                    TimerModeName(plan.CountUp ?? engine.Settings.CountUp), FormatPlanDays(plan.Days), plan.Enabled ? "已启用" : "已停用" };
            }));
        }

        private static string FormatPlanDays(int mask)
        {
            if (mask == 127) return "每天";
            if (mask == 62) return "周一至周五";
            if (mask == 65) return "周末";
            return string.Join("、", Weeknames.Where((name, index) => (mask & (1 << ((index + 1) % 7))) != 0));
        }

        private void EditSchedules()
        {
            RequireEditable();
            var draft = plans.Select(plan => plan.Copy()).ToList();
            TableLayoutPanel panel;
            using (var dialog = SettingsDialog("自动开始计划", 700, 440, out panel, 32, 30, -1, 50, 44))
            {
                panel.Controls.Add(Label("自动开始计划", 15, "title"), 0, 0);
                panel.Controls.Add(Label("勾选左侧方框即可开启对应计划。", 10, "muted"), 0, 1);
                var list = new BufferedAppList
                {
                    AccessibleName = "自动开始计划", Dock = DockStyle.Fill, CheckBoxes = true, FullRowSelect = true,
                    View = View.Details, HeaderStyle = ColumnHeaderStyle.Nonclickable, MultiSelect = false,
                    HideSelection = false, BorderStyle = BorderStyle.None
                };
                list.Columns.Add("开始时间", 140);
                list.Columns.Add("结束时间", 120);
                list.Columns.Add("计时方式", 200);
                list.Columns.Add("重复日期", 170);
                list.HeaderStyle = ColumnHeaderStyle.None;
                var listHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 9) };
                listHost.Controls.Add(list);
                listHost.Controls.Add(new TableHeader(list));
                panel.Controls.Add(listHost, 0, 2);
                DialogList.Attach(list, palette);
                bool filling = false;
                Action<FocusSchedule> refresh = delegate(FocusSchedule selected)
                {
                    filling = true;
                    list.BeginUpdate();
                    try
                    {
                        list.Items.Clear();
                        foreach (var plan in draft.OrderBy(item => item.Minute))
                        {
                            int end = plan.EndOnStartDay(engine.Settings.Minutes);
                            var row = new ListViewItem(PlanTime(plan.Minute)) { Tag = plan };
                            row.SubItems.Add((end >= 1440 ? "次日 " : "") + PlanTime(end));
                            string timing = TimerModeName(plan.CountUp ?? engine.Settings.CountUp);
                            row.SubItems.Add(plan.CountUp.HasValue ? timing : timing + "（跟随设置）");
                            row.SubItems.Add(FormatPlanDays(plan.Days));
                            row.Checked = plan.Enabled;
                            list.Items.Add(row);
                            row.Selected = plan == selected;
                        }
                    }
                    finally { list.EndUpdate(); filling = false; }
                };
                list.ItemChecked += delegate(object sender, ItemCheckedEventArgs e)
                {
                    if (!filling) ((FocusSchedule)e.Item.Tag).Enabled = e.Item.Checked;
                };
                var actions = Flow();
                actions.Controls.Add(Button("新增计划", 110, delegate
                {
                    if (draft.Count >= 50) { MessageBox.Show(dialog, "最多可添加 50 条计划。"); return; }
                    var entry = new FocusSchedule
                    {
                        Days = 127, Minute = 870, Enabled = true,
                        EndMinute = (870 + Math.Min(engine.Settings.Minutes, 1439)) % 1440,
                        CountUp = engine.Settings.CountUp
                    };
                    if (!EditSchedule(dialog, entry)) return;
                    draft.Add(entry);
                    refresh(entry);
                }));
                var edit = Button("修改选中", 110, delegate
                {
                    if (list.SelectedItems.Count == 0) return;
                    var entry = (FocusSchedule)list.SelectedItems[0].Tag;
                    if (EditSchedule(dialog, entry)) refresh(entry);
                });
                actions.Controls.Add(edit);
                list.DoubleClick += delegate { edit.PerformClick(); };
                var remove = Button("移除选中", 110, delegate
                {
                    if (list.SelectedItems.Count == 0) return;
                    draft.Remove((FocusSchedule)list.SelectedItems[0].Tag);
                    refresh(null);
                });
                actions.Controls.Add(remove);
                list.SelectedIndexChanged += delegate { edit.Enabled = remove.Enabled = list.SelectedItems.Count != 0; };
                edit.Enabled = remove.Enabled = false;
                actions.Padding = new Padding(0, 7, 0, 0);
                panel.Controls.Add(actions, 0, 3);
                var footer = Flow();
                var savePlans = Button("保存计划", 118, delegate
                {
                    try
                    {
                        RequireEditable();
                        if (draft.GroupBy(plan => new { plan.Minute, plan.Days }).Any(group => group.Count() > 1))
                            throw new InvalidOperationException("有两条时间和日期都相同的计划，请保留一条。");
                        var updated = engine.Settings.Copy();
                        updated.Schedules = draft.OrderBy(plan => plan.Minute).ToList();
                        updated.ScheduleDays = 0;
                        engine.UpdateSettings(DateTimeOffset.Now, updated);
                        plans = engine.Settings.EffectiveSchedules().Select(plan => plan.Copy()).ToList();
                        ShowPlanSummary();
                        ShowSaved();
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "自动开始计划"); }
                }, true);
                footer.Controls.Add(savePlans);
                footer.Controls.Add(CancelButtonFor(dialog));
                panel.Controls.Add(footer, 0, 4);
                dialog.AcceptButton = savePlans;
                PaintTree(dialog, bg);
                refresh(null);
                dialog.ShowDialog(this);
            }
        }

        private bool EditSchedule(Form owner, FocusSchedule plan)
        {
            TableLayoutPanel table;
            using (var dialog = SettingsDialog("编辑计划", 550, 300, out table, 38, 42, 42, 48, -1, 37))
            {
                table.Controls.Add(Label("设置开始、结束时间和重复日期", 13, "title"), 0, 0);
                var startTime = PlanTimePicker(plan.Minute);
                var endTime = PlanTimePicker(plan.EndOnStartDay(engine.Settings.Minutes));
                table.Controls.Add(Fields("开始时间", startTime, "结束时间", endTime), 0, 1);
                var timing = Combo(TimerModeName(false), TimerModeName(true));
                timing.SelectedIndex = (plan.CountUp ?? engine.Settings.CountUp) ? 1 : 0;
                table.Controls.Add(Fields("计时方式", timing), 0, 2);
                var row = Flow();
                var checks = new List<CheckBox>();
                for (int i = 0; i < Weeknames.Length; i++)
                {
                    var check = Check(Weeknames[i]);
                    check.Margin = new Padding(0, 5, 10, 5);
                    check.Checked = (plan.Days & (1 << ((i + 1) % 7))) != 0;
                    checks.Add(check);
                    row.Controls.Add(check);
                }
                table.Controls.Add(row, 0, 3);
                var warning = Label("结束时间早于开始时间时，计划持续到次日。", 9, "muted");
                table.Controls.Add(warning, 0, 4);
                var buttons = Flow();
                var done = Button("确定", 105, delegate
                {
                    int days = 0;
                    for (int i = 0; i < checks.Count; i++)
                        if (checks[i].Checked) days |= 1 << ((i + 1) % 7);
                    if (days == 0) { warning.Text = "请至少选择一天。"; return; }
                    int start = (int)startTime.Value.TimeOfDay.TotalMinutes;
                    int end = (int)endTime.Value.TimeOfDay.TotalMinutes;
                    if (start == end) { warning.Text = "开始和结束时间必须不同。"; return; }
                    plan.Minute = start;
                    plan.EndMinute = end;
                    plan.Days = days;
                    plan.CountUp = timing.SelectedIndex == 1;
                    dialog.DialogResult = DialogResult.OK;
                }, true);
                buttons.Controls.Add(done);
                buttons.Controls.Add(CancelButtonFor(dialog));
                table.Controls.Add(buttons, 0, 5);
                dialog.AcceptButton = done;
                PaintTree(dialog, bg);
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

        private static string TimerModeName(bool countUp) { return countUp ? "正计时" : "倒计时"; }

        private static string PlanTime(int minutes)
        {
            return DateTime.Today.AddMinutes(minutes % 1440).ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static DateTimePicker PlanTimePicker(int minutes)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 112,
                Value = DateTime.Today.AddMinutes(minutes % 1440)
            };
        }

        private int ScheduledCountdown(DateTimeOffset now, DateTimeOffset? next, ScheduleWindow due)
        {
            if (due == null)
            {
                if (!next.HasValue || next.Value - now > TimeSpan.FromSeconds(ScheduleCountdownSeconds))
                {
                    countdownSlot = null;
                    return 0;
                }
                countdownSlot = countdownDeadline = next.Value;
            }
            else if (countdownSlot != due.Start)
            {
                countdownSlot = due.Start;
                var deadline = now.AddSeconds(ScheduleCountdownSeconds);
                countdownDeadline = deadline < due.End ? deadline : due.End;
            }
            return Math.Max(0, (int)Math.Ceiling((countdownDeadline - now).TotalSeconds));
        }

        private void ShowScheduleCountdown(int seconds)
        {
            if (seconds <= 0)
            {
                if (countdownNotice != null) countdownNotice.Close();
                return;
            }
            string message = "专注将在 " + seconds + " 秒后自动开始";
            if (countdownNotice != null)
            {
                countdownLabel.Text = message;
                return;
            }
            var dialog = new NoticeWindow
            {
                Text = "自动开始倒计时", TopMost = true, ShowInTaskbar = false, ControlBox = false,
                StartPosition = FormStartPosition.Manual, FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ClientSize = new Size(300, 102), Font = Font
            };
            var content = Area(2);
            content.Padding = new Padding(12, 5, 12, 10);
            Rows(content, -1, 34);
            countdownLabel = Label(message, 12, "accent");
            countdownLabel.TextAlign = ContentAlignment.MiddleCenter;
            content.Controls.Add(countdownLabel, 0, 0);
            var skip = Button("跳过本次", 105, delegate { Act(SkipNextSchedule); });
            skip.Anchor = AnchorStyles.None;
            content.Controls.Add(skip, 0, 1);
            dialog.Controls.Add(content);
            dialog.FormClosed += delegate { countdownNotice = null; countdownLabel = null; };
            dialog.HandleCreated += delegate { ApplyCaption(dialog); };
            PositionNotice(dialog);
            PaintTree(dialog, bg);
            countdownNotice = dialog;
#if FOCUS_TEST
            if (Opacity == 0) dialog.Opacity = 0;
#endif
            dialog.Show();
        }

        private void ShowBlockedApp(ProcessGuard.BlockedWindow blocked)
        {
            string name = blocked.Edge ? "普通 Edge 窗口" : AppCatalog.DisplayName(blocked.Path);
            string message = blocked.Edge ? "请在主窗口选择「打开专注 Edge」。普通窗口会保留在后台。"
                : blocked.Minimized ? "窗口已收回后台，软件仍在运行。" : "当前窗口不在白名单内，Windows 暂未将它最小化。";
            if (blockNotice != null)
            {
                var labels = (Label[])blockNotice.Tag;
                labels[0].Text = name;
                labels[1].Text = message;
                return;
            }

            var dialog = new NoticeWindow
            {
                Text = "专注限制", TopMost = true, ShowInTaskbar = false, MaximizeBox = false, MinimizeBox = false,
                StartPosition = FormStartPosition.Manual, FormBorderStyle = FormBorderStyle.FixedToolWindow,
                ClientSize = new Size(390, 172), Font = Font
            };
            var layout = Area(4);
            layout.Padding = new Padding(16, 10, 16, 10);
            Rows(layout, 27, 31, -1, 36);
            layout.Controls.Add(Label("专注期间无法使用此窗口", 11, "accent"), 0, 0);
            var app = Label(name, 13, "title");
            var details = Label(message, 9, "muted");
            layout.Controls.Add(app, 0, 1);
            layout.Controls.Add(details, 0, 2);
            var actions = Flow();
            var dismiss = Button("知道了", 108, delegate { dialog.Close(); }, true);
            dismiss.DialogResult = DialogResult.Cancel;
            dialog.CancelButton = dismiss;
            actions.Controls.Add(dismiss);
            layout.Controls.Add(actions, 0, 3);
            dialog.Controls.Add(layout);
            dialog.Tag = new[] { app, details };
            var autoClose = new Timer { Interval = 4000 };
            autoClose.Tick += delegate { dialog.Close(); };
            dialog.FormClosed += delegate { autoClose.Dispose(); blockNotice = null; };
            dialog.HandleCreated += delegate { ApplyCaption(dialog); };
            PositionNotice(dialog);
            PaintTree(dialog, bg);
            blockNotice = dialog;
#if FOCUS_TEST
            if (Opacity == 0) dialog.Opacity = 0;
#endif
            dialog.Show();
            autoClose.Start();
        }

        private static void PositionNotice(Form dialog)
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            dialog.Location = new Point(area.Right - dialog.Width - 22, area.Bottom - dialog.Height - 22);
        }

        private sealed class NoticeWindow : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
        }
    }
}
