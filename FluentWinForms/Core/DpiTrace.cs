// TEMPORAL (Fase 0): borrar este archivo y sus llamadas despues de diagnosticar.
using System.Diagnostics;
using System.Text;

namespace FluentWinForms.Core
{
    internal static class DpiTrace
    {
        private static readonly object _lock = new();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "dpi-trace.txt");
        private static int _lines;
        private const int MaxLines = 3000;

        public static void Log(string message)
        {
            lock (_lock)
            {
                if (_lines >= MaxLines) return;
                _lines++;
                try { File.AppendAllText(_path, $"[{_clock.ElapsedMilliseconds,7} ms] {message}{Environment.NewLine}"); }
                catch { }
            }
        }

        public static string Callers(int skip = 2, int take = 7)
        {
            var frames = new StackTrace(skip, false).GetFrames();
            var sb = new StringBuilder();
            for (int i = 0; i < frames.Length && i < take; i++)
            {
                if (i > 0) sb.Append(" < ");
                sb.Append(frames[i].GetMethod()?.Name);
            }
            return sb.ToString();
        }
    }
}