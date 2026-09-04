using System;
using UnityEngine;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// The language the chat panel is in, and the one place that decides it.
    ///
    /// <para>WHY THIS EXISTS AT ALL. The EN/ES button already persisted — to
    /// <c>NPCMemory.preferredLanguage</c>, per NPC, on disk — and that value was read by
    /// exactly ONE thing in the project: <c>OpenAiChatProvider</c>, when building a prompt.
    /// The default provider is the OFFLINE one, so for anybody without an API key the button
    /// wrote a field, saved it correctly, and changed nothing a player could see. Same shape
    /// as <c>animation_map.json</c> and the four casting flags: authored, round-tripped and
    /// inert.</para>
    ///
    /// <para>GLOBAL, NOT PER NPC. A language preference is a fact about the person holding
    /// the controller, not about Gatita — switching to English with her and then finding
    /// Pavel still in Spanish is the behaviour of a bug, whatever the storage says. The
    /// per-NPC field is kept and synced FROM here on every chat open, so the prompt builder
    /// goes on reading exactly what it already read and needed no change.</para>
    ///
    /// <para>What it cannot do is translate the OFFLINE dialogue. Those lines are authored
    /// Spanish recovered from the Python archive; there is no English persona to switch to,
    /// and inventing one would break the rule that nothing in the dialogue is made up. So
    /// English moves the panel's own chrome and the instruction the model is given, and
    /// leaves the authored lines alone.</para>
    /// </summary>
    public static class ChatLanguage
    {
        public const string SPANISH = "es";
        public const string ENGLISH = "en";

        private const string PREF_KEY = "valkur.chat.language";

        private static string _current = SPANISH;
        private static bool _loaded;

        /// <summary>Raised when the language changes. Never for a repeat.</summary>
        public static event Action<string> OnChanged;

        /// <summary>
        /// Static mutable state with Domain Reload off. Assignment rather than a helper
        /// call, because <c>DomainReloadStaticResetTests</c> reads this method's raw IL and
        /// only recognises a direct <c>stsfld</c>.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = SPANISH;
            _loaded = false;
            OnChanged = null;
        }

        /// <summary>The active language code, loaded from PlayerPrefs on first read.</summary>
        public static string Current
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _current = Normalize(PlayerPrefs.GetString(PREF_KEY, SPANISH));
                }
                return _current;
            }
        }

        public static bool IsEnglish => Current == ENGLISH;

        /// <summary>The two-letter code the button shows.</summary>
        public static string Label => Current.ToUpperInvariant();

        /// <summary>Switches to the other language and writes it through.</summary>
        public static string Toggle() => Set(Current == SPANISH ? ENGLISH : SPANISH);

        /// <summary>
        /// Sets the language, persists it, and announces it. Anything unrecognised falls
        /// back to Spanish rather than being stored — a preference file holding "fr" would
        /// otherwise put the panel in a language with no strings and no way back.
        /// </summary>
        public static string Set(string language)
        {
            string next = Normalize(language);
            _loaded = true;
            if (next == _current) return _current;

            _current = next;
            PlayerPrefs.SetString(PREF_KEY, next);
            PlayerPrefs.Save();

            OnChanged?.Invoke(next);
            return next;
        }

        private static string Normalize(string language) =>
            string.Equals(language, ENGLISH, StringComparison.OrdinalIgnoreCase) ? ENGLISH : SPANISH;

        // ── Panel strings ───────────────────────────────────────────────────
        //
        // A table this small rather than a localisation SYSTEM, on purpose: the chat panel is
        // the only screen in Valkur with a language control, and a project-wide i18n layer
        // built for six captions would be a framework nothing else uses. If a second screen
        // ever grows one, this is what gets replaced.

        public static string InputPlaceholder =>
            IsEnglish ? "Type a message..." : "Escribe un mensaje...";

        public static string Send => IsEnglish ? "Send" : "Enviar";

        public static string Trade => IsEnglish ? "Trade" : "Comerciar";

        public static string Accept => IsEnglish ? "Accept" : "Aceptar";

        public static string Decline => IsEnglish ? "No" : "No";

        public static string Close => IsEnglish ? "Close (ESC)" : "Cerrar (ESC)";
    }
}
