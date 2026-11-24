
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private const int CornerRadius = 12;
        private const int ButtonRadius = 8;
        private static readonly string UiFontFamily = PickFont();

        private static string PickFont()
        {
            string[] candidates = { "Microsoft YaHei UI", "Noto Sans SC", "MiSans", "HarmonyOS Sans SC" };
            foreach (string name in candidates)
            {
                using (var probe = new Font(name, 10f))
                    if (string.Equals(probe.Name, name, StringComparison.OrdinalIgnoreCase)) return name;
            }
            return "Microsoft YaHei UI";
        }

        private static float TypeSize(float size)
        {
            if (size <= 9.5f) return 9f;
            if (size <= 12.5f) return 12f;
            if (size <= 16.5f) return 15f;
            if (size <= 20f) return 18f;
            if (size <= 25.5f) return 24f;
            return 27f;
        }

        private static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(UiFontFamily, size, style);
        }

        private static Card Area(int rows)
        {
            var result = new Card { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, Margin = Padding.Empty };
            result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return result;
        }

        internal static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            if (bounds.Width <= diameter || bounds.Height <= diameter)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr handle, string appName, string idList);

        internal sealed class Card : TableLayoutPanel
        {
            public Color BorderColor { get; set; }

            public Card()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (!(Tag as string == "card"))
                {
                    base.OnPaintBackground(e);
                    return;
                }
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var behind = new SolidBrush(Parent == null ? BackColor : Parent.BackColor))
                    graphics.FillRectangle(behind, ClientRectangle);
                var bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                using (var path = RoundedPath(bounds, (int)Math.Round(CornerRadius * graphics.DpiX / 96.0)))
                {
                    using (var fill = new SolidBrush(BackColor)) graphics.FillPath(fill, path);
                    if (!BorderColor.IsEmpty)
                        using (var pen = new Pen(BorderColor)) graphics.DrawPath(pen, path);
                }
            }
        }

        private static TableLayoutPanel Columns(params int[] sizes)
        {
            var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = sizes.Length, RowCount = 1, Margin = Padding.Empty };
            foreach (int size in sizes) result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, size));
            return result;
        }

        private static void Rows(TableLayoutPanel target, params int[] sizes)
        {
            foreach (int size in sizes)
                target.RowStyles.Add(size < 0 ? new RowStyle(SizeType.Percent, -size) : new RowStyle(SizeType.Absolute, size));
        }

        private static FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty, WrapContents = true
            };
        }

        private static FlowLayoutPanel Fields(string name, Control field, string next = null, Control nextField = null)
        {
            var row = Flow();
            row.WrapContents = false;
            row.Controls.Add(Hint(name));
            field.AccessibleName = name;
            var number = field as NumberField;
            if (number != null) number.SetLabel(name);
            field.Margin = new Padding(4, 3, 11, 3);
            row.Controls.Add(field);
            if (nextField != null)
            {
                row.Controls.Add(Hint(next));
                nextField.AccessibleName = next;
                var nextNumber = nextField as NumberField;
                if (nextNumber != null) nextNumber.SetLabel(next);
                nextField.Margin = field.Margin;
                row.Controls.Add(nextField);
            }
            return row;
        }

        private static void Add(TableLayoutPanel table, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(control, 0, row);
        }

        private static TableLayoutPanel RuleColumn()
        {
            var column = new TableLayoutPanel
            {
                Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
            };
            column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return column;
        }

        private static TableLayoutPanel Section(TableLayoutPanel column, string title, int glyph = -1)
        {
            var section = new Card
            {
                Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 14), Padding = new Padding(20, 14, 20, 16), Tag = "card"
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (glyph >= 0)
            {
                int row = section.RowCount++;
                section.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
                section.Controls.Add(PolishedHeading(title, glyph), 0, row);
            }
            else
            {
                var heading = Hint(title);
                heading.Font = UiFont(13);
                heading.Tag = "title";
                Add(section, heading);
            }
            section.SizeChanged += delegate
            {
                int width = Math.Max(100, section.ClientSize.Width - section.Padding.Horizontal);
                foreach (Control child in section.Controls)
                    if (child is Label) child.MaximumSize = new Size(width, 0);
            };
            Add(column, section);
            return section;
        }

        private static Label Label(string text, float size, string tag = null)
        {
            return new ThemeLabel
            {
                Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, Margin = Padding.Empty, Tag = tag,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiFont(TypeSize(size), tag == "title" || tag == "metric" ? FontStyle.Bold : FontStyle.Regular)
            };
        }

        internal static class Typography
        {
            internal static string Space(string text)
            {
                if (string.IsNullOrEmpty(text)) return text;
                bool changed = false;
                var builder = new System.Text.StringBuilder(text.Length + 8);
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (i > 0 && NeedsSpace(text[i - 1], c)) { builder.Append(' '); changed = true; }
                    builder.Append(c);
                }
                return changed ? builder.ToString() : text;
            }

            private static bool NeedsSpace(char left, char right)
            {
                return (Cjk(left) && Latin(right)) || (Latin(left) && Cjk(right));
            }

            private static bool Cjk(char c)
            {
                return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF);
            }

            private static bool Latin(char c)
            {
                return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            }
        }

        private static class DialogList
        {
            internal static void Attach(ListView list, WindowPalette colors)
            {
                list.OwnerDraw = true;
                Action skin = delegate
                {
                    if (list.IsHandleCreated) SetWindowTheme(list.Handle, colors.Dark ? "DarkMode_Explorer" : "Explorer", null);
                };
                list.HandleCreated += delegate { skin(); };
                skin();
                list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e)
                {
                    if (e.ItemIndex < 0) return;
                    var background = e.Item.Selected ? colors.Subtle
                        : e.ItemIndex % 2 == 0 ? colors.Card : Motion.Blend(colors.Card, colors.Field, .5f);
                    using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.Bounds);
                    if (list.CheckBoxes && list.Columns.Count > 0)
                        DrawSwitch(e.Graphics, new Rectangle(e.Bounds.Left + 8, e.Bounds.Top + (e.Bounds.Height - 18) / 2, 36, 18),
                            e.Item.Checked, colors);
                    int x = e.Bounds.Left;
                    for (int column = 0; column < list.Columns.Count; column++)
                    {
                        int width = list.Columns[column].Width;
                        if (list.Columns.Count == 1) width = Math.Max(width, list.ClientSize.Width - (x - e.Bounds.Left) - 2);
                        var cell = new Rectangle(x, e.Bounds.Top, width, e.Bounds.Height);
                        var text = Rectangle.Inflate(cell, -10, 0);
                        if (column == 0 && list.CheckBoxes) text.X += 50;
                        if (column == 0 && list.SmallImageList != null)
                        {
                            int index = e.Item.ImageIndex;
                            if (index >= 0 && index < list.SmallImageList.Images.Count)
                            {
                                var image = list.SmallImageList.Images[index];
                                e.Graphics.DrawImage(image, text.Left, text.Top + (text.Height - image.Height) / 2, image.Width, image.Height);
                                text.X += image.Width + 8;
                                text.Width -= image.Width + 8;
                            }
                        }
                        TextRenderer.DrawText(e.Graphics, e.Item.SubItems[column].Text, list.Font, text, colors.Ink,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                        x += width;
                    }
                };
                list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e) { e.DrawDefault = false; };
                list.MouseClick += delegate(object sender, MouseEventArgs e)
                {
                    if (!list.CheckBoxes || e.Button != MouseButtons.Left) return;
                    var hit = list.HitTest(e.Location);
                    if (hit.Item == null) return;
                    if ((hit.Location & ListViewHitTestLocations.StateImage) != 0) return;
                    if (e.X - hit.Item.Bounds.Left > 48) return;
                    hit.Item.Checked = !hit.Item.Checked;
                };
                list.Invalidate();
            }

            private static void DrawSwitch(Graphics graphics, Rectangle box, bool on, WindowPalette colors)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int radius = Math.Max(2, box.Height / 2 - 1);
                using (var path = RoundedPath(box, radius))
                using (var fill = new SolidBrush(on ? colors.PrimaryFill : colors.Field))
                    graphics.FillPath(fill, path);
                using (var pen = new Pen(on ? colors.PrimaryFill : colors.Outline, 1.4f))
                using (var path = RoundedPath(box, radius))
                    graphics.DrawPath(pen, path);
                int size = box.Height - 4;
                int x = on ? box.Right - size - 2 : box.Left + 2;
                using (var knob = new SolidBrush(on ? colors.PrimaryInk : colors.Muted))
                    graphics.FillEllipse(knob, x, box.Top + 2, size, size);
            }
            private static void DrawCheck(Graphics graphics, Rectangle box, bool selected, WindowPalette colors)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(selected ? colors.PrimaryFill : colors.Field))
                    graphics.FillRectangle(fill, box);
                using (var pen = new Pen(selected ? colors.PrimaryFill : colors.Outline, 1.4f))
                    graphics.DrawRectangle(pen, box);
                if (!selected) return;
                using (var mark = new Pen(colors.PrimaryInk, 2f))
                {
                    graphics.DrawLine(mark, box.Left + 3, box.Top + 7, box.Left + 6, box.Top + 10);
                    graphics.DrawLine(mark, box.Left + 6, box.Top + 10, box.Right - 3, box.Top + 3);
                }
            }
        }
        private sealed class ThemeLabel : Label
        {
            public override string Text
            {
                get { return base.Text; }
                set { base.Text = Typography.Space(value); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (Enabled) { base.OnPaint(e); return; }
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private static Label Hint(string text)
        {
            var label = Label(text, 9, "muted");
            label.AutoSize = true;
            label.Dock = DockStyle.None;
            label.Margin = new Padding(0, 6, 5, 6);
            label.MaximumSize = new Size(610, 0);
            return label;
        }

        private static Button Button(string text, int width, EventHandler handler, bool primary = false)
        {
            int measured = TextRenderer.MeasureText(text, UiFont(12)).Width + 36;
            if (width < measured) width = measured;
            var button = new ThemeButton
            {
                Text = text, Width = width, Height = 36, Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand,
                Tag = primary ? "primary" : "" , Font = UiFont(10.5f)
            };
            button.Click += handler;
            return button;
        }

        private sealed class ThemeButton : Button
        {
            public Color DisabledInk { get; set; }
            public Color DisabledFill { get; set; }
            public Color LinkInk { get; set; }
            public Color LinkHoverInk { get; set; }
            public Color SelectionMark { get; set; }
            public Color Underline { get; set; }
            public bool Gradient { get; set; }
            public int CornerOverride { get; set; }
            public bool FitIconToText { get; set; }
            private float hoverAmount;

            public ThemeButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e) { AnimateHover(1f); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { AnimateHover(0f); base.OnMouseLeave(e); }
            protected override void OnVisibleChanged(EventArgs e)
            {
                if (!Visible) { Motion.Stop(this); hoverAmount = 0; }
                base.OnVisibleChanged(e);
            }

            private void AnimateHover(float target)
            {
                Motion.Stop(this);
                float from = hoverAmount;
                Motion.Run(this, 90, delegate(float t)
                {
                    hoverAmount = from + (target - from) * t;
                    Invalidate();
                });
            }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Invalidate(); }
            protected override void OnMouseUp(MouseEventArgs e) { Invalidate(); base.OnMouseUp(e); }
            protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var behind = new SolidBrush(Parent == null ? BackColor : Parent.BackColor))
                    graphics.FillRectangle(behind, ClientRectangle);
                bool pressed = Enabled && Capture && (MouseButtons & MouseButtons.Left) != 0 &&
                               ClientRectangle.Contains(PointToClient(Cursor.Position));
                bool link = (Tag as string) == "link";
                Color fill = Enabled ? Motion.Blend(BackColor, FlatAppearance.MouseOverBackColor, hoverAmount)
                    : DisabledFill.IsEmpty ? BackColor : DisabledFill;
                if (pressed) fill = ControlPaint.Dark(fill, 0.08f);
                float scale = graphics.DpiX / 96f;
                int radius = (int)Math.Round((CornerOverride > 0 ? CornerOverride : ButtonRadius) * scale);
                var bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                if (!link)
                {
                    using (var path = RoundedPath(bounds, radius))
                    {
                        if (Enabled && Gradient && Width > 1 && Height > 1)
                            using (var brush = new LinearGradientBrush(bounds, Motion.Blend(fill, Color.FromArgb(95, 178, 255), .38f),
                                fill, LinearGradientMode.Vertical)) graphics.FillPath(brush, path);
                        else using (var brush = new SolidBrush(fill)) graphics.FillPath(brush, path);
                        if (FlatAppearance.BorderSize > 0)
                            using (var pen = new Pen(Enabled ? FlatAppearance.BorderColor : fill)) graphics.DrawPath(pen, path);
                    }
                }
                if (!SelectionMark.IsEmpty)
                    using (var mark = new SolidBrush(SelectionMark))
                        graphics.FillRectangle(mark, 0, 9 * scale, 2 * scale, Math.Max(0f, Height - 18 * scale));
                if (!Underline.IsEmpty)
                    using (var mark = new SolidBrush(Underline))
                        graphics.FillRectangle(mark, 4 * scale, Height - 3 * scale, Math.Max(0f, Width - 8 * scale), 2 * scale);
                Color ink = Enabled ? ForeColor : DisabledInk;
                if (link && Enabled) ink = Motion.Blend(LinkInk, LinkHoverInk, hoverAmount);
                int left = Padding.Left;
                int width = Math.Max(0, Width - Padding.Horizontal);
                int inset = (int)Math.Round(10 * scale), gap = (int)Math.Round(7 * scale);
                bool showIcon = Image != null && (!FitIconToText ||
                    TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width +
                    inset * 2 + Image.Width + gap <= width);
                if (showIcon)
                {
                    graphics.DrawImage(Image, inset, (Height - Image.Height) / 2, Image.Width, Image.Height);
                    left = inset + Image.Width + gap;
                    width = Math.Max(0, Width - left - 8);
                }
                var textBounds = new Rectangle(left, 0, width, Height);
                bool alignLeft = showIcon || TextAlign == ContentAlignment.MiddleLeft;
                TextRenderer.DrawText(graphics, Text, Font, textBounds, ink,
                    (alignLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                    TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(graphics, new Rectangle(4, 4, Math.Max(0, Width - 8), Math.Max(0, Height - 8)),
                        Enabled ? ForeColor : DisabledInk, fill);
            }
        }

        private static CheckBox Check(string name)
        {
            return new CheckBox { Text = name, AutoSize = true, Margin = new Padding(0, 5, 14, 5) };
        }

        private static NumberField Number(int min, int max)
        {
            return new NumberField(min, max);
        }

        private sealed class NumberField : TableLayoutPanel
        {
            private readonly int min, max;
            private readonly TextBox input;
            public event EventHandler EditCommitted;

            public NumberField(int min, int max)
            {
                this.min = min;
                this.max = max;
                Width = 121;
                Height = 38;
                RowCount = 1;
                ColumnCount = 3;
                ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
                input = new TextBox
                {
                    Text = min.ToString(), TextAlign = HorizontalAlignment.Center, Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new Padding(3, 1, 3, 1), BorderStyle = BorderStyle.FixedSingle, MaxLength = max.ToString().Length
                };
                Leave += delegate { Commit(); };
                input.KeyDown += delegate(object sender, KeyEventArgs args)
                {
                    if (args.KeyCode == Keys.Enter && EditCommitted != null)
                    {
                        Commit();
                        args.SuppressKeyPress = true;
                        return;
                    }
                    if (args.KeyCode != Keys.Up && args.KeyCode != Keys.Down) return;
                    Step(args.KeyCode == Keys.Up ? 1 : -1);
                    args.SuppressKeyPress = true;
                };
                var less = Button("−", 0, delegate { Step(-1); });
                var more = Button("+", 0, delegate { Step(1); });
                less.AccessibleName = "减少";
                more.AccessibleName = "增加";
                less.Dock = more.Dock = DockStyle.Fill;
                less.Margin = more.Margin = Padding.Empty;
                Controls.Add(less, 0, 0);
                Controls.Add(input, 1, 0);
                Controls.Add(more, 2, 0);
            }

            public int Value
            {
                get
                {
                    int number;
                    if (!int.TryParse(input.Text, out number) || number < min || number > max)
                        throw new InvalidOperationException(AccessibleName + "请输入 " + min + " 到 " + max + " 之间的整数。");
                    return number;
                }
                set { input.Text = value.ToString(); }
            }

            public void SetLabel(string name) { input.AccessibleName = name; }

            private void Step(int step)
            {
                int number;
                if (!int.TryParse(input.Text, out number)) number = min;
                Value = Math.Max(min, Math.Min(max, number + step));
                Commit();
            }

            private void Commit()
            {
                if (EditCommitted != null) EditCommitted(this, EventArgs.Empty);
            }
        }

        private static ModeChoice Combo(params string[] choices)
        {
            return new ModeChoice(choices);
        }

        private sealed class ModeChoice : TableLayoutPanel
        {
            private readonly ThemeButton[] buttons;
            private int selectedIndex;
            private Color surface, selected, text, selectionFill, border, disabled;
            public event EventHandler SelectedIndexChanged;
            public bool Secondary { get; set; }
            public bool Accented { get; set; }
            public int[] Glyphs { get; set; }

            public ModeChoice(string[] choices)
            {
                RowCount = 1;
                ColumnCount = choices.Length;
                Width = choices.Length * Math.Max(45, choices.Max(choice => choice.Length) * 16 + 28);
                Height = 37;
                Rows(this, -1);
                buttons = new ThemeButton[choices.Length];
                for (int i = 0; i < choices.Length; i++)
                {
                    int index = i;
                    var button = (ThemeButton)Button(choices[i], 0, delegate { SelectedIndex = index; });
                    button.Dock = DockStyle.Fill;
                    button.FitIconToText = true;
                    button.Margin = new Padding(i == 0 ? 0 : 3, 0, 0, 0);
                    buttons[i] = button;
                    ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / choices.Length));
                    Controls.Add(button, i, 0);
                }
            }

            public void Enlarge(float size, int height, int gap)
            {
                Height = height;
                for (int i = 0; i < buttons.Length; i++)
                {
                    Font old = buttons[i].Font;
                    buttons[i].Font = UiFont(size, FontStyle.Bold);
                    old.Dispose();
                    buttons[i].Margin = new Padding(i == 0 ? 0 : gap, 0, 0, 0);
                }
            }

            public int SelectedIndex
            {
                get { return selectedIndex; }
                set
                {
                    if (selectedIndex == value) return;
                    selectedIndex = value;
                    RefreshSelection();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
            }

            public void Colors(Color surface, Color selected, Color text, Color selectionFill, Color border, Color disabled)
            {
                this.surface = surface;
                this.selected = selected;
                this.text = text;
                this.selectionFill = selectionFill;
                this.border = border;
                this.disabled = disabled;
                RefreshSelection();
            }

            private void RefreshSelection()
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    var button = buttons[i];
                    bool active = i == selectedIndex;
                    Color accentFill = WindowPalette.AccentIndex > 0 && WindowPalette.AccentIndex < WindowPalette.AccentPresets.Length
                        ? Motion.Blend(WindowPalette.AccentPresets[WindowPalette.AccentIndex], Color.Black, 0.15f)
                        : Color.FromArgb(35, 83, 147);
                    button.BackColor = active && Accented ? accentFill : active && !Secondary ? selectionFill : surface;
                    button.ForeColor = active && Accented ? Color.White : active ? selected : Secondary ? disabled : text;
                    button.Gradient = active && Accented;
                    button.FlatAppearance.BorderColor = active ? selected : border;
                    button.FlatAppearance.MouseOverBackColor = Secondary ? selectionFill : button.BackColor;
                    button.DisabledFill = button.BackColor;
                    button.DisabledInk = disabled;
                    button.FlatAppearance.BorderSize = Secondary ? 0 : 1;
                    button.Underline = (Secondary || Accented) && active ? selected : Color.Empty;
                    button.AccessibleDescription = active ? "已选择" : "";
                    if (Glyphs != null)
                    {
                        var previous = button.Image;
                        button.Image = NavGlyph(Glyphs[i], button.ForeColor, DeviceDpi / 96f);
                        if (previous != null) previous.Dispose();
                    }
                    button.Invalidate();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && Glyphs != null)
                    foreach (var button in buttons)
                        if (button.Image != null) button.Image.Dispose();
                base.Dispose(disposing);
            }
        }

        private static ListBox List(string name)
        {
            return new ListBox
            {
                AccessibleName = name, Dock = DockStyle.Top, Height = 68, IntegralHeight = false,
                BorderStyle = BorderStyle.None, Margin = new Padding(0, 3, 0, 9)
            };
        }

        private ListView AppList(string name, bool picker)
        {
            var list = new BufferedAppList
            {
                AccessibleName = name, Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 9), BorderStyle = BorderStyle.None,
                View = View.Details, HeaderStyle = ColumnHeaderStyle.None, HideSelection = false,
                CheckBoxes = picker, FullRowSelect = true, MultiSelect = false, SmallImageList = appImages.Images,
                ShowItemToolTips = picker
            };
            list.Columns.Add("软件");
            if (picker)
            {
                list.MouseClick += delegate(object sender, MouseEventArgs args)
                {
                    if (args.Button != MouseButtons.Left) return;
                    var hit = list.HitTest(args.Location);
                    if (hit.Item != null && (hit.Location & ListViewHitTestLocations.StateImage) == 0)
                        hit.Item.Checked = !hit.Item.Checked;
                };
            }
            list.ClientSizeChanged += delegate
            {
                list.Columns[0].Width = Math.Max(0, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 3);
            };
            return list;
        }

        private sealed class BufferedAppList : ListView
        {
            private readonly ImageList rowSize;
            private readonly Bitmap rowMarker;
            private readonly int rowHeight;

            public BufferedAppList(int rowHeight = 0)
            {
                DoubleBuffered = true;
                this.rowHeight = rowHeight;
                if (rowHeight == 0) return;
                rowSize = new ImageList();
                rowMarker = new Bitmap(1, 1);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                ApplyRowHeight();
            }

            protected override void OnDpiChangedAfterParent(EventArgs e)
            {
                base.OnDpiChangedAfterParent(e);
                ApplyRowHeight();
            }

            private void ApplyRowHeight()
            {
                if (rowSize == null) return;
                var size = new Size(1, Math.Min(128, (int)Math.Round(rowHeight * DeviceDpi / 96f)));
                if (rowSize.Images.Count != 0 && rowSize.ImageSize == size) return;
                SmallImageList = null;
                rowSize.ImageSize = size;
                rowSize.Images.Clear();
                rowSize.Images.Add(rowMarker);
                SmallImageList = rowSize;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && rowSize != null)
                {
                    SmallImageList = null;
                    rowSize.Dispose();
                    rowMarker.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private static int TableTextWidth(ListView list, string text)
        {
            return TextRenderer.MeasureText(text, list.Font).Width + (int)Math.Round(20 * list.DeviceDpi / 96f);
        }

        private ListViewItem AppItem(AppRule entry, bool picker)
        {
            var item = new ListViewItem(entry.Name, appImages.IndexFor(entry.Path)) { Tag = entry };
            if (picker) item.ToolTipText = System.IO.Path.GetFileName(entry.Path);
            return item;
        }

        private void RefreshAppImages(ListView list)
        {
            if (list.IsDisposed) return;
            list.BeginUpdate();
            try
            {
                foreach (ListViewItem item in list.Items)
                {
                    var app = item.Tag as AppRule;
                    if (app == null) continue;
                    int index = appImages.IndexFor(app.Path);
                    if (item.ImageIndex != index) item.ImageIndex = index;
                }
            }
            finally { list.EndUpdate(); }
        }

        private void Theme(bool dark)
        {
            darkTheme = dark;
            palette = new WindowPalette(dark);
            PaintTree(this, bg);
            PaintPolishedCanvas();
            if (floatingTimer != null) floatingTimer.ApplyPalette(palette);
            if (statisticsChart != null) statisticsChart.Colors(accent, ink, muted, outline);
            if (distributionChart != null) distributionChart.Colors(accent, muted);
            var menu = tray.ContextMenuStrip;
            menu.Renderer = new ToolStripProfessionalRenderer(new TrayColors(card, outline, subtle))
            {
                RoundedEdges = false
            };
            menu.BackColor = card;
            foreach (ToolStripItem item in menu.Items) item.ForeColor = ink;
            ApplyCaption(this);
            PaintTabs();
            if (settingsScroll != null && settingsScroll.IsHandleCreated)
                SetWindowTheme(settingsScroll.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            if (websiteSearch != null)
            {
                websiteSearch.BackColor = palette.Field;
                websiteSearch.ForeColor = palette.Ink;
            }
            if (websiteInput != null)
            {
                websiteInput.BackColor = palette.Field;
                websiteInput.ForeColor = palette.Ink;
            }
            dial.Invalidate();
            if (recordsIcon != null)
            {
                var previousIcon = recordsIcon.Image;
                recordsIcon.Image = EmptyGlyph(muted, DeviceDpi / 96f);
                if (previousIcon != null) previousIcon.Dispose();
            }
        }

        private sealed class TrayColors : ProfessionalColorTable
        {
            private readonly Color surface, line, selection;

            public TrayColors(Color surface, Color line, Color selection)
            {
                this.surface = surface;
                this.line = line;
                this.selection = selection;
            }

            public override Color ToolStripDropDownBackground { get { return surface; } }
            public override Color ImageMarginGradientBegin { get { return surface; } }
            public override Color ImageMarginGradientMiddle { get { return surface; } }
            public override Color ImageMarginGradientEnd { get { return surface; } }
            public override Color MenuBorder { get { return line; } }
            public override Color MenuItemBorder { get { return line; } }
            public override Color MenuItemSelected { get { return selection; } }
        }

        private void ApplyCaption(Form window)
        {
            ApplyCaption(window, palette);
        }

        private static void ApplyCaption(Form window, WindowPalette colors)
        {
            if (!window.IsHandleCreated || colors == null) return;
            const int captionColor = 35;
            const int textColor = 36;
            int background = ColorTranslator.ToWin32(colors.Background);
            int foreground = ColorTranslator.ToWin32(colors.Ink);
            int immersive = colors.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(window.Handle, 20, ref immersive, sizeof(int)) != 0)
                DwmSetWindowAttribute(window.Handle, 19, ref immersive, sizeof(int));
            DwmSetWindowAttribute(window.Handle, captionColor, ref background, sizeof(int));
            DwmSetWindowAttribute(window.Handle, textColor, ref foreground, sizeof(int));
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

        private bool darkTheme;

        private static void ConfigureDpi(Form window)
        {
            window.AutoScaleMode = AutoScaleMode.Dpi;
            window.AutoScaleDimensions = new SizeF(96, 96);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCaption(this);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            const int settingChanged = 0x001A;
            if (message.Msg == settingChanged) Motion.RefreshPreference();
        }


        private void PaintTree(Control control, Color parentColor)
        {
            PaintTree(control, parentColor, palette);
        }

        private void PaintPolishedCanvas()
        {
            bool apps = page == 1 && ruleSections.SelectedIndex == 1;
            Color canvas = !darkTheme && (page == 4 || apps) ? new WindowPalette(false, true).Background : bg;
            workspace.BackColor = workspaceHeading.BackColor = sectionTitle.BackColor = state.BackColor = info.BackColor = main.BackColor = canvas;
            pages[1].BackColor = apps ? canvas : bg;
        }

        private static void PaintTree(Control control, Color parentColor, WindowPalette colors)
        {
            string role = control.Tag as string;
            if (role == "polished-page")
            {
                colors = new WindowPalette(colors.Dark, true);
                parentColor = colors.Background;
            }
            control.BackColor = role == "card" ? colors.Card : parentColor;
            control.ForeColor = role == "muted" ? colors.Muted : role == "accent" ? colors.Accent : colors.Ink;
            var cardSurface = control as Card;
            if (cardSurface != null)
            {
                cardSurface.BorderColor = Color.Empty;
                cardSurface.Invalidate();
            }
            var button = control as Button;
            if (button != null)
            {
                bool primary = role == "primary";
                bool signal = role == "signal";
                bool danger = role == "danger";
                button.BackColor = primary ? colors.PrimaryFill : signal ? colors.Subtle : danger ? colors.DangerFill : parentColor;
                button.ForeColor = primary ? colors.PrimaryInk : signal ? colors.Signal : danger ? colors.Danger : colors.Ink;
                button.FlatAppearance.BorderColor = primary ? colors.PrimaryFill : signal ? colors.Signal : danger ? colors.Danger : colors.Outline;
                button.FlatAppearance.MouseOverBackColor = primary ? Motion.Blend(colors.PrimaryFill, colors.PrimaryInk, 0.1f) : colors.Subtle;
                if (danger) button.FlatAppearance.MouseOverBackColor = Motion.Blend(colors.DangerFill, colors.Danger, 0.12f);
                var themed = button as ThemeButton;
                if (themed != null)
                {
                    themed.Gradient = primary && colors.Dark;
                    themed.DisabledInk = colors.Muted;
                    themed.DisabledFill = colors.Dark ? Color.FromArgb(31, 44, 66) : Color.FromArgb(233, 235, 239);
                }
                if (role == "link")
                {
                    button.BackColor = parentColor;
                    button.ForeColor = colors.Muted;
                    button.FlatAppearance.BorderSize = 0;
                    button.FlatAppearance.BorderColor = parentColor;
                    button.FlatAppearance.MouseOverBackColor = parentColor;
                    if (themed != null) { themed.LinkInk = colors.Muted; themed.LinkHoverInk = colors.Accent; }
                }
            }
            if (control is ListView || role == "list")
                control.BackColor = colors.Card;
            else if (control is TextBox || control is ListBox || control is ComboBox || control is NumericUpDown)
                control.BackColor = colors.Background;
            var calendar = control as DateTimePicker;
            if (calendar != null)
            {
                calendar.CalendarMonthBackground = colors.Card;
                calendar.CalendarForeColor = colors.Ink;
            }
            foreach (Control child in control.Controls) PaintTree(child, control.BackColor, colors);
            var choice = control as ModeChoice;
            if (choice != null) choice.Colors(colors.Card, colors.Accent, colors.Ink, colors.Subtle, colors.Outline, colors.Muted);
            var polished = control as IPolishedControl;
            if (polished != null) polished.ApplyColors(colors);
        }

        private void PaintTabs(int previousPage = -1)
        {
            float scale = DeviceDpi / 96f;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (previousPage >= 0 && i != previousPage && i != page) continue;
                Button tab = tabs[i];
                Color background = page == i ? (darkTheme ? palette.PrimaryFill : subtle) : card;
                Color foreground = page == i ? (darkTheme ? palette.PrimaryInk : accent) : muted;
                ((ThemeButton)tab).SelectionMark = page == i ? accent : Color.Empty;
                ((ThemeButton)tab).Gradient = page == i && darkTheme;
                tab.FlatAppearance.BorderSize = 0;
                tab.FlatAppearance.MouseOverBackColor = page == i && darkTheme ? palette.PrimaryFill : subtle;
                tab.AccessibleDescription = page == i ? "当前页面" : "";
                var previous = tab.Image;
                tab.Image = NavGlyph(i, foreground, scale);
                if (previous != null) previous.Dispose();
                tab.BackColor = background;
                tab.ForeColor = foreground;
            }
        }

        private static Image NavGlyph(int index, Color color, float scale)
        {
            int size = (int)Math.Round(18 * scale) + 2;
            var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = size / 2f, cy = size / 2f;
                using (var pen = new Pen(color, 1.6f * scale))
                using (var brush = new SolidBrush(color))
                {
                    switch (index)
                    {
                        case 0:
                            graphics.DrawEllipse(pen, cx - 6.4f * scale, cy - 6.4f * scale, 12.8f * scale, 12.8f * scale);
                            graphics.DrawLine(pen, cx, cy, cx, cy - 4f * scale);
                            graphics.DrawLine(pen, cx, cy, cx + 3.2f * scale, cy + 1.4f * scale);
                            graphics.FillEllipse(brush, cx - scale, cy - scale, 2 * scale, 2 * scale);
                            break;
                        case 1:
                            for (int i = 0; i < 3; i++)
                            {
                                float y = cy + (i - 1) * 4.2f * scale;
                                graphics.DrawLine(pen, cx - 6.4f * scale, y, cx + 6.4f * scale, y);
                                float knob = cx + (i == 0 ? -2.8f : i == 1 ? 2.4f : 3.4f) * scale;
                                graphics.FillEllipse(brush, knob - 1.9f * scale, y - 1.9f * scale, 3.8f * scale, 3.8f * scale);
                            }
                            break;
                        case 2:
                            for (int i = 0; i < 3; i++)
                            {
                                float y = cy + (i - 1) * 4.6f * scale;
                                graphics.FillEllipse(brush, cx - 6.4f * scale, y - 1.3f * scale, 2.6f * scale, 2.6f * scale);
                                graphics.DrawLine(pen, cx - 2.2f * scale, y, cx + 6.4f * scale, y);
                            }
                            break;
                        case 4:
                            for (int i = 0; i < 2; i++)
                            {
                                float y = cy + (i == 0 ? -3.4f : 3.4f) * scale;
                                float knob = cx + (i == 0 ? -2.5f : 2.5f) * scale;
                                graphics.DrawLine(pen, cx - 6.4f * scale, y, cx + 6.4f * scale, y);
                                graphics.DrawLine(pen, knob, y - 2.4f * scale, knob, y + 2.4f * scale);
                            }
                            break;
                        case 5:
                            graphics.DrawPolygon(pen, new[] {
                                new PointF(cx, cy - 7 * scale), new PointF(cx + 6 * scale, cy - 5 * scale),
                                new PointF(cx + 5 * scale, cy + 2 * scale), new PointF(cx, cy + 7 * scale),
                                new PointF(cx - 5 * scale, cy + 2 * scale), new PointF(cx - 6 * scale, cy - 5 * scale)
                            });
                            graphics.DrawLines(pen, new[] { new PointF(cx - 3 * scale, cy), new PointF(cx - scale, cy + 2 * scale),
                                new PointF(cx + 3 * scale, cy - 2 * scale) });
                            break;
                        case 6:
                            graphics.DrawEllipse(pen, cx - 6.4f * scale, cy - 6.4f * scale, 12.8f * scale, 12.8f * scale);
                            graphics.DrawEllipse(pen, cx - 2.8f * scale, cy - 6.4f * scale, 5.6f * scale, 12.8f * scale);
                            graphics.DrawLine(pen, cx - 6 * scale, cy, cx + 6 * scale, cy);
                            break;
                        case 7:
                            graphics.DrawRectangle(pen, cx - 6 * scale, cy - 5 * scale, 12 * scale, 11 * scale);
                            graphics.DrawLine(pen, cx - 6 * scale, cy - scale, cx + 6 * scale, cy - scale);
                            graphics.DrawLine(pen, cx - 3 * scale, cy - 7 * scale, cx - 3 * scale, cy - 3 * scale);
                            graphics.DrawLine(pen, cx + 3 * scale, cy - 7 * scale, cx + 3 * scale, cy - 3 * scale);
                            break;
                        case 8:
                            graphics.DrawRectangle(pen, cx - 6 * scale, cy - 5 * scale, 12 * scale, 8 * scale);
                            graphics.DrawLine(pen, cx, cy + 3 * scale, cx, cy + 6 * scale);
                            graphics.DrawLine(pen, cx - 3 * scale, cy + 6 * scale, cx + 3 * scale, cy + 6 * scale);
                            break;
                        default:
                            graphics.FillRectangle(brush, cx - 6.4f * scale, cy + 0.8f * scale, 3.1f * scale, 5.2f * scale);
                            graphics.FillRectangle(brush, cx - 1.55f * scale, cy - 4.6f * scale, 3.1f * scale, 10.6f * scale);
                            graphics.FillRectangle(brush, cx + 3.3f * scale, cy - 1.6f * scale, 3.1f * scale, 7.6f * scale);
                            break;
                    }
                }
            }
            return bitmap;
        }
    }
}