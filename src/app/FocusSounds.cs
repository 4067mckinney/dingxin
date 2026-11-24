using System;
using System.IO;
using System.Media;
using System.Text;

namespace StudyFocus
{
    internal static class FocusSounds
    {
        private static readonly SoundPlayer start = new SoundPlayer(Chime(523.25, 659.25, 783.99));
        private static readonly SoundPlayer end = new SoundPlayer(Chime(659.25, 523.25, 392.00));

#if FOCUS_TEST
        internal static Action<bool> ObservePlayback;
#endif

        public static void PlayStart() { Play(true); }
        public static void PlayEnd() { Play(false); }

        private static void Play(bool starting)
        {
#if FOCUS_TEST
            if (ObservePlayback != null) { ObservePlayback(starting); return; }
#endif
            try { (starting ? start : end).Play(); }
            catch (InvalidOperationException) { (starting ? SystemSounds.Asterisk : SystemSounds.Exclamation).Play(); }
            catch (TimeoutException) { (starting ? SystemSounds.Asterisk : SystemSounds.Exclamation).Play(); }
        }

        private static Stream Chime(params double[] notes)
        {
            const int sampleRate = 16000;
            const int stepSamples = 2400;
            const int noteSamples = 1960;
            int dataLength = notes.Length * stepSamples * sizeof(short);
            var stream = new MemoryStream(44 + dataLength);
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * sizeof(short));
                writer.Write((short)sizeof(short));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);
                foreach (double frequency in notes)
                {
                    double phaseStep = 2 * Math.PI * frequency / sampleRate;
                    for (int i = 0; i < stepSamples; i++)
                    {
                        double envelope = Math.Max(0, Math.Min(i / 128.0, (noteSamples - i) / 600.0));
                        envelope = Math.Min(1, envelope);
                        double phase = phaseStep * i;
                        writer.Write((short)(7600 * envelope * (Math.Sin(phase) + 0.16 * Math.Sin(2 * phase))));
                    }
                }
            }
            stream.Position = 0;
            return stream;
        }
    }
}
