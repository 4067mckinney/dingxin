
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow : Form
    {
        private readonly FocusEngine engine;
        private readonly FocusHistory history;
        private readonly EventWaitHandle showSignal, uninstallSignal, updateSignal;
        private readonly string data;
        private readonly ProcessGuard guard;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 300 };
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem quit, emergencyMenu;
        private WebsiteProxy proxy;
        private bool enforced, enforcingTest, exitAllowed, loadingRules;
        private Stopwatch trial;
        private const int TrialSeconds = 20;
        private const int ScheduleCountdownSeconds = 5;
        private DateTimeOffset? warned;
        private DateTimeOffset nextRetry;
        private DateTimeOffset? countdownSlot;
        private DateTimeOffset countdownDeadline;
        private Form countdownNotice;
        private Label countdownLabel;
        private Form blockNotice;
        private string notice = "";
        private string ruleError = "";
        private DateTimeOffset savedUntil;
        private int savedPage = -1;
        private DateTimeOffset? skippableSchedule;
        private int page;
        private WindowPalette palette;
        private Color bg { get { return palette.Background; } }
        private Color card { get { return palette.Card; } }
        private Color ink { get { return palette.Ink; } }
        private Color muted { get { return palette.Muted; } }
        private Color accent { get { return palette.Accent; } }
        private Color subtle { get { return palette.Subtle; } }
        private Color outline { get { return palette.Outline; } }
        private readonly Panel main = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private TableLayoutPanel workspace, workspaceHeading;
        private readonly Control[] pages = new Control[5];
        private readonly bool[] pageVisited = { true, false, false, false, false };
        private readonly Button[] tabs = new Button[5];
        private Label sectionTitle, state, info, modeLabel, goal, scheduleLabel, pauseLabel, balance, editHint, durationLabel;
        private Label focusAppCount, focusWebsiteCount, planNext;
        private Panel dial;
        private readonly Font dialFont = UiFont(40, FontStyle.Bold);
        private readonly Font compactDialFont = UiFont(26, FontStyle.Bold);
        private readonly Font idleDialFont = UiFont(29, FontStyle.Bold);
        private string clockText = "未开始";
        private long renderedSeconds = -1;
        private double ratio, targetRatio;
        private Session renderedSession;
        private bool renderedPaused, renderedTrial;
        private Button start, pause, stop, previewRules, emergency, skipSchedule, openEdge;
        private Control idleActions, activeActions;
        private TableLayoutPanel editor;
        private ModeChoice mode;
        private NumberField duration;
        private Label planSummary, appSummary, websiteSummary;
        private ModeChoice ruleSections;
        private readonly Control[] ruleViews = new Control[4];
        private Button managePlans, manageApps, manageWebsites, trialRules;
        private ListView ruleWebsites, rulePlans;
        private Label webHelp;
        private int allowedAppCount;
        private bool statisticsBuilt;
        private List<FocusSchedule> plans;
        private ListView records;
        private int displayedRecordStops = -1;
        private Control recordsEmpty;
        private Label recordsCount;
        private PictureBox recordsIcon;
        private readonly AppImages appImages = new AppImages();
        private Task<IList<AppCatalog.Entry>> appDiscovery;
        private DateTimeOffset discoveryStarted;

        public MainWindow(FocusEngine engine, EventWaitHandle show, EventWaitHandle uninstall, string data, bool hidden, EventWaitHandle update = null)
        {
            this.engine = engine;
            history = new FocusHistory(data);
            showSignal = show;
            uninstallSignal = uninstall;
            updateSignal = update;
            this.data = data;
            guard = new ProcessGuard();
            DoubleBuffered = true;
            Icon = Icon.ExtractAssociatedIcon(ProcessGuard.ApplicationPath()) ?? SystemIcons.Application;
            tray = new NotifyIcon { Icon = Icon, Text = Program.Name, Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, delegate { Reveal(); });
            floatingMenu = (ToolStripMenuItem)menu.Items.Add("显示悬浮计时", null, delegate { ToggleFloatingTimer(); });
            menu.Items.Add("打开 Edge", null, delegate { Act(OpenEdge); });
            emergencyMenu = (ToolStripMenuItem)menu.Items.Add("紧急解除限制", null, delegate { Reveal(); Act(EmergencyRelease); });
            quit = (ToolStripMenuItem)menu.Items.Add("退出应用", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Reveal(); };
            tray.BalloonTipClicked += delegate { Reveal(); };
            Text = Program.Name;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 620);
            var desktop = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1120, desktop.Width - 36), Math.Min(790, desktop.Height - 58));
            Font = UiFont(11.5f);
            menu.Font = Font;
            ConfigureDpi(this);
            var shell = Columns(176, 100);
            shell.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 184);
            shell.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);
            Controls.Add(shell);
            var nav = Area(8);
            nav.Tag = "card";
            nav.Padding = new Padding(16, 26, 16, 20);
            Rows(nav, 84, 51, 51, 51, 51, -1, 47, 24);
            nav.Controls.Add(BrandHeading(true), 0, 0);
            nav.Controls.Add(new SideHorizon { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 5);
            string[] titles = { "专注", "规则设置", "停止记录", "专注统计", "设置" };
            for (int i = 0; i < tabs.Length; i++)
            {
                int selected = i;
                tabs[i] = Button(titles[i], 110, delegate { SelectPage(selected); });
                tabs[i].Dock = DockStyle.Fill;
                tabs[i].TextAlign = ContentAlignment.MiddleLeft;
                tabs[i].Padding = Padding.Empty;
                tabs[i].Font = UiFont(11.5f, FontStyle.Bold);
                tabs[i].Margin = new Padding(0, 0, 0, 7);
                nav.Controls.Add(tabs[i], 0, i == tabs.Length - 1 ? 6 : i + 1);
            }
            var versionLabel = Label("v" + Program.Version.ToString(3), 9, "muted");
            versionLabel.Padding = new Padding(8, 0, 0, 0);
            versionLabel.AccessibleName = "程序版本 " + Program.Version.ToString(3);
            nav.Controls.Add(versionLabel, 0, 7);
            shell.Controls.Add(nav, 0, 0);
            workspace = Area(3);
            workspace.Padding = new Padding(24, 22, 24, 12);
            Rows(workspace, 77, -1, 0);
            var heading = workspaceHeading = Columns(65, 35);
            sectionTitle = Label(titles[0], 25, "title");
            heading.Controls.Add(sectionTitle);
            state = new StatusTag { Font = UiFont(11.5f), Dock = DockStyle.Fill, Tag = "accent", Margin = Padding.Empty };
            heading.Controls.Add(state);
            workspace.Controls.Add(heading, 0, 0);
            workspace.Controls.Add(main, 0, 1);
            info = Label("", 9, "muted");
            workspace.Controls.Add(info, 0, 2);
            shell.Controls.Add(workspace, 1, 0);
            pages[0] = BuildFocus();
            pages[1] = BuildRules();
            pages[2] = BuildRecords();
            pages[3] = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            pages[4] = BuildSettings();
            main.Controls.AddRange(pages);
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].Dock = DockStyle.None;
                pages[i].Bounds = main.ClientRectangle;
            }
            pages[0].BringToFront();
            main.Resize += delegate
            {
                for (int i = 0; i < pages.Length; i++)
                {
                    if (pages[i].Size == main.ClientSize) continue;
                    Motion.Stop(pages[i]);
                    pages[i].Bounds = main.ClientRectangle;
                }
            };
            LoadRules();
            LoadPreferences();
            Motion.SetUserPreference(!engine.Settings.ReduceMotion);
            WindowPalette.AccentIndex = engine.Settings.Accent;
            WindowPalette.HudAccentIndex = engine.Settings.HudAccent;
            Theme(Program.UsesDarkTheme(engine.Settings));
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
            var now = DateTimeOffset.Now;
            Render(now, "", engine.NextSchedule(now));
            timer.Tick += delegate { Tick(); };
            timer.Start();
            Shown += delegate
            {
                Tick();
                if (hidden && engine.Session == null) { Hide(); return; }
                if (!Motion.Enabled) return;
                Opacity = 0;
                Motion.Run(this, 167, delegate(float t) { Opacity = t; }, delegate { Opacity = 1; });
            };
        }


        private Control BuildFocus()
        {
            var result = Columns(58, 42);
            var hero = Area(4);
            hero.Tag = "card";
            hero.Margin = new Padding(0, 0, 12, 0);
            hero.Padding = new Padding(20, 12, 20, 17);
            Rows(hero, 46, -1, 52, 46);
            modeLabel = Label("", 14, "title");
            goal = Label("", 11, "muted");
            goal.TextAlign = ContentAlignment.MiddleCenter;
            dial = new DialPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, TabStop = false, AccessibleName = "专注计时" };
            dial.Paint += DrawDial;
            dial.SizeChanged += delegate { dial.Invalidate(); };
            var modeHead = Columns(0, 100);
            modeHead.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 29);
            modeHead.Controls.Add(new ScreenGlyph(0, 1.1f), 0, 0);
            modeHead.Controls.Add(modeLabel, 1, 0);
            hero.Controls.Add(modeHead, 0, 0);
            hero.Controls.Add(dial, 0, 1);
            hero.Controls.Add(goal, 0, 3);
            var actions = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var ready = Columns(53, 47);
            ready.SizeChanged += delegate
            {
                int primary = ready.Width < 310 ? 49 : 53;
                if (ready.ColumnStyles[0].Width == primary) return;
                ready.ColumnStyles[0].Width = primary;
                ready.ColumnStyles[1].Width = 100 - primary;
            };
            start = Button("开始专注", 100, delegate { Act(trial == null ? (Action)StartFocus : StartTrial); }, true);
            previewRules = Button("查看本轮限制", 130, delegate { Act(delegate { ShowRulePreview(false); }); });
            start.Font = previewRules.Font = UiFont(12, FontStyle.Bold);
            start.Dock = previewRules.Dock = DockStyle.Fill;
            previewRules.Margin = Padding.Empty;
            ready.Controls.Add(start, 0, 0);
            ready.Controls.Add(previewRules, 1, 0);
            idleActions = ready;
            var inProgress = Columns(58, 42);
            pause = Button("暂停", 100, delegate { Act(PauseOrResume); }, true);
            stop = Button("提前停止", 100, delegate { Act(StopFocus); });
            pause.Font = stop.Font = start.Font;
            pause.Dock = stop.Dock = DockStyle.Fill;
            stop.Margin = Padding.Empty;
            inProgress.Controls.Add(pause, 0, 0);
            inProgress.Controls.Add(stop, 1, 0);
            activeActions = inProgress;
            actions.Controls.Add(activeActions);
            actions.Controls.Add(idleActions);
            hero.Controls.Add(actions, 0, 2);
            result.Controls.Add(hero, 0, 0);

            var context = Area(4);
            Rows(context, -34, -23, -29, -14);
            context.SizeChanged += delegate
            {
                bool compact = context.Height < 500;
                int top = compact ? 40 : 34;
                if (context.RowStyles[0].Height == top) return;
                context.RowStyles[0].Height = top;
                context.RowStyles[3].Height = compact ? 8 : 14;
            };
            var limits = ContextCard(5, 29, -1, -1, 8, 42);
            limits.Padding = new Padding(15, 10, 15, 12);
            limits.Controls.Add(IconHeading("本轮限制", 5), 0, 0);
            limits.Controls.Add(FocusDatum("软件白名单", 8, out focusAppCount), 0, 1);
            limits.Controls.Add(FocusDatum("Edge 网站", 6, out focusWebsiteCount, "Edge"), 0, 2);
            openEdge = Button("打开 Edge", 105, delegate { Act(OpenEdge); });
            openEdge.Tag = "signal";
            openEdge.Font = start.Font;
            openEdge.Dock = DockStyle.Fill;
            openEdge.Margin = Padding.Empty;
            limits.Controls.Add(openEdge, 0, 4);
            context.Controls.Add(limits, 0, 0);

            var schedule = ContextCard(3, 29, -1, 0);
            schedule.Padding = new Padding(15, 9, 15, 8);
            schedule.Controls.Add(IconHeading("下次自动开始", 7), 0, 0);
            scheduleLabel = Label("", 17, "title");
            schedule.Controls.Add(scheduleLabel, 0, 1);
            skipSchedule = Button("跳过本次", 96, delegate { Act(SkipNextSchedule); });
            skipSchedule.Dock = DockStyle.Fill;
            skipSchedule.Margin = Padding.Empty;
            schedule.Controls.Add(skipSchedule, 0, 2);
            context.Controls.Add(schedule, 0, 1);

            var allowances = ContextCard(3, 29, -1, 36);
            allowances.Padding = new Padding(15, 9, 15, 8);
            allowances.Controls.Add(IconHeading("本轮剩余暂停", 0), 0, 0);
            pauseLabel = Label("", 17, "metric");
            allowances.Controls.Add(pauseLabel, 0, 1);
            allowances.Controls.Add(FocusDatum("本月可提前停止", 2, out balance, "本月可停止"), 0, 2);
            context.Controls.Add(allowances, 0, 2);

            preflightEntry = Button("开始前检查  »", 140, delegate { Act(delegate { SelectPage(1); ruleSections.SelectedIndex = 3; ShowRuleSection(); }); }, true);
            preflightEntry.Dock = DockStyle.Fill;
            preflightEntry.Height = 40;
            preflightEntry.Margin = new Padding(0, 0, 0, 6);
            context.Controls.Add(preflightEntry, 0, 3);
            emergency = Button("紧急解除限制", 130, delegate { Act(EmergencyRelease); });
            emergency.Dock = DockStyle.Top;
            emergency.Height = 30;
            emergency.Margin = Padding.Empty;
            emergency.Tag = "link";
            result.Controls.Add(context, 1, 0);
            return result;
        }

        private static Control FocusDatum(string caption, int symbol, out Label value, string compactCaption = null)
        {
            var row = Columns(0, 100, 0);
            Rows(row, -1);
            row.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 29);
            row.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 80);
            var icon = new ScreenGlyph(symbol, .83f);
            var name = Label(caption, 10.5f, "muted");
            var result = Label("", 11, "title");
            row.Controls.Add(icon, 0, 0);
            row.Controls.Add(name, 1, 0);
            value = result;
            value.TextAlign = ContentAlignment.MiddleRight;
            row.Controls.Add(value, 2, 0);
            Action measure = delegate
            {
                bool compact = row.Width < 230;
                icon.Visible = !compact;
                int iconWidth = compact ? 0 : 29;
                if (row.ColumnStyles[0].Width != iconWidth) row.ColumnStyles[0].Width = iconWidth;
                name.Text = compact && compactCaption != null ? compactCaption : caption;
                int textWidth = result.PreferredWidth + (compact ? 3 : 12);
                if (row.ColumnStyles[2].Width != textWidth) row.ColumnStyles[2].Width = textWidth;
            };
            row.SizeChanged += delegate { measure(); };
            result.TextChanged += delegate { measure(); };
            result.FontChanged += delegate { measure(); };
            return row;
        }

        private static Card ContextCard(int count, params int[] heights)
        {
            var area = Area(count);
            area.Tag = "card";
            area.Padding = new Padding(12, 8, 12, 8);
            area.Margin = new Padding(0, 0, 0, 8);
            Rows(area, heights);
            return area;
        }

        private void DrawDial(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            float scale = graphics.DpiX / 96f;
            float x = dial.Width / 2f, y = dial.Height / 2f;
            float diameter = Math.Min(428 * scale, Math.Min(dial.Width, dial.Height) - 30 * scale);
            if (diameter <= 0) return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var halo = new GraphicsPath())
            {
                halo.AddEllipse(x - diameter * 0.91f, y - diameter * 0.63f, diameter * 1.82f, diameter * 1.26f);
                int intensity = darkTheme ? 40 : 16;
                using (var brush = new PathGradientBrush(halo)
                {
                    CenterColor = Color.FromArgb(intensity, accent),
                    SurroundColors = new[] { Color.FromArgb(0, accent) }
                }) graphics.FillPath(brush, halo);
            }
            if (!darkTheme)
            using (var grid = new Pen(Color.FromArgb(68, outline)))
            {
                float step = 24 * scale;
                for (float dx = x % step; dx < dial.Width; dx += step)
                    graphics.DrawLine(grid, dx, 0, dx, dial.Height);
                for (float dy = y % step; dy < dial.Height; dy += step)
                    graphics.DrawLine(grid, 0, dy, dial.Width, dy);
            }

            float radius = diameter / 2f;
            var ring = new RectangleF(x - radius, y - radius, diameter, diameter);
            using (var line = new Pen(outline, scale))
            {
                using (var track = new Pen(Motion.Blend(outline, accent, darkTheme ? .19f : .12f), 7 * scale))
                    graphics.DrawEllipse(track, ring);
                for (int i = 0; i < 60; i++)
                {
                    double angle = i * Math.PI / 30 - Math.PI / 2;
                    float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle);
                    float inside = radius - (i % 5 == 0 ? 16 : 11) * scale;
                    graphics.DrawLine(line, x + dx * inside, y + dy * inside,
                        x + dx * (radius - 8 * scale), y + dy * (radius - 8 * scale));
                }
            }
            if (ratio > 0.001)
            {
                float sweep = (float)(ratio * 360);
                using (var glow = new Pen(Color.FromArgb(34, accent), 15 * scale))
                    graphics.DrawArc(glow, ring, -90, sweep);
                int segments = Math.Max(2, (int)(sweep / 4));
                for (int i = 0; i < segments; i++)
                {
                    float from = -90 + sweep * i / segments;
                    float to = -90 + sweep * (i + 1) / segments + 0.7f;
                    Color color = Motion.Blend(accent, Motion.Blend(accent, Color.White, .55f), i / (float)Math.Max(1, segments));
                    using (var arc = new Pen(color, 7 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        graphics.DrawArc(arc, ring, from, to - from);
                }
                if (diameter >= 160 * scale)
                {
                    double angle = ratio * Math.PI * 2 - Math.PI / 2;
                    float tipX = x + radius * (float)Math.Cos(angle);
                    float tipY = y + radius * (float)Math.Sin(angle);
                    using (var tip = new SolidBrush(Motion.Blend(accent, Color.White, darkTheme ? .66f : .25f)))
                        graphics.FillEllipse(tip, tipX - 4.7f * scale, tipY - 4.7f * scale, 9.4f * scale, 9.4f * scale);
                }
            }
            else
                using (var mark = new SolidBrush(accent))
                    graphics.FillEllipse(mark, x - 4 * scale, y - radius - 4 * scale, 8 * scale, 8 * scale);

            var align = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            Font clockFont = renderedSeconds == -2 ? idleDialFont : diameter < 255 * scale ? compactDialFont : dialFont;
            TextRenderer.DrawText(graphics, clockText, clockFont, dial.ClientRectangle, ink, align);
            if (diameter > 160 * scale)
            {
                int badgeWidth = (int)Math.Round(86 * scale), badgeHeight = (int)Math.Round(26 * scale);
                var badge = new Rectangle((int)x - badgeWidth / 2, (int)(y - radius * .45f) - badgeHeight / 2, badgeWidth, badgeHeight);
                Color badgeInk = renderedPaused ? palette.Paused : renderedSession != null ? accent : muted;
                using (var path = RoundedPath(badge, badgeHeight / 2 - 1))
                using (var background = new SolidBrush(Motion.Blend(card, badgeInk, .12f)))
                using (var border = new Pen(Motion.Blend(outline, badgeInk, .25f)))
                {
                    graphics.FillPath(background, path);
                    graphics.DrawPath(border, path);
                }
                string caption = renderedTrial ? "测试中" : renderedPaused ? "暂停中" : renderedSession != null ? (renderedSession.Scheduled ? "自动开始" : "专注中") : "准备开始";
                TextRenderer.DrawText(graphics, caption, Font, badge, badgeInk, align);
            }
        }

        private Control Metric(string title, out Label value, float valueSize = 16, int glyph = 0)
        {
            var box = Area(2);
            Rows(box, -45, -55);
            box.Padding = new Padding(16, 10, 12, 10);
            box.SizeChanged += delegate
            {
                var inset = box.Width < 160 ? new Padding(9, 10, 6, 10) : new Padding(16, 10, 12, 10);
                if (box.Padding != inset) box.Padding = inset;
            };
            box.Margin = new Padding(0, 0, 8, 0);
            box.Tag = "card";
            value = Label("", valueSize, "metric");
            var heading = Columns(0, 100);
            Rows(heading, -1);
            heading.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 24);
            var headingIcon = new ScreenGlyph(glyph, .8f);
            heading.Controls.Add(headingIcon, 0, 0);
            heading.Controls.Add(Label(title, 9, "muted"), 1, 0);
            box.SizeChanged += delegate
            {
                bool showIcon = box.Width >= 130;
                headingIcon.Visible = showIcon;
                int iconWidth = showIcon ? 24 : 0;
                if (heading.ColumnStyles[0].Width != iconWidth) heading.ColumnStyles[0].Width = iconWidth;
            };
            box.Controls.Add(heading);
            box.Controls.Add(value);
            return box;
        }

        private sealed class DialPanel : Panel
        {
            public DialPanel() { DoubleBuffered = true; }
        }

        private Control BuildRules()
        {
            var result = Area(3);
            Rows(result, 0, 68, -1);
            editHint = Label("", 9, "muted");
            result.Controls.Add(editHint, 0, 0);
            ruleSections = Combo("计时与计划", "软件白名单", "网站白名单", "开始前检查");
            ruleSections.Accented = true;
            ruleSections.Enlarge(12, 50, 9);
            ruleSections.Glyphs = new[] { 7, 8, 6, 5 };
            ruleSections.AccessibleName = "规则分类";
            ruleSections.Dock = DockStyle.Top;
            ruleSections.Margin = Padding.Empty;
            result.Controls.Add(ruleSections, 0, 1);

            var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            result.Controls.Add(body, 0, 2);
            ruleViews[0] = BuildTimingRules();
            ruleViews[1] = BuildAllowlistRules(false);
            ruleViews[2] = BuildAllowlistRules(true);
            ruleViews[3] = BuildRuleChecks();
            foreach (var view in ruleViews) body.Controls.Add(view);
            ruleSections.SelectedIndexChanged += delegate { ShowRuleSection(); };
            ShowRuleSection();
            return result;
        }

        private void ShowRuleSection()
        {
            for (int i = 0; i < ruleViews.Length; i++)
                ruleViews[i].Visible = i == ruleSections.SelectedIndex;
            if (ruleSections.SelectedIndex == 1 && IsHandleCreated) RefreshRuleApps();
            if (ruleSections.SelectedIndex == 3) RefreshPreflight();
            if (palette != null) PaintPolishedCanvas();
        }

        private Control BuildTimingRules()
        {
            var layout = Area(2);
            Rows(layout, 149, -1);
            editor = ContextCard(3, 32, 44, -1);
            editor.Padding = new Padding(18, 10, 18, 12);
            editor.Controls.Add(IconHeading("手动计时", 0), 0, 0);
            mode = Combo(TimerModeName(false), TimerModeName(true));
            mode.Width = 150;
            duration = Number(1, 1440);
            editor.Controls.Add(TimerRuleRow("计时方式", mode), 0, 1);
            var durationFields = TimerRuleRow("时长（分钟）", duration);
            durationLabel = (Label)durationFields.GetControlFromPosition(0, 0);
            editor.Controls.Add(durationFields, 0, 2);
            layout.Controls.Add(editor, 0, 0);

            managePlans = Button("管理自动开始", 132, delegate { Act(EditSchedules); });
            var schedule = RuleDetail("自动开始计划", managePlans, 7);
            planSummary = Label("", 9, "muted");
            planNext = Label("", 9, "accent");
            var summary = Columns(47, 53);
            planNext.TextAlign = ContentAlignment.MiddleRight;
            summary.Controls.Add(planSummary, 0, 0);
            summary.Controls.Add(planNext, 1, 0);
            schedule.Controls.Add(summary, 0, 1);
            rulePlans = RuleTable("自动开始计划概览", "开始时间", "结束时间", "计时方式", "重复日期", "状态");
            schedule.Controls.Add(rulePlans, 0, 2);
            layout.Controls.Add(schedule, 0, 1);

            duration.EditCommitted += delegate { SaveTimingRule(true); };
            mode.SelectedIndexChanged += delegate { SaveTimingRule(false); UpdateTimerFields(); };
            return layout;
        }

        private static TableLayoutPanel TimerRuleRow(string name, Control field)
        {
            var row = Columns(0, 100);
            row.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 146);
            row.Controls.Add(Label(name, 10, "muted"), 0, 0);
            field.AccessibleName = name;
            var number = field as NumberField;
            if (number != null) number.SetLabel(name);
            field.Anchor = AnchorStyles.Left;
            row.Controls.Add(field, 1, 0);
            return row;
        }

        private Control BuildAllowlistRules(bool websites)
        {
            if (!websites) return BuildPolishedApps();
            manageWebsites = Button("管理网站白名单", 144, delegate { Act(EditAllowedWebsites); });
            var layout = Area(2);
            Rows(layout, 120, -1);

            var banner = RuleDetail("网站白名单", manageWebsites, 6);
            websiteSummary = Label("", 10, "muted");
            banner.Controls.Add(websiteSummary, 0, 1);
            layout.Controls.Add(banner, 0, 0);

            var detail = ContextCard(4, 45, 42, -1, 39);
            detail.Padding = new Padding(17, 10, 17, 10);
            var toolbar = Columns(55, 0, 45);
            toolbar.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 70);
            toolbar.Controls.Add(Label("已允许的域名", 13, "title"), 0, 0);
            toolbar.Controls.Add(Label("搜索域名", 9, "muted"), 1, 0);
            websiteSearch = SearchField("搜索域名");
            toolbar.Controls.Add(websiteSearch, 2, 0);
            detail.Controls.Add(toolbar, 0, 0);
            polishedWebsites = new PolishedWebsiteTable();
            polishedWebsites.DeleteRequested = delegate(string domain) { RemoveWebsite(domain); };
            polishedWebsites.HeaderStyle = ColumnHeaderStyle.None;
            ruleWebsites = polishedWebsites;
            var webHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            webTableHost = webHost;
            webHost.Controls.Add(ruleWebsites);
            webHost.Controls.Add(new TableHeader(ruleWebsites));
            detail.Controls.Add(webHost, 0, 2);
            var webEmpty = Area(6);
            Rows(webEmpty, -1, 62, 34, 32, 44, -1);
            webEmpty.Controls.Add(new PolishedGlyph(6, 40) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
            websiteEmptyTitle = Label("", 13, "title");
            websiteEmptyHelp = Label("", 9, "muted");
            websiteEmptyTitle.TextAlign = websiteEmptyHelp.TextAlign = ContentAlignment.MiddleCenter;
            webEmpty.Controls.Add(websiteEmptyTitle, 0, 2);
            webEmpty.Controls.Add(websiteEmptyHelp, 0, 3);
            websiteEmptyAction = Button("添加网站", 112, delegate
            {
                websiteSearch.Clear();
                websiteInput.Focus();
            }, true);
            websiteEmptyAction.Anchor = AnchorStyles.None;
            webEmpty.Controls.Add(websiteEmptyAction, 0, 4);
            websiteEmpty = webEmpty;
            detail.Controls.Add(websiteEmpty, 0, 2);

            var add = Columns(0, 100, 0);
            add.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 70);
            add.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 103);
            add.Controls.Add(Label("网站域名", 9, "muted"), 0, 0);
            websiteInput = SearchField("添加网站域名");
            websiteInput.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                if (websiteAdd.Enabled) Act(AddWebsiteInline);
            };
            add.Controls.Add(websiteInput, 1, 0);
            websiteAdd = Button("＋ 添加", 100, delegate { Act(AddWebsiteInline); }, true);
            websiteAdd.Anchor = AnchorStyles.Right;
            websiteAdd.Margin = Padding.Empty;
            add.Controls.Add(websiteAdd, 2, 0);
            detail.Controls.Add(add, 0, 1);
            webHelp = Label("", 9, "muted");
            detail.Controls.Add(webHelp, 0, 3);
            websiteSearch.TextChanged += delegate { RefreshRuleWebsites(); };
            layout.Controls.Add(detail, 0, 1);
            return layout;
        }

        private static Card RuleDetail(string title, Button action, int glyph, bool polished = false)
        {
            var result = ContextCard(3, 48, 37, -1);
            result.Padding = new Padding(18, 10, 18, 12);
            var heading = Columns(100, 0);
            Rows(heading, -1);
            heading.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, action.Width);
            heading.Controls.Add(polished ? PolishedHeading(title, glyph) : IconHeading(title, glyph), 0, 0);
            action.Tag = "primary";
            action.Anchor = AnchorStyles.Right;
            action.Margin = Padding.Empty;
            heading.Controls.Add(action, 1, 0);
            result.Controls.Add(heading, 0, 0);
            return result;
        }

        private ListView RuleTable(string name, params string[] names)
        {
            var list = new BufferedAppList
            {
                AccessibleName = name, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 0),
                View = View.Details, OwnerDraw = true, FullRowSelect = true, MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.None, ShowItemToolTips = true
            };
            foreach (string title in names) list.Columns.Add(title);
            list.ClientSizeChanged += delegate
            {
                int width = Math.Max(1, list.ClientSize.Width);
                int compact = Math.Min(148, width / names.Length);
                list.Columns[0].Width = width - compact * (names.Length - 1);
                for (int i = 1; i < names.Length; i++) list.Columns[i].Width = compact;
            };
            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (var fill = new SolidBrush(subtle)) e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, Rectangle.Inflate(e.Bounds, -10, 0), muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                using (var fill = new SolidBrush(e.Item.Selected ? subtle : e.ItemIndex % 2 == 0 ? card : Motion.Blend(card, subtle, 0.26f)))
                    e.Graphics.FillRectangle(fill, e.Bounds);
                using (var line = new Pen(outline)) e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var textBounds = Rectangle.Inflate(e.Bounds, -10, 0);
                if (e.ColumnIndex == 0 && list.SmallImageList != null && e.Item.ImageIndex >= 0)
                {
                    var size = list.SmallImageList.ImageSize;
                    list.SmallImageList.Draw(e.Graphics, textBounds.X, textBounds.Y + (textBounds.Height - size.Height) / 2, e.Item.ImageIndex);
                    textBounds.X += size.Width + 9;
                    textBounds.Width -= size.Width + 9;
                }
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, textBounds,
                    e.ColumnIndex == 0 ? ink : muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            return list;
        }

        private Control BuildRuleChecks()
        {
            var layout = Columns(55, 45);
            var check = ContextCard(8, 44, 59, 59, 59, 59, 36, -1, 42);
            check.Margin = new Padding(0, 0, 10, 0);
            check.Padding = new Padding(16, 9, 16, 9);
            check.Controls.Add(IconHeading("开始前检查", 5), 0, 0);
            check.Controls.Add(CheckDatum("软件白名单", 8, out checkApps), 0, 1);
            check.Controls.Add(CheckDatum("网站白名单", 6, out checkSites), 0, 2);
            check.Controls.Add(CheckDatum("专注 Edge", 6, out checkEdge), 0, 3);
            check.Controls.Add(CheckDatum("自动开始计划", 7, out checkSchedule), 0, 4);
            preflightStatus = Label("", 10, "accent");
            check.Controls.Add(preflightStatus, 0, 5);
            var checks = Flow();
            checks.WrapContents = false;
            checks.Controls.Add(Button("预览本轮限制", 123, delegate { Act(delegate { ShowRulePreview(false); }); }));
            trialRules = Button("测试 20 秒", 110, delegate { Act(TryRules); });
            checks.Controls.Add(trialRules);
            check.Controls.Add(checks, 0, 7);
            layout.Controls.Add(check, 0, 0);

            var plan = ContextCard(6, 44, 60, 60, 60, -1, 45);
            plan.Padding = new Padding(16, 9, 16, 12);
            plan.Margin = Padding.Empty;
            plan.Controls.Add(IconHeading("本轮专注计划", 7), 0, 0);
            plan.Controls.Add(CheckDatum("计时方式", 0, out checkMode), 0, 1);
            plan.Controls.Add(CheckDatum("剩余暂停", 0, out checkPause), 0, 2);
            plan.Controls.Add(CheckDatum("可提前停止", 2, out checkStops), 0, 3);
            preflightStart = Button("开始专注", 132, delegate { Act(BeginManualFocus); }, true);
            preflightStart.Dock = DockStyle.Fill;
            preflightStart.Margin = new Padding(0, 5, 0, 0);
            plan.Controls.Add(preflightStart, 0, 5);
            layout.Controls.Add(plan, 1, 0);
            return layout;
        }

        private Control BuildRecords()
        {
            var result = Area(3);
            Rows(result, 92, 60, -1);
            var metrics = Columns(33, 34, 33);
            Rows(metrics, -1);
            metrics.Controls.Add(Metric("本月提前停止", out monthStops, 18, 2), 0, 0);
            metrics.Controls.Add(Metric("本月可提前停止", out monthRemainingStops, 18, 5), 1, 0);
            var latest = Metric("最近一次停止", out latestStop, 16, 7);
            latest.Margin = Padding.Empty;
            metrics.Controls.Add(latest, 2, 0);
            result.Controls.Add(metrics, 0, 0);

            var filters = Columns(55, 45);
            recordScope = Combo("全部", "本周", "本月");
            recordScope.Enlarge(11, 42, 5);
            recordScope.AccessibleName = "停止记录范围";
            recordScope.Dock = DockStyle.None;
            recordScope.Anchor = AnchorStyles.Left;
            recordScope.SelectedIndexChanged += delegate { RefreshRecords(); };
            filters.Controls.Add(recordScope, 0, 0);
            var export = Button("导出记录", 102, delegate { Act(ExportStopRecords); });
            export.Anchor = AnchorStyles.Right;
            filters.Controls.Add(export, 1, 0);
            result.Controls.Add(filters, 0, 1);

            var card = Area(2);
            card.Tag = "card";
            card.Padding = new Padding(16, 10, 16, 14);
            Rows(card, 43, -1);
            var head = Columns(55, 45);
            Rows(head, -1);
            head.Controls.Add(IconHeading("停止记录列表", 2, 13));
            recordsCount = Label("", 9, "muted");
            recordsCount.TextAlign = ContentAlignment.MiddleRight;
            head.Controls.Add(recordsCount);
            card.Controls.Add(head, 0, 0);

            var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            records = new BufferedAppList(32)
            {
                Dock = DockStyle.Fill, AccessibleName = "提前停止记录", View = View.Details,
                FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.None,
                HideSelection = true, MultiSelect = false, OwnerDraw = true, ShowItemToolTips = true
            };
            records.Columns.Add("时间");
            records.Columns.Add("剩余时长");
            records.Columns.Add("原因");
            records.DrawColumnHeader += DrawRecordHeader;
            records.DrawSubItem += DrawRecordCell;
            records.DrawItem += delegate(object sender, DrawListViewItemEventArgs args) { args.DrawDefault = false; };
            records.ClientSizeChanged += delegate { FitRecordColumns(); };
            host.Controls.Add(records);

            recordsEmpty = EmptyState("还没有提前停止记录", "提前停止的时间、剩余时长和理由会显示在这里。");
            host.Controls.Add(recordsEmpty);
            card.Controls.Add(host, 0, 1);
            result.Controls.Add(card, 0, 2);
            return result;
        }

        private Control EmptyState(string title, string subtitle)
        {
            var box = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            Rows(box, -1, 66, 34, 30, -1);
            recordsIcon = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.CenterImage, Margin = Padding.Empty };
            box.Controls.Add(recordsIcon, 0, 1);
            var heading = Label(title, 13, "title");
            heading.TextAlign = ContentAlignment.MiddleCenter;
            box.Controls.Add(heading, 0, 2);
            var line = Label(subtitle, 9, "muted");
            line.TextAlign = ContentAlignment.MiddleCenter;
            box.Controls.Add(line, 0, 3);
            return box;
        }

        private static Image EmptyGlyph(Color color, float scale)
        {
            int size = (int)Math.Round(72 * scale);
            var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = size / 2f, cy = size / 2f, radius = size * 0.28f;
                using (var ring = new Pen(Color.FromArgb(70, color), 2.2f * scale))
                    graphics.DrawEllipse(ring, cx - radius, cy - radius, radius * 2, radius * 2);
                using (var hand = new Pen(Color.FromArgb(150, color), 2.2f * scale))
                {
                    graphics.DrawLine(hand, cx, cy, cx, cy - radius * 0.62f);
                    graphics.DrawLine(hand, cx, cy, cx + radius * 0.46f, cy + radius * 0.18f);
                }
                using (var dot = new SolidBrush(Color.FromArgb(120, color)))
                    graphics.FillEllipse(dot, cx - 2.2f * scale, cy - 2.2f * scale, 4.4f * scale, 4.4f * scale);
            }
            return bitmap;
        }

        private void DrawRecordHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var fill = new SolidBrush(subtle)) e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, Rectangle.Inflate(e.Bounds, -6, 0), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void DrawRecordCell(object sender, DrawListViewSubItemEventArgs e)
        {
            using (var fill = new SolidBrush(e.Item.Selected ? subtle : e.ItemIndex % 2 == 0 ? card : Motion.Blend(card, subtle, 0.26f)))
                e.Graphics.FillRectangle(fill, e.Bounds);
            using (var line = new Pen(outline)) e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, records.Font, Rectangle.Inflate(e.Bounds, -6, 0), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void FitRecordColumns()
        {
            if (records == null || records.Columns.Count < 3) return;
            int width = records.ClientSize.Width - 2;
            if (width < 80) return;
            int first = Math.Min(width / 2, TableTextWidth(records, "8888-88-88 88:88"));
            int second = Math.Min(width / 3, TableTextWidth(records, "8888 分钟"));
            records.Columns[0].Width = first;
            records.Columns[1].Width = second;
            records.Columns[2].Width = width - first - second;
        }

        private void UpdateTimerFields()
        {
            durationLabel.Parent.Visible = mode.SelectedIndex == 0;
        }


        private void BuildStatisticsPage()
        {
            var stats = BuildStatistics();
            stats.Dock = DockStyle.None;
            stats.Bounds = main.ClientRectangle;
            var placeholder = pages[3];
            pages[3] = stats;
            main.Controls.Add(stats);
            if (placeholder != null) placeholder.Dispose();
            statisticsBuilt = true;
            statisticsRefreshPending = true;
            Theme(darkTheme);
        }
        private void SelectPage(int n)
        {
            int previous = page;
            if (previous == n) return;
            if (n == 3 && !statisticsBuilt) BuildStatisticsPage();
            Control incoming = pages[n];
            Control outgoing = pages[previous];
            page = n;
            Motion.Stop(outgoing);
            if (outgoing.Left != 0) outgoing.Left = 0;
            Motion.Stop(incoming);
            int shift = Motion.Enabled && pageVisited[n]
                ? (int)Math.Round(10 * DeviceDpi / 96.0) * (n > previous ? 1 : -1) : 0;
            if (incoming.Left != shift) incoming.Left = shift;
            incoming.BringToFront();
            if (outgoing.ContainsFocus) tabs[n].Select();
            sectionTitle.Text = n == 2 ? "提前停止记录" : tabs[n].Text;
            PaintTabs(previous);
            if (n == 3) statisticsRefreshPending |= !pageVisited[n] || engine.Session != null ||
                displayedHistoryRevision != history.Revision || displayedStops != engine.State.Stops.Count ||
                nextStatisticsRefresh.Date != DateTime.Today;
            pageVisited[n] = true;
            PaintPolishedCanvas();
            if (n == 1) ShowRuleSection();
            if (n == 2 && (displayedRecordStops != engine.State.Stops.Count || displayedRecordDay != DateTime.Today)) RefreshRecords();
            if (shift == 0) return;
            Motion.Run(incoming, 160, delegate(float t)
            {
                incoming.Left = (int)Math.Round(shift * (1.0 - t));
            }, delegate
            {
                incoming.Left = 0;
            });
        }

        protected override bool ProcessTabKey(bool forward)
        {
            var active = ActiveControl;
            var next = active;
            bool wrapped = false;
            while (true)
            {
                next = GetNextControl(next, forward);
                if (next == null)
                {
                    if (wrapped) return false;
                    wrapped = true;
                    continue;
                }
                if (next == active) return false;
                if (!next.TabStop || !next.CanSelect || (main.Contains(next) && !pages[page].Contains(next)))
                    continue;
                next.Select();
                return true;
            }
        }

        private void RefreshRecords()
        {
            displayedRecordStops = engine.State.Stops.Count;
            var now = DateTimeOffset.Now;
            displayedRecordDay = now.Date;
            var today = now.Date;
            var month = new DateTime(today.Year, today.Month, 1);
            var currentMonth = StopsInRange(month, month.AddMonths(1)).ToList();
            monthStops.Text = currentMonth.Count + " 次";
            monthRemainingStops.Text = engine.StopsRemaining(now) + " 次";
            latestStop.Text = engine.State.Stops.Count == 0 ? "—" : FocusHistory.Local(engine.State.Stops.Max(entry => entry.WhenUtc)).ToString("MM-dd HH:mm");
            var week = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
            var selected = recordScope.SelectedIndex == 1 ? StopsInRange(week, week.AddDays(7))
                : recordScope.SelectedIndex == 2 ? currentMonth : engine.State.Stops;
            records.BeginUpdate();
            try
            {
                records.Items.Clear();
                foreach (var entry in selected.OrderByDescending(item => item.WhenUtc))
                {
                    var item = new ListViewItem(FocusHistory.Local(entry.WhenUtc).ToString("yyyy-MM-dd HH:mm")) { Tag = entry };
                    item.SubItems.Add(entry.Minutes + " 分钟");
                    item.SubItems.Add(entry.Reason.Replace('\n', ' ').Replace('\r', ' '));
                    item.ToolTipText = entry.Reason;
                    records.Items.Add(item);
                }
            }
            finally { records.EndUpdate(); }
            bool empty = records.Items.Count == 0;
            records.Visible = !empty;
            if (recordsEmpty != null) recordsEmpty.Visible = empty;
            if (recordsCount != null) recordsCount.Text = records.Items.Count + " 条";
            FitRecordColumns();
        }
    }
}
