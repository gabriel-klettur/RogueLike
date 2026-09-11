using System.Text;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// The <c>music</c> console probe.
    ///
    /// <para>The panel draws from four independent sources — the audio service, the catalog's
    /// analysed data, the pre-volume signal tap and the beat clock — and each can fail in a way
    /// the others hide: a flat resonance looks the same whether the song is silent, the tap is
    /// dead or the catalog has no envelope. This answers "which one" in a line, the way
    /// <c>minimap</c> and <c>boot</c> do for theirs.</para>
    /// </summary>
    public sealed partial class MusicPlayerHUD
    {
        private bool _consoleRegistered;

        private void RegisterConsoleCommand()
        {
            if (_consoleRegistered || !DevConsole.HasInstance) return;
            _consoleRegistered = true;
            DevConsole.Instance.RegisterCommand(new DevConsole.ConsoleCommand
            {
                Name     = "music",
                Aliases  = new[] { "musica" },
                Usage    = "music [abrir|cerrar|resonancia|reset]",
                Help     = "estado del panel de música: pista, datos del catálogo, señal y ventana",
                Category = "hud",
                Handler  = args => DevConsole.Instance.Print(RunCommand(args)),
            });
        }

        /// <summary>Execute a <c>music</c> command and return its report. Public for tests.</summary>
        public string RunCommand(string[] args)
        {
            string sub = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            switch (sub)
            {
                case "abrir":
                case "open":
                    SetHidden(false);
                    return "[music] Panel abierto.";
                case "cerrar":
                case "close":
                    SetHidden(true);
                    return "[music] Panel cerrado; se abre desde el icono de la bandeja.";
                case "resonancia":
                case "resonance":
                    SetExpanded(!_expanded);
                    return _expanded ? "[music] Resonancia abierta." : "[music] Resonancia cerrada.";
                case "reset":
                    ResetDock();
                    return "[music] Panel devuelto a su esquina.";
            }
            return Report();
        }

        private string Report()
        {
            var sb = new StringBuilder();
            sb.Append("[music] ");
            if (_audio == null) return sb.Append("sin servicio de audio.").ToString();

            bool playing = _audio.IsMusicPlaying, paused = _audio.IsMusicPaused;
            sb.Append(playing ? "sonando" : paused ? "en pausa" : "parado");
            sb.Append(" · ").Append(string.IsNullOrEmpty(_audio.CurrentTrackTitle) ? "-" : _audio.CurrentTrackTitle);
            sb.Append(" (").Append(_audio.CurrentTrackId).Append(')');
            var clip = _audio.CurrentMusicClip;
            if (clip != null)
                sb.Append(" · ").Append(MusicTrackInfo.FormatTime(_audio.CurrentMusicTime))
                  .Append('/').Append(MusicTrackInfo.FormatTime(clip.length));
            sb.Append(" · vol ").Append(Mathf.RoundToInt(_audio.MusicVolume * 100f)).Append('%');

            sb.Append("\n  catálogo: ");
            if (_track == null) sb.Append("pista no encontrada");
            else
            {
                int env = _track.DecodeEnvelope().Length;
                sb.Append(_track.bpm > 0f ? _track.bpm.ToString("0.#") + " BPM" : "SIN BPM");
                sb.Append(" · ").Append(string.IsNullOrEmpty(_track.key) ? "sin tonalidad"
                    : MusicTrackInfo.KeyInSpanish(_track.key) + " (confianza " + _track.keyConfidence.ToString("0.00")
                      + (_track.keyConfidence >= MusicTrackInfo.KeyConfidenceFloor ? ")" : ", no se muestra)"));
                sb.Append(" · envolvente ").Append(env > 0 ? env + " tramos" : "AUSENTE");
                sb.Append(" · ").Append(_track.beatTimes != null ? _track.beatTimes.Length : 0).Append(" pulsos");
            }

            sb.Append("\n  señal: ");
            if (_signal == null) sb.Append("sin fuente pre-volumen");
            else
            {
                var probe = new float[256];
                int n = _signal.ReadMusicSignal(probe);
                float peak = 0f;
                for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(probe[i]));
                sb.Append(n > 0 ? $"viva, pico {peak:0.000}" : "sin muestras").Append(" @ ").Append(_signal.MusicSignalSampleRate).Append(" Hz");
            }
            sb.Append(" · reloj ").Append(_clock != null || Valkur.Infrastructure.MusicBeatClock.Instance != null ? "presente" : "ausente");

            sb.Append("\n  ventana: ").Append(_hidden ? "cerrada" : "abierta");
            sb.Append(_expanded ? " con resonancia" : string.Empty);
            sb.Append(" · ").Append(_style.widthTexels).Append('x').Append(_style.HeightTexels(_expanded)).Append(" texels a ")
              .Append(_pixelScale).Append(" px · alfa ").Append(Alpha.ToString("0.00"));
            sb.Append(" · esquina (").Append(_dockRight.ToString("0")).Append(", ").Append(_dockBottom.ToString("0")).Append(')');
            sb.Append(" · motas ").Append(_motes != null ? _motes.Alive : 0);
            return sb.ToString();
        }
    }
}
