using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private const int AllowlistLimit = 150;
        private TextBox appSearch, websiteSearch, websiteInput;
        private PolishedWebsiteTable polishedWebsites;
        private Control websiteEmpty, webTableHost;
        private Label websiteEmptyTitle, websiteEmptyHelp;
        private Button websiteEmptyAction;
        private Button websiteAdd;

        private List<AppRule> ConfiguredApps()
        {
            var settings = engine.Settings;
            var rules = settings.AppRules != null ? settings.AppRules.ToList() : new List<AppRule>();
            foreach (string path in settings.Apps.Where(AppCatalog.IsSelectable))
            {
                if (rules.Any(rule => rule.Contains(path))) continue;
                var entry = new AppCatalog.Entry(path, null);
                if (!rules.Any(rule => rule.SameApp(entry.Rule))) rules.Add(entry.Rule);
            }
            return rules;
        }

        private void ShowAllowlistSummaries()
        {
            var apps = ConfiguredApps();
            allowedAppCount = apps.Count;
            int websiteCount = engine.Settings.Websites.Count;
            appSummary.Text = "已允许 " + allowedAppCount + " 个软件组";
            websiteSummary.Text = websiteCount == 0 ? "未添加域名。专注 Edge 将打开空白页。" : "已允许 " + websiteCount + " 个域名";
            focusAppCount.Text = allowedAppCount + " 项";
            focusWebsiteCount.Text = websiteCount + " 个域名";
            RefreshRuleApps();
            RefreshRuleWebsites();
            webHelp.Text = engine.Settings.Websites.Contains("chat.openai.com")
                ? "ChatGPT 现用 chatgpt.com，可在「管理网站白名单」中更新。"
                : "网站跳转或登录时，也需要允许所用的目标域名。";
        }

        private void RefreshRuleApps()
        {
            string query = appSearch.Text.Trim();
            var matching = ConfiguredApps().Where(app => query.Length == 0 ||
                app.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                app.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (app.Members != null && app.Members.Any(path => path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                .Select(app => new AppTableRow(app)).Where(row => appCategory == 0 || row.System == (appCategory == 2))
                .OrderBy(row => row.App.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            ruleApps.SetRows(matching);
            bool noRules = allowedAppCount == 0;
            appEmptyTitle.Text = noRules ? "还没有软件白名单" : "找不到符合条件的软件";
            appEmptyHelp.Text = noRules ? "添加专注时需要使用的学习或工作软件。" : "可换一个名称、进程或软件类别。";
            appEmptyAction.Text = noRules ? "添加软件" : "清除筛选";
            appEmptyAction.Enabled = !noRules || manageApps.Enabled;
            ruleApps.Visible = matching.Length > 0;
            appEmpty.Visible = matching.Length == 0;
            appResults.Text = noRules ? "" : matching.Length == allowedAppCount ? "共 " + allowedAppCount + " 个软件组"
                : "显示 " + matching.Length + " / " + allowedAppCount + " 个软件组";
            if (IsHandleCreated && page == 1 && ruleSections.SelectedIndex == 1)
                appImages.Load(matching.Select(row => row.App.Path));
        }

        private void RefreshRuleWebsites()
        {
            string query = websiteSearch.Text.Trim();
            var matches = engine.Settings.Websites
                .Where(domain => domain.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            polishedWebsites.SetRows(matches);
            webTableHost.Visible = matches.Length > 0;
            bool none = engine.Settings.Websites.Count == 0;
            websiteEmpty.Visible = matches.Length == 0;
            websiteEmptyTitle.Text = none ? "还没有网站白名单" : "没有匹配的域名";
            websiteEmptyHelp.Text = none ? "添加学习或工作要用的网站，专注时只放行它们。" : "换一个关键词，或清除搜索查看全部。";
            websiteEmptyAction.Text = none ? "添加网站" : "清除搜索";
        }

        private void WebsiteSelfTest()
        {
            var sites = engine.Settings.Websites.ToArray();
            var lines = new List<string>();
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                foreach (string site in sites)
                {
                    string host = site.StartsWith("*.", StringComparison.Ordinal) ? site.Substring(2) : site;
                    try
                    {
                        var addresses = Dns.GetHostAddresses(host);
                        if (addresses.Length == 0) throw new InvalidOperationException("解析不到地址");
                        var watch = Stopwatch.StartNew();
                        using (var client = new TcpClient())
                        {
                            var connect = client.ConnectAsync(host, 443);
                            if (!connect.Wait(4000)) throw new TimeoutException("443 连接超时");
                        }
                        watch.Stop();
                        lines.Add("✓ " + host + "  解析 " + addresses[0] + " · 443 可用 " + watch.ElapsedMilliseconds + "ms");
                    }
                    catch (Exception ex) { lines.Add("✗ " + host + "  " + ex.Message); }
                }
                if (sites.Length == 0) lines.Add("· 白名单为空，没有可检测的网站。");
                if (proxy != null)
                {
                    try
                    {
                        var request = (HttpWebRequest)WebRequest.Create("http://example.com/");
                        request.Proxy = new WebProxy("http://127.0.0.1:" + proxy.Port);
                        request.Timeout = 5000;
                        using (var response = (HttpWebResponse)request.GetResponse())
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            string body = reader.ReadToEnd();
                            lines.Add(body.Contains("不在本轮白名单") ? "✓ 拦截生效：未允许的网站被替换为拦截页" : "✗ 代理可能未拦截 example.com");
                        }
                    }
                    catch (Exception ex) { lines.Add("✗ 拦截测试失败：" + ex.Message); }
                }
                else lines.Add("· 代理未启动（需要先开始一轮专注再自检）。");
            }
            finally { Cursor.Current = Cursors.Default; }
            TableLayoutPanel panel;
            using (var dialog = SettingsDialog("网页自检", 640, 430, out panel, 44, -1, 44))
            {
                panel.Controls.Add(Label("检查结果", 15, "title"), 0, 0);
                var output = new TextBox
                {
                    Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
                    BorderStyle = BorderStyle.None, Font = UiFont(10.5f)
                };
                output.Text = string.Join(Environment.NewLine + Environment.NewLine, lines.ToArray());
                panel.Controls.Add(output, 0, 1);
                var buttons = Flow();
                var close = CancelButtonFor(dialog, "关闭");
                buttons.Controls.Add(close);
                panel.Controls.Add(buttons, 0, 2);
                dialog.AcceptButton = dialog.CancelButton = close;
                PaintTree(dialog, bg);
                dialog.ShowDialog(this);
            }
        }
        private void RemoveWebsite(string domain)
        {
            try
            {
                RequireEditable();
                if (MessageBox.Show(this, "从白名单移除「" + domain + "」？", Program.Name,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                var updated = engine.Settings.Copy();
                updated.Websites = updated.Websites
                    .Where(item => !string.Equals(item, domain, StringComparison.OrdinalIgnoreCase)).ToList();
                engine.UpdateSettings(DateTimeOffset.Now, updated);
                ShowAllowlistSummaries();
            }
            catch (Exception error) { Warn(error.Message); }
        }

        private void AddWebsiteInline()
        {
            RequireEditable();
            var updated = engine.Settings.Copy();
            string domain = Website.Parse(websiteInput.Text);
            if (updated.Websites.Contains(domain)) throw new InvalidOperationException("该域名已在白名单中。");
            if (updated.Websites.Count >= AllowlistLimit)
                throw new InvalidOperationException("网站白名单最多可添加 " + AllowlistLimit + " 项。");
            updated.Websites = new List<string>(updated.Websites) { domain };
            engine.UpdateSettings(DateTimeOffset.Now, updated);
            websiteInput.Clear();
            websiteSearch.Clear();
            ShowAllowlistSummaries();
            ShowSaved();
        }

        private static void SetRuleList(ListView list, IEnumerable<string[]> entries)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                list.Items.AddRange(entries.Select(row => new ListViewItem(row)
                    { ToolTipText = string.Join("  ·  ", row) }).ToArray());
                list.Visible = list.Items.Count != 0;
            }
            finally { list.EndUpdate(); }
        }

        private bool ShowRulePreview(bool begin)
        {
            var settings = engine.Settings;
            var apps = ConfiguredApps().Select(app => app.Name).Distinct().ToArray();
            string timing = settings.CountUp ? "无固定时长的正计时" : settings.Minutes + " 分钟倒计时";
            bool hasEdge = ProcessGuard.FindEdge() != null;
            TableLayoutPanel panel;
            using (var dialog = SettingsDialog("开始前检查", 720, 392, out panel, 42, 42, -1, 55, 43))
            {
                panel.Controls.Add(Label(timing, 16, "title"), 0, 0);
                panel.Controls.Add(Label("专注会收起名单外的前台窗口。系统允许项仍可使用。", 10, "muted"), 0, 1);
                var lists = Columns(50, 50);
                var applications = PreviewList("允许的软件", apps, "软件白名单为空");
                applications.Margin = new Padding(0, 0, 8, 0);
                lists.Controls.Add(applications, 0, 0);
                lists.Controls.Add(PreviewList("允许的网站", settings.Websites.ToArray(), "网站白名单为空"), 1, 0);
                panel.Controls.Add(lists, 0, 2);
                string browserHelp = hasEdge ? "网站限制通过「打开专注 Edge」生效。"
                    : "当前未检测到 Microsoft Edge。本轮仍可使用计时和软件限制。";
                panel.Controls.Add(Label(browserHelp, 10, "muted"), 0, 3);

                var actions = Flow();
                var cancel = CancelButtonFor(dialog, begin ? "返回" : "关闭");
                if (begin)
                {
                    var confirm = Button("开始这轮专注", 144, delegate { dialog.DialogResult = DialogResult.OK; }, true);
                    actions.Controls.Add(confirm);
                }
                if (page != 1)
                    actions.Controls.Add(Button("修改规则", 104, delegate { dialog.DialogResult = DialogResult.Retry; }));
                actions.Controls.Add(cancel);
                dialog.AcceptButton = cancel;
                dialog.CancelButton = cancel;
                panel.Controls.Add(actions, 0, 4);
                PaintTree(dialog, bg);
                DialogResult answer = dialog.ShowDialog(this);
                if (answer == DialogResult.Retry) SelectPage(1);
                return begin && answer == DialogResult.OK;
            }
        }

        private Control PreviewList(string title, string[] entries, string emptyText)
        {
            var section = Area(2);
            section.Tag = "card";
            section.Padding = new Padding(12, 7, 12, 7);
            Rows(section, 32, -1);
            section.Controls.Add(Label(title, 11, "title"), 0, 0);
            if (entries.Length == 0)
            {
                section.Controls.Add(Label(emptyText, 10, "muted"), 0, 1);
            }
            else
            {
                var values = new ListView { AccessibleName = title, Dock = DockStyle.Top, Height = 68, View = View.Details, HeaderStyle = ColumnHeaderStyle.None, BorderStyle = BorderStyle.None, FullRowSelect = true, MultiSelect = false, Margin = new Padding(0, 3, 0, 9) };
                values.Columns.Add(title, 220);
                DialogList.Attach(values, palette);
                values.SizeChanged += delegate
                {
                    if (values.Columns.Count > 0) values.Columns[0].Width = Math.Max(60, values.ClientSize.Width - 2);
                };
                values.Dock = DockStyle.Fill;
                values.Items.AddRange(entries.Select(entry => new ListViewItem(entry)).ToArray());
                section.Controls.Add(values, 0, 1);
            }
            return section;
        }

        private void EditAllowedApps()
        {
            RequireEditable();
            var draft = ConfiguredApps();
            TableLayoutPanel panel;
            using (var dialog = AllowlistDialog("软件白名单", "同一软件的组件按组放行。", out panel))
            {
                var hint = (Label)panel.GetControlFromPosition(0, 1);
                var category = Combo("第三方软件", "系统软件");
                category.AccessibleName = "软件类别";
                var list = AppList("软件白名单列表", false);
                list.MultiSelect = true;
                panel.Controls.Add(list, 0, 3);
                var status = Label("", 9, "muted");
                panel.Controls.Add(status, 0, 4);
                var toolbar = Flow();
                toolbar.Controls.Add(category);
                Action refresh = delegate
                {
                    bool system = category.SelectedIndex == 1;
                    var visible = draft.Where(app => ProcessGuard.IsSystemApplication(app.Path) == system)
                        .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    list.BeginUpdate();
                    try
                    {
                        list.Items.Clear();
                        list.Items.AddRange(visible.Select(app => AppItem(app, false)).ToArray());
                        if (system)
                            foreach (var name in new[] { "文件资源管理器", "Windows 设置", "记事本", "计算器", "画图", "输入法与系统组件" })
                                list.Items.Add(new ListViewItem(name + "  ·  系统默认放行"));
                    }
                    finally { list.EndUpdate(); }
                    appImages.Load(visible.Select(app => app.Path));
                    hint.Text = system ? "Windows 基础组件默认放行。可额外添加已识别的系统应用。" : "同一软件的组件按组放行。";
                    status.Text = "已手动放行 " + visible.Count + " 个软件组。";
                };
                toolbar.Controls.Add(Button("选择已识别软件", 148, delegate
                {
                    ChangeAllowlist(status, delegate { ChooseApps(dialog, draft, category.SelectedIndex == 1); refresh(); });
                }, true));
                toolbar.Controls.Add(Button("手动添加", 103, delegate
                {
                    ChangeAllowlist(status, delegate { AddApps(dialog, draft, category.SelectedIndex == 1); refresh(); });
                }));
                var remove = Button("移除选中", 103, delegate
                {
                    ChangeAllowlist(status, delegate
                    {
                        var selected = list.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag as AppRule).Where(rule => rule != null).ToList();
                        foreach (var rule in selected) draft.Remove(rule);
                        refresh();
                    });
                });
                remove.Enabled = false;
                list.SelectedIndexChanged += delegate { remove.Enabled = list.SelectedItems.Cast<ListViewItem>().Any(item => item.Tag is AppRule); };
                category.SelectedIndexChanged += delegate { refresh(); };
                toolbar.Controls.Add(remove);
                panel.Controls.Add(toolbar, 0, 2);
                AddAllowlistFooter(dialog, panel, status, delegate
                {
                    var updated = engine.Settings.Copy();
                    updated.AppRules = draft;
                    updated.Apps = new List<string>();
                    return updated;
                });

                Action refreshIcons = delegate { RefreshAppImages(list); };
                appImages.Changed += refreshIcons;
                try
                {
                    refresh();
                    PaintTree(dialog, bg);
                    dialog.ShowDialog(this);
                }
                finally
                {
                    appImages.Changed -= refreshIcons;
                    list.SmallImageList = null;
                }
            }
        }

        private void AddApps(Form owner, List<AppRule> draft, bool system)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "选择要允许的软件", Filter = "Windows 软件 (*.exe)|*.exe", Multiselect = true,
                CheckFileExists = true, DereferenceLinks = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                RequireEditable();
                var valid = dialog.FileNames.Select(Path.GetFullPath).Where(path =>
                    AppCatalog.IsSelectable(path) && ProcessGuard.IsSystemApplication(path) == system).ToList();
                if (valid.Count == 0) throw new InvalidOperationException("请选择当前分类里的应用。Edge 在网站白名单中管理。");
                var additions = new List<AppRule>();
                foreach (var entry in valid.Select(path => new AppCatalog.Entry(path, null)))
                    if (!draft.Concat(additions).Any(app => app.SameApp(entry.Rule))) additions.Add(entry.Rule);
                if (additions.Count == 0) throw new InvalidOperationException("所选软件已在白名单中。");
                CheckAppLimit(draft.Count + additions.Count);
                draft.AddRange(additions);
            }
        }

        private static void CheckAppLimit(int count)
        {
            if (count > AllowlistLimit) throw new InvalidOperationException("软件白名单最多可添加 " + AllowlistLimit + " 项。");
        }

        private void EditAllowedWebsites()
        {
            RequireEditable();
            TableLayoutPanel panel;
            using (var dialog = AllowlistDialog("Edge 网站白名单", "输入域名或网址。使用 *.example.com 可同时允许主域名和子域名。", out panel))
            {
                var list = List("网站白名单列表");
                list.Dock = DockStyle.Fill;
                list.Tag = "list";
                list.HorizontalScrollbar = true;
                list.SelectionMode = SelectionMode.MultiExtended;
                list.Items.AddRange(engine.Settings.Websites.ToArray());
                panel.Controls.Add(list, 0, 3);
                var status = Label("", 9, "muted");
                panel.Controls.Add(status, 0, 4);
                Action updateCount = delegate { status.Text = "已列出 " + list.Items.Count + " 个域名。"; };
                updateCount();

                var toolbar = Columns(100, 110, 115);
                toolbar.ColumnStyles[1].SizeType = toolbar.ColumnStyles[2].SizeType = SizeType.Absolute;
                var input = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 6, 10, 0), AccessibleName = "域名或网址" };
                toolbar.Controls.Add(input, 0, 0);
                Action add = delegate
                {
                    string domain = Website.Parse(input.Text);
                    if (list.Items.Contains(domain))
                    {
                        status.Text = "该域名已在白名单中。";
                    }
                    else
                    {
                        if (list.Items.Count >= AllowlistLimit)
                            throw new InvalidOperationException("网站白名单最多可添加 " + AllowlistLimit + " 项。");
                        list.Items.Add(domain);
                        updateCount();
                    }
                    input.Clear();
                };
                toolbar.Controls.Add(Button("添加网站", 96, delegate { ChangeAllowlist(status, add); }, true), 1, 0);
                input.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode != Keys.Enter) return;
                    e.SuppressKeyPress = true;
                    ChangeAllowlist(status, add);
                };
                var remove = Button("移除选中", 107, delegate
                {
                    ChangeAllowlist(status, delegate
                    {
                        foreach (int index in list.SelectedIndices.Cast<int>().OrderByDescending(index => index).ToArray())
                            list.Items.RemoveAt(index);
                        updateCount();
                    });
                });
                remove.Enabled = false;
                list.SelectedIndexChanged += delegate { remove.Enabled = list.SelectedIndices.Count != 0; };
                toolbar.Controls.Add(remove, 2, 0);
                panel.Controls.Add(toolbar, 0, 2);
                AddAllowlistFooter(dialog, panel, status, delegate
                {
                    if (!string.IsNullOrWhiteSpace(input.Text)) add();
                    var updated = engine.Settings.Copy();
                    updated.Websites = list.Items.Cast<string>().ToList();
                    return updated;
                });
                PaintTree(dialog, bg);
                dialog.ShowDialog(this);
            }
        }

        private Form AllowlistDialog(string title, string hint, out TableLayoutPanel panel)
        {
            var dialog = SettingsDialog(title, 778, 560, out panel, 41, 34, 44, -1, 30, 43);
            dialog.Icon = Icon;
            dialog.FormBorderStyle = FormBorderStyle.Sizable;
            dialog.MaximizeBox = true;
            dialog.MinimumSize = new Size(620, 430);
            panel.Controls.Add(Label(title, 17, "title"), 0, 0);
            panel.Controls.Add(Label(hint, 10, "muted"), 0, 1);
            return dialog;
        }

        private void AddAllowlistFooter(Form dialog, TableLayoutPanel panel, Label status, Func<Settings> build)
        {
            var footer = Flow();
            var commit = Button("保存白名单", 122, delegate
            {
                ChangeAllowlist(status, delegate
                {
                    var updated = build();
                    engine.UpdateSettings(DateTimeOffset.Now, updated);
                    ShowAllowlistSummaries();
                    ShowSaved();
                    dialog.DialogResult = DialogResult.OK;
                });
            }, true);
            footer.Controls.Add(commit);
            footer.Controls.Add(CancelButtonFor(dialog));
            panel.Controls.Add(footer, 0, 5);
        }

        private void ChangeAllowlist(Label status, Action change)
        {
            try { RequireEditable(); change(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }
    }
}
