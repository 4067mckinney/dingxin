using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StudyFocus
{
    /// <summary>
    /// 一个极轻的补间引擎：全进程只有一个 15ms 定时器，没有动画时自动停表；
    /// 每帧只让目标控件失效（Invalidate），不做整窗重绘。
    /// </summary>
    internal static class Motion
    {
        private sealed class Tween
        {
            public object Key;
            public long Start;
            public int Duration;
            public Action<float> Step;
            public Action Done;
        }

        private const int SpiGetClientAreaAnimation = 0x1042;

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(int action, int param, ref bool value, int winIni);

        private static readonly List<Tween> active = new List<Tween>();
        private static readonly List<Tween> finished = new List<Tween>();
        private static readonly Timer clock = new Timer { Interval = 15 };
        private static bool initialized;
        private static bool enabled = true;
        private static bool systemEnabled = true;
        private static bool userEnabled = true;

        internal static float LastFrameMs;
        internal static float MaxFrameMs;
        internal static int FrameCount;

        internal static bool Enabled
        {
            get { EnsureInit(); return enabled; }
            set
            {
                EnsureInit();
                if (enabled == value) return;
                enabled = value;
                if (enabled) return;
                clock.Stop();
                var pending = active.ToArray();
                active.Clear();
                foreach (var tween in pending)
                {
                    tween.Step(1f);
                    if (tween.Done != null) tween.Done();
                }
            }
        }

        internal static bool Busy { get { return active.Count > 0; } }

        internal static void Run(object key, int duration, Action<float> step, Action done = null)
        {
            EnsureInit();
            if (key == null) throw new ArgumentNullException("key");
            if (!enabled || duration <= 0)
            {
                step(1f);
                if (done != null) done();
                return;
            }
            Stop(key);
            active.Add(new Tween
            {
                Key = key,
                Start = Stopwatch.GetTimestamp(),
                Duration = duration,
                Step = step,
                Done = done
            });
            if (!clock.Enabled) clock.Start();
        }

        internal static void Stop(object key)
        {
            for (int i = active.Count - 1; i >= 0; i--)
                if (ReferenceEquals(active[i].Key, key))
                    active.RemoveAt(i);
            if (active.Count == 0) clock.Stop();
        }

        internal static bool IsRunning(object key)
        {
            for (int i = 0; i < active.Count; i++)
                if (ReferenceEquals(active[i].Key, key)) return true;
            return false;
        }

        private static void EnsureInit()
        {
            if (initialized) return;
            initialized = true;
            clock.Tick += delegate { Frame(); };
            RefreshPreference();
        }

        internal static void RefreshPreference()
        {
            if (!initialized) return;
            bool clientAnimation = systemEnabled;
            if (SystemParametersInfo(SpiGetClientAreaAnimation, 0, ref clientAnimation, 0))
                systemEnabled = clientAnimation;
            Enabled = systemEnabled && userEnabled;
        }

        internal static void SetUserPreference(bool allowAnimation)
        {
            userEnabled = allowAnimation;
            EnsureInit();
            Enabled = systemEnabled && userEnabled;
        }

        private static void Frame()
        {
            long began = Stopwatch.GetTimestamp();
            long now = Stopwatch.GetTimestamp();
            finished.Clear();
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Tween tween = active[i];
                double raw = tween.Duration <= 0 ? 1.0 : ((now - tween.Start) * 1000.0 / Stopwatch.Frequency) / tween.Duration;
                if (raw > 1.0) raw = 1.0;
                float eased = EaseOutCubic((float)raw);
                try { tween.Step(eased); }
                catch
                {
                    active.RemoveAt(i);
                    continue;
                }
                if (raw >= 1.0)
                {
                    finished.Add(tween);
                    active.RemoveAt(i);
                }
            }
            if (active.Count == 0) clock.Stop();
            for (int i = 0; i < finished.Count; i++)
            {
                Action done = finished[i].Done;
                if (done == null) continue;
                try { done(); } catch { }
            }
            float ms = (float)((Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency);
            LastFrameMs = ms;
            FrameCount++;
            if (ms > MaxFrameMs) MaxFrameMs = ms;
        }

        internal static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        internal static Color Blend(Color from, Color to, float t)
        {
            if (t <= 0f) return from;
            if (t >= 1f) return to;
            return Color.FromArgb(
                Mix(from.A, to.A, t), Mix(from.R, to.R, t), Mix(from.G, to.G, t), Mix(from.B, to.B, t));
        }

        private static int Mix(int from, int to, float t)
        {
            int value = (int)Math.Round(from + (to - from) * t);
            return value < 0 ? 0 : value > 255 ? 255 : value;
        }
    }
}
