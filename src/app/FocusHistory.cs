using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace StudyFocus
{
    internal sealed class FocusUse
    {
        public long From { get; set; }
        public long To { get; set; }
        public long Round { get; set; }
        public string App { get; set; }
        public bool SystemApp { get; set; }
        public bool Blocked { get; set; }

        public FocusUse Copy() { return (FocusUse)MemberwiseClone(); }
    }

    internal sealed class FocusHistory
    {
        private readonly string directory;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Dictionary<DateTime, List<FocusUse>> months = new Dictionary<DateTime, List<FocusUse>>();
        private FocusUse current;
        private string currentPath;
        public int Revision { get; private set; }

        public FocusHistory(string data)
        {
            directory = Path.Combine(data, "FocusHistory");
        }

        public void Observe(DateTimeOffset now, Session session, string path, string blockedPath)
        {
            long tick = now.UtcTicks;
            long round = session == null ? 0 : session.StartedUtc;
            if (current != null)
            {
                long boundary = round == current.Round || session == null ? tick : Math.Min(tick, round);
                // Gaps in foreground samples can mean the PC slept or the session was locked.
                if (boundary > current.To && boundary - current.To <= TimeSpan.FromSeconds(3).Ticks)
                    current.To = boundary;
                bool newSpan = round != current.Round || currentPath != path ||
                    Month(current.From) != Month(tick) || tick < current.To ||
                    tick - current.To > TimeSpan.FromSeconds(3).Ticks || current.To - current.From >= TimeSpan.FromSeconds(30).Ticks;
                if (newSpan) Close();
            }
            if (round == 0) return;
            if (current == null)
            {
                currentPath = path;
                current = Entry(tick, round, path, false);
            }
            if (blockedPath != null) Append(Entry(tick, round, blockedPath, true));
        }

        public void Close()
        {
            if (current == null) return;
            if (current.To > current.From) Append(current);
            current = null;
            currentPath = null;
        }

        public IEnumerable<FocusUse> Read(DateTime start, DateTime end)
        {
            long from = new DateTimeOffset(start).UtcTicks;
            long until = new DateTimeOffset(end).UtcTicks;
            for (var month = Month(from).AddMonths(-1); month <= Month(until); month = month.AddMonths(1))
                foreach (var item in LoadMonth(month))
                    if (item.From < until && (item.Blocked ? item.From >= from : item.To > from)) yield return item;
            if (current != null && current.From < until && current.To > from) yield return current.Copy();
        }

        internal static DateTime Local(long ticks)
        {
            return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
        }

        private static DateTime Month(long ticks)
        {
            var date = Local(ticks);
            return new DateTime(date.Year, date.Month, 1);
        }

        private static FocusUse Entry(long now, long round, string path, bool blocked)
        {
            return new FocusUse
            {
                From = now, To = now, Round = round, Blocked = blocked,
                App = path == null ? null : AppCatalog.DisplayName(path),
                SystemApp = path != null && ProcessGuard.IsSystemApplication(path)
            };
        }

        private string FileName(DateTime month)
        {
            return Path.Combine(directory, month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl");
        }

        private List<FocusUse> LoadMonth(DateTime month)
        {
            List<FocusUse> items;
            if (months.TryGetValue(month, out items)) return items;
            items = new List<FocusUse>();
            string path = FileName(month);
            if (File.Exists(path))
                foreach (string line in File.ReadLines(path))
                {
                    try
                    {
                        var item = json.Deserialize<FocusUse>(line);
                        if (item != null && item.From > 0 && item.To >= item.From && item.To <= DateTime.MaxValue.Ticks) items.Add(item);
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
            months.Add(month, items);
            return items;
        }

        private void Append(FocusUse item)
        {
            var month = Month(item.From);
            var items = LoadMonth(month);
            Directory.CreateDirectory(directory);
            File.AppendAllText(FileName(month), json.Serialize(item) + Environment.NewLine);
            items.Add(item);
            Revision++;
        }
    }
}
