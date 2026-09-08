namespace Valkur.Core.Boot
{
    /// <summary>
    /// Every player-visible string the loading screen can show, in one place.
    ///
    /// The project has no localisation framework and inventing one here would be a
    /// different job; what this fixes is the smaller, real defect: the loading
    /// screen was the only surface in the game still written in English, sitting
    /// in front of a UI that says "Comerciar", "Diario", "Reiniciar" and "GUARDAR",
    /// with its strings scattered through a 520-line MonoBehaviour. One file is the
    /// seam a real i18n layer would replace, and until then it is at least the same
    /// language as the rest of the game.
    /// </summary>
    public static class LoadingText
    {
        public const string Preparing   = "Preparando";
        public const string LoadingRes  = "Cargando recursos";
        public const string BuildingWorld = "Construyendo el mundo";
        public const string Ready       = "Listo";
        public const string Failed      = "El arranque termino con errores";
        public const string FailedHint  = "El mundo puede estar incompleto. Revisa la consola (tecla `) o el comando 'boot'.";
        public const string Timeout     = "El arranque esta tardando mas de lo normal";
        public const string TimeoutHint = "Sigue trabajando. Si no avanza, vuelve al menu y reintenta.";

        /// <summary>
        /// Rotating tips. Data, not a <c>static readonly string[]</c> buried in the
        /// controller — which is how five of the ten shipped tips came to name keys
        /// retired on 2026-09-05 ("Press F1-F12 to open the in-game editors",
        /// "F10 opens the Buildings editor", "F8 is the Tile editor", "F12 lets you
        /// author boss FSMs", "Spells can be remapped from the F4 in-game editor").
        /// The fourteen editor toggles ship UNBOUND; the way in is Escape. F4 was
        /// never the spells editor.
        ///
        /// <b>Every line here must name a control or a rule that exists today.</b>
        /// <c>LoadingTipsTests</c> refuses any tip that mentions an F-key.
        /// </summary>
        [Valkur.Core.SelfHealingStatic(
            "Immutable table of literal strings, written once at type init and never " +
            "mutated. Holds no Unity object, so it cannot carry a destroyed reference " +
            "or a stale registration across a Play session.")]
        public static readonly string[] Tips =
        {
            "Escape abre el Editor General: desde ahi se llega a todos los editores del juego.",
            "Tab cambia entre postura de Guerra y de Paz. En Paz no puedes danar a nadie por accidente.",
            "Puedes reasignar cualquier tecla en ESC -> Controles, incluidos los botones del raton.",
            "El acento grave (`) abre la consola de desarrollo.",
            "Al morir dejas tu cuerpo donde caiste: camina hasta un altar para resucitar entero.",
            "Volver al altar te devuelve la vida completa; el rescate automatico solo una parte.",
            "El mercado sigue un ciclo de seis a catorce dias. Guarda el mineral y vende en el Pico.",
            "Habla con un vendedor antes de comerciar: el descuento depende de como vaya la charla.",
            "Cada conversacion se guarda en el Diario, una pagina por personaje y por dia.",
            "La nieve se acumula de verdad sobre los tejados, y se derrite mas rapido de dia.",
            "Cocinar sube de nivel. Las recetas de seis ingredientes o mas piden una estacion.",
            "Los gemelos oscuros esquivan los proyectiles: apunta a donde van a estar.",
            "Tus partidas rotan cinco copias de seguridad con suma de verificacion.",
        };
    }
}
