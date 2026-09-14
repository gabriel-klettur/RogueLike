namespace Valkur.Core.UI
{
    /// <summary>
    /// Every player-visible string of the ten screens BEFORE the game starts, in one place
    /// and in the language <see cref="GameLanguage"/> says.
    ///
    /// <para><b>Why this exists.</b> The pre-game menus were hard-coded English literals in
    /// fifteen files, in front of a game whose HUD, chat and loading screen are Spanish. Worse,
    /// the literals were LOAD-BEARING: <c>ExecuteOptionsItem</c> dispatched on
    /// <c>case "Inputs"</c> and <c>MainMenuUITests</c> asserted <c>Contains("New Game")</c>, so
    /// translating the menu broke the navigation AND three tests. A label that is also a key is
    /// a label nobody can change.</para>
    ///
    /// <para><b>The fix is two separate things and both are needed.</b> The strings move here,
    /// and the DISPATCH moves to an enum (<c>MainMenuItem</c>, <c>OptionsItem</c>) so what the
    /// row means no longer depends on how it is spelled. This file can then be translated,
    /// reordered or re-worded without a single behavioural risk.</para>
    ///
    /// <para><b>It is a table, not a framework.</b> Two languages, ~110 strings, no catalogue
    /// asset and no key lookup — the same judgement <c>ChatLanguage</c> made for its own panel
    /// and <c>LoadingText</c> made for the loading screen. If a third language ever arrives this
    /// is the seam a real i18n layer replaces; until then, a dictionary keyed by string would
    /// only add a way to ask for a key that does not exist.</para>
    ///
    /// <para><b>Accents are used.</b> The project's ASCII-only rule belongs to
    /// <c>LoadingText</c>, whose tips had a mojibake history; these strings are read by TMP from
    /// a UTF-8 source exactly as <c>ChatLanguage</c>'s are ("Todavía", "página", "Tú"), and
    /// "Vídeo" without its accent is a misspelling on the first screen the player reads.</para>
    /// </summary>
    public static class MenuText
    {
        private static string P(string english, string spanish) => GameLanguage.Pick(english, spanish);

        // ── Title screen ─────────────────────────────────────────────────────

        /// <summary>The game's name, drawn as particles rather than as a texture.</summary>
        public const string GameTitle = "VALKUR";

        public static string PressToStart => P("Press any key", "Pulsa cualquier tecla");

        // ── Main menu ────────────────────────────────────────────────────────

        public static string Continue => P("Continue", "Continuar");
        public static string NewGame => P("New game", "Partida nueva");
        public static string LoadGame => P("Load game", "Cargar partida");
        public static string Options => P("Options", "Opciones");
        public static string Credits => P("Credits", "Créditos");
        public static string Exit => P("Exit", "Salir");

        public static string MainMenuHint =>
            P("Move  W / S  ·  Choose  Enter  ·  Back  Esc",
              "Mover  W / S  ·  Elegir  Intro  ·  Volver  Esc");

        // ── Options ──────────────────────────────────────────────────────────

        public static string OptionsTitle => P("Options", "Opciones");
        public static string OptionsAudio => P("Audio", "Audio");
        public static string OptionsVideo => P("Video", "Vídeo");
        public static string OptionsControls => P("Controls", "Controles");
        public static string OptionsGameplay => P("Gameplay", "Juego");
        public static string OptionsBack => P("Back", "Volver");

        // ── Audio ────────────────────────────────────────────────────────────

        public static string AudioTitle => P("Audio", "Audio");
        public static string AudioMaster => P("Master", "General");
        public static string AudioMusic => P("Music", "Música");
        public static string AudioAmbient => P("Ambience", "Ambiente");
        public static string AudioSfx => P("Effects", "Efectos");
        public static string AudioTest => P("Test sound", "Sonido de prueba");
        public static string AudioTestValue => P("Play", "Probar");
        public static string AudioAdvanced => P("Advanced mixing", "Mezcla avanzada");
        public static string AudioAmbientMin => P("Ambience: minimum gap", "Ambiente: hueco mínimo");
        public static string AudioAmbientMax => P("Ambience: maximum gap", "Ambiente: hueco máximo");
        public static string AudioDuckAtten => P("Ducking: attenuation", "Atenuación al hablar");
        public static string AudioDuckHold => P("Ducking: hold", "Sostenido al hablar");
        public static string AudioDuckRelease => P("Ducking: release", "Recuperación");

        public static string AudioHint =>
            P("Adjust  ←  →  ·  Back  Esc", "Ajustar  ←  →  ·  Volver  Esc");

        // ── Video ────────────────────────────────────────────────────────────

        public static string VideoTitle => P("Video", "Vídeo");
        public static string VideoResolution => P("Resolution", "Resolución");
        public static string VideoDisplayMode => P("Display mode", "Modo de pantalla");
        public static string VideoVSync => P("Vertical sync", "Sincronía vertical");
        public static string VideoFrameCap => P("Frame limit", "Límite de fotogramas");
        public static string VideoUiScale => P("Interface size", "Tamaño de la interfaz");
        public static string VideoApply => P("Apply", "Aplicar");

        public static string VideoHint =>
            P("Change  ←  →  ·  Apply  Enter  ·  Back  Esc",
              "Cambiar  ←  →  ·  Aplicar  Intro  ·  Volver  Esc");

        public static string VideoOff => P("Off", "Desactivada");
        public static string VideoOn => P("On", "Activada");
        public static string VideoUnlimited => P("Unlimited", "Sin límite");
        public static string VideoUiScaleAuto => P("Automatic", "Automático");

        /// <summary>Live readout under the rows. Never the value the player just picked.</summary>
        public static string VideoViewport(int windowW, int windowH, int viewW, int viewH, bool exact)
            => P($"Window {windowW} × {windowH}   ·   viewport {viewW} × {viewH}   ·   " +
                 (exact ? "exact 2:1, no seams" : "not 2:1, seams possible"),
                 $"Ventana {windowW} × {windowH}   ·   área {viewW} × {viewH}   ·   " +
                 (exact ? "2:1 exacto, sin costuras" : "no es 2:1, pueden verse costuras"));

        public static string VideoEditorNote =>
            P("In the Editor the Game View size wins; set it to a fixed 2:1 size.",
              "En el Editor manda el tamaño de la Game View; ponla en un tamaño 2:1 fijo.");

        /// <summary>The revert countdown. A display mode a monitor cannot show has no way back.</summary>
        public static string VideoKeepQuestion(int seconds)
            => P($"Keep this configuration?   Enter to keep   ·   reverting in {seconds} s",
                 $"¿Mantener esta configuración?   Intro para conservarla   ·   se revierte en {seconds} s");

        public static string VideoReverted => P("Reverted to the previous configuration.",
                                                "Revertido a la configuración anterior.");

        // ── Gameplay options ─────────────────────────────────────────────────

        public static string GameplayTitle => P("Gameplay", "Juego");
        public static string GameplayLanguage => P("Language", "Idioma");
        public static string GameplayReduceMotion => P("Reduce motion", "Reducir movimiento");
        public static string GameplayScreenShake => P("Screen shake", "Temblor de cámara");
        public static string GameplayTextSize => P("Text size", "Tamaño del texto");
        public static string GameplayShowHints => P("Show hints", "Mostrar ayudas");

        public static string GameplayReduceMotionNote =>
            P("Turns off the menu's particles and panel animations.",
              "Apaga las partículas del menú y las animaciones de los paneles.");

        public static string TextSizeSmall => P("Small", "Pequeño");
        public static string TextSizeNormal => P("Normal", "Normal");
        public static string TextSizeLarge => P("Large", "Grande");

        public static string LanguageSpanish => "Español";
        public static string LanguageEnglish => "English";

        // ── Controls ─────────────────────────────────────────────────────────

        public static string ControlsTitle => P("Controls", "Controles");

        public static string ControlsIntro =>
            P("Click a row and then a key to reassign it. Right click clears it.",
              "Pulsa una fila y después una tecla para reasignarla. Clic derecho la borra.");

        public static string ControlsCapture =>
            P("Press the new key…   ·   Esc cancels   ·   right click clears",
              "Pulsa la tecla nueva…   ·   Esc cancela   ·   clic derecho la borra");

        public static string ControlsUnbound => P("unassigned", "sin asignar");
        public static string ControlsReset => P("Restore defaults", "Valores por defecto");
        public static string ControlsConflict => P("in use", "en uso");

        public static string ControlsHint =>
            P("Move  ↑  ↓  ·  Reassign  Enter  ·  Clear  Del  ·  Back  Esc",
              "Mover  ↑  ↓  ·  Reasignar  Intro  ·  Borrar  Supr  ·  Volver  Esc");

        public static string ControlsMoreInGame =>
            P("Every action, including the 24 spell slots, is in the in-game Controls screen.",
              "Todas las acciones, incluidos los 24 huecos de hechizo, están en la pantalla de Controles del juego.");

        // ── Load game ────────────────────────────────────────────────────────

        public static string LoadTitle => P("Load game", "Cargar partida");
        public static string LoadRuns => P("RUNS", "PARTIDAS");
        public static string LoadSaves => P("SAVES", "GUARDADOS");
        public static string LoadLoad => P("Load", "Cargar");
        public static string LoadRename => P("Rename", "Renombrar");
        public static string LoadDelete => P("Delete", "Borrar");
        public static string LoadNoSaves => P("No saved games yet.", "Todavía no hay partidas guardadas.");
        public static string LoadPickOne => P("Pick a save.", "Elige un guardado.");
        public static string LoadLegacyRun => P("Old run", "Partida antigua");
        public static string LoadAutoSave => P("Autosave", "Autoguardado");
        public static string LoadCorrupted => P("Corrupted", "Dañado");

        public static string LoadCorruptedDetail =>
            P("This save cannot be read. You can delete it.",
              "Este guardado no se puede leer. Puedes borrarlo.");

        public static string LoadClass => P("Class", "Clase");
        public static string LoadZone => P("Place", "Lugar");
        public static string LoadLevel => P("Level", "Nivel");
        public static string LoadXp => P("XP", "EXP");
        public static string LoadHp => P("Health", "Vida");
        public static string LoadSaved => P("Saved", "Guardado");
        public static string LoadPlaytime => P("Played", "Jugado");

        public static string LoadSelected(string name)
            => P($"Selected: {name}", $"Seleccionado: {name}");

        public static string LoadConfirmDelete(string name)
            => P($"Delete “{name}”? This cannot be undone.",
                 $"¿Borrar «{name}»? No se puede deshacer.");

        public static string LoadRenameTitle => P("New name", "Nombre nuevo");
        public static string Cancel => P("Cancel", "Cancelar");
        public static string Accept => P("Accept", "Aceptar");
        public static string Yes => P("Yes", "Sí");
        public static string No => P("No", "No");

        public static string LoadHint =>
            P("Move  ↑  ↓  ·  Switch column  ←  →  ·  Load  Enter  ·  Back  Esc",
              "Mover  ↑  ↓  ·  Cambiar columna  ←  →  ·  Cargar  Intro  ·  Volver  Esc");

        // ── Class selector ───────────────────────────────────────────────────

        public static string ClassTitle => P("Choose your character", "Elige tu personaje");
        public static string ClassBegin => P("Begin", "Empezar");
        public static string ClassHealth => P("Health", "Vida");
        public static string ClassAttack => P("Attack", "Ataque");
        public static string ClassArmour => P("Armour", "Armadura");
        public static string ClassSpeed => P("Speed", "Velocidad");
        public static string ClassMana => P("Mana", "Maná");
        public static string ClassStamina => P("Stamina", "Aguante");

        public static string ClassHint =>
            P("Choose  ←  →  ·  Begin  Enter  ·  Back  Esc",
              "Elegir  ←  →  ·  Empezar  Intro  ·  Volver  Esc");

        // ── Credits ──────────────────────────────────────────────────────────

        public static string CreditsTitle => P("Credits", "Créditos");

        public static string CreditsBody =>
            P("Valkur\n\nDesign, code and art direction\nGabriel Roca\n\n" +
              "Built with Unity 2022.3 and the URP 2D renderer.\n\n" +
              "Thanks for playing.",
              "Valkur\n\nDiseño, código y dirección de arte\nGabriel Roca\n\n" +
              "Hecho con Unity 2022.3 y el renderizador 2D de URP.\n\n" +
              "Gracias por jugar.");

        // ── Shared ───────────────────────────────────────────────────────────

        public static string Back => P("Back", "Volver");

        /// <summary>
        /// A date a person reads, not an ISO timestamp. The load panel used to print
        /// <c>2026-09-12T02:16:34</c> straight from the save's metadata.
        /// </summary>
        public static string FormatTimestamp(System.DateTime when)
        {
            var now = System.DateTime.Now;
            var day = when.Date;
            if (day == now.Date) return P($"today {when:HH:mm}", $"hoy {when:HH:mm}");
            if (day == now.Date.AddDays(-1)) return P($"yesterday {when:HH:mm}", $"ayer {when:HH:mm}");
            return GameLanguage.IsEnglish ? when.ToString("d MMM yyyy, HH:mm")
                                          : when.ToString("d MMM yyyy, HH:mm");
        }

        /// <summary>Hours and minutes, never a raw second count.</summary>
        public static string FormatPlaytime(System.TimeSpan span)
        {
            if (span.TotalMinutes < 1d) return P("under a minute", "menos de un minuto");
            if (span.TotalHours < 1d) return P($"{(int)span.TotalMinutes} min", $"{(int)span.TotalMinutes} min");
            return P($"{(int)span.TotalHours} h {span.Minutes:00} min",
                     $"{(int)span.TotalHours} h {span.Minutes:00} min");
        }
    }
}
