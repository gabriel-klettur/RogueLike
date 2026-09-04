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
        /// Generates a reply asynchronously.
        ///
        /// The result carries what the character SAYS and, optionally, a
        /// <see cref="TradeProposal"/> — an offer the game then validates against the live
        /// shop before anything moves. A provider that cannot propose trades returns
        /// <see cref="ChatReply.Spoken"/>.
        ///
        /// <see cref="ChatRequest"/> carries the persona, the player's running memory
        /// (visitCount, ephemeral history, language preference) and what the game knows
        /// about trading with this character. An implementation decides which of those it
        /// uses — the offline provider ignores most of it.
        /// </summary>
        Task<ChatReply> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken);
    }
}
