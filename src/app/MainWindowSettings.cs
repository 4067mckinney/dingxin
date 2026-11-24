using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private TableLayoutPanel behavior;
        private Control advanced;
        private Label settingsHint;
        private ModeChoice theme;
        private ModeChoice settingsSections;
        private CheckBox startup, showFloating, reduceMotion, showFloatingOnPage, showNotifications, playChimes;
        private TrackBar opacity;
        private Label opacityValue;
        private HudPreview hudPreview;
        private bool loadingPreferences;
        private ToolStripMenuItem floatingMenu;
        private FloatingTimerWindow floatingTimer;
        private Session floatingSession;
        private bool floatingRequested;
        private AccentSwatch[] accentSwatches;
        private Panel settingsScroll;
        private AccentSwatch[] hudSwatches;
        private PolishedSwitch hudFollow, hudLockedSwitch, hudPinnedSwitch;

        private Control BuildSettings()
        {
            var result = Area(3);
            result.Tag = "polished-page";
            Rows(result, 0, 68, -1);
            settingsHint = Label("", 9, "muted");
            result.Controls.Add(settingsHint, 0, 0);
            var sections = settingsSections = Combo("常规设置", "悬浮窗");
            sections.Accented = true;
            sections.Enlarge(12, 50, 9);
            sections.AccessibleName = "设置分类";
            sections.Dock = DockStyle.Top;
            result.Controls.Add(sections, 0, 1);
            var host = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            result.Controls.Add(host, 0, 2);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
            settingsScroll = scroll;
            var preferences = RuleColumn();
            scroll.Controls.Add(preferences);
            host.Controls.Add(scroll);

            var display = Section(preferences, "外观", 4);
            theme = Combo("浅色", "深色", "跟随系统");
            Add(display, Fields("界面主题", theme));
            var accentColors = Flow();
            accentColors.WrapContents = false;
            accentSwatches = new AccentSwatch[WindowPalette.AccentPresets.Length];
            for (int i = 0; i < accentSwatches.Length; i++)
            {
                int index = i;
                var swatch = new AccentSwatch(WindowPalette.AccentPresets[i]);
                swatch.Click += delegate { ChooseAccent(index); };
                accentColors.Controls.Add(swatch);
                accentSwatches[i] = swatch;
            }
            Add(display, Fields("强调色", accentColors));
            showFloating = new PolishedSwitch("专注时显示悬浮计时");
            reduceMotion = new PolishedSwitch("减少界面动效");
            Add(display, showFloating);
            Add(display, reduceMotion);

            behavior = Section(preferences, "系统", 8);
            startup = new PolishedSwitch("登录 Windows 后自动启动");
            startup.AccessibleDescription = "登录 Windows 后在后台启动" + Program.Name;
            Add(behavior, startup);

            var actions = Flow();
            advanced = Button("高级设置", 130, delegate { Act(OpenAdvanced); });
            actions.Controls.Add(advanced);
            Add(behavior, actions);

            var bottom = Columns(50, 50);
            bottom.AutoSize = true;
            bottom.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var left = RuleColumn();
            left.Margin = new Padding(0, 0, 6, 0);
            var announcements = Section(left, "提醒与通知", 0);
            playChimes = new PolishedSwitch("播放专注开始和结束提示音");
            showNotifications = new PolishedSwitch("显示专注和计划的托盘通知");
            Add(announcements, playChimes);
            Add(announcements, showNotifications);
            bottom.Controls.Add(left, 0, 0);
            var right = RuleColumn();
            right.Margin = new Padding(6, 0, 0, 0);
            var dataTools = Section(right, "数据与记录", 5);
            Add(dataTools, Hint("导出本机保存的提前停止记录。"));
            var export = Flow();
            export.Controls.Add(Button("导出停止记录", 128, delegate { Act(delegate { ExportStops(engine.State.Stops.ToArray()); }); }));
            Add(dataTools, export);
            Add(dataTools, Hint("备份包含设置、白名单与全部专注记录。"));
            var backup = Flow();
            backup.Controls.Add(Button("导出备份", 104, delegate { Act(ExportBackup); }));
            backup.Controls.Add(Button("导入备份", 104, delegate { Act(ImportBackup); }));
            Add(dataTools, backup);
            bottom.Controls.Add(right, 1, 0);
            Add(preferences, bottom);

            var floating = BuildHudSettings();
            floating.Visible = false;
            host.Controls.Add(floating);
            sections.SelectedIndexChanged += delegate
            {
                bool show = sections.SelectedIndex == 1;
                scroll.Visible = !show;
                floating.Visible = show;
            };

            theme.SelectedIndexChanged += delegate { SaveDisplayPreferences(); };
            showFloating.CheckedChanged += delegate { SaveDisplayPreferences(showFloating.Checked); };
            showFloatingOnPage.CheckedChanged += delegate { SaveDisplayPreferences(showFloatingOnPage.Checked); };
            reduceMotion.CheckedChanged += delegate { SaveDisplayPreferences(); };
            startup.CheckedChanged += delegate { SaveStartupPreference(); };
            playChimes.CheckedChanged += delegate { SaveAlertPreferences(); };
            showNotifications.CheckedChanged += delegate { SaveAlertPreferences(); };
            return result;
        }

        private void ExportBackup()
        {
            using (var dialog = new SaveFileDialog
            {
                Title = "导出备份", Filter = Program.Name + "备份 (*.zip)|*.zip",
                FileName = "StudyFocus-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string path = CreateBackup(dialog.FileName);
                ShowSaved();
                MessageBox.Show(this, "备份已导出：\n" + path, Program.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ImportBackup()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "导入备份", Filter = Program.Name + "备份 (*.zip)|*.zip", CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this, "导入会覆盖当前设置、白名单与专注记录，确定继续？", Program.Name,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                RestoreBackup(dialog.FileName);
                if (MessageBox.Show(this, "恢复完成，需要重启应用才能生效。现在重启？", Program.Name,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    Program.RestartNow();
                    return;
                }
                ShowSaved();
            }
        }

        internal string CreateBackup(string path)
        {
            Directory.CreateDirectory(Program.Data);
            string temp = Path.Combine(Program.Data, ".backup-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                string state = Path.Combine(Program.Data, "state.dat");
                if (File.Exists(state)) File.Copy(state, Path.Combine(temp, "state.dat"), true);
                string history = Path.Combine(Program.Data, "FocusHistory");
                if (Directory.Exists(history)) CopyFolder(history, Path.Combine(temp, "FocusHistory"));
                File.WriteAllText(Path.Combine(temp, "backup.txt"),
                    "app=StudyFocus" + Environment.NewLine + "version=" + Program.Version + Environment.NewLine +
                    "created=" + DateTimeOffset.Now.ToString("o") + Environment.NewLine, Encoding.UTF8);
                if (File.Exists(path)) File.Delete(path);
                ZipFile.CreateFromDirectory(temp, path, CompressionLevel.Optimal, false);
                return path;
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        internal void RestoreBackup(string path)
        {
            RequireEditable();
            Directory.CreateDirectory(Program.Data);
            string temp = Path.Combine(Program.Data, ".restore-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                ZipFile.ExtractToDirectory(path, temp);
                string state = Path.Combine(temp, "state.dat");
                if (!File.Exists(state)) throw new InvalidOperationException("备份中没有 state.dat，可能不是本软件的备份。");
                string manifest = Path.Combine(temp, "backup.txt");
                if (!File.Exists(manifest)) throw new InvalidOperationException("备份缺少 backup.txt，无法确认来源。");
                Directory.CreateDirectory(Program.Data);
                File.Copy(state, Path.Combine(Program.Data, "state.dat"), true);
                string history = Path.Combine(temp, "FocusHistory");
                string target = Path.Combine(Program.Data, "FocusHistory");
                if (Directory.Exists(history))
                {
                    if (Directory.Exists(target)) Directory.Delete(target, true);
                    CopyFolder(history, target);
                }
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        private static void CopyFolder(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string folder in Directory.GetDirectories(source))
                CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
        }
        private void SaveHudBehavior()
        {
            if (loadingPreferences) return;
            bool locked = hudLockedSwitch.Checked;
            bool pinned = hudPinnedSwitch.Checked;
            engine.UpdateHudBehavior(locked, pinned);
            if (floatingTimer != null)
            {
                floatingTimer.SetLocked(locked);
                floatingTimer.SetPinned(pinned);
            }
            ShowSaved();
        }
        private void SaveHudAccent(int index)
        {
            if (loadingPreferences) return;
            if (engine.Settings.HudAccent == index) return;
            try
            {
                engine.UpdateHudAccent(index);
                WindowPalette.HudAccentIndex = index;
                Theme(darkTheme);
                if (hudPreview != null) hudPreview.Invalidate();
                LoadPreferences();
                ShowSaved();
            }
            catch (Exception error) { Warn(error.Message); }
        }
        private void ChooseAccent(int index)
        {
            if (engine.Settings.Accent == index) return;
            try
            {
                engine.UpdateAccent(index);
                WindowPalette.AccentIndex = index;
                Theme(darkTheme);
                if (hudPreview != null) hudPreview.Invalidate();
                LoadPreferences();
                ShowSaved();
            }
            catch (Exception error) { Warn(error.Message); }
        }

        private sealed class AccentSwatch : Control
        {
            private readonly Color value;
            public bool Selected;

            public AccentSwatch(Color value)
            {
                this.value = value;
                Size = new Size(24, 24);
                Margin = new Padding(0, 0, 8, 0);
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
                TabStop = false;
                AccessibleName = "强调色";
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(value))
                    graphics.FillEllipse(fill, new Rectangle(1, 1, Width - 3, Height - 3));
                if (!Selected) return;
                using (var ring = new Pen(Color.FromArgb(235, 245, 255), 2f))
                    graphics.DrawEllipse(ring, new Rectangle(0, 0, Width - 1, Height - 1));
                using (var mark = new Pen(Color.White, 2f))
                {
                    graphics.DrawLine(mark, Width * .30f, Height * .52f, Width * .45f, Height * .66f);
                    graphics.DrawLine(mark, Width * .45f, Height * .66f, Width * .70f, Height * .36f);
                }
            }
        }
        private Control BuildHudSettings()
        {
            var content = Columns(55, 45);
            var sample = ContextCard(2, 48, -1);
            sample.Margin = new Padding(0, 0, 10, 0);
            sample.Padding = new Padding(16, 10, 16, 16);
            sample.Controls.Add(PolishedHeading("预览效果", 8), 0, 0);
            hudPreview = new HudPreview { Dock = DockStyle.Fill, AccessibleName = "悬浮计时预览" };
            sample.Controls.Add(hudPreview, 0, 1);
            content.Controls.Add(sample, 0, 0);

            var settings = RuleColumn();
            var window = Section(settings, "窗口设置", 8);
            showFloatingOnPage = new PolishedSwitch("专注开始时显示悬浮窗");
            Add(window, showFloatingOnPage);
            Add(window, Hint("窗口透明度"));
            var slider = Columns(100, 0);
            Rows(slider, -1);
            slider.Height = 41;
            slider.MinimumSize = new Size(0, 38);
            slider.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 83);
            opacity = new PolishedSlider { Minimum = 65, Maximum = 100, Value = 100, TickStyle = TickStyle.None, AutoSize = false,
                Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "悬浮窗透明度" };
            opacityValue = Label("100%", 10, "accent");
            opacityValue.TextAlign = ContentAlignment.MiddleRight;
            slider.Controls.Add(opacity, 0, 0);
            slider.Controls.Add(opacityValue, 1, 0);
            Add(window, slider);
            Add(window, Hint("窗口颜色"));
            hudFollow = new PolishedSwitch("跟随界面强调色");
            Add(window, hudFollow);
            var hudColors = Flow();
            hudColors.WrapContents = false;
            hudSwatches = new AccentSwatch[WindowPalette.AccentPresets.Length];
            for (int i = 0; i < hudSwatches.Length; i++)
            {
                int index = i;
                var swatch = new AccentSwatch(WindowPalette.AccentPresets[i]);
                swatch.Click += delegate { SaveHudAccent(index + 1); };
                hudColors.Controls.Add(swatch);
                hudSwatches[i] = swatch;
            }
            Add(window, hudColors);
            hudFollow.CheckedChanged += delegate
            {
                if (hudFollow.Checked) SaveHudAccent(0);
                else SaveHudAccent(engine.Settings.HudAccent > 0 ? engine.Settings.HudAccent : 1);
            };
            var behavior = Section(settings, "位置与操作", 6);
            Add(behavior, Hint("拖动小窗可记住位置。"));
            hudLockedSwitch = new PolishedSwitch("锁定悬浮窗位置");
            hudPinnedSwitch = new PolishedSwitch("窗口保持在最前面");
            Add(behavior, hudLockedSwitch);
            Add(behavior, hudPinnedSwitch);
            hudLockedSwitch.CheckedChanged += delegate { SaveHudBehavior(); };
            hudPinnedSwitch.CheckedChanged += delegate { SaveHudBehavior(); };
            Add(behavior, Hint("双击返回主窗口，右键查看操作。"));
            content.Controls.Add(settings, 1, 0);
            opacity.ValueChanged += delegate
            {
                opacityValue.Text = opacity.Value + "%";
                hudPreview.SetOpacity(opacity.Value);
                if (floatingTimer != null) floatingTimer.SetSteadyOpacity(opacity.Value);
            };
            opacity.MouseUp += delegate { SaveHudOpacity(); };
            opacity.KeyUp += delegate { SaveHudOpacity(); };
            opacity.Leave += delegate { SaveHudOpacity(); };
            return content;
        }

        private void LoadPreferences()
        {
            loadingPreferences = true;
            theme.SelectedIndex = engine.Settings.FollowSystemTheme ? 2 : engine.Settings.Dark ? 1 : 0;
            startup.Checked = engine.Settings.Startup;
            showFloating.Checked = engine.Settings.ShowFloatingTimer;
            showFloatingOnPage.Checked = engine.Settings.ShowFloatingTimer;
            reduceMotion.Checked = engine.Settings.ReduceMotion;
            playChimes.Checked = engine.Settings.PlayChimes;
            showNotifications.Checked = engine.Settings.ShowNotifications;
            opacity.Value = engine.Settings.EffectiveHudOpacity;
            if (accentSwatches != null)
                for (int i = 0; i < accentSwatches.Length; i++)
                {
                    accentSwatches[i].Selected = engine.Settings.Accent == i;
                    accentSwatches[i].Invalidate();
                }
            if (hudFollow != null) hudFollow.Checked = engine.Settings.HudAccent == 0;
            if (hudLockedSwitch != null) hudLockedSwitch.Checked = engine.Settings.HudLocked;
            if (hudPinnedSwitch != null) hudPinnedSwitch.Checked = engine.Settings.HudPinned;
            if (hudSwatches != null)
                for (int i = 0; i < hudSwatches.Length; i++)
                {
                    hudSwatches[i].Selected = engine.Settings.HudAccent == i + 1;
                    hudSwatches[i].Invalidate();
                }
            loadingPreferences = false;
        }

        private void SaveDisplayPreferences(bool? floating = null)
        {
            if (loadingPreferences || theme.SelectedIndex < 0) return;
            var previous = engine.Settings;
            bool show = floating ?? previous.ShowFloatingTimer;
            bool followSystem = theme.SelectedIndex == 2;
            bool dark = followSystem ? previous.Dark : theme.SelectedIndex == 1;
            try
            {
                engine.UpdateDisplayPreferences(dark, followSystem, show, reduceMotion.Checked);
            }
            catch (Exception ex)
            {
                LoadPreferences();
                Warn(ex.Message);
                return;
            }

            if (previous.ReduceMotion != reduceMotion.Checked) Motion.SetUserPreference(!reduceMotion.Checked);
            loadingPreferences = true;
            showFloating.Checked = showFloatingOnPage.Checked = show;
            loadingPreferences = false;
            RefreshSystemTheme();
            ShowSaved();
            if (previous.ShowFloatingTimer != show && engine.Session != null)
            {
                floatingSession = engine.Session;
                floatingRequested = show;
                Tick();
            }
        }

        private void SaveAlertPreferences()
        {
            if (loadingPreferences) return;
            try { engine.UpdateAlertPreferences(playChimes.Checked, showNotifications.Checked); ShowSaved(); }
            catch (Exception ex) { LoadPreferences(); Warn(ex.Message); }
        }

        private void SaveHudOpacity()
        {
            if (loadingPreferences || opacity.Value == engine.Settings.EffectiveHudOpacity) return;
            try { engine.UpdateHudOpacity(opacity.Value); ShowSaved(); }
            catch (Exception ex) { LoadPreferences(); Warn(ex.Message); }
        }

        private void SaveStartupPreference()
        {
            if (loadingPreferences || startup.Checked == engine.Settings.Startup) return;
            var previous = engine.Settings;
            try
            {
                Program.SetStartup(startup.Checked);
                try { engine.UpdateStartupPreference(startup.Checked); }
                catch { Program.SetStartup(previous.Startup); throw; }
                ShowSaved();
            }
            catch (Exception ex)
            {
                LoadPreferences();
                Warn(ex.Message);
            }
        }

        private void RefreshSystemTheme()
        {
            bool dark = Program.UsesDarkTheme(engine.Settings);
            if (dark != darkTheme) Theme(dark);
        }

        private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            if (!engine.Settings.FollowSystemTheme || IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke((Action)RefreshSystemTheme); }
            catch (InvalidOperationException) { }
        }

        private void SaveHudPosition(Point position)
        {
            try { engine.UpdateHudPosition(position.X, position.Y); }
            catch (Exception ex) { Warn("无法保存悬浮窗位置。\n" + ex.Message); }
        }

        private void ToggleFloatingTimer()
        {
            if (engine.Session == null) return;
            floatingSession = engine.Session;
            floatingRequested = !floatingRequested;
            Tick();
        }

        private void RenderFloatingTimer(Session round, bool paused, bool openEnded, double progress, int remainingStops)
        {
            if (floatingSession != round)
            {
                floatingSession = round;
                floatingRequested = round != null && engine.Settings.ShowFloatingTimer;
            }
            floatingMenu.Enabled = round != null;
            floatingMenu.Text = floatingRequested ? "隐藏悬浮计时" : "显示悬浮计时";
            if (!floatingRequested)
            {
                if (floatingTimer != null) floatingTimer.HideTimer();
                return;
            }

            if (floatingTimer == null)
            {
                Point? position = engine.Settings.HudX.HasValue && engine.Settings.HudY.HasValue
                    ? (Point?)new Point(engine.Settings.HudX.Value, engine.Settings.HudY.Value) : null;
                floatingTimer = new FloatingTimerWindow(Reveal, ToggleFloatingTimer,
                    delegate { Act(PauseOrResume, floatingTimer); },
                    delegate { Act(delegate { StopFocusWithOwner(floatingTimer); }, floatingTimer); },
                    position, SaveHudPosition, engine.Settings.HudLocked, engine.Settings.HudPinned);
                floatingTimer.BehaviorChanged = delegate(bool locked, bool pinned) { engine.UpdateHudBehavior(locked, pinned); };
                floatingTimer.ApplyPalette(palette);
                floatingTimer.SetSteadyOpacity(opacity.Value);
#if FOCUS_TEST
                floatingTimer.SuppressTransparency = Opacity == 0;
                if (floatingTimer.SuppressTransparency) floatingTimer.Opacity = 0;
#endif
            }
            floatingTimer.UpdateDisplay(clockText, round.CountUp, paused, !openEnded, progress, pause.Enabled,
                stop.Enabled && (openEnded || remainingStops > 0));
            floatingTimer.ShowTimer();
        }
    }
}
