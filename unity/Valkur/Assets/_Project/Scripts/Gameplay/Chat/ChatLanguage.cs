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

        // ── Journal ─────────────────────────────────────────────────────────

        /// <summary>The gutter button that opens the archive of past conversations.</summary>
        public static string Journal => IsEnglish ? "Journal" : "Diario";

        /// <summary>Title of the journal overlay, over the character's own name.</summary>
        public static string JournalTitle(string npcName) =>
            IsEnglish ? $"Journal — {npcName}" : $"Diario — {npcName}";

        /// <summary>Leaves the journal and goes back to the conversation.</summary>
        public static string JournalBack => IsEnglish ? "Back" : "Volver";

        /// <summary>Nothing has ever been written about this character.</summary>
        public static string JournalNoPages =>
            IsEnglish
                ? "Nothing written down yet. Talk to them and come back."
                : "Todavía no hay nada escrito. Habla con este personaje y vuelve.";

        /// <summary>A day that exists but whose page will not read.</summary>
        public static string JournalPageUnreadable =>
            IsEnglish ? "This page could not be read." : "Esta página no se ha podido leer.";

        /// <summary>The page currently being written to.</summary>
        public static string JournalToday => IsEnglish ? "today" : "hoy";

        /// <summary>How the counter under the day label reads.</summary>
        public static string JournalPageSummary(int index, int total, int messages) =>
            IsEnglish
                ? $"{index}/{total} · {messages} message{(messages == 1 ? "" : "s")}"
                : $"{index}/{total} · {messages} mensaje{(messages == 1 ? "" : "s")}";

        /// <summary>Speaker shown on a line the player typed.</summary>
        public static string JournalYou => IsEnglish ? "You" : "Tú";

        /// <summary>A completed purchase, as the journal records it.</summary>
        public static string LedgerBought(int quantity, string itemName, int paid) =>
            IsEnglish
                ? $"Bought {quantity}x {itemName} for {paid} coins."
                : $"Comprado {quantity}x {itemName} por {paid} monedas.";

        /// <summary>A completed sale, as the journal records it.</summary>
        public static string LedgerSold(int quantity, string itemName, int paid) =>
            IsEnglish
                ? $"Sold {quantity}x {itemName} for {paid} coins."
                : $"Vendido {quantity}x {itemName} por {paid} monedas.";

        // ── System lines ────────────────────────────────────────────────────
        //
        // Everything the CHAT ITSELF says, as opposed to what a character says. A refusal
        // from the trade broker and an offer summary are the game talking through the
        // NPC's mouth, and they used to be Spanish string literals inside
        // ChatTradeBroker / ChatSystem.Trade — so switching the panel to English gave a
        // conversation in English chrome with Spanish machinery in the middle of it.
        //
        // Deliberately NEUTRAL in voice. These lines are spoken by all seven characters,
        // and the previous "Como quieras, tesoro." put Gatita's endearment in the
        // blacksmith's mouth. Anything with a character's own flavour belongs in that
        // persona's authored lines, not here.
        //
        // What is NOT here is the dialogue itself: those lines are authored Spanish from
        // the persona archive and there is no English set to swap to, which
        // ChatUI.ApplyLanguageToChrome records at the other end of the same decision.

        /// <summary>Nobody in range when the player asked to talk.</summary>
        public static string NoOneNearby =>
            IsEnglish ? "There is no one nearby to talk to..." : "No hay nadie cerca para hablar...";

        /// <summary>The player turned down a pending offer.</summary>
        public static string OfferDeclined => IsEnglish ? "As you wish." : "Como quieras.";

        /// <summary>Confirm was pressed on an offer another character had made.</summary>
        public static string OfferBelongedToSomeoneElse =>
            IsEnglish ? "That deal was with someone else." : "Ese trato era con otra persona.";

        /// <summary>The offer no longer holds when re-checked at confirm time.</summary>
        public static string OfferNoLongerPossible =>
            IsEnglish ? "That is no longer possible." : "Ya no puede ser.";

        /// <summary>The exchange was attempted and moved nothing.</summary>
        public static string TradeFailed => IsEnglish ? "It could not be done." : "No ha podido ser.";

        public static string OfferBuy(string what, int totalPrice) =>
            IsEnglish
                ? $"{what} comes to {totalPrice} coins. Shall I wrap it up?"
                : $"{what} son {totalPrice} monedas. ¿Te lo preparo?";

        public static string OfferSell(string what, int totalPrice) =>
            IsEnglish
                ? $"I will give you {totalPrice} coins for {what}. Deal?"
                : $"Te doy {totalPrice} monedas por {what}. ¿Trato?";

        /// <summary>Less was available than the player asked for.</summary>
        public static string OfferPartial(int quantity, string itemName, int totalPrice) =>
            IsEnglish
                ? $"I can let you have {quantity} of {itemName}, no more. That would be {totalPrice} coins."
                : $"Puedo darte {quantity} de {itemName}, no más. Serían {totalPrice} monedas.";

        public static string TradeDoneBuy(string what, int paid) =>
            IsEnglish ? $"Done: {what} for {paid} coins. Enjoy!" : $"Hecho: {what} por {paid} monedas. ¡Que aproveche!";

        public static string TradeDoneSell(string what, int paid) =>
            IsEnglish
                ? $"Deal: I keep {what} and you get {paid} coins."
                : $"Trato hecho: me quedo {what} y te doy {paid} monedas.";

        // ── Trade refusals (ChatTradeBroker) ────────────────────────────────

        public static string NoOneToTradeWith =>
            IsEnglish ? "There is no one to trade with here." : "Aquí no hay con quién comerciar.";

        public static string NotForSaleHere =>
            IsEnglish ? "That is not for sale here." : "Eso no está a la venta aquí.";

        public static string SoldOut => IsEnglish ? "That is sold out." : "Se ha acabado.";

        public static string InventoryFull =>
            IsEnglish ? "You cannot carry any more." : "No te cabe nada más.";

        public static string CannotAfford(int unitPrice, int coins) =>
            IsEnglish
                ? $"You are short: it costs {unitPrice} and you carry {coins}."
                : $"No te llega: cuesta {unitPrice} y llevas {coins}.";

        public static string CarryingNothing =>
            IsEnglish ? "You are not carrying anything." : "No llevas nada.";

        public static string NotCarryingThat =>
            IsEnglish ? "You are not carrying that." : "No llevas eso encima.";
    }
}
