using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private sealed class FloatingTimerWindow : Form
        {
            private readonly TableLayoutPanel layout;
            private readonly Label phase, mode, clock, detail;
            private readonly Panel stateDot;
            private readonly MeterStrip meter;
            private readonly ThemeButton pauseButton, stopButton;
            private readonly ToolTip tooltips = new ToolTip();
            private readonly Font clockFont = UiFont(27, FontStyle.Bold);
            private readonly ContextMenuStrip menu;
            private readonly ToolStripMenuItem stopItem;
            private readonly Action<Point> rememberPosition;
            private readonly object stateTransition = new object();
            private WindowPalette colors;
            private Color signal, border;
            private Point? dragOffset;
            private Point dragStart;
            private bool locked, pinned = true;
            internal Action<bool, bool> BehaviorChanged;
            private bool positioned, fadingOut, hasState, paused;
            private double steadyOpacity = 1;
#if FOCUS_TEST
            public bool SuppressTransparency { get; set; }
#endif

            public FloatingTimerWindow(Action reveal, Action hide, Action pause, Action stop,
                Point? preferredPosition, Action<Point> rememberPosition, bool locked = false, bool pinned = true)
            {
                this.rememberPosition = rememberPosition;
                this.locked = locked;
                this.pinned = pinned;
                TopMost = pinned;
                Text = "悬浮专注计时";
                StartPosition = FormStartPosition.Manual;
                FormBorderStyle = FormBorderStyle.None;
                TopMost = true;
                ShowInTaskbar = false;
                DoubleBuffered = true;
                ClientSize = new Size(310, 184);
                Padding = new Padding(1);
                ConfigureDpi(this);
                if (preferredPosition.HasValue)
                {
                    Location = preferredPosition.Value;
                    positioned = true;
                }

                menu = new ContextMenuStrip { Font = UiFont(12) };
                menu.Items.Add("显示主窗口", null, delegate { reveal(); });
                menu.Items.Add("隐藏悬浮窗", null, delegate { hide(); });
                stopItem = (ToolStripMenuItem)menu.Items.Add("提前停止", null, delegate { stop(); });
                ContextMenuStrip = menu;

                layout = Area(5);
                layout.Padding = new Padding(13, 8, 13, 8);
                Rows(layout, 32, 64, 23, 10, -1);
                var header = Columns(56, 44, 0, 0);
                header.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 30);
                header.ColumnStyles[3] = new ColumnStyle(SizeType.Absolute, 30);
                Rows(header, -1);
                var indicator = Columns(0, 100);
                Rows(indicator, -1);
                indicator.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 16);
                stateDot = new Panel { Dock = DockStyle.Fill, TabStop = false, Margin = Padding.Empty };
                stateDot.Paint += DrawStatusDot;
                phase = Label("", 10, "accent");
                indicator.Controls.Add(stateDot, 0, 0);
                indicator.Controls.Add(phase, 1, 0);
                header.Controls.Add(indicator, 0, 0);
                mode = Label("", 9, "muted");
                mode.TextAlign = ContentAlignment.MiddleRight;
                mode.Margin = new Padding(0, 0, 5, 0);
                header.Controls.Add(mode, 1, 0);
                header.Controls.Add(UtilityButton("↗", "打开主窗口", reveal), 2, 0);
                header.Controls.Add(UtilityButton("×", "隐藏悬浮窗", hide), 3, 0);
                layout.Controls.Add(header, 0, 0);

                clock = Label("", 28);
                clock.Font = clockFont;
                layout.Controls.Add(clock, 0, 1);
                detail = Label("", 9, "muted");
                layout.Controls.Add(detail, 0, 2);
                meter = new MeterStrip { Dock = DockStyle.Fill, Margin = new Padding(1, 1, 1, 1), TabStop = false };
                layout.Controls.Add(meter, 0, 3);

                var actions = Columns(100, 0, 0);
                actions.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 44);
                actions.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 44);
                actions.Padding = new Padding(0, 0, 10, 0);
                pauseButton = ActionButton("", "暂停", "primary", pause);
                stopButton = ActionButton("", "提前停止", "danger", stop);
                actions.Controls.Add(pauseButton, 1, 0);
                actions.Controls.Add(stopButton, 2, 0);
                layout.Controls.Add(actions, 0, 4);
                Controls.Add(layout);

                foreach (Control surface in new Control[] { layout, header, indicator, phase, stateDot, mode, clock, detail, meter })
                {
                    surface.Cursor = Cursors.SizeAll;
                    surface.MouseDown += BeginDrag;
                    surface.MouseMove += Drag;
                    surface.MouseUp += EndDrag;
                    surface.MouseCaptureChanged += delegate { if (!surface.Capture) dragOffset = null; };
                    surface.DoubleClick += delegate { reveal(); };
                    surface.ContextMenuStrip = menu;
                }
            }

            private static Image ActionIcon(bool stop)
            {
                var bitmap = new Bitmap(16, 16);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var brush = new SolidBrush(Color.White))
                    {
                        if (stop) graphics.FillRectangle(brush, 3, 3, 10, 10);
                        else
                        {
                            graphics.FillRectangle(brush, 3, 2, 4, 12);
                            graphics.FillRectangle(brush, 9, 2, 4, 12);
                        }
                    }
                }
                return bitmap;
            }
            private ThemeButton ActionButton(string symbol, string name, string role, Action action)
            {
                var button = (ThemeButton)Button("", 40, delegate { action(); });
                button.Size = new Size(40, 26);
                button.MinimumSize = button.MaximumSize = new Size(40, 26);
                button.Anchor = AnchorStyles.None;
                button.Margin = new Padding(6, 0, 0, 0);
                button.CornerOverride = 10;
                button.Image = ActionIcon(role == "danger");
                button.ImageAlign = ContentAlignment.MiddleCenter;
                button.Tag = role;
                button.AccessibleName = name;
                tooltips.SetToolTip(button, name);
                return button;
            }

            private ThemeButton lockButton, pinButton;

            private Button UtilityButton(string symbol, string name, Action action)
            {
                var button = Button(symbol, 30, delegate { action(); });
                button.Dock = DockStyle.Fill;
                button.Margin = Padding.Empty;
                button.Tag = "link";
                button.AccessibleName = name;
                tooltips.SetToolTip(button, name);
                return button;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    const int toolWindow = 0x80, noActivate = 0x08000000;
                    var parameters = base.CreateParams;
                    parameters.ExStyle |= toolWindow | noActivate;
                    return parameters;
                }
            }

            public void ApplyPalette(WindowPalette palette)
            {
                colors = palette;
                BackColor = colors.Card;
                border = colors.Outline;
                PaintTree(layout, BackColor, colors);
                if (stopButton != null)
                {
                    stopButton.BackColor = Color.FromArgb(230, 32, 32);
                    stopButton.FlatAppearance.BorderColor = stopButton.BackColor;
                    stopButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 24, 24);
                }
                menu.Renderer = new ToolStripProfessionalRenderer(new TrayColors(colors.Card, colors.Outline, colors.Subtle)) { RoundedEdges = false };
                menu.BackColor = colors.Card;
                foreach (ToolStripItem item in menu.Items) item.ForeColor = colors.Ink;
                Motion.Stop(stateTransition);
                SetSignal(StateColor(paused));
                meter.TrackColor = colors.Outline;
                Invalidate();
            }

            private Color StateColor(bool paused)
            {
                return paused ? colors.Paused : colors.HudAccent;
            }

            private void SetSignal(Color color)
            {
                signal = color;
                phase.ForeColor = color;
                meter.SignalColor = color;
                stateDot.Invalidate();
            }

            public void UpdateDisplay(string time, bool countUp, bool isPaused, bool bounded, double progress,
                bool canPause, bool canStop)
            {
                bool changed = hasState && paused != isPaused;
                if (!hasState || changed)
                {
                    hasState = true;
                    paused = isPaused;
                    var from = signal;
                    var to = StateColor(paused);
                    if (changed && Visible)
                        Motion.Run(stateTransition, 167, delegate(float t) { SetSignal(Motion.Blend(from, to, t)); });
                    else SetSignal(to);
                }
                string caption = isPaused ? "暂停中" : "专注中";
                if (phase.Text != caption) phase.Text = caption;
                string timerMode = countUp ? "正计时" : "倒计时";
                if (mode.Text != timerMode) mode.Text = timerMode;
                string displayDetail = isPaused ? "暂停剩余" : countUp ? "已专注" : "专注剩余";
                if (clock.Text != time || detail.Text != displayDetail)
                {
                    detail.Text = displayDetail;
                    clock.Text = time;
                    clock.AccessibleName = displayDetail + " " + time;
                }
                pauseButton.Enabled = canPause;
                stopButton.Enabled = canStop;
                string pauseName = isPaused ? "继续专注" : "暂停";
                string stopName = bounded ? "提前停止" : "结束";
                UpdateAction(pauseButton, "", pauseName);
                UpdateAction(stopButton, "", stopName);
                if (stopItem.Text != stopName) stopItem.Text = stopName;
                stopItem.Enabled = canStop;
                meter.SetProgress(bounded, progress);
            }

            private void UpdateAction(Button button, string symbol, string name)
            {
                if (button.Text != symbol) button.Text = symbol;
                if (button.AccessibleName == name) return;
                button.AccessibleName = name;
                tooltips.SetToolTip(button, name);
            }

            private bool AnimateWindow
            {
                get
                {
#if FOCUS_TEST
                    if (SuppressTransparency) return false;
#endif
                    return Motion.Enabled;
                }
            }

            public void SetSteadyOpacity(int percent)
            {
                double next = percent / 100.0;
                if (steadyOpacity == next) return;
                steadyOpacity = next;
#if FOCUS_TEST
                if (SuppressTransparency) return;
#endif
                if (Visible && !fadingOut && !Motion.IsRunning(this)) Opacity = steadyOpacity;
            }

            public void ShowTimer()
            {
                if (fadingOut)
                {
                    Motion.Stop(this);
                    fadingOut = false;
                    Opacity = steadyOpacity;
                }
                if (Visible) return;
                var monitor = positioned ? Screen.FromRectangle(Bounds) : Screen.FromPoint(Cursor.Position);
                if (!positioned)
                {
                    Location = new Point(monitor.WorkingArea.Right - Width - 20, monitor.WorkingArea.Top + 20);
                    positioned = true;
                }
                KeepInside(monitor.WorkingArea);
                if (!AnimateWindow)
                {
#if FOCUS_TEST
                    if (!SuppressTransparency)
#endif
                        Opacity = steadyOpacity;
                    Show();
                    KeepInside(monitor.WorkingArea);
                    return;
                }
                Opacity = 0;
                Show();
                KeepInside(monitor.WorkingArea);
                var target = Location;
                int travel = Math.Max(1, (int)Math.Round(6 * DeviceDpi / 96f));
                int fromY = Math.Max(monitor.WorkingArea.Top, target.Y - travel);
                MoveWithoutActivation(new Point(target.X, fromY));
                Motion.Run(this, 167, delegate(float t)
                {
                    Opacity = t * steadyOpacity;
                    MoveWithoutActivation(new Point(target.X, fromY + (int)Math.Round((target.Y - fromY) * t)));
                });
            }

            public void HideTimer()
            {
                if (!Visible || fadingOut) return;
                Motion.Stop(this);
                Motion.Stop(stateTransition);
                if (hasState) SetSignal(StateColor(paused));
                if (!AnimateWindow)
                {
                    Hide();
                    return;
                }
                fadingOut = true;
                double from = Opacity;
                Motion.Run(this, 83, delegate(float t) { Opacity = from * (1 - t); }, delegate
                {
                    Hide();
                    fadingOut = false;
                    Opacity = steadyOpacity;
                });
            }

            internal void SetLocked(bool value) { locked = value; }
            internal void SetPinned(bool value) { pinned = value; TopMost = value; }

            private void NotifyBehavior()
            {
                if (BehaviorChanged != null) BehaviorChanged(locked, pinned);
            }

            private void UpdateToggle(ThemeButton button, bool on)
            {
                if (button == null) return;
                button.ForeColor = on ? colors.Accent : colors.Muted;
                button.AccessibleDescription = on ? "已开启" : "已关闭";
            }

            private ThemeButton ToggleButton(string text, string name, Action action)
            {
                var button = (ThemeButton)UtilityButton(text, name, action);
                button.Font = UiFont(9, FontStyle.Bold);
                button.ForeColor = colors == null ? Color.White : colors.Muted;
                return button;
            }

            private void BeginDrag(object sender, MouseEventArgs e)
            {
                if (locked) return;
                if (e.Button != MouseButtons.Left || fadingOut) return;
                Motion.Stop(this);
                if (AnimateWindow && Opacity < steadyOpacity) Opacity = steadyOpacity;
                var pointer = Cursor.Position;
                dragStart = Location;
                dragOffset = new Point(pointer.X - Left, pointer.Y - Top);
                ((Control)sender).Capture = true;
            }

            private void Drag(object sender, MouseEventArgs e)
            {
                if (!dragOffset.HasValue) return;
                var pointer = Cursor.Position;
                MoveWithoutActivation(new Point(pointer.X - dragOffset.Value.X, pointer.Y - dragOffset.Value.Y));
            }

            private void EndDrag(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || !dragOffset.HasValue) return;
                dragOffset = null;
                ((Control)sender).Capture = false;
                KeepInside(Screen.FromPoint(Cursor.Position).WorkingArea);
                if (Location != dragStart && rememberPosition != null) rememberPosition(Location);
            }

            private void KeepInside(Rectangle area)
            {
                MoveWithoutActivation(new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                    Math.Max(area.Top, Math.Min(Top, area.Bottom - Height))));
            }

            // Use the same no-activation window movement pattern as PowerToys' Always On Top overlay.
            // https://github.com/microsoft/PowerToys/blob/b3b01fcc0a059e58433bc1e15e196abff5f76f25/src/modules/alwaysontop/AlwaysOnTop/WindowBorder.cpp
            private void MoveWithoutActivation(Point target)
            {
                if (target == Location) return;
                const uint noSize = 0x0001, noZOrder = 0x0004, noActivate = 0x0010;
                if (!IsHandleCreated || !SetWindowPos(Handle, IntPtr.Zero, target.X, target.Y, 0, 0, noSize | noZOrder | noActivate))
                    Location = target;
            }

            [DllImport("user32.dll")]
            private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                const int displayChanged = 0x007E, settingChanged = 0x001A;
                if (Visible && (message.Msg == displayChanged || message.Msg == settingChanged))
                    KeepInside(Screen.FromRectangle(Bounds).WorkingArea);
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (Width < 2 || Height < 2) return;
                using (var path = RoundedPath(new Rectangle(0, 0, Width, Height), (int)Math.Round(13 * DeviceDpi / 96f)))
                {
                    var old = Region;
                    Region = new Region(path);
                    if (old != null) old.Dispose();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (border.IsEmpty) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), (int)Math.Round(13 * DeviceDpi / 96f)))
                using (var pen = new Pen(border))
                    e.Graphics.DrawPath(pen, path);
            }

            private void DrawStatusDot(object sender, PaintEventArgs e)
            {
                float diameter = 7 * e.Graphics.DpiX / 96f;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(signal))
                    e.Graphics.FillEllipse(brush, 2, (stateDot.Height - diameter) / 2, diameter, diameter);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Motion.Stop(this);
                    Motion.Stop(stateTransition);
                    tooltips.Dispose();
                    menu.Dispose();
                }
                base.Dispose(disposing);
                if (disposing) clockFont.Dispose();
            }

            private sealed class MeterStrip : Control
            {
                private Color signalColor, trackColor;
                private bool bounded;
                private double progress;

                public MeterStrip() { DoubleBuffered = true; }

                public Color SignalColor
                {
                    set { if (signalColor != value) { signalColor = value; Invalidate(); } }
                }

                public Color TrackColor
                {
                    set { if (trackColor != value) { trackColor = value; Invalidate(); } }
                }

                public void SetProgress(bool showProgress, double value)
                {
                    double next = showProgress ? Math.Max(0, Math.Min(value, 1)) : 0;
                    if (bounded == showProgress && progress == next) return;
                    bounded = showProgress;
                    progress = next;
                    Invalidate();
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    int thickness = bounded ? Math.Max(2, (int)Math.Round(4 * e.Graphics.DpiX / 96f)) : 1;
                    var track = new Rectangle(0, (Height - thickness) / 2, Width, thickness);
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var brush = new SolidBrush(trackColor))
                    using (var path = RoundedPath(track, thickness / 2))
                        e.Graphics.FillPath(brush, path);
                    int marked = (int)Math.Round(track.Width * progress);
                    if (marked <= 0) return;
                    using (var brush = new SolidBrush(signalColor))
                    using (var path = RoundedPath(new Rectangle(0, track.Y, marked, thickness), thickness / 2))
                        e.Graphics.FillPath(brush, path);
                }
            }
        }
    }
}