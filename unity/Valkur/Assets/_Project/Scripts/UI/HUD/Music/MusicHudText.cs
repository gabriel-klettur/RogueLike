using UnityEngine;

namespace Valkur.UI.HUD
{
    /// <summary>
    /// Every string the music panel shows, in one place and in Spanish. The panel's pixel face
    /// spells capitals, digits and a little punctuation and no accents, so the labels drawn in
    /// it avoid accented words; the tooltips are TMP and carry accents freely.
    /// </summary>
    public static class MusicHudText
    {
        public const string Idle = "SIN MUSICA";
        public const string Paused = "EN PAUSA";
        public const string Muted = "SILENCIO";

        public const string TipPrevious = "Anterior";
        public const string TipNext = "Siguiente";
        public const string TipPlay = "Reproducir";
        public const string TipPause = "Pausa";
        public const string TipMute = "Silenciar";
        public const string TipUnmute = "Quitar el silencio";
        public const string TipVolume = "Volumen";
        public const string TipResonanceOpen = "Resonancia";
        public const string TipResonanceClose = "Ocultar la resonancia";
        public const string TipClose = "Cerrar";
        public const string TipSeek = "Clic o arrastre para saltar aquí";
        public const string TipNoPlaylist = "No hay lista de reproducción";
        public const string TipCloseBody = "Vuelve a abrirlo desde el icono de la bandeja";
        public const string TipResonanceBody = "El espectro de la canción y su forma entera";
        public const string TipVolumeBody = "Clic en una muesca o rueda del ratón";
        public const string TipIdleBody = "Ahora mismo no suena ninguna pista";

        /// <summary>"Pepitoria · pista 3 de 13".</summary>
        public static string GroupLine(string group, Vector2Int position)
        {
            if (string.IsNullOrEmpty(group)) return string.Empty;
            return position.y > 0 ? $"{group} · pista {position.x} de {position.y}" : group;
        }

        /// <summary>"117 BPM · La menor", or whatever of the two is known.</summary>
        public static string TempoLine(float bpm, string keySpanish)
        {
            string tempo = bpm > 0f ? Mathf.RoundToInt(bpm) + " BPM" : string.Empty;
            if (string.IsNullOrEmpty(keySpanish)) return tempo;
            return string.IsNullOrEmpty(tempo) ? keySpanish : tempo + " · " + keySpanish;
        }
    }
}
