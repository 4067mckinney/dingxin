using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed partial class MainWindow
    {
        private DateTime statisticsDate = DateTime.Today;
        private DateTimeOffset nextStatisticsRefresh;
        private bool statisticsRefreshPending;
        private int displayedHistoryRevision, displayedStops;
        private ModeChoice statisticsPeriod, statisticsView;
        private Label statisticsRange, statisticsRounds, statisticsTotal, statisticsAverage, statisticsStops, statisticsHint;
        private ListView statisticsList;
        private TrendChart statisticsChart;
        private DistributionChart distributionChart;
        private Button statisticsNext;
        private int[] statisticsColumnWeights = new int[0];
        private bool fittingStatisticsColumns;
        private string widestStatisticsDuration;
        private long statisticsTotalTicks, statisticsAverageTicks;

        private Control BuildStatistics()
        {
            var panel = Area(4);
            Rows(panel, 57, 104, -44, -56);
            var controls = Columns(50, 50);
            Rows(controls, -1);
            statisticsPeriod = Combo("日", "周", "月", "年");
            statisticsPeriod.AccessibleName = "统计范围";
            statisticsPeriod.Enlarge(11.5f, 44, 5);
            statisticsPeriod.SelectedIndex = 1;
            statisticsPeriod.Dock = DockStyle.None;
            statisticsPeriod.Anchor = AnchorStyles.Left;
            statisticsPeriod.Margin = Padding.Empty;
            statisticsPeriod.SelectedIndexChanged += delegate { RefreshStatistics(); };
            controls.Controls.Add(statisticsPeriod, 0, 0);
            var navigation = Flow();
            navigation.Dock = DockStyle.None;
            navigation.Anchor = AnchorStyles.Right;
            navigation.WrapContents = false;
            controls.Controls.Add(navigation, 1, 0);
            var previous = Button("‹", 32, delegate { MoveStatistics(-1); });
            previous.Margin = Padding.Empty;
            previous.AccessibleName = "上一个统计周期";
            navigation.Controls.Add(previous);
            statisticsRange = Label("", 10, "title");
            statisticsRange.Dock = DockStyle.None;
            statisticsRange.Size = new Size(166, 36);
            statisticsRange.TextAlign = ContentAlignment.MiddleCenter;
            navigation.Controls.Add(statisticsRange);
            statisticsNext = Button("›", 32, delegate { MoveStatistics(1); });
            statisticsNext.Margin = Padding.Empty;
            statisticsNext.AccessibleName = "下一个统计周期";
            navigation.Controls.Add(statisticsNext);
            panel.Controls.Add(controls, 0, 0);

            var metrics = Columns(25, 25, 25, 25);
            Rows(metrics, -1);
            metrics.SizeChanged += delegate
            {
                int ends = metrics.Width < 680 ? 22 : 25;
                if (metrics.ColumnStyles[0].Width == ends) return;
                metrics.SuspendLayout();
                metrics.ColumnStyles[0].Width = metrics.ColumnStyles[3].Width = ends;
                metrics.ColumnStyles[1].Width = metrics.ColumnStyles[2].Width = 50 - ends;
                metrics.ResumeLayout();
            };
            metrics.Controls.Add(Metric("专注次数", out statisticsRounds, 16, 3));
            metrics.Controls.Add(Metric("专注时长", out statisticsTotal, 16));
            metrics.Controls.Add(Metric("日均专注", out statisticsAverage, 16, 7));
            statisticsTotal.SizeChanged += delegate { SetStatisticDuration(statisticsTotal, statisticsTotalTicks); };
            statisticsAverage.SizeChanged += delegate { SetStatisticDuration(statisticsAverage, statisticsAverageTicks); };
            metrics.Controls.Add(Metric("提前停止", out statisticsStops, 16, 2));
            metrics.Controls[3].Margin = Padding.Empty;
            panel.Controls.Add(metrics, 0, 1);

            statisticsChart = new TrendChart { Dock = DockStyle.Fill, Tag = "card", Margin = new Padding(0, 8, 0, 0),
                AccessibleName = "专注时长趋势" };
            var charts = Columns(53, 47);
            statisticsChart.Margin = new Padding(0, 12, 10, 0);
            charts.Controls.Add(statisticsChart, 0, 0);
            var distribution = ContextCard(2, 34, -1);
            distribution.Margin = new Padding(0, 12, 0, 0);
            distribution.Padding = new Padding(8, 5, 8, 7);
            distribution.Controls.Add(IconHeading("软件前台用时分布", 3, 10), 0, 0);
            distributionChart = new DistributionChart { AccessibleName = "软件前台用时分布", Dock = DockStyle.Fill, Margin = Padding.Empty };
            distribution.Controls.Add(distributionChart, 0, 1);
            charts.Controls.Add(distribution, 1, 0);
            panel.Controls.Add(charts, 0, 2);
            var detail = Area(3);
            detail.Tag = "card";
            detail.Margin = new Padding(0, 12, 0, 0);
            detail.Padding = new Padding(12, 6, 12, 10);
            Rows(detail, 45, 29, -1);
            var viewControls = Columns(100, 0);
            Rows(viewControls, -1);
            viewControls.ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, 96);
            statisticsView = Combo("软件明细", "软件用时", "提前停止");
            statisticsView.Secondary = true;
            statisticsView.Enlarge(11, 41, 5);
            statisticsView.Width = 334;
            statisticsView.AccessibleName = "统计明细";
            statisticsView.SelectedIndexChanged += delegate { RefreshStatistics(); };
            statisticsView.Anchor = AnchorStyles.Left;
            viewControls.Controls.Add(statisticsView, 0, 0);
            var export = Button("导出数据", 92, delegate { Act(ExportStatistics); });
            export.Anchor = AnchorStyles.Right;
            export.Margin = Padding.Empty;
            viewControls.Controls.Add(export, 1, 0);
            detail.Controls.Add(viewControls, 0, 0);
            statisticsHint = Label("", 9, "muted");
            detail.Controls.Add(statisticsHint, 0, 1);
            statisticsList = new BufferedAppList(32)
            {
                Dock = DockStyle.Fill, AccessibleName = "专注使用记录",
                View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None, ShowItemToolTips = true, OwnerDraw = true
            };
            statisticsList.DrawColumnHeader += DrawStatisticsHeader;
            statisticsList.DrawSubItem += DrawStatisticsCell;
            statisticsList.ClientSizeChanged += delegate { FitStatisticsColumns(); };
            statisticsList.SizeChanged += delegate { FitStatisticsColumns(); };
            detail.Controls.Add(statisticsList, 0, 2);
            panel.Controls.Add(detail, 0, 3);
            return panel;
        }

        private void DrawStatisticsHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var fill = new SolidBrush(subtle)) e.Graphics.FillRectangle(fill, e.Bounds);
            var textBounds = Rectangle.Inflate(e.Bounds, -5, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, textBounds, muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void DrawStatisticsCell(object sender, DrawListViewSubItemEventArgs e)
        {
            using (var fill = new SolidBrush(e.Item.Selected ? subtle : e.ItemIndex % 2 == 0 ? card : Motion.Blend(card, subtle, 0.26f)))
                e.Graphics.FillRectangle(fill, e.Bounds);
            var textBounds = Rectangle.Inflate(e.Bounds, -5, 0);
            string text = e.SubItem.Text;
            if (e.ColumnIndex == 0 && statisticsView.SelectedIndex == 0 && statisticsList.ClientSize.Width < 650 && text.Length == 14)
                text = text.Substring(0, 11);
            TextRenderer.DrawText(e.Graphics, text, statisticsList.Font, textBounds, ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void MoveStatistics(int direction)
        {
            switch (statisticsPeriod.SelectedIndex)
            {
                case 0: statisticsDate = statisticsDate.AddDays(direction); break;
                case 1: statisticsDate = statisticsDate.AddDays(direction * 7); break;
                case 2: statisticsDate = statisticsDate.AddMonths(direction); break;
                case 3: statisticsDate = statisticsDate.AddYears(direction); break;
            }
            if (statisticsDate > DateTime.Today) statisticsDate = DateTime.Today;
            RefreshStatistics();
        }

        private void StatisticsBounds(out DateTime start, out DateTime end)
        {
            start = statisticsDate.Date;
            switch (statisticsPeriod.SelectedIndex)
            {
                case 0: end = start.AddDays(1); return;
                case 1:
                    start = start.AddDays(-((int)start.DayOfWeek + 6) % 7);
                    end = start.AddDays(7);
                    return;
                case 2:
                    start = new DateTime(start.Year, start.Month, 1);
                    end = start.AddMonths(1);
                    return;
                default:
                    start = new DateTime(start.Year, 1, 1);
                    end = start.AddYears(1);
                    return;
            }
        }

        private void RefreshStatistics()
        {
            if (!statisticsBuilt) return;
            statisticsRefreshPending = false;
            nextStatisticsRefresh = DateTimeOffset.Now.AddSeconds(3);
            displayedHistoryRevision = history.Revision;
            displayedStops = engine.State.Stops.Count;
            DateTime start, end;
            StatisticsBounds(out start, out end);
            int period = statisticsPeriod.SelectedIndex;
            statisticsNext.Enabled = end <= DateTime.Today;
            statisticsRange.Text = period == 0 ? start.ToString("yyyy-MM-dd")
                : period == 1 ? start.ToString("MM-dd") + " 至 " + end.AddDays(-1).ToString("MM-dd")
                : period == 2 ? start.ToString("yyyy 年 M 月") : start.ToString("yyyy 年");
            long from = new DateTimeOffset(start).UtcTicks;
            long until = new DateTimeOffset(end).UtcTicks;
            var entries = history.Read(start, end).OrderBy(item => item.From).ToList();
            var focused = entries.Where(item => !item.Blocked).ToList();
            long total = focused.Sum(item => DurationInside(item, from, until));
            statisticsRounds.Text = focused.Select(item => item.Round).Distinct().Count().ToString() + " 次";
            statisticsTotalTicks = total;
            SetStatisticDuration(statisticsTotal, statisticsTotalTicks);
            int days = Math.Max(1, (int)((end < DateTime.Today.AddDays(1) ? end : DateTime.Today.AddDays(1)) - start).TotalDays);
            statisticsAverageTicks = total / days;
            SetStatisticDuration(statisticsAverage, statisticsAverageTicks);
            statisticsStops.Text = StopsInRange(start, end).Count() + " 次";
            var appUsage = focused.Where(item => !string.IsNullOrEmpty(item.App)).GroupBy(item => item.App, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new KeyValuePair<string, long>(group.Key, group.Sum(item => DurationInside(item, from, until))))
                .Where(item => item.Value > 0).OrderByDescending(item => item.Value).ToList();
            var visibleUsage = appUsage.Take(3).ToList();
            if (appUsage.Count > 3) visibleUsage.Add(new KeyValuePair<string, long>("其他", appUsage.Skip(3).Sum(item => item.Value)));
            distributionChart.Render(visibleUsage.ToArray(), accent, muted);

            var labels = new List<string>();
            var values = new List<long>();
            for (var point = start; point < end;)
            {
                var after = period == 0 ? point.AddHours(1) : period == 3 ? point.AddMonths(1) : point.AddDays(1);
                long lower = new DateTimeOffset(point).UtcTicks;
                long upper = new DateTimeOffset(after).UtcTicks;
                labels.Add(point.ToString(period == 0 ? "HH" : period == 3 ? "%M" : "dd"));
                values.Add(focused.Sum(item => DurationInside(item, lower, upper)));
                point = after;
            }
            statisticsChart.Render(labels.ToArray(), values.ToArray(), accent, ink, muted, outline);
            RenderStatisticsList(entries, from, until);
        }

        private void ExportStatistics()
        {
            if (statisticsList.Columns.Count == 0 || statisticsRefreshPending) RefreshStatistics();
            var headers = statisticsList.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
            var rows = statisticsList.Items.Cast<ListViewItem>().Select(item =>
                item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(cell => cell.Text).ToArray()).ToArray();
            ExportCsv("专注统计", headers, rows);
        }

        private void RenderStatisticsList(List<FocusUse> entries, long from, long until)
        {
            int top = statisticsList.Items.Count > 0 && statisticsList.TopItem != null ? statisticsList.TopItem.Index : 0;
            statisticsList.BeginUpdate();
            try
            {
                statisticsList.Clear();
                switch (statisticsView.SelectedIndex)
                {
                    case 0:
                        AddStatisticsColumns("开始使用", 142, "软件", 177, "前台时长", 104, "分类", 96);
                        foreach (var item in MergeUses(entries).Reverse())
                            if (!string.IsNullOrEmpty(item.App))
                                AddStatisticsRow(FocusHistory.Local(item.From).ToString("MM-dd HH:mm"), item.App,
                                    item.Blocked ? "—" : DisplayMetricDuration(DurationInside(item, from, until)),
                                    item.Blocked ? "已拦截" : item.SystemApp ? "系统软件" : "第三方");
                        statisticsHint.Text = statisticsList.Items.Count == 0 ? "暂无前台软件记录。" : "切到前台开始计时，后台驻留不计入用时。";
                        break;
                    case 1:
                        AddStatisticsColumns("软件", 220, "前台总时长", 126, "分类", 118);
                        var ranking = entries.Where(item => !item.Blocked && !string.IsNullOrEmpty(item.App))
                            .GroupBy(item => new { item.App, item.SystemApp })
                            .Select(group => new { group.Key, Ticks = group.Sum(item => DurationInside(item, from, until)) })
                            .OrderByDescending(item => item.Ticks);
                        foreach (var item in ranking)
                            AddStatisticsRow(item.Key.App, DisplayMetricDuration(item.Ticks), item.Key.SystemApp ? "系统软件" : "第三方软件");
                        statisticsHint.Text = statisticsList.Items.Count == 0 ? "暂无软件用时。" : "按前台总用时排行。";
                        break;
                    default:
                        AddStatisticsColumns("提前停止理由", 327, "次数", 93);
                        foreach (var group in engine.State.Stops.Where(stop => stop.WhenUtc >= from && stop.WhenUtc < until)
                            .GroupBy(stop => stop.Reason).OrderByDescending(group => group.Count()))
                            AddStatisticsRow(group.Key.Replace('\n', ' ').Replace('\r', ' '), group.Count().ToString());
                        statisticsHint.Text = statisticsList.Items.Count == 0 ? "暂无提前停止记录。" : "按填写的理由统计停止次数。";
                        break;
                }
                FitStatisticsColumns();
                if (top > 0 && statisticsList.Items.Count > 0)
                    statisticsList.TopItem = statisticsList.Items[Math.Min(top, statisticsList.Items.Count - 1)];
            }
            finally { statisticsList.EndUpdate(); }
        }

        private void AddStatisticsColumns(params object[] columns)
        {
            statisticsColumnWeights = new int[columns.Length / 2];
            widestStatisticsDuration = "8小时88分";
            for (int i = 0; i < columns.Length; i += 2)
            {
                statisticsColumnWeights[i / 2] = (int)columns[i + 1];
                statisticsList.Columns.Add((string)columns[i]);
            }
            FitStatisticsColumns();
        }

        private void FitStatisticsColumns()
        {
            int count = statisticsList.Columns.Count;
            int width = statisticsList.ClientSize.Width - 2;
            if (fittingStatisticsColumns || count == 0 || count != statisticsColumnWeights.Length || width <= 0) return;
            int total = statisticsColumnWeights.Sum();
            int flexible = count == 4 ? 1 : 0;
            bool compact = width < 650;
            string[] samples = count == 4 ? new[] { compact ? "88-88 88:88" : "88-88 88:88:88", "", widestStatisticsDuration, "系统软件" }
                : count == 3 ? new[] { "", widestStatisticsDuration, "第三方软件" } : new[] { "", "8888" };
            int[] target = new int[count];
            int remaining = width;
            fittingStatisticsColumns = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (i == flexible) continue;
                    int preferred = compact ? 0 : width * statisticsColumnWeights[i] / total;
                    int measured = TableTextWidth(statisticsList, samples[i]) - (compact ? 10 : 0);
                    target[i] = Math.Min(width / count + 30, Math.Max(preferred, measured));
                    remaining -= target[i];
                }
                target[flexible] = Math.Max(5, remaining);
                for (int i = 0; i < count; i++) statisticsList.Columns[i].Width = target[i];
                const int horizontal = 0;
                if (statisticsList.IsHandleCreated) ShowScrollBar(statisticsList.Handle, horizontal, false);
            }
            finally { fittingStatisticsColumns = false; }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);

        private void AddStatisticsRow(params string[] columns)
        {
            if (columns.Length >= 3 && columns[columns.Length - 2].Length > widestStatisticsDuration.Length)
                widestStatisticsDuration = columns[columns.Length - 2];
            var item = new ListViewItem(columns) { ToolTipText = string.Join("   ", columns) };
            statisticsList.Items.Add(item);
        }

        private static IEnumerable<FocusUse> MergeUses(IEnumerable<FocusUse> entries)
        {
            FocusUse previous = null;
            foreach (var item in entries)
            {
                if (previous != null && !previous.Blocked && !item.Blocked && previous.To == item.From &&
                    previous.Round == item.Round && previous.App == item.App && previous.SystemApp == item.SystemApp)
                {
                    previous.To = item.To;
                    continue;
                }
                if (previous != null) yield return previous;
                previous = item.Copy();
            }
            if (previous != null) yield return previous;
        }

        private static long DurationInside(FocusUse item, long from, long until)
        {
            return Math.Max(0, Math.Min(until, item.To) - Math.Max(from, item.From));
        }

        private static string DisplayDuration(long ticks, bool clock)
        {
            var time = TimeSpan.FromTicks(ticks);
            if (clock) return ((int)time.TotalMinutes).ToString("00") + ":" + time.Seconds.ToString("00");
            if (time.TotalHours >= 1) return (int)time.TotalHours + " 小时 " + time.Minutes + " 分";
            if (time.TotalMinutes >= 1) return time.Minutes + " 分 " + time.Seconds + " 秒";
            return time.Seconds + " 秒";
        }

        private static void SetStatisticDuration(Label label, long ticks)
        {
            string full = DisplayMetricDuration(ticks);
            label.AccessibleDescription = full;
            string visible = label.Width >= TextRenderer.MeasureText(full, label.Font).Width + 6 ? full : DisplayMetricDuration(ticks, true);
            if (label.Width < TextRenderer.MeasureText(visible, label.Font).Width + 6)
            {
                var time = TimeSpan.FromTicks(ticks);
                visible = time.TotalHours >= 1 ? (int)time.TotalHours + ":" + time.Minutes.ToString("00")
                    : time.TotalMinutes >= 1 ? time.Minutes + ":" + time.Seconds.ToString("00") : visible;
            }
            label.Text = visible;
        }

        private static string DisplayMetricDuration(long ticks, bool compact = false)
        {
            var time = TimeSpan.FromTicks(ticks);
            return time.TotalHours >= 1 ? (int)time.TotalHours + (compact ? "时" : "小时") + time.Minutes + "分"
                : time.TotalMinutes >= 1 ? time.Minutes + "分" + time.Seconds + "秒" : time.Seconds + " 秒";
        }

        private sealed class TrendChart : Control
        {
            private string[] labels = new string[0];
            private long[] values = new long[0];
            private long maximum;
            private Color bar, ink, muted, line;
            private int hovered = -1;

            public TrendChart() { DoubleBuffered = true; ResizeRedraw = true; }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var behind = new SolidBrush(Parent == null ? BackColor : Parent.BackColor))
                    graphics.FillRectangle(behind, ClientRectangle);
                var bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                using (var path = MainWindow.RoundedPath(bounds, (int)Math.Round(CornerRadius * graphics.DpiX / 96.0)))
                {
                    using (var fill = new SolidBrush(BackColor)) graphics.FillPath(fill, path);
                    if (!line.IsEmpty)
                        using (var pen = new Pen(line)) graphics.DrawPath(pen, path);
                }
            }

            public void Render(string[] labels, long[] values, Color bar, Color ink, Color muted, Color line)
            {
                this.labels = labels;
                this.values = values;
                maximum = values.Length == 0 ? 0 : values.Max();
                AccessibleDescription = maximum > 0 ? "按当前周期显示各时间段的专注时长" : "暂无专注时长";
                ClearHover();
                Colors(bar, ink, muted, line);
            }

            public void Colors(Color bar, Color ink, Color muted, Color line)
            {
                this.bar = bar;
                this.ink = ink;
                this.muted = muted;
                this.line = line;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                TextRenderer.DrawText(e.Graphics, "专注时长趋势", Font, new Point(13, 9), ink);
                if (maximum == 0)
                {
                    DrawEmpty(e.Graphics);
                    return;
                }
                int bottom = Height - Font.Height - 11;
                long max = Math.Max(TimeSpan.TicksPerMinute, maximum);
                float step = Math.Max(1, Width - 28) / (float)values.Length;
                var plot = Rectangle.FromLTRB(14, 37, Math.Max(15, Width - 14), Math.Max(38, bottom));
                using (var pen = new Pen(line))
                using (var brush = new LinearGradientBrush(plot, bar, Color.FromArgb(150, bar), LinearGradientMode.Vertical))
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        int y = bottom - (bottom - 37) * i / 4;
                        e.Graphics.DrawLine(pen, 14, y, Width - 14, y);
                    }
                    e.Graphics.DrawLine(pen, 14, bottom, Width - 14, bottom);
                    for (int i = 0; i < labels.Length; i++)
                    {
                        float height = (bottom - 42) * values[i] / (float)max;
                        float x = 14 + i * step;
                        if (height > 0) e.Graphics.FillRectangle(brush, x + step * 0.28f, bottom - height, step * 0.44f, height);
                        if (i % (labels.Length > 15 ? 3 : 1) == 0)
                            TextRenderer.DrawText(e.Graphics, labels[i], Font, new Rectangle((int)x, bottom + 5, (int)step + 2, Font.Height),
                                muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    }
                }
                if (hovered >= 0)
                {
                    string text = labels[hovered] + "  ·  " + DisplayDuration(values[hovered], false);
                    int width = Math.Min(Width - 12, TextRenderer.MeasureText(text, Font).Width + 16);
                    int x = Math.Max(6, Math.Min((int)(14 + hovered * step), Width - width - 6));
                    var badge = new Rectangle(x, 35, width, Font.Height + 9);
                    PaintPill(e.Graphics, badge, 7, BackColor, bar);
                    TextRenderer.DrawText(e.Graphics, text, Font, badge, ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }

            private void DrawEmpty(Graphics graphics)
            {
                float scale = graphics.DpiX / 96f;
                int iconWidth = (int)Math.Round(38 * scale);
                int lineHeight = Math.Max((int)Math.Round(19 * scale), Font.Height);
                int firstLine = Math.Max((int)Math.Round(52 * scale), Height / 2);
                int center = Width / 2;
                int baseline = firstLine - (int)Math.Round(9 * scale);
                int step = iconWidth / 4;
                using (var pen = new Pen(line, 2 * scale))
                {
                    graphics.DrawLine(pen, center - step, baseline, center - step, baseline - 9 * scale);
                    graphics.DrawLine(pen, center, baseline, center, baseline - 15 * scale);
                    graphics.DrawLine(pen, center + step, baseline, center + step, baseline - 21 * scale);
                }
                var upper = new Rectangle(0, firstLine, Width, lineHeight);
                var lower = new Rectangle(0, firstLine + lineHeight, Width, lineHeight);
                TextRenderer.DrawText(graphics, "暂无专注时长", Font, upper, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var caption = UiFont(10))
                    TextRenderer.DrawText(graphics, "完成一次专注后，这里会显示趋势。", caption, lower, muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (maximum == 0 || e.X < 14 || e.X >= Width - 14)
                {
                    ClearHover();
                    return;
                }
                float step = Math.Max(1, Width - 28) / (float)values.Length;
                int index = (int)((e.X - 14) / step);
                if (index < 0 || index >= values.Length || values[index] <= 0) { ClearHover(); return; }
                int bottom = Height - Font.Height - 11;
                float height = (bottom - 42) * values[index] / (float)Math.Max(TimeSpan.TicksPerMinute, maximum);
                float left = 14 + (index + .28f) * step;
                if (e.X < left || e.X > left + .44f * step || e.Y < bottom - height || e.Y >= bottom)
                { ClearHover(); return; }
                if (index == hovered) return;
                hovered = index;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                ClearHover();
            }

            private void ClearHover()
            {
                if (hovered < 0) return;
                hovered = -1;
                Invalidate();
            }
        }
    }
}
