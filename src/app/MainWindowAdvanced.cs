using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private Form SettingsDialog(string title, int width, int height, out TableLayoutPanel table, params int[] rows)
        {
            var dialog = new Form
            {
                Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(width, height),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false, Font = Font
            };
            ConfigureDpi(dialog);
#if FOCUS_TEST
            if (Opacity == 0) dialog.Opacity = 0;
#endif
            dialog.HandleCreated += delegate { ApplyCaption(dialog); };
            dialog.Shown += delegate { PaintTree(dialog, bg); };
            table = Area(rows.Length);
            Rows(table, rows);
            table.Padding = new Padding(22, 16, 22, 12);
            dialog.Controls.Add(table);
            return dialog;
        }

        private void OpenAdvanced()
        {
            RequireEditable();
            var credential = engine.State.AdvancedCredential;
            if (credential == null)
            {
                if (!SetNewPassword(this, "设置高级设置密码")) return;
            }
            else
            {
                var answer = AskSecret("解锁高级设置", "输入密码", value => credential.MatchesPassword(value)
                    ? null : "密码错误，请重新输入。", true);
                if (answer == DialogResult.Retry)
                {
                    if (AskSecret("找回高级设置密码", "输入设置密码时保存的恢复码", value => credential.MatchesRecovery(value)
                        ? null : "恢复码有误，请检查后重试。", false) != DialogResult.OK ||
                        !SetNewPassword(this, "重置高级设置密码")) return;
                }
                else if (answer != DialogResult.OK) return;
            }

            RequireEditable();
            TableLayoutPanel panel;
            using (var dialog = SettingsDialog("高级设置", 572, 385, out panel, 42, 49, 49, 45, 31, 51, -1))
            {
                panel.Controls.Add(Label("暂停与提前停止", 15, "title"), 0, 0);
                var pauseMinutes = Number(0, 1440);
                var monthly = Number(0, 1000);
                pauseMinutes.Value = engine.PauseLimit;
                monthly.Value = engine.MonthlyStopLimit;
                panel.Controls.Add(Fields("每轮累计暂停分钟", pauseMinutes), 0, 1);
                panel.Controls.Add(Fields("每月可提前停止次数", monthly), 0, 2);
                var message = Label("恢复后可继续使用剩余暂停时间。提前停止需填写理由，每次扣 1 次。", 9, "muted");
                panel.Controls.Add(message, 0, 3);
                panel.Controls.Add(Label("专注统计的前台软件名称与使用时长保存在本机。", 9, "muted"), 0, 4);
                var passwords = Flow();
                passwords.Controls.Add(Button("重置密码", 105, delegate
                {
                    try { SetNewPassword(dialog, "重置高级设置密码"); }
                    catch (Exception ex) { message.Text = ex.Message; }
                }));
                passwords.Controls.Add(Button("删除密码", 105, delegate
                {
                    if (MessageBox.Show(dialog, "删除密码和恢复码？下次进入高级设置需要设置新密码。", "删除密码",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
                    try
                    {
                        RequireEditable();
                        engine.SetAdvancedCredential(DateTimeOffset.Now, null);
                        notice = "已删除高级设置密码。";
                        dialog.Close();
                    }
                    catch (Exception ex) { message.Text = ex.Message; }
                }));
                panel.Controls.Add(passwords, 0, 5);
                var buttons = Flow();
                var saveAdvanced = Button("保存高级设置", 133, delegate
                {
                    try
                    {
                        RequireEditable();
                        var rules = engine.Settings.Copy();
                        rules.PauseBudgetMinutes = pauseMinutes.Value;
                        rules.MonthlyStopCount = monthly.Value;
                        rules.MonthlyStopMinutes = null;
                        engine.UpdateSettings(DateTimeOffset.Now, rules);
                        ShowSaved();
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { message.Text = ex.Message; }
                }, true);
                buttons.Controls.Add(saveAdvanced);
                buttons.Controls.Add(CancelButtonFor(dialog));
                panel.Controls.Add(buttons, 0, 6);
                dialog.AcceptButton = saveAdvanced;
                PaintTree(dialog, bg);
                dialog.ShowDialog(this);
            }
        }

        private DialogResult AskSecret(string title, string prompt, Func<string, string> validate, bool forgot)
        {
            TableLayoutPanel layout;
            using (var dialog = SettingsDialog(title, 468, 204, out layout, 40, 35, -1, 38))
            {
                layout.Controls.Add(Label(prompt, 11, "title"), 0, 0);
                var secret = new TextBox { Dock = DockStyle.Fill, MaxLength = 128, UseSystemPasswordChar = forgot, AccessibleName = prompt };
                var status = Label("", 9, "muted");
                layout.Controls.Add(secret, 0, 1);
                layout.Controls.Add(status, 0, 2);
                var buttons = Flow();
                var ok = Button("继续", 100, delegate
                {
                    string error;
                    try { error = validate(secret.Text); }
                    catch (Exception ex) { error = ex.Message; }
                    if (error == null) dialog.DialogResult = DialogResult.OK;
                    else
                    {
                        status.Text = error;
                        secret.SelectAll();
                        secret.Focus();
                    }
                }, true);
                buttons.Controls.Add(ok);
                if (forgot) buttons.Controls.Add(Button("忘记密码", 106, delegate { dialog.DialogResult = DialogResult.Retry; }));
                buttons.Controls.Add(CancelButtonFor(dialog));
                layout.Controls.Add(buttons, 0, 3);
                dialog.AcceptButton = ok;
                PaintTree(dialog, bg);
                return dialog.ShowDialog(this);
            }
        }

        private bool SetNewPassword(Form owner, string title)
        {
            RequireEditable();
            TableLayoutPanel panel;
            using (var dialog = SettingsDialog(title, 560, 268, out panel, 38, 44, 44, -1, 38))
            {
                panel.Controls.Add(Label("设置仅用于高级设置的密码", 12, "title"), 0, 0);
                var password = new TextBox { Width = 304, MaxLength = 128, UseSystemPasswordChar = true };
                var confirmation = new TextBox { Width = 304, MaxLength = 128, UseSystemPasswordChar = true };
                panel.Controls.Add(Fields("新密码", password), 0, 1);
                panel.Controls.Add(Fields("确认密码", confirmation), 0, 2);
                var status = Label("使用 6 至 128 个字符。", 9, "muted");
                panel.Controls.Add(status, 0, 3);
                var buttons = Flow();
                var ok = Button("生成恢复码", 117, delegate
                {
                    if (!AdvancedCredential.ValidPassword(password.Text) || password.Text != confirmation.Text)
                    {
                        status.Text = "请确认两次输入一致，且密码满足长度要求。";
                        return;
                    }
                    try
                    {
                        RequireEditable();
                        string recovery;
                        var updated = AdvancedCredential.Create(password.Text, out recovery);
                        if (!ShowRecoveryCode(dialog, recovery)) return;
                        RequireEditable();
                        engine.SetAdvancedCredential(DateTimeOffset.Now, updated);
                        notice = "高级设置密码已保存。";
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { status.Text = ex.Message; }
                }, true);
                buttons.Controls.Add(ok);
                buttons.Controls.Add(CancelButtonFor(dialog));
                panel.Controls.Add(buttons, 0, 4);
                dialog.AcceptButton = ok;
                PaintTree(dialog, bg);
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

        private bool ShowRecoveryCode(Form owner, string recovery)
        {
            TableLayoutPanel panel;
            using (var dialog = SettingsDialog("保存恢复码", 560, 215, out panel, 55, 37, -1, 38))
            {
                panel.Controls.Add(Label("忘记密码时可用此恢复码重置。请保存在你能找到的地方。", 10), 0, 0);
                var code = new TextBox { Text = recovery, ReadOnly = true, Dock = DockStyle.Fill, Font = UiFont(13, FontStyle.Bold), AccessibleName = "恢复码" };
                panel.Controls.Add(code, 0, 1);
                var status = Label("密码重置后会生成新的恢复码。", 9, "muted");
                panel.Controls.Add(status, 0, 2);
                var controls = Flow();
                controls.Controls.Add(Button("我已保存", 106, delegate { dialog.DialogResult = DialogResult.OK; }, true));
                controls.Controls.Add(Button("复制恢复码", 116, delegate
                {
                    try { Clipboard.SetText(recovery); status.Text = "已复制。"; }
                    catch (System.Runtime.InteropServices.ExternalException) { status.Text = "请选中恢复码后手动复制。"; }
                }));
                controls.Controls.Add(CancelButtonFor(dialog));
                panel.Controls.Add(controls, 0, 3);
                PaintTree(dialog, bg);
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

        private Button CancelButtonFor(Form dialog, string caption = "取消")
        {
            var cancel = Button(caption, 96, delegate { dialog.DialogResult = DialogResult.Cancel; });
            dialog.CancelButton = cancel;
            return cancel;
        }

        private void ChooseApps(Form owner, List<AppRule> draft, bool system)
        {
            TableLayoutPanel panel;
            using (var dialog = SettingsDialog("选择已识别的软件", 660, 510, out panel, 38, 42, -1, 36, 39))
            using (var searchTimer = new Timer { Interval = 140 })
            {
                panel.Controls.Add(Label(system ? "选择系统软件" : "选择第三方软件", 15, "title"), 0, 0);
                var search = new TextBox { Width = 380, MaxLength = 100, AccessibleName = "搜索软件名称或程序文件" };
                panel.Controls.Add(Fields("搜索", search), 0, 1);
                var results = AppList("识别到的软件", true);
                DialogList.Attach(results, palette);
                panel.Controls.Add(results, 0, 2);
                var status = Label("正在识别开始菜单和已运行的软件…", 9, "muted");
                panel.Controls.Add(status, 0, 3);
                var buttons = Flow();
                var add = Button("加入白名单", 124, delegate { dialog.DialogResult = DialogResult.OK; }, true);
                add.Enabled = false;
                buttons.Controls.Add(add);
                buttons.Controls.Add(CancelButtonFor(dialog));
                panel.Controls.Add(buttons, 0, 4);
                IList<AppCatalog.Entry> catalog = null;
                var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool filling = false;
                string previousTerm = null;
                Action updateStatus = delegate
                {
                    status.Text = results.Items.Count == 0 ? "没有匹配的软件。可返回后使用「手动添加」。"
                        : "当前显示 " + results.Items.Count + " 项，已勾选 " + checkedPaths.Count + " 项。" +
                            (system ? "" : " Windows 内置应用可在「系统软件」中查找。");
                };
                Action filter = delegate
                {
                    if (catalog == null) return;
                    string term = search.Text.Trim();
                    if (string.Equals(previousTerm, term, StringComparison.CurrentCultureIgnoreCase)) return;
                    previousTerm = term;
                    var matching = new List<ListViewItem>();
                    foreach (var entry in catalog)
                        if (entry.Name.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                            entry.Path.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var row = AppItem(entry.Rule, true);
                            row.Checked = checkedPaths.Contains(entry.Path);
                            matching.Add(row);
                        }
                    filling = true;
                    results.BeginUpdate();
                    try
                    {
                        results.Items.Clear();
                        results.Items.AddRange(matching.ToArray());
                    }
                    finally
                    {
                        results.EndUpdate();
                        filling = false;
                    }
                    updateStatus();
                };
                results.ItemChecked += delegate(object sender, ItemCheckedEventArgs args)
                {
                    if (filling) return;
                    string path = ((AppRule)args.Item.Tag).Path;
                    if (args.Item.Checked) checkedPaths.Add(path);
                    else checkedPaths.Remove(path);
                    add.Enabled = checkedPaths.Count > 0;
                    updateStatus();
                };
                search.TextChanged += delegate
                {
                    searchTimer.Stop();
                    searchTimer.Start();
                };
                searchTimer.Tick += delegate
                {
                    searchTimer.Stop();
                    filter();
                };
                dialog.Shown += async delegate
                {
                    try
                    {
                        var detected = await DiscoverApps();
                        if (dialog.IsDisposed) return;
                        catalog = detected.Where(x => x.IsSystem == system &&
                            !draft.Any(app => app.SameApp(x.Rule))).ToList();
                        filter();
                        appImages.Load(catalog.Select(x => x.Path));
                    }
                    catch (Exception)
                    {
                        if (!dialog.IsDisposed) status.Text = "自动识别未完成。可以通过「手动添加」选择软件。";
                    }
                };
                dialog.AcceptButton = add;
                PaintTree(dialog, bg);
                Action updateIcons = delegate { RefreshAppImages(results); };
                appImages.Changed += updateIcons;
                DialogResult answer;
                try { answer = dialog.ShowDialog(owner); }
                finally { appImages.Changed -= updateIcons; results.SmallImageList = null; }
                if (answer != DialogResult.OK || catalog == null) return;
                RequireEditable();
                CheckAppLimit(draft.Count + checkedPaths.Count);
                draft.AddRange(catalog.Where(entry => checkedPaths.Contains(entry.Path)).Select(entry => entry.Rule));
            }
        }

        private Task<IList<AppCatalog.Entry>> DiscoverApps()
        {
            var now = DateTimeOffset.UtcNow;
            if (appDiscovery == null || appDiscovery.IsFaulted || appDiscovery.IsCanceled ||
                (appDiscovery.IsCompleted && now - discoveryStarted > TimeSpan.FromMinutes(1)))
            {
                discoveryStarted = now;
                appDiscovery = Task.Run((Func<IList<AppCatalog.Entry>>)AppCatalog.Discover);
            }
            return appDiscovery;
        }
    }
}
