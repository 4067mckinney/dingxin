using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private sealed class WindowPalette
        {
            public readonly bool Dark;
            public readonly Color Background, Card, Field, Ink, Muted, Accent, Subtle, Outline, PrimaryFill, PrimaryInk;
            public readonly Color Signal, Paused, Danger, DangerFill, SetupRail, HudAccent;

            public static int AccentIndex;
            public static int HudAccentIndex = 0;
            internal static readonly Color[] AccentPresets =
            {
                ColorTranslator.FromHtml("#3B82F6"), ColorTranslator.FromHtml("#06B6D4"),
                ColorTranslator.FromHtml("#8B5CF6"), ColorTranslator.FromHtml("#22C55E"),
                ColorTranslator.FromHtml("#F59E0B"), ColorTranslator.FromHtml("#EC4899")
            };

            public WindowPalette(bool dark, bool polished = false)
            {
                Dark = dark;
                Background = ColorTranslator.FromHtml(dark ? "#101827" : "#EEF3FA");
                Card = ColorTranslator.FromHtml(dark ? "#192438" : "#FFFFFF");
                Ink = ColorTranslator.FromHtml(dark ? "#F2F6FF" : "#14161B");
                Muted = ColorTranslator.FromHtml(dark ? "#B4C1D6" : "#4E5B70");
                Accent = ColorTranslator.FromHtml(dark ? "#81BBFF" : "#034D92");
                Subtle = Motion.Blend(Card, Accent, 0.13f);
                Outline = ColorTranslator.FromHtml(dark ? "#334461" : "#DCE4F0");
                PrimaryFill = dark ? ColorTranslator.FromHtml("#285FC6") : Accent;
                PrimaryInk = Color.White;
                Signal = Accent;
                Paused = ColorTranslator.FromHtml(dark ? "#F5CB7D" : "#80530A");
                Danger = ColorTranslator.FromHtml(dark ? "#FFB2AF" : "#98272D");
                DangerFill = ColorTranslator.FromHtml(dark ? "#341A1A" : "#FCEBED");
                SetupRail = ColorTranslator.FromHtml(dark ? "#142039" : "#EAF4F8");
                Field = ColorTranslator.FromHtml(dark ? "#121D30" : "#F2F7FD");
                if (polished && !dark)
                {
                    Background = ColorTranslator.FromHtml("#EDF3FA");
                    Ink = ColorTranslator.FromHtml("#172841");
                    Muted = ColorTranslator.FromHtml("#485B76");
                    Accent = ColorTranslator.FromHtml("#1556AF");
                    Subtle = ColorTranslator.FromHtml("#E8F1FE");
                    Outline = ColorTranslator.FromHtml("#CBDAED");
                    PrimaryFill = ColorTranslator.FromHtml("#1E5DC4");
                }
                if (AccentIndex > 0 && AccentIndex < AccentPresets.Length)
                {
                    Color preset = AccentPresets[AccentIndex];
                    Accent = dark ? Motion.Blend(preset, Color.White, .30f) : Motion.Blend(preset, Color.Black, .12f);
                    PrimaryFill = dark ? Motion.Blend(preset, Color.Black, .12f) : Motion.Blend(preset, Color.Black, .28f);
                    Subtle = Motion.Blend(Card, Accent, dark ? .16f : .13f);
                    Signal = Accent;
                }
                HudAccent = Accent;
                if (HudAccentIndex > 0 && HudAccentIndex <= AccentPresets.Length)
                {
                    Color hudPreset = AccentPresets[HudAccentIndex - 1];
                    HudAccent = dark ? Motion.Blend(hudPreset, Color.White, .22f) : Motion.Blend(hudPreset, Color.Black, .10f);
                }
            }
        }

        internal sealed class SetupWindow : Form
        {
            private readonly WindowPalette colors;
            private readonly Card rail, details;
            private readonly TableLayoutPanel actions;
            private readonly TextBox location;
            private readonly Font bodyFont, titleFont, headingFont, versionFont;
            private readonly Icon windowIcon;
            private CheckBox startup;

            private SetupWindow(string operation, string title, string explanation, string sectionTitle,
                bool dark, Version version, Version previous = null)
            {
                colors = new WindowPalette(dark);
                bodyFont = UiFont(11.5f);
                titleFont = UiFont(22, FontStyle.Bold);
                headingFont = UiFont(15, FontStyle.Bold);
                versionFont = UiFont(11);
                Font = bodyFont;

                Text = Program.Name + " v" + version.ToString(3) + " · " + operation;
                windowIcon = Icon.ExtractAssociatedIcon(ProcessGuard.ApplicationPath());
                Icon = windowIcon ?? SystemIcons.Application;
                AccessibleName = title;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(696, 368);
                ConfigureDpi(this);

                var shell = Columns(50, 50);
                shell.ColumnStyles[0] = new ColumnStyle(SizeType.Absolute, 224);
                rail = Area(6);
                rail.Padding = new Padding(24, 26, 22, 26);
                Rows(rail, 41, 62, 42, -1, 24, previous == null ? 26 : 52);
                rail.Controls.Add(BrandHeading(false), 0, 0);
                rail.Controls.Add(Copy(title, font: titleFont), 0, 1);
                rail.Controls.Add(Copy(explanation, "muted"), 0, 2);
                rail.Controls.Add(new SideHorizon { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);
                string versionText = "v" + version.ToString(3);
                rail.Controls.Add(Copy(previous == null ? "程序版本" : "版本变更", "muted"), 0, 4);
                var versionLabel = Copy(previous == null ? versionText : "v" + previous.ToString(3) + "\n→ " + versionText, "accent", versionFont);
                versionLabel.AccessibleName = "程序版本 " + versionLabel.Text.Replace('\n', ' ');
                rail.Controls.Add(versionLabel, 0, 5);
                shell.Controls.Add(rail, 0, 0);

                var body = Area(3);
                body.Padding = new Padding(24);
                Rows(body, 44, -1, 40);
                body.Controls.Add(Copy(sectionTitle, font: headingFont), 0, 0);
                details = Area(6);
                details.Tag = "card";
                details.Dock = DockStyle.Top;
                details.AutoSize = true;
                details.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                details.Padding = new Padding(14);
                details.Margin = new Padding(0, 0, 0, 8);
                body.Controls.Add(details, 0, 1);
                location = new TextBox
                {
                    ReadOnly = true, AccessibleName = "完整安装位置", BorderStyle = BorderStyle.FixedSingle,
                    Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3)
                };

                actions = Columns(50, 25, 25);
                actions.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 98);
                actions.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 124);
                body.Controls.Add(actions, 0, 2);
                shell.Controls.Add(body, 1, 0);
                Controls.Add(shell);
            }

            internal string Destination { get { return location.Text; } }
            internal bool StartupEnabled { get { return startup != null && startup.Checked; } }

            internal static SetupWindow Install(string[] locations, string recommended, Version previous, Version version,
                bool dark, bool startupEnabled)
            {
                bool upgrade = previous != null;
                var window = new SetupWindow("安装", upgrade ? "覆盖升级" : "首次安装",
                    upgrade ? "更新完成后打开。" : "安装完成后打开。",
                    "选择安装位置", dark, version, previous);
                window.ClientSize = new Size(696, 381);
                Rows(window.details, 39, 24, 62, 14, upgrade ? 26 : 38, 33);
                window.location.Multiline = true;
                var choices = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "安装位置",
                    FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed, Margin = new Padding(0, 5, 0, 0)
                };
                choices.DrawItem += window.DrawLocation;
                for (int i = 0; i < locations.Length; i++)
                {
                    string path = locations[i];
                    string label = upgrade && i == 0 ? "保持当前安装位置"
                        : string.Equals(path, recommended, StringComparison.OrdinalIgnoreCase) ? "当前用户目录（推荐）" : path;
                    choices.Items.Add(new InstallLocation(label, path));
                }
                choices.SelectedIndexChanged += delegate
                {
                    var picked = choices.SelectedItem as InstallLocation;
                    if (picked != null) window.location.Text = picked.Path;
                };
                choices.SelectedIndex = 0;
                window.details.Controls.Add(choices, 0, 0);
                window.details.Controls.Add(window.Copy("完整安装路径", "muted"), 0, 1);
                var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
                pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
                window.location.Dock = DockStyle.Fill;
                pathRow.Controls.Add(window.location, 0, 0);
                var browse = new Button
                {
                    Text = "浏览…", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = new Padding(6, 0, 0, 0),
                    BackColor = window.colors.Background, ForeColor = window.colors.Ink
                };
                browse.FlatAppearance.BorderColor = window.colors.Outline;
                browse.Click += delegate
                {
                    using (var picker = new FolderBrowserDialog())
                    {
                        picker.Description = "选择安装位置";
                        picker.SelectedPath = window.location.Text;
                        if (picker.ShowDialog(window) != DialogResult.OK) return;
                        window.location.Text = Path.Combine(picker.SelectedPath, Program.Name);
                        choices.SelectedIndex = -1;
                    }
                };
                pathRow.Controls.Add(browse, 1, 0);
                window.details.Controls.Add(pathRow, 0, 2);
                window.AddDivider(3);
                if (upgrade)
                {
                    window.details.Controls.Add(window.Copy("将保留设置、记录和当前专注进度", "accent"), 0, 4);
                    var changes = window.Copy("更名为" + Program.Name + "，更新图标与界面细节。", "muted");
                    changes.TextAlign = ContentAlignment.TopLeft;
                    window.details.Controls.Add(changes, 0, 5);
                }
                else
                {
                    window.startup = Check("登录 Windows 后自动启动");
                    window.startup.Checked = startupEnabled;
                    window.startup.Anchor = AnchorStyles.Left;
                    window.startup.Margin = Padding.Empty;
                    window.details.Controls.Add(window.startup, 0, 4);
                    var local = window.Copy("设置和专注记录保存在本机。", "muted");
                    local.TextAlign = ContentAlignment.TopLeft;
                    window.details.Controls.Add(local, 0, 5);
                }
                window.SetActions(upgrade ? "安装新版" : "安装", false);
                window.ActiveControl = choices;
                return window;
            }

            private sealed class InstallLocation
            {
                public readonly string Title, Path;
                public InstallLocation(string title, string path) { Title = title; Path = path; }
                public override string ToString() { return Title; }
            }

            internal static bool ConfirmDeleteData(IWin32Window owner, bool dark, Version version, out bool deleteData)
        {
            deleteData = false;
            using (var window = new SetupWindow("卸载", "再次确认", "", "最后确认", dark, version))
            {
                window.ClientSize = new Size(696, 336);
                Rows(window.details, 46, 24, 38, 0, 0, 0);
                window.details.Controls.Add(window.Copy("卸载后无法恢复，安装目录会被清空。"), 0, 0);
                var choice = new PolishedSwitch("同时删除个人数据（设置、白名单、专注记录）");
                ((MainWindow.IPolishedControl)choice).ApplyColors(window.colors);
                window.details.Controls.Add(choice, 0, 2);
                window.SetActions("确认卸载", true);
                if (window.ShowOwned(owner) != DialogResult.OK) return false;
                deleteData = choice.Checked;
                return true;
            }
        }
        internal static SetupWindow Uninstall(string path, bool dark, Version version)
            {
                var window = new SetupWindow("卸载", "卸载确认", "程序将从电脑移除。", "卸载影响", dark, version);
                Rows(window.details, 24, 37, 15, 52, 12, 54);
                window.location.Text = path;
                window.details.Controls.Add(window.Copy("原安装位置", "muted"), 0, 0);
                window.details.Controls.Add(window.location, 0, 1);
                window.AddDivider(2);
                window.details.Controls.Add(window.Describe("将移除", "程序、开始菜单快捷方式、开机启动项", "muted"), 0, 3);
                window.details.Controls.Add(window.Describe("会保留", "白名单、自动开始计划、专注记录、Edge 浏览数据", "accent"), 0, 5);
                window.SetActions("确认卸载", true);
                return window;
            }

            internal static void Notice(IWin32Window owner, string operation, string heading, string message, bool dark, Version version)
            {
                using (var window = new SetupWindow(operation, operation, "", heading, dark, version))
                {
                    window.ClientSize = new Size(696, 308);
                    Rows(window.details, 84, 0, 0, 0, 0, 0);
                    window.details.Controls.Add(window.Copy(message), 0, 0);
                    window.SetActions("关闭", false, false);
                    window.ShowOwned(owner);
                }
            }

            private Label Copy(string value, string role = null, Font font = null)
            {
                return new ThemeLabel
                {
                    Text = value, Tag = role, Font = font ?? bodyFont, Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, UseMnemonic = false
                };
            }

            private void AddDivider(int row)
            {
                var divider = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
                divider.Paint += delegate(object sender, PaintEventArgs e)
                {
                    using (var line = new Pen(colors.Outline))
                        e.Graphics.DrawLine(line, 0, divider.Height / 2, divider.Width, divider.Height / 2);
                };
                details.Controls.Add(divider, 0, row);
            }

            private Control Describe(string title, string text, string role)
            {
                var row = Area(2);
                Rows(row, 24, -1);
                row.Controls.Add(Copy(title, role), 0, 0);
                var description = Copy(text);
                description.TextAlign = ContentAlignment.TopLeft;
                row.Controls.Add(description, 0, 1);
                return row;
            }

            private void SetActions(string title, bool destructive, bool showCancel = true)
            {
                var confirm = Button(title, 124, null, !destructive);
                confirm.Font = bodyFont;
                confirm.Dock = DockStyle.Fill;
                confirm.Margin = Padding.Empty;
                confirm.DialogResult = DialogResult.OK;
                actions.Controls.Add(confirm, 2, 0);

                if (showCancel)
                {
                    var cancel = Button("取消", 88, null);
                    cancel.Font = bodyFont;
                    cancel.Dock = DockStyle.Fill;
                    cancel.Margin = new Padding(0, 0, 10, 0);
                    cancel.DialogResult = DialogResult.Cancel;
                    actions.Controls.Add(cancel, 1, 0);
                    CancelButton = cancel;
                }
                else CancelButton = confirm;
                AcceptButton = destructive ? CancelButton : confirm;

                PaintTree(this, colors.Background, colors);
                PaintTree(rail, colors.SetupRail, colors);
                if (!destructive) return;
                ActiveControl = (Control)CancelButton;
                confirm.BackColor = colors.DangerFill;
                confirm.ForeColor = colors.Danger;
                confirm.FlatAppearance.BorderColor = confirm.ForeColor;
                confirm.FlatAppearance.MouseOverBackColor = Motion.Blend(confirm.BackColor, confirm.ForeColor, 0.12f);
            }

            protected override void Dispose(bool disposing)
            {
                bool release = disposing && !IsDisposed;
                base.Dispose(disposing);
                if (!release) return;
                bodyFont.Dispose();
                titleFont.Dispose();
                headingFont.Dispose();
                versionFont.Dispose();
                if (windowIcon != null) windowIcon.Dispose();
            }

            private void DrawLocation(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                var drive = (ComboBox)sender;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var fill = new SolidBrush(selected ? colors.Subtle : colors.Background))
                    e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, drive.Items[e.Index].ToString(), e.Font, e.Bounds, colors.Ink,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            }

            internal DialogResult ShowOwned(IWin32Window owner)
            {
                if (owner != null) return ShowDialog(owner);
                StartPosition = FormStartPosition.CenterScreen;
                ShowInTaskbar = true;
                return ShowDialog();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                ApplyCaption(this, colors);
            }
        }
    }
}
