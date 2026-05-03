using System.Threading;
using System.Threading.Tasks;
using Valkur.Data;
using Valkur.Gameplay.Chat;

namespace Valkur.Gameplay.Chat.Providers
{
    /// <summary>
    /// Generates an NPC reply to a player message. Implementations may be
    /// offline (cycle through persona.dialogueLines) or online (call a remote
    /// LLM). The ChatSystem schedules the reply text in chunks for delivery
    /// independently of how it was produced.
    ///
    /// Maps to Python's abstract <c>chat_provider.ChatProvider</c> base class.
    /// </summary>
    public interface IChatProvider
    {
        /// <summary>
        /// True if the provider can reach its backend (e.g. network up).
        /// Offline providers return true always.
        /// </summary>
        bool IsOnline { get; }

        /// <summary>Short label for UI ("offline", "openai", etc.).</summary>
        string ProviderName { get; }

        /// <summary>
        /// Generates a reply asynchronously. Receives the persona and the
        /// player's running memory (visitCount, ephemeral history, language
        /// preference) as context for personalized replies. Implementation
        /// decides whether and how to use them — offline provider can simply
        /// ignore memory.
        /// </summary>
        Task<string> GenerateReplyAsync(
            NPCPersonaDefinition persona,
            NPCMemory memory,
            string playerText,
            CancellationToken cancellationToken);
    }
}
