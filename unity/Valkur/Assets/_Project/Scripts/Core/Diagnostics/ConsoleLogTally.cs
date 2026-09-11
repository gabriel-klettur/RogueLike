using UnityEngine;

namespace Valkur.Core.Diagnostics
{
    /// <summary>
    /// Counts the errors and warnings the console received this session and keeps the last
    /// error's first line.
    ///
    /// <para><b>Why the game needs to know.</b> An error in the console is invisible from the
    /// game itself: in a build there is no console, and in the Editor it scrolls past while the
    /// author is looking at the Game view. This project's cardinal rule is a clean console, and
    /// the only surface that could say "it is not clean right now" is the one drawn over the
    /// game.</para>
    ///
    /// <para><b>Thread-safe on purpose.</b> It is fed from
    /// <c>Application.logMessageReceivedThreaded</c>, which Unity raises on whatever thread
    /// logged — the asset import workers, the job system, a <c>Task.Run</c> save. Every read and
    /// write takes the same lock; the lock is held for a few instructions and never while
    /// logging, so it cannot recurse into itself.</para>
    /// </summary>
    public sealed class ConsoleLogTally
    {
        private const int MaxMessageLength = 160;

        private readonly object _gate = new object();
        private int _errors;
        private int _warnings;
        private string _lastError = string.Empty;
        private float _lastErrorTime = -1f;

        public int Errors { get { lock (_gate) return _errors; } }
        public int Warnings { get { lock (_gate) return _warnings; } }
        public string LastError { get { lock (_gate) return _lastError; } }

        /// <summary>When the last error arrived, on whatever clock the feeder passes (the monitor
        /// uses the thread-safe <c>Stopwatch</c>), or -1 when there was none. For ORDERING only:
        /// a main-thread reader that wants "how long ago" watches <see cref="Errors"/> change.</summary>
        public float LastErrorTime { get { lock (_gate) return _lastErrorTime; } }

        /// <summary>Feeds one console message. <paramref name="realtime"/> is passed in because
        /// <c>Time</c> may not be read off the main thread.</summary>
        public void Record(LogType type, string message, float realtime)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    string line = FirstLine(message);
                    lock (_gate)
                    {
                        _errors++;
                        _lastError = line;
                        _lastErrorTime = realtime;
                    }
                    break;

                case LogType.Warning:
                    lock (_gate) _warnings++;
                    break;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _errors = 0;
                _warnings = 0;
                _lastError = string.Empty;
                _lastErrorTime = -1f;
            }
        }

        /// <summary>The first line of a message, trimmed, capped for a one-row readout.</summary>
        public static string FirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            // Two IndexOf calls rather than IndexOfAny over a char[]: a static readonly array here
            // is a static the Domain-Reload ratchet cannot see reset, and a fresh one allocates.
            int cr = message.IndexOf('\r'), lf = message.IndexOf('\n');
            int cut = cr < 0 ? lf : lf < 0 ? cr : System.Math.Min(cr, lf);
            string line = (cut >= 0 ? message.Substring(0, cut) : message).Trim();
            return line.Length > MaxMessageLength ? line.Substring(0, MaxMessageLength) : line;
        }
    }
}
