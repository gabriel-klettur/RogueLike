using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Chat.Providers;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>chatprovider</c> command.
    ///
    /// Two jobs. The first is diagnosis: when an NPC answers with a canned line, the only
    /// question that matters is whether the language model was even asked, and the answer
    /// involves a settings asset, an environment variable, a gitignored file and a session
    /// flag that latches on a rejected key. Reading that off one status line beats guessing.
    ///
    /// The second is comparison. Being able to force the same NPC through both providers and
    /// ask it the same question is how you tell "the model is off" from "the model is on and
    /// the prompt is wrong", and those look identical from the Game view.
    ///
    /// It never prints the key, or any part of it — only whether a name resolved.
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        /// <summary>
        /// Mirrors <c>GameplaySceneSetup.CHAT_LLM_SETTINGS_RESOURCE_PATH</c> rather than
        /// sharing it: that one is private to a bootstrap partial, and a console command
        /// that silently looked somewhere else than the thing it reports on would be worse
        /// than useless. <c>ChatLlmSettingsTests</c> asserts the two agree.
        /// </summary>
        private const string CHAT_LLM_SETTINGS_PATH = "Chat/ChatLlmSettings";

        private void RegisterChatCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "chatprovider",
                Aliases  = new[] { "chatp" },
                Usage    = "chatprovider [status|offline|llm|auto]",
                Help     = "show or force which provider answers NPC chat",
                Category = "chat",
                Handler  = args => CmdChatProvider(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "journal",
                Aliases  = new[] { "diario" },
                Usage    = "journal [npcs|<day index>]",
                Help     = "read the conversation journal of the open chat",
                Category = "chat",
                Handler  = args => CmdJournal(args)
            });
        }

        /// <summary>
        /// The <c>journal</c> probe.
        ///
        /// <para>It exists for the same reason <c>faces</c> does: the journal is written by
        /// the message path, sealed by a clock and read by a panel, and every one of those
        /// three can fail in a way the others hide. Without this, "did today's page get
        /// written" is answered by finding a directory under
        /// <c>Application.persistentDataPath</c>, and "did midnight seal yesterday" is not
        /// answerable at all without waiting for midnight.</para>
        /// </summary>
        private void CmdJournal(string[] args)
        {
            string verb = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (verb == "npcs")
            {
                ReportJournalArchives();
                return;
            }

            var chat = ChatSystem.Instance;
            if (chat == null || !chat.IsChatOpen)
            {
                Log("No chat is open. 'journal npcs' lists every archive on disk.");
                return;
            }

            var pages = chat.ListJournalPages();
            if (pages.Count == 0)
            {
                Log($"'{chat.ActiveNpcKey}' has no journal pages yet.");
                return;
            }

            if (verb.Length > 0 && int.TryParse(verb, out int index))
            {
                ReportJournalPage(chat, pages, index);
                return;
            }

            Log($"{pages.Count} page(s) for '{chat.ActiveNpcKey}', newest first:");
            for (int i = 0; i < pages.Count; i++)
            {
                bool today = pages[i].DayKey == ChatDayClock.TodayKey;
                Log($"  {i + 1,2}. {pages[i].Label(english: false)}{(today ? "  (today)" : "")}");
            }
            Log("journal <n> prints one.");
        }

        /// <summary>Prints one day, 1-based to match the list above it.</summary>
        private void ReportJournalPage(
            ChatSystem chat, System.Collections.Generic.List<ChatJournalPageRef> pages, int index)
        {
            if (index < 1 || index > pages.Count)
            {
                Log($"No page {index}. There are {pages.Count}.");
                return;
            }

            var pageRef = pages[index - 1];
            ChatJournalPage page = chat.LoadJournalPage(pageRef);
            if (page == null)
            {
                Log($"Page {index} ({pageRef.Stem}) could not be read.");
                return;
            }

            Log($"— {pageRef.Label(english: false)} — {page.entries.Count} line(s), " +
                $"{page.conversations} conversation(s){(page.IsSealed ? ", sealed" : "")}");

            for (int i = 0; i < page.entries.Count; i++)
            {
                var entry = page.entries[i];
                string who = entry.role == ChatJournalEntry.ROLE_SYSTEM
                    ? "·"
                    : (string.IsNullOrEmpty(entry.speaker) ? entry.role : entry.speaker);
                Log($"  {who}: {entry.text}");
            }
        }

        /// <summary>
        /// Every archive on disk, named by the directory slug rather than by the character.
        /// Slugging is one-way, so this is the honest answer — and it is enough to tell an
        /// archive that exists from one that does not.
        /// </summary>
        private void ReportJournalArchives()
        {
            var slugs = ChatJournalStore.ListArchivedSlugs();
            if (slugs.Count == 0)
            {
                Log("No journals on disk yet.");
                return;
            }

            Log($"{slugs.Count} archive(s):");
            for (int i = 0; i < slugs.Count; i++)
            {
                var pages = ChatJournalStore.ListPagesBySlug(slugs[i]);
                string newest = pages.Count > 0 ? pages[0].CalendarDate : "—";
                Log($"  {slugs[i]}: {pages.Count} page(s), newest {newest}");
            }
        }

        private void CmdChatProvider(string[] args)
        {
            var settings = Resources.Load<ChatLlmSettings>(CHAT_LLM_SETTINGS_PATH);
            if (settings == null)
            {
                Log($"No ChatLlmSettings at Resources/{CHAT_LLM_SETTINGS_PATH}. " +
                    "NPC chat is answering from each persona's authored lines.");
                return;
            }

            string verb = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";

            switch (verb)
            {
                case "offline": SetMode(settings, ChatProviderMode.ForceOffline); break;
                case "llm":
                case "online":  SetMode(settings, ChatProviderMode.ForceOnline); break;
                case "auto":    SetMode(settings, ChatProviderMode.Auto); break;
                case "status":  break;
                default:
                    Log("Usage: chatprovider [status|offline|llm|auto]");
                    return;
            }

            ReportStatus(settings);
        }

        /// <summary>
        /// Writes the mode onto the live asset.
        ///
        /// The provider re-reads it per message, so this takes effect on the very next line
        /// the player types — no reload, no Play-mode restart. In the Editor the change also
        /// sticks to the asset on disk, which is what you want while tuning and is worth
        /// knowing before you wonder why it survived a restart.
        /// </summary>
        private void SetMode(ChatLlmSettings settings, ChatProviderMode mode)
        {
            settings.mode = mode;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(settings);
#endif
        }

        private void ReportStatus(ChatLlmSettings settings)
        {
            bool hasKey = EnvFile.Has(settings.apiKeyEnvVar);

            Log($"mode      : {settings.mode}");
            Log($"model     : {settings.model}");
            Log($"key       : {settings.apiKeyEnvVar} = {(hasKey ? "resolved" : "NOT FOUND")}");
            if (!hasKey) Log($"            looked in the environment, then {EnvFile.ResolvePath()}");
            Log($"history   : {settings.historyTurns} turns sent as context");
            Log($"max tokens: {settings.maxOutputTokens} via '{settings.maxOutputTokensField}'");

            string ceiling = settings.maxRequestsPerSession > 0
                ? settings.maxRequestsPerSession + " requests/session"
                : "no ceiling";
            Log($"budget    : {ceiling}, min {settings.minSecondsBetweenRequests:0.##}s apart");

            if (ServiceLocator.TryGet<IChatProvider>(out var provider))
            {
                Log($"live      : {provider.ProviderName} (online={provider.IsOnline})");

                // The counter is the only place the session's spending is visible at all —
                // there is no bill to read from inside the game.
                if (provider is OpenAiChatProvider openAi)
                    Log($"spent     : {openAi.RequestsThisSession} requests this session" +
                        (openAi.CooldownRemaining > 0f
                            ? $" (next allowed in {openAi.CooldownRemaining:0.#}s)"
                            : ""));
            }
            else
            {
                Log("live      : no IChatProvider registered — bootstrap has not run.");
            }

            var chat = ChatSystem.Instance;
            if (chat != null && chat.IsChatOpen && chat.ActivePersona != null)
                Log($"talking to: {chat.ActivePersona.displayName}");
        }
    }
}
