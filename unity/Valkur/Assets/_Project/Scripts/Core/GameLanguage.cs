using System;
using UnityEngine;

namespace Valkur.Core
{
    /// <summary>
    /// The language the whole game is in, and the one place that decides it.
    ///
    /// <para><b>Why this exists.</b> <c>ChatLanguage</c> already owned a persisted,
    /// announced EN/ES preference and its own doc argued the right principle — "a language
    /// preference is a fact about the person holding the controller". It was right and it
    /// was scoped to one panel: the ten screens the player sees BEFORE the chat exists were
    /// hard-coded English while the loading screen, the HUD and the chat were Spanish. Three
    /// languages on adjacent screens is not a localisation gap, it is two models.</para>
    ///
    /// <para><b>It lives in Core</b> because every layer needs it — <c>LoadingText</c> and
    /// <c>MenuText</c> are Core, the chat is Gameplay, the menus are UI — and Core may
    /// reference none of them. <c>ChatLanguage</c> now forwards here and keeps its whole API,
    /// its PlayerPrefs key and its per-NPC sync, so nothing that already read it changed.</para>
    ///
    /// <para><b>The key is the one the chat already wrote</b> (<c>valkur.chat.language</c>),
    /// deliberately: renaming it would silently reset every player who had chosen English.
    /// The name is now wrong and the data is right, which is the correct side to be wrong on.</para>
    ///
    /// <para>What it cannot do is translate the authored NPC dialogue — those lines are
    /// recovered Spanish and there is no English persona to switch to. English moves the
    /// game's own chrome and leaves the authored lines alone.</para>
    ///
    /// <para><b>A TEST THAT FORCES THE LANGUAGE MUST RESTORE IT IN A <c>finally</c>, NOT IN A
    /// TEARDOWN.</b> This value lives in PlayerPrefs: MACHINE state that survives the run, the
    /// Editor and the reboot. A run that is CUT SHORT — an abort, a domain reload, a crash —
    /// never reaches TearDown, and the machine is left in whatever language the last test set.
    /// Measured: one aborted fixture left this whole checkout in Spanish, and two other sessions
    /// saw their own windows change language with nothing to explain it. Same rule the project
    /// already records for a probe that touches <c>Debug.unityLogger.logEnabled</c>.</para>
    /// </summary>
    public static class GameLanguage
    {
        public const string SPANISH = "es";
        public const string ENGLISH = "en";

        /// <summary>
        /// The key <c>ChatLanguage</c> shipped with. Kept verbatim so an existing preference
        /// survives this move; see the class note.
        /// </summary>
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

        /// <summary>The two-letter code a button shows.</summary>
        public static string Label => Current.ToUpperInvariant();

        /// <summary>Switches to the other language and writes it through.</summary>
        public static string Toggle() => Set(Current == SPANISH ? ENGLISH : SPANISH);

        /// <summary>
        /// Sets the language, persists it, and announces it. Anything unrecognised falls
        /// back to Spanish rather than being stored — a preference file holding "fr" would
        /// otherwise put the game in a language with no strings and no way back.
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

        /// <summary>Picks between two authored strings. The shape every table here uses.</summary>
        public static string Pick(string english, string spanish) => IsEnglish ? english : spanish;

        private static string Normalize(string language) =>
            string.Equals(language, ENGLISH, StringComparison.OrdinalIgnoreCase) ? ENGLISH : SPANISH;
    }
}
