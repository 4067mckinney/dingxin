using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StudyFocus
{
    internal sealed class AppImages : IDisposable
    {
        private readonly Queue<string> pending = new Queue<string>();
        private readonly HashSet<string> requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int Capacity = 96;
        private readonly Dictionary<string, int> indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> lru = new LinkedList<string>();
        private readonly Dictionary<string, LinkedListNode<string>> lruNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Bitmap> bitmaps = new List<Bitmap>();
        private bool loading, disposed;

        public ImageList Images { get; private set; }
        public event Action Changed;

        public AppImages()
        {
            Images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(22, 22) };
            AddImage(SystemIcons.Application.ToBitmap());
        }

        public int IndexFor(string path)
        {
            int index;
            if (!indexes.TryGetValue(path, out index)) return 0;
            Touch(path);
            return index;
        }

        private void Touch(string path)
        {
            LinkedListNode<string> node;
            if (!lruNodes.TryGetValue(path, out node)) return;
            lru.Remove(node);
            lru.AddLast(node);
        }

        private void Register(string path)
        {
            lruNodes[path] = lru.AddLast(path);
        }

        private void Trim()
        {
            while (indexes.Count > Capacity && lru.First != null)
            {
                string victim = lru.First.Value;
                lru.RemoveFirst();
                lruNodes.Remove(victim);
                int index;
                if (!indexes.TryGetValue(victim, out index)) continue;
                indexes.Remove(victim);
                Images.Images.RemoveAt(index);
                if (index < bitmaps.Count)
                {
                    bitmaps[index].Dispose();
                    bitmaps.RemoveAt(index);
                }
                foreach (string key in indexes.Keys.ToList())
                    if (indexes[key] > index) indexes[key]--;
                requested.Remove(victim);
            }
        }

        public async void Load(IEnumerable<string> paths)
        {
            if (disposed) return;
            foreach (string path in paths)
                if (requested.Add(path)) pending.Enqueue(path);
            if (loading || pending.Count == 0) return;
            loading = true;
            try
            {
                while (pending.Count > 0 && !disposed)
                {
                    var batch = new string[Math.Min(12, pending.Count)];
                    for (int i = 0; i < batch.Length; i++) batch[i] = pending.Dequeue();
                    Bitmap[] icons = await Task.Run(() => batch.Select(ReadIcon).ToArray());
                    bool changed = false;
                    for (int i = 0; i < icons.Length; i++)
                    {
                        if (icons[i] == null) continue;
                        if (disposed) { icons[i].Dispose(); continue; }
                        indexes.Add(batch[i], AddImage(icons[i]));
                        Register(batch[i]);
                        Trim();
                        changed = true;
                    }
                    Trim();
                    if (changed && Changed != null) Changed();
                }
            }
            finally { loading = false; }
        }

        private int AddImage(Bitmap bitmap)
        {
            bitmaps.Add(bitmap);
            int index = Images.Images.Count;
            Images.Images.Add(bitmap);
            return index;
        }

        private static Bitmap ReadIcon(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (Icon icon = Icon.ExtractAssociatedIcon(path))
                    return icon == null ? null : icon.ToBitmap();
            }
            catch (ArgumentException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Runtime.InteropServices.ExternalException) { return null; }
        }

        public void Dispose()
        {
            disposed = true;
            pending.Clear();
            Images.Dispose();
            foreach (Bitmap bitmap in bitmaps) bitmap.Dispose();
            bitmaps.Clear();
        }
    }
}
