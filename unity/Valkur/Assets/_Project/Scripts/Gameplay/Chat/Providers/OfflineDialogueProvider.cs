using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Valkur.Data;
using Valkur.Gameplay.Chat;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// Cycles through <c>persona.dialogueLines</c> deterministically. State
    /// (current index per persona) is tracked here so the cycle survives
    /// across multiple chat opens with the same NPC within a session — but
    /// is NOT persisted to disk.
    ///
    /// Maps to Python's offline fallback in <c>chat_router.py</c> which reads
    /// <c>persona.dialogue_lines[index % len]</c> when no LLM worker is active.
    ///
    /// Domain Reload is OFF: this class is instantiated fresh by ChatSystem on
    /// each Play entry (not static), so no explicit reset is needed.
    /// </summary>
    public sealed class OfflineDialogueProvider : IChatProvider
    {
        // Offline provider is always "available" — it never needs a network.
        public bool IsOnline => true;
        public string ProviderName => "offline";

        // Per-persona cursor (key: persona.personaId or persona.displayName).
        private readonly Dictionary<string, int> _cursorByPersona = new Dictionary<string, int>();

        public Task<string> GenerateReplyAsync(
            NPCPersonaDefinition persona,
            NPCMemory memory,
            string playerText,
            CancellationToken cancellationToken)
        {
            string reply = GetNextLine(persona);
            return Task.FromResult(reply);
        }

        private string GetNextLine(NPCPersonaDefinition persona)
        {
            if (persona == null || persona.dialogueLines == null || persona.dialogueLines.Count == 0)
                return "...";

            string key = !string.IsNullOrEmpty(persona.personaId)
                ? persona.personaId
                : (!string.IsNullOrEmpty(persona.displayName) ? persona.displayName : "unknown");

            int idx = _cursorByPersona.TryGetValue(key, out int cur) ? cur : 0;
            string line = persona.dialogueLines[idx % persona.dialogueLines.Count];
            _cursorByPersona[key] = idx + 1;
            return line;
        }
    }
}
