using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private PolishedAppTable ruleApps;
        private ToolStripMenuItem[] appCategories;
        private int appCategory;
        private Label appResults, appEmptyTitle, appEmptyHelp;
        private Button appEmptyAction;
        private Control appEmpty;

        private Control BuildPolishedApps()
        {
            var layout = Area(2);
            layout.Tag = "polished-page";
            Rows(layout, 113, -1);

            manageApps = Button("管理软件白名单", 142, delegate { Act(EditAllowedApps); }, true);
            var banner = RuleDetail("软件白名单", manageApps, 8, true);
            appSummary = Label("", 10, "muted");
            banner.Controls.Add(appSummary, 0, 1);
            layout.Controls.Add(banner, 0, 0);

            var detail = ContextCard(3, 48, -1, 32);
            detail.Margin = Padding.Empty;
            detail.Padding = new Padding(16, 10, 16, 9);
            var tools = Columns(39, 61, 0);
            Rows(tools, -1);
            tools.ColumnStyles[2] = new ColumnStyle(SizeType.Absolute, 140);
            tools.Controls.Add(Label("已允许的软件", 13, "title"), 0, 0);
            var input = new PolishedInput("搜索名称或进程");
            appSearch = input.Editor;
            tools.Controls.Add(input, 1, 0);

            var menu = new ContextMenuStrip { Font = Font };
            var category = Button("全部分类  ▾", 132, null);
            category.AccessibleName = "按软件类别筛选";
            category.Anchor = AnchorStyles.Right;
            category.Margin = Padding.Empty;
            category.Click += delegate
            {
                var colors = new WindowPalette(darkTheme, true);
                menu.Renderer = new ToolStripProfessionalRenderer(new TrayColors(colors.Card, colors.Outline, colors.Subtle));
                menu.BackColor = colors.Card;
                for (int i = 0; i < appCategories.Length; i++)
                {
                    appCategories[i].ForeColor = colors.Ink;
                    appCategories[i].Checked = i == appCategory;
                }
                menu.Show(category, new Point(0, category.Height));
            };
            category.Disposed += delegate { menu.Dispose(); };
            appCategories = new[] { "全部分类", "第三方软件", "系统软件" }.Select((name, index) =>
            {
                var item = new ToolStripMenuItem(name);
                item.Click += delegate { SetAppCategory(index, category); };
                menu.Items.Add(item);
                return item;
            }).ToArray();
            tools.Controls.Add(category, 2, 0);
            detail.Controls.Add(tools, 0, 0);

            var content = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 0) };
            ruleApps = new PolishedAppTable(appImages);
            ruleApps.RemoveRequested = delegate(AppRule app) { RemoveAppRule(app); };
            ruleApps.EditRequested = delegate { Act(EditAllowedApps); };
            var tableHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            tableHost.Controls.Add(ruleApps);
            tableHost.Controls.Add(new TableHeader(ruleApps));
            ruleApps.HeaderStyle = ColumnHeaderStyle.None;
            content.Controls.Add(tableHost);
            appImages.Changed += delegate { if (!ruleApps.IsDisposed) ruleApps.Invalidate(); };

            var empty = Area(6);
            Rows(empty, -1, 57, 32, 32, 42, -1);
            empty.Controls.Add(new PolishedGlyph(8, 40) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 1);
            appEmptyTitle = Label("", 13, "title");
            appEmptyHelp = Label("", 9, "muted");
            appEmptyTitle.TextAlign = appEmptyHelp.TextAlign = ContentAlignment.MiddleCenter;
            empty.Controls.Add(appEmptyTitle, 0, 2);
            empty.Controls.Add(appEmptyHelp, 0, 3);
            appEmptyAction = Button("添加软件", 114, delegate
            {
                if (allowedAppCount == 0) Act(EditAllowedApps);
                else
                {
                    SetAppCategory(0, category);
                    appSearch.Clear();
                    appSearch.Focus();
                }
            }, true);
            appEmptyAction.Anchor = AnchorStyles.None;
            empty.Controls.Add(appEmptyAction, 0, 4);
            appEmpty = empty;
            content.Controls.Add(empty);
            detail.Controls.Add(content, 0, 1);
            appResults = Label("", 9, "muted");
            appResults.TextAlign = ContentAlignment.MiddleRight;
            detail.Controls.Add(appResults, 0, 2);
            layout.Controls.Add(detail, 0, 1);

            appSearch.TextChanged += delegate { RefreshRuleApps(); };
            appSearch.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode != Keys.Escape || appSearch.TextLength == 0) return;
                appSearch.Clear();
                args.SuppressKeyPress = true;
            };
            return layout;
        }

        private void SetAppCategory(int index, Button button)
        {
            if (appCategory == index) return;
            appCategory = index;
            button.Text = appCategories[index].Text + "  ▾";
            button.AccessibleDescription = appCategories[index].Text;
            RefreshRuleApps();
        }

        private void RemoveAppRule(AppRule app)
        {
            try
            {
                RequireEditable();
                var answer = MessageBox.Show(this, "把「" + app.Name + "」从软件白名单移除？", Program.Name,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (answer != DialogResult.OK) return;
                var updated = engine.Settings.Copy();
                updated.AppRules = (updated.AppRules ?? new List<AppRule>())
                    .Where(rule => !rule.SameApp(app)).ToList();
                updated.Apps = new List<string>();
                engine.UpdateSettings(DateTimeOffset.Now, updated);
                ShowAllowlistSummaries();
            }
            catch (Exception error) { Warn(error.Message); }
        }
        private sealed class AppTableRow
        {
            public readonly AppRule App;
            public readonly bool System;
            public readonly string Programs;

            public AppTableRow(AppRule app)
            {
                App = app;
                System = ProcessGuard.IsSystemApplication(app.Path);
                int count = new[] { app.Path }.Concat(app.Members ?? Enumerable.Empty<string>())
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                Programs = count + " 个程序";
            }
        }

        private sealed class TableHeader : Control, IPolishedControl
        {
            private readonly ListView list;
            private readonly Font headerFont = UiFont(11.5f);
            private WindowPalette colors;

            internal TableHeader(ListView list)
            {
                this.list = list;
                Dock = DockStyle.Top;
                Height = (int)Math.Round(42 * DeviceDpi / 96f);
                Margin = Padding.Empty;
                DoubleBuffered = true;
                TabStop = false;
                list.SizeChanged += delegate { Invalidate(); };
                list.ColumnWidthChanged += delegate { Invalidate(); };
            }

            void IPolishedControl.ApplyColors(WindowPalette palette)
            {
                colors = palette;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var palette = colors ?? new WindowPalette(BackColor.GetBrightness() < .45f);
                var graphics = e.Graphics;
                using (var fill = new SolidBrush(palette.Field)) graphics.FillRectangle(fill, ClientRectangle);
                using (var line = new Pen(Motion.Blend(palette.Card, palette.Outline, .7f)))
                    graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
                int x = 0;
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                foreach (ColumnHeader column in list.Columns)
                {
                    int width = Math.Max(0, column.Width - 28);
                    if (width > 0)
                        TextRenderer.DrawText(graphics, column.Text, headerFont,
                            new Rectangle(x + 14, 0, width, Height), palette.Muted, flags);
                    x += column.Width;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) headerFont.Dispose();
                base.Dispose(disposing);
            }
        }
        private sealed class PolishedWebsiteTable : ListView, IPolishedControl
        {
            private readonly Font headerFont = UiFont(11.5f);
            private readonly Font cellFont = UiFont(11.5f);
            private readonly ImageList heights = new ImageList();
            private readonly Bitmap marker = new Bitmap(1, 1);
            private string[] rows = new string[0];
            private WindowPalette colors;
            private int hover = -1;
            private int actionRow = -1;
            private bool fitting;

            internal Action<string> DeleteRequested;

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            private static extern int SetWindowTheme(IntPtr handle, string appName, string idList);

            public PolishedWebsiteTable()
            {
                Dock = DockStyle.Fill;
                Margin = Padding.Empty;
                AccessibleName = "允许的网站列表";
                Font = UiFont(11.5f);
                View = View.Details;
                BorderStyle = BorderStyle.None;
                FullRowSelect = true;
                HeaderStyle = ColumnHeaderStyle.Nonclickable;
                MultiSelect = false;
                HideSelection = false;
                ShowItemToolTips = true;
                VirtualMode = true;
                OwnerDraw = true;
                DoubleBuffered = true;
                Columns.Add("域名");
                Columns.Add("匹配范围");
                RetrieveVirtualItem += GetRow;
                DrawColumnHeader += DrawHeader;
                DrawSubItem += DrawCell;
                SizeChanged += delegate { FitColumns(); };
                ClientSizeChanged += delegate { FitColumns(); };
                MouseMove += delegate(object sender, MouseEventArgs e)
                {
                    var hit = HitTest(e.Location).Item;
                    SetHover(hit == null ? -1 : hit.Index);
                    int row = hit == null ? -1 : hit.Index;
                    bool delete = false;
                    if (row >= 0)
                    {
                        var sub = HitTest(e.Location).SubItem;
                        delete = sub != null && sub.Bounds.Right - e.X < 40;
                    }
                    if (row == actionRow && delete == (actionRow >= 0 && actionRow == row)) return;
                    actionRow = delete ? row : -1;
                    if (row >= 0 && row < VirtualListSize) Invalidate(GetItemRect(row));
                    Cursor = delete ? Cursors.Hand : Cursors.Default;
                };
                MouseLeave += delegate { SetHover(-1); actionRow = -1; Cursor = Cursors.Default; };
                MouseClick += delegate(object sender, MouseEventArgs e)
                {
                    var hit = HitTest(e.Location);
                    if (hit.Item == null || hit.Item.Index >= rows.Length || hit.SubItem == null) return;
                    if (hit.SubItem.Bounds.Right - e.X >= 40 || DeleteRequested == null) return;
                    DeleteRequested(rows[hit.Item.Index]);
                };
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SetRowHeight();
                if (colors != null) SetWindowTheme(Handle, colors.Dark ? "DarkMode_Explorer" : "Explorer", null);
            }

            private void SetRowHeight()
            {
                int height = Math.Min(128, (int)Math.Round(44 * DeviceDpi / 96f));
                if (heights.Images.Count != 0 && heights.ImageSize.Height == height) return;
                SmallImageList = null;
                heights.ImageSize = new Size(1, height);
                heights.Images.Clear();
                heights.Images.Add(marker);
                SmallImageList = heights;
                FitColumns();
            }

            public void SetRows(string[] domains)
            {
                BeginUpdate();
                rows = domains ?? new string[0];
                VirtualListSize = rows.Length;
                Invalidate();
                EndUpdate();
            }

            private void GetRow(object sender, RetrieveVirtualItemEventArgs e)
            {
                string domain = rows[e.ItemIndex];
                string match = domain.StartsWith("*.", StringComparison.Ordinal) ? "包含子域名" : "精确域名";
                e.Item = new ListViewItem(new[] { domain, match });
            }

            private void FitColumns()
            {
                if (fitting || Columns.Count != 2 || ClientSize.Width < 4) return;
                fitting = true;
                try
                {
                    float scale = DeviceDpi / 96f;
                    int match = Math.Max(TextRenderer.MeasureText("包含子域名", cellFont).Width,
                        TextRenderer.MeasureText("匹配范围", headerFont).Width) + (int)Math.Round(72 * scale);
                    match = Math.Min(match, Math.Max(150, ClientSize.Width / 2));
                    Columns[0].Width = Math.Max(180, ClientSize.Width - match);
                    Columns[1].Width = match;
                }
                finally { fitting = false; }
            }

            private void SetHover(int index)
            {
                if (index == hover) return;
                int previous = hover;
                hover = index;
                if (previous >= 0 && previous < VirtualListSize) Invalidate(GetItemRect(previous));
                if (index >= 0 && index < VirtualListSize) Invalidate(GetItemRect(index));
            }

            private void DrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                if (colors == null) { e.DrawDefault = true; return; }
                using (var fill = new SolidBrush(colors.Field)) e.Graphics.FillRectangle(fill, e.Bounds);
                using (var line = new Pen(Motion.Blend(colors.Card, colors.Outline, .7f)))
                    e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var text = Rectangle.Inflate(e.Bounds, -14, 0);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont, text, colors.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            private void DrawCell(object sender, DrawListViewSubItemEventArgs e)
            {
                if (colors == null || e.ItemIndex >= rows.Length) { e.DrawDefault = true; return; }
                var fill = e.Item.Selected ? colors.Subtle : e.ItemIndex == hover ? Motion.Blend(colors.Card, colors.Subtle, .72f)
                    : e.ItemIndex % 2 == 0 ? colors.Card : Motion.Blend(colors.Card, colors.Field, .53f);
                var g = e.Graphics;
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, e.Bounds);
                using (var line = new Pen(Motion.Blend(colors.Card, colors.Outline, .56f)))
                    g.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var text = Rectangle.Inflate(e.Bounds, -14, 0);
                var align = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                if (e.ColumnIndex == 0)
                {
                    TextRenderer.DrawText(g, e.SubItem.Text, cellFont, text, colors.Ink, align);
                    if (e.Item.Selected && Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(e.Bounds, -2, -3));
                }
                else
                {
                    string match = rows[e.ItemIndex].StartsWith("*.", StringComparison.Ordinal) ? "包含子域名" : "精确域名";
                    var size = TextRenderer.MeasureText(match, cellFont);
                    int height = Math.Min(text.Height - 4, size.Height + 6);
                    var pill = new Rectangle(text.Left, text.Top + (text.Height - height) / 2, Math.Min(size.Width + 15, text.Width), height);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    PaintPill(g, pill, 6, colors.Field, colors.Outline);
                    TextRenderer.DrawText(g, match, cellFont, pill, colors.Muted, align | TextFormatFlags.HorizontalCenter);
                    int trashLeft = e.Bounds.Right - 34;
                    int top = e.Bounds.Top + (e.Bounds.Height - 18) / 2;
                    DrawTrash(g, new Rectangle(trashLeft, top, 18, 18),
                        e.ItemIndex == actionRow ? colors.Danger : colors.Muted);
                }
            }

            private static void DrawTrash(Graphics g, Rectangle box, Color color)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(color, 1.6f))
                {
                    g.DrawLine(pen, box.Left + 3, box.Top + 6, box.Right - 3, box.Top + 6);
                    g.DrawLine(pen, box.Left + 6, box.Top + 4, box.Right - 6, box.Top + 4);
                    g.DrawRectangle(pen, box.Left + 5, box.Top + 8, box.Width - 10, box.Height - 12);
                }
            }

            void IPolishedControl.ApplyColors(WindowPalette palette)
            {
                colors = palette;
                if (IsHandleCreated) SetWindowTheme(Handle, palette.Dark ? "DarkMode_Explorer" : "Explorer", null);
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    SmallImageList = null;
                    heights.Dispose();
                    marker.Dispose();
                    headerFont.Dispose();
                    cellFont.Dispose();
                }
                base.Dispose(disposing);
            }
        }
        private sealed class PolishedAppTable : ListView, IPolishedControl
        {
            private readonly AppImages icons;
            private readonly Font headerFont = UiFont(11.5f);
            private readonly Font cellFont = UiFont(11.5f);

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            private static extern int SetWindowTheme(IntPtr handle, string appName, string idList);

            private readonly ImageList heights = new ImageList();
            private readonly Bitmap marker = new Bitmap(1, 1);
            private AppTableRow[] rows = new AppTableRow[0];
            private WindowPalette colors;
            private int hover = -1;
            private int actionRow = -1;
            private int actionKind;
            private bool fitting;

            internal Action<AppRule> RemoveRequested;
            internal Action EditRequested;
            public PolishedAppTable(AppImages icons)
            {
                this.icons = icons;
                Dock = DockStyle.Fill;
                Margin = Padding.Empty;
                AccessibleName = "允许的软件列表";
                Font = UiFont(11.5f);
                View = View.Details;
                BorderStyle = BorderStyle.None;
                FullRowSelect = true;
                HeaderStyle = ColumnHeaderStyle.Nonclickable;
                MultiSelect = false;
                HideSelection = false;
                ShowItemToolTips = true;
                VirtualMode = true;
                OwnerDraw = true;
                DoubleBuffered = true;
                Columns.Add("软件组");
                Columns.Add("类型");
                Columns.Add("包含程序");
                Columns.Add("");
                RetrieveVirtualItem += GetRow;
                DrawColumnHeader += DrawHeader;
                DrawSubItem += DrawCell;
                SizeChanged += delegate { FitColumns(); };
                ClientSizeChanged += delegate { FitColumns(); };
                MouseLeave += delegate { SetHover(-1); UpdateActionHover(new Point(-1, -1)); };
                MouseClick += delegate(object sender, MouseEventArgs e) { ClickAction(e.Location); };
                MouseMove += delegate(object sender, MouseEventArgs e)
                {
                    var hit = HitTest(e.Location).Item;
                    SetHover(hit == null ? -1 : hit.Index);
                    UpdateActionHover(e.Location);
                };
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SetRowHeight();
                if (colors != null) SetWindowTheme(Handle, colors.Dark ? "DarkMode_Explorer" : "Explorer", null);
            }

            protected override void OnDpiChangedAfterParent(EventArgs e)
            {
                base.OnDpiChangedAfterParent(e);
                SetRowHeight();
            }

            private void SetRowHeight()
            {
                int height = Math.Min(128, (int)Math.Round(46 * DeviceDpi / 96f));
                if (heights.Images.Count != 0 && heights.ImageSize.Height == height) return;
                SmallImageList = null;
                heights.ImageSize = new Size(1, height);
                heights.Images.Clear();
                heights.Images.Add(marker);
                SmallImageList = heights;
                FitColumns();
            }

            public void SetRows(AppTableRow[] items)
            {
                BeginUpdate();
                try
                {
                    SelectedIndices.Clear();
                    hover = -1;
                    rows = items;
                    VirtualListSize = items.Length;
                    if (IsHandleCreated && items.Length != 0) EnsureVisible(0);
                    FitColumns();
                }
                finally { EndUpdate(); }
                Invalidate();
            }

            private void GetRow(object sender, RetrieveVirtualItemEventArgs e)
            {
                if (e.ItemIndex < 0 || e.ItemIndex >= rows.Length) { e.Item = new ListViewItem(""); return; }
                var row = rows[e.ItemIndex];
                e.Item = new ListViewItem(new[] { row.App.Name, row.System ? "系统软件" : "第三方软件", row.Programs, "" })
                { ToolTipText = row.App.Name + "\n" + row.App.Path };
            }

            void IPolishedControl.ApplyColors(WindowPalette palette)
            {
                colors = palette;
                if (IsHandleCreated) SetWindowTheme(Handle, palette.Dark ? "DarkMode_Explorer" : "Explorer", null);
                Invalidate();
            }

            private void FitColumns()
            {
                int width = ClientSize.Width;
                if (fitting || Columns.Count != 4 || width < 4) return;
                fitting = true;
                try
                {
                    float scale = DeviceDpi / 96f;
                    bool compact = width < 760;
                    int type = Math.Max(TextRenderer.MeasureText("第三方软件", cellFont).Width,
                        TextRenderer.MeasureText("类型", headerFont).Width) + (int)Math.Round(56 * scale);
                    int programs = Math.Max(TextRenderer.MeasureText("00 个程序", cellFont).Width,
                        TextRenderer.MeasureText("包含程序", headerFont).Width) + (int)Math.Round(40 * scale);
                    int actions = (int)Math.Round((compact ? 52 : 76) * scale);
                    if (compact) programs = 0;
                    type = Math.Min(type, Math.Max(96, width / 3));
                    if (!compact) programs = Math.Min(programs, Math.Max(80, width / 4));
                    int name = width - type - programs - actions;
                    if (name < 150 && type > 96)
                    {
                        type = Math.Max(96, type - (150 - name));
                        name = width - type - programs - actions;
                    }
                    Columns[0].Width = Math.Max(110, name);
                    Columns[1].Width = type;
                    Columns[2].Width = programs;
                    Columns[3].Width = actions;
                    if (IsHandleCreated) ShowScrollBar(Handle, 0, false);
                }
                finally { fitting = false; }
            }

            private void SetHover(int index)
            {
                if (index == hover) return;
                int previous = hover;
                hover = index;
                if (previous >= 0 && previous < VirtualListSize) Invalidate(GetItemRect(previous));
                if (hover >= 0 && hover < VirtualListSize) Invalidate(GetItemRect(hover));
            }

            private void DrawHeader(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                if (colors == null) { e.DrawDefault = true; return; }
                using (var fill = new SolidBrush(colors.Field)) e.Graphics.FillRectangle(fill, e.Bounds);
                var text = Rectangle.Inflate(e.Bounds, -14, 0);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont, text, colors.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            private void DrawCell(object sender, DrawListViewSubItemEventArgs e)
            {
                if (colors == null || e.ItemIndex >= rows.Length) { e.DrawDefault = true; return; }
                var row = rows[e.ItemIndex];
                bool selected = e.Item.Selected;
                var fill = selected ? colors.Subtle : e.ItemIndex == hover ? Motion.Blend(colors.Card, colors.Subtle, .72f)
                    : e.ItemIndex % 2 == 0 ? colors.Card : Motion.Blend(colors.Card, colors.Field, .53f);
                var g = e.Graphics;
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, e.Bounds);
                using (var line = new Pen(Motion.Blend(colors.Card, colors.Outline, .56f)))
                    g.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var text = Rectangle.Inflate(e.Bounds, -14, 0);
                var align = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                if (e.ColumnIndex == 0)
                {
                    var size = icons.Images.ImageSize;
                    icons.Images.Draw(g, text.Left, text.Top + (text.Height - size.Height) / 2, icons.IndexFor(row.App.Path));
                    text.X += size.Width + 10;
                    text.Width -= size.Width + 10;
                    TextRenderer.DrawText(g, e.SubItem.Text, cellFont, text, colors.Ink, align);
                    if (selected && Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(e.Bounds, -2, -3));
                }
                else if (e.ColumnIndex == 1)
                {
                    var size = TextRenderer.MeasureText(e.SubItem.Text, cellFont);
                    int height = Math.Min(text.Height - 4, size.Height + 6);
                    var pill = new Rectangle(text.Left, text.Top + (text.Height - height) / 2, Math.Min(size.Width + 15, text.Width), height);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    PaintPill(g, pill, 6, row.System ? colors.Subtle : colors.Field, colors.Outline);
                    TextRenderer.DrawText(g, e.SubItem.Text, cellFont, pill, row.System ? colors.Accent : colors.Muted,
                        align | TextFormatFlags.HorizontalCenter);
                }
                else if (e.ColumnIndex == 2) TextRenderer.DrawText(g, row.Programs, cellFont, text, colors.Muted, align);
                else
                {
                    int trashLeft = e.Bounds.Right - 34;
                    int editLeft = trashLeft - 28;
                    int top = e.Bounds.Top + (e.Bounds.Height - 18) / 2;
                    bool hot = e.ItemIndex == actionRow;
                    DrawActionGlyph(g, new Rectangle(editLeft, top, 18, 18), 1, hot && actionKind == 1 ? colors.Accent : colors.Muted);
                    DrawActionGlyph(g, new Rectangle(trashLeft, top, 18, 18), 2, hot && actionKind == 2 ? Color.FromArgb(224, 96, 96) : colors.Muted);
                }
            }

            private void UpdateActionHover(Point location)
            {
                int row = -1, kind = 0;
                if (location.X >= 0)
                {
                    var hit = HitTest(location);
                    if (hit.Item != null && hit.Item.Index < VirtualListSize)
                    {
                        row = hit.Item.Index;
                        if (hit.SubItem != null && hit.Item.SubItems.IndexOf(hit.SubItem) == 3)
                            kind = ActionAt(hit.SubItem.Bounds, location.X);
                    }
                }
                if (row == actionRow && kind == actionKind) return;
                int previous = actionRow;
                actionRow = row;
                actionKind = kind;
                if (previous >= 0 && previous < VirtualListSize) Invalidate(GetItemRect(previous));
                if (row >= 0 && row < VirtualListSize) Invalidate(GetItemRect(row));
                Cursor = kind == 0 ? Cursors.Default : Cursors.Hand;
            }

            private static int ActionAt(Rectangle bounds, int x)
            {
                int trashLeft = bounds.Right - 34;
                int editLeft = trashLeft - 28;
                if (x >= trashLeft && x <= trashLeft + 22) return 2;
                if (x >= editLeft && x <= editLeft + 22) return 1;
                return 0;
            }

            private void ClickAction(Point location)
            {
                var hit = HitTest(location);
                if (hit.Item == null || hit.Item.Index >= rows.Length) return;
                if (hit.SubItem == null || hit.Item.SubItems.IndexOf(hit.SubItem) != 3) return;
                int kind = ActionAt(hit.SubItem.Bounds, location.X);
                if (kind == 1 && EditRequested != null) EditRequested();
                else if (kind == 2 && RemoveRequested != null) RemoveRequested(rows[hit.Item.Index].App);
            }

            private static void DrawActionGlyph(Graphics g, Rectangle box, int kind, Color color)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(color, 1.6f))
                {
                    if (kind == 1)
                    {
                        g.DrawLine(pen, box.Left + 3, box.Bottom - 4, box.Right - 5, box.Top + 4);
                        g.DrawLine(pen, box.Left + 3, box.Bottom - 4, box.Left + 7, box.Bottom - 5);
                        g.DrawLine(pen, box.Right - 6, box.Top + 3, box.Right - 4, box.Top + 6);
                    }
                    else
                    {
                        g.DrawLine(pen, box.Left + 3, box.Top + 6, box.Right - 3, box.Top + 6);
                        g.DrawLine(pen, box.Left + 6, box.Top + 4, box.Right - 6, box.Top + 4);
                        g.DrawRectangle(pen, box.Left + 5, box.Top + 8, box.Width - 10, box.Height - 12);
                    }
                }
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    SmallImageList = null;
                    heights.Dispose();
                    marker.Dispose();
                    headerFont.Dispose();
                    cellFont.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}