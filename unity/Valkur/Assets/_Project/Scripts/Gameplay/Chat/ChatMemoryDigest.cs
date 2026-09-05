using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// What a character still knows about the player once the verbatim window has rolled
    /// past.
    ///
    /// <para><c>NPCMemory.ephemeralHistory</c> keeps twelve messages and then forgets, so
    /// before this the only thing that survived a long acquaintance was <c>visitCount</c> —
    /// the character could say "we have spoken nine times" and not one thing about who you
    /// are. This is the second tier: a short, keyed list of facts the player volunteered and
    /// events worth remembering, appended as they happen and rendered into the system
    /// prompt.</para>
    ///
    /// <para>It is deliberately RULE-BASED and offline. Asking a model to summarise the
    /// conversation would cost a second request per exchange and would put invented facts
    /// into permanent storage; the patterns here only fire on an explicit self-disclosure
    /// ("me llamo …", "busco …") or on an intent the classifier already computes for other
    /// reasons, so a note is always something the player actually said.</para>
    ///
    /// <para>Notes store a symbolic KEY and the captured VALUE rather than a finished
    /// sentence, because the conversation's language can change after a note is written —
    /// prose captured in Spanish would still be Spanish in an English prompt.
    /// <see cref="Render"/> is where a note becomes a sentence.</para>
    /// </summary>
    [Valkur.Core.SelfHealingStatic(
        "One immutable marker table built once from string literals. Nothing writes to it " +
        "after the static initialiser, it holds no Unity object and no decision made during " +
        "a session, so it cannot go stale across a Play-mode boundary — the same reasoning " +
        "DialogueIntentClassifier records for its own keyword tables.")]
    public static class ChatMemoryDigest
    {
        public const string KEY_NAME = "name";
        public const string KEY_ORIGIN = "origin";
        public const string KEY_QUEST = "quest";
        public const string KEY_LIKES = "likes";
        public const string KEY_HATES = "hates";
        public const string KEY_INSULTED = "insulted";
        public const string KEY_CONFIDED = "confided";
        public const string KEY_FLIRTED = "flirted";
        public const string KEY_WARNED = "warned";

        /// <summary>Prefixes for a completed trade, one note per item id.</summary>
        public const string KEY_BOUGHT_PREFIX = "bought:";
        public const string KEY_SOLD_PREFIX = "sold:";

        /// <summary>
        /// How much of the player's sentence is kept after a marker. Six words holds "a mi
        /// hermana Elena, que vive lejos" and refuses a paragraph — a note is a fact, and
        /// every word of it is billed on every later message.
        /// </summary>
        private const int MAX_VALUE_WORDS = 6;

        /// <summary>
        /// Self-disclosure markers, checked in order. Each is matched on WORD boundaries
        /// against a folded copy of the line, and whatever follows it is the value.
        ///
        /// <para>The list is short on purpose. A looser pattern ("soy …") would capture
        /// "soy tonto" as the traveller's name and then repeat it back forever, which is
        /// worse than remembering nothing: a wrong note cannot be corrected by the player
        /// and outlives the conversation that produced it.</para>
        /// </summary>
        private static readonly (string marker, string key)[] Markers =
        {
            ("me llamo", KEY_NAME),
            ("mi nombre es", KEY_NAME),
            ("llamame", KEY_NAME),
            ("my name is", KEY_NAME),
            ("i am called", KEY_NAME),
            ("call me", KEY_NAME),

            ("vengo de", KEY_ORIGIN),
            ("soy de", KEY_ORIGIN),
            ("i come from", KEY_ORIGIN),
            ("i am from", KEY_ORIGIN),
            ("i m from", KEY_ORIGIN),

            ("estoy buscando", KEY_QUEST),
            ("necesito encontrar", KEY_QUEST),
            ("busco", KEY_QUEST),
            ("i am looking for", KEY_QUEST),
            ("i m looking for", KEY_QUEST),
            ("i need to find", KEY_QUEST),

            ("me encanta", KEY_LIKES),
            ("me gustan", KEY_LIKES),
            ("me gusta", KEY_LIKES),
            ("i like", KEY_LIKES),
            ("i love", KEY_LIKES),

            ("no soporto", KEY_HATES),
            ("detesto", KEY_HATES),
            ("odio", KEY_HATES),
            ("i hate", KEY_HATES),
            ("i can t stand", KEY_HATES),
        };

        // ── Capture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads one player line and writes at most one note from it. Returns true when the
        /// digest changed, which is the caller's cue to persist.
        ///
        /// <para>A disclosure wins over an intent: "me llamo Bruno, y odio a los lobos"
        /// records the name rather than the mood. Both are only ever ONE note per line, so a
        /// chatty message cannot flood the eight slots.</para>
        /// </summary>
        public static bool RecordPlayerLine(NPCMemory memory, string text, DialogueIntent intent)
        {
            if (memory == null || string.IsNullOrWhiteSpace(text)) return false;

            if (TryExtract(text, out string key, out string value))
                return Write(memory, key, value);

            string eventKey = EventKeyFor(intent);
            return eventKey != null && Write(memory, eventKey, "");
        }

        /// <summary>
        /// Remembers a trade that actually completed — one note per item id, so buying bread
        /// every morning keeps one slot and not thirty.
        /// </summary>
        public static bool RecordTrade(
            NPCMemory memory, string itemId, string itemName, int quantity, bool playerBought)
        {
            if (memory == null || quantity <= 0) return false;
            if (string.IsNullOrWhiteSpace(itemId) && string.IsNullOrWhiteSpace(itemName)) return false;

            string id = !string.IsNullOrWhiteSpace(itemId) ? itemId : itemName;
            string label = !string.IsNullOrWhiteSpace(itemName) ? itemName : itemId;
            string key = (playerBought ? KEY_BOUGHT_PREFIX : KEY_SOLD_PREFIX) + id;
            string value = quantity > 1 ? quantity + "x " + label : label;

            return Write(memory, key, value);
        }

        /// <summary>
        /// The note an intent is worth on its own, or null for the intents that say nothing
        /// durable about the player. Trade and small talk are deliberately absent: everyone
        /// asks a vendor about prices, so "asked about prices" is not a fact about anyone.
        /// </summary>
        private static string EventKeyFor(DialogueIntent intent)
        {
            switch (intent)
            {
                case DialogueIntent.Insult: return KEY_INSULTED;
                case DialogueIntent.Distress: return KEY_CONFIDED;
                case DialogueIntent.Flirt: return KEY_FLIRTED;
                case DialogueIntent.Danger: return KEY_WARNED;
                default: return null;
            }
        }

        // ── Storage ─────────────────────────────────────────────────────────

        /// <summary>
        /// Writes one note, replacing any note with the same key and moving it to the end.
        ///
        /// <para>Recency ordering is what makes the cap survivable: the oldest note is the
        /// one dropped, and re-stating a fact refreshes it rather than spending a slot.
        /// Returns false when nothing changed, so an unchanged line costs no file
        /// write.</para>
        /// </summary>
        internal static bool Write(NPCMemory memory, string key, string value)
        {
            if (memory == null || string.IsNullOrWhiteSpace(key)) return false;
            if (memory.digest == null) memory.digest = new List<MemoryNote>();

            value = value?.Trim() ?? "";

            int existing = IndexOf(memory.digest, key);
            if (existing >= 0)
            {
                bool sameValue = string.Equals(memory.digest[existing].value, value, StringComparison.Ordinal);
                bool alreadyNewest = existing == memory.digest.Count - 1;
                if (sameValue && alreadyNewest) return false;
                memory.digest.RemoveAt(existing);
            }

            memory.digest.Add(new MemoryNote
            {
                key = key,
                value = value,
                timestampIso8601 = DateTime.UtcNow.ToString("o"),
            });

            while (memory.digest.Count > NPCMemory.DIGEST_CAP)
                memory.digest.RemoveAt(0);

            return true;
        }

        private static int IndexOf(List<MemoryNote> notes, string key)
        {
            for (int i = 0; i < notes.Count; i++)
                if (string.Equals(notes[i].key, key, StringComparison.Ordinal)) return i;
            return -1;
        }

        // ── Rendering ───────────────────────────────────────────────────────

        /// <summary>
        /// One note as a sentence the model can read, in the language the conversation is
        /// being held in. An unknown key renders as its raw value rather than being dropped,
        /// so a note written by a later version is never silently invisible.
        /// </summary>
        public static string Render(MemoryNote note, string language)
        {
            bool en = string.Equals(language, ChatLanguage.ENGLISH, StringComparison.OrdinalIgnoreCase);
            string v = note.value ?? "";

            if (note.key != null && note.key.StartsWith(KEY_BOUGHT_PREFIX, StringComparison.Ordinal))
                return en ? $"They once bought {v} from you." : $"Alguna vez te compró {v}.";
            if (note.key != null && note.key.StartsWith(KEY_SOLD_PREFIX, StringComparison.Ordinal))
                return en ? $"They once sold you {v}." : $"Alguna vez te vendió {v}.";

            switch (note.key)
            {
                case KEY_NAME: return en ? $"Their name is {v}." : $"Se llama {v}.";
                case KEY_ORIGIN: return en ? $"They come from {v}." : $"Viene de {v}.";
                case KEY_QUEST: return en ? $"They are looking for {v}." : $"Anda buscando {v}.";
                case KEY_LIKES: return en ? $"They like {v}." : $"Le gusta {v}.";
                case KEY_HATES: return en ? $"They hate {v}." : $"Odia {v}.";
                case KEY_INSULTED:
                    return en
                        ? "They have been rude to you before."
                        : "Alguna vez te ha faltado al respeto.";
                case KEY_CONFIDED:
                    return en
                        ? "They once told you they were having a hard time."
                        : "Alguna vez te contó que lo estaba pasando mal.";
                case KEY_FLIRTED:
                    return en ? "They have flirted with you." : "Ha coqueteado contigo.";
                case KEY_WARNED:
                    return en
                        ? "They warned you about danger out there."
                        : "Te avisó de que hay peligro ahí fuera.";
                default: return v;
            }
        }

        // ── Extraction ──────────────────────────────────────────────────────

        /// <summary>
        /// The fact one line discloses, if any. Internal so the tests can state the contract
        /// on the patterns themselves rather than through a whole conversation.
        /// </summary>
        internal static bool TryExtract(string text, out string key, out string value)
        {
            key = null;
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string folded = Fold(text, out int[] map);

            foreach (var entry in Markers)
            {
                int at = folded.IndexOf(" " + entry.marker + " ", StringComparison.Ordinal);
                if (at < 0) continue;

                int valueStart = at + entry.marker.Length + 2;
                if (valueStart >= folded.Length) continue;

                string captured = TakeWords(text, map[valueStart], MAX_VALUE_WORDS);
                if (string.IsNullOrWhiteSpace(captured)) continue;

                key = entry.key;
                value = captured;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Up to <paramref name="maxWords"/> words of <paramref name="text"/> from
        /// <paramref name="start"/>, stopping at a sentence break so "me llamo Bruno. ¿Y tú?"
        /// records a name and not a question.
        /// </summary>
        private static string TakeWords(string text, int start, int maxWords)
        {
            if (start < 0 || start >= text.Length) return "";

            var words = new List<string>(maxWords);
            var word = new StringBuilder();

            for (int i = start; i < text.Length && words.Count < maxWords; i++)
            {
                char c = text[i];
                if (c == '.' || c == ',' || c == ';' || c == '!' || c == '?' || c == '\n')
                    break;

                if (char.IsWhiteSpace(c))
                {
                    if (word.Length > 0) { words.Add(word.ToString()); word.Clear(); }
                    continue;
                }
                word.Append(c);
            }

            if (word.Length > 0 && words.Count < maxWords) words.Add(word.ToString());
            return string.Join(" ", words).Trim();
        }

        /// <summary>
        /// A lowercase, accent-stripped, punctuation-collapsed copy of the line, padded with
        /// a space at each end so a marker can be matched on word boundaries — plus a map
        /// from every folded character back to the ORIGINAL index it came from.
        ///
        /// <para>The map is the reason this cannot reuse
        /// <c>DialogueIntentClassifier.Normalize</c>: that one decomposes the whole string at
        /// once, which changes its length, and the captured value has to be lifted from the
        /// player's own text with their accents and capitals intact. Folding per character
        /// keeps the two in step.</para>
        ///
        /// <para>Runs once per player message, never per frame.</para>
        /// </summary>
        private static string Fold(string text, out int[] map)
        {
            var sb = new StringBuilder(text.Length + 2);
            var indices = new List<int>(text.Length + 2);

            sb.Append(' ');
            indices.Add(0);
            bool lastWasSpace = true;

            for (int i = 0; i < text.Length; i++)
            {
                string decomposed = char.ToLowerInvariant(text[i])
                    .ToString()
                    .Normalize(NormalizationForm.FormD);

                bool appended = false;
                foreach (char d in decomposed)
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark)
                        continue;
                    if (!char.IsLetterOrDigit(d)) continue;

                    sb.Append(d);
                    indices.Add(i);
                    appended = true;
                    lastWasSpace = false;
                }

                if (!appended && !lastWasSpace)
                {
                    sb.Append(' ');
                    indices.Add(i);
                    lastWasSpace = true;
                }
            }

            if (!lastWasSpace)
            {
                sb.Append(' ');
                indices.Add(text.Length);
            }

            map = indices.ToArray();
            return sb.ToString();
        }
    }
}
