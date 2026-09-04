using System.Collections.Generic;
using System.Text;
using Valkur.Data;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// Turns a persona into the system prompt that makes a model answer AS that character.
    ///
    /// <para>This is the only reader of <see cref="PersonaProfileDefinition"/>, and the
    /// reason that asset exists: the prose in it — origin, background, boundaries, speech
    /// habits, local lore — is worth nothing to the runtime and is the whole difference
    /// between Gatita and a helpful assistant wearing her name.</para>
    ///
    /// <para>It sends the shop EXACTLY as the shop has it: every item on the counter by name
    /// and id, at the price <c>GetBuyPrice</c> charges, with the stock remaining, plus the
    /// coins in the player's purse. An earlier revision withheld all of that on the reasoning
    /// that "a model handed an inventory writes prices for it" — half true, and it produced
    /// something far worse. With no inventory the model invents one: Gatita offered apples,
    /// pears, plums, blueberries and blackberries, none of which exist in this game, and
    /// deflected four straight requests for a price because she had none she was allowed to
    /// say. Given the real list she can only name real things at real prices.</para>
    ///
    /// <para>The player's own inventory is still not sent. Nothing here needs it, and it is
    /// the one list long enough to crowd the character out of its own prompt.</para>
    /// </summary>
    public static class PersonaPromptBuilder
    {
        /// <summary>Cap on any single list folded into the prompt. Beyond this it is padding.</summary>
        private const int MAX_ITEMS_PER_SECTION = 6;

        /// <summary>
        /// The system prompt for <paramref name="persona"/>.
        ///
        /// <paramref name="sharedRules"/> comes last on purpose: the character sketch reads
        /// as description and the rules read as instruction, and a model follows the
        /// instruction it saw most recently more reliably than one buried above a page of
        /// lore.
        /// </summary>
        public static string BuildSystemPrompt(
            NPCPersonaDefinition persona, NPCMemory memory, ChatTradeContext trade,
            string sharedRules, string language)
        {
            var sb = new StringBuilder(1024);
            if (persona == null) return sharedRules ?? "";

            string name = !string.IsNullOrWhiteSpace(persona.displayName)
                ? persona.displayName
                : persona.personaId;

            sb.Append("Eres ").Append(name).Append('.');
            if (!string.IsNullOrWhiteSpace(persona.role) && persona.role != "generic")
                sb.Append(" Tu papel en el mundo: ").Append(persona.role).Append('.');
            sb.AppendLine();

            AppendLine(sb, "Tono", persona.tone);

            var profile = persona.profile;
            if (profile != null)
            {
                AppendLine(sb, "Origen", profile.origin);
                AppendLine(sb, "Quién eres", profile.background);
                AppendList(sb, "Lo que quieres", profile.goals);

                if (profile.traits != null)
                {
                    AppendList(sb, "Virtudes", profile.traits.positive);
                    AppendList(sb, "Defectos", profile.traits.negative);
                    AppendList(sb, "Manías", profile.traits.quirks);
                }

                if (profile.speech != null)
                {
                    AppendLine(sb, "Registro", profile.speech.register);
                    AppendList(sb, "Palabras que usas", profile.speech.slang);
                    AppendList(sb, "Muletillas", profile.speech.fillerWords);
                    AppendList(sb, "Frases tuyas", profile.speech.catchphrases);
                    AppendLine(sb, "Puntuación", profile.speech.punctuation);
                    AppendLine(sb, "Cómo coqueteas", profile.speech.flirtStyle);
                }

                if (profile.humour != null && profile.humour.enabled)
                {
                    AppendLine(sb, "Humor", profile.humour.style);
                    AppendList(sb, "Bromeas sobre", profile.humour.topics);
                    AppendList(sb, "Ejemplos de tu humor", profile.humour.examples);
                }

                if (profile.knowledge != null)
                {
                    AppendList(sb, "Sabes de", profile.knowledge.domain);
                    AppendList(sb, "Cosas que solo tú sabes", profile.knowledge.localLore);
                    AppendList(sb, "No hablas de", profile.knowledge.tabooTopics);
                }

                if (profile.smallTalk != null)
                {
                    AppendList(sb, "Te gusta hablar de", profile.smallTalk.topicsPreferred);
                    AppendList(sb, "Evitas", profile.smallTalk.topicsAvoid);
                    AppendList(sb, "Ejemplos de cómo hablas", profile.smallTalk.examples);
                }

                if (profile.moods != null && profile.moods.enabled)
                    AppendLine(sb, "Tu ánimo de base", profile.moods.baseline);

                AppendList(sb, "Límites que nunca cruzas", profile.boundaries);

                if (profile.negotiation != null)
                {
                    AppendLine(sb, "Cómo negocias", profile.negotiation.style);
                    AppendList(sb, "Frases al regatear", profile.negotiation.phrases);
                }
            }

            AppendRelationship(sb, memory);
            AppendPurse(sb, trade);
            AppendStyleRules(sb, persona, language);

            if (!string.IsNullOrWhiteSpace(sharedRules))
            {
                sb.AppendLine();
                sb.AppendLine("Reglas:");
                sb.AppendLine(sharedRules.Trim());
            }

            return sb.ToString();
        }

        /// <summary>
        /// The remembered conversation, oldest first, as alternating turns.
        ///
        /// Trimmed to <paramref name="maxTurns"/> from the END, because the last exchange is
        /// what a reply has to follow. Every turn here is billed on every message, so this is
        /// where the cost of remembering is actually paid.
        /// </summary>
        public static List<(string role, string content)> BuildHistory(NPCMemory memory, int maxTurns)
        {
            var turns = new List<(string, string)>();
            var history = memory?.ephemeralHistory;
            if (history == null || maxTurns <= 0) return turns;

            int start = history.Count > maxTurns ? history.Count - maxTurns : 0;
            for (int i = start; i < history.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(history[i].content)) continue;
                turns.Add((history[i].role == "user" ? "user" : "assistant", history[i].content));
            }
            return turns;
        }

        // ── Sections ────────────────────────────────────────────────────────

        /// <summary>
        /// How well this character knows the player. Stated as a relationship rather than as
        /// a number, because "friendshipScore: 40" invites a model to talk about the score.
        /// </summary>
        private static void AppendRelationship(StringBuilder sb, NPCMemory memory)
        {
            if (memory == null) return;

            sb.AppendLine();
            if (memory.visitCount <= 1)
                sb.AppendLine("Es la primera vez que hablas con este viajero.");
            else if (memory.visitCount <= 4)
                sb.AppendLine($"Has hablado con este viajero {memory.visitCount} veces; empiezas a reconocerle.");
            else
                sb.AppendLine($"Este viajero te visita a menudo ({memory.visitCount} veces); ya es un conocido.");

            if (memory.friendshipScore >= 40) sb.AppendLine("Le tienes cariño.");
            else if (memory.friendshipScore <= -40) sb.AppendLine("No te cae bien y se te nota, sin ser grosero.");
        }

        /// <summary>
        /// What this character can see about the traveller's ability to pay, and nothing
        /// else about the shop.
        ///
        /// <para>Stated as a fact the NPC already knows rather than as an instruction, so it
        /// colours the whole reply instead of producing a line about money bolted onto the
        /// end. A vendor who can see an empty purse offers the cheap thing, or offers
        /// nothing and is kind about it; one who can see a full one upsells. That is the
        /// difference the player feels between an NPC and a vending machine.</para>
        ///
        /// <para>The cheapest price is real — it comes from the same <c>GetBuyPrice</c> the
        /// counter charges — so the NPC quoting it cannot contradict the shop. No other
        /// price and no stock list is ever sent, because a model handed an inventory writes
        /// prices for the rest of it.</para>
        /// </summary>
        private static void AppendPurse(StringBuilder sb, ChatTradeContext trade)
        {
            if (!trade.IsVendor) return;

            sb.AppendLine();
            if (trade.StockCount <= 0)
            {
                sb.AppendLine("Hoy no tienes nada que vender; el puesto está vacío.");
                return;
            }

            if (trade.PlayerCoins <= 0)
            {
                AppendCounter(sb, trade);
                sb.Append("El viajero no lleva ni una moneda encima, y lo más barato que ")
                  .Append("vendes cuesta ").Append(trade.CheapestPrice)
                  .AppendLine(". No puede comprarte nada hoy y tú lo sabes: no le ofrezcas " +
                              "cosas que no puede pagar.");
                return;
            }

            AppendCounter(sb, trade);

            sb.Append("El viajero lleva ").Append(trade.PlayerCoins).Append(" monedas. ");

            if (trade.AffordableCount <= 0)
                sb.Append("No le llega para nada de lo que vendes; lo más barato son ")
                  .Append(trade.CheapestPrice).AppendLine(" monedas.");
            else if (trade.AffordableCount >= trade.StockCount)
                sb.AppendLine("Puede permitirse cualquier cosa de tu puesto.");
            else
                sb.Append("Puede permitirse ").Append(trade.AffordableCount)
                  .Append(" de las ").Append(trade.StockCount)
                  .AppendLine(" cosas que vendes.");
        }

        /// <summary>
        /// The counter, item by item, with the prices the shop actually charges.
        ///
        /// <para>Withholding this was a deliberate mistake. The reasoning — "a model handed
        /// an inventory writes prices for it" — is half true and produced something far
        /// worse: with NO inventory the model invents one outright. Measured in a shipped
        /// conversation, Gatita offered apples, pears, plums, blueberries and blackberries,
        /// not one of which exists in this game, then said "aquí tienes, dos manzanas" for a
        /// sale that never happened. A player who asked "¿cuánto cuestan?" was deflected
        /// four times running, because she had no price she was allowed to say.</para>
        ///
        /// <para>Item IDs go in alongside the names because they are what a purchase is
        /// executed against; a character never speaks them, but naming them here is what
        /// lets a proposed trade be matched to a real row rather than to a description.</para>
        /// </summary>
        private static void AppendCounter(StringBuilder sb, ChatTradeContext trade)
        {
            var stock = trade.Stock;
            if (stock == null || stock.Count == 0) return;

            sb.AppendLine("Esto y SOLO esto es lo que hay hoy en tu puesto:");
            foreach (var line in stock)
            {
                sb.Append("  - ").Append(line.DisplayName)
                  .Append(" (id ").Append(line.ItemId).Append(") — ")
                  .Append(line.Price).Append(" monedas, quedan ")
                  .Append(line.Stock).AppendLine(".");
            }
            sb.AppendLine();
        }

        private static void AppendStyleRules(StringBuilder sb, NPCPersonaDefinition persona, string language)
        {
            sb.AppendLine();
            sb.Append("Responde en ")
              .Append(language == "en" ? "inglés" : "español")
              .Append(", en un máximo de ")
              .Append(persona.maxSentences > 0 ? persona.maxSentences : 3)
              .AppendLine(" frases.");

            if (!string.IsNullOrWhiteSpace(persona.verbosity))
                sb.Append("Extensión: ").Append(persona.verbosity).AppendLine(".");

            // The emoji palette is only meaningful when the persona allows emoji at all, and
            // handing a model a specific set is what stops it reaching for the generic ones.
            if (persona.useEmoji)
            {
                var palette = persona.profile?.speech?.emojiPalette;
                if (palette != null && palette.Count > 0)
                    sb.Append("Puedes usar estos emoji, con moderación: ")
                      .AppendLine(string.Join(" ", Take(palette, MAX_ITEMS_PER_SECTION)));
            }
            else
            {
                sb.AppendLine("No uses emoji.");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static void AppendLine(StringBuilder sb, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            sb.Append(label).Append(": ").AppendLine(value.Trim());
        }

        private static void AppendList(StringBuilder sb, string label, List<string> values)
        {
            if (values == null || values.Count == 0) return;

            var kept = Take(values, MAX_ITEMS_PER_SECTION);
            if (kept.Count == 0) return;

            sb.Append(label).Append(": ").AppendLine(string.Join("; ", kept));
        }

        private static List<string> Take(List<string> values, int max)
        {
            var kept = new List<string>(max);
            foreach (string v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                kept.Add(v.Trim());
                if (kept.Count >= max) break;
            }
            return kept;
        }
    }
}
