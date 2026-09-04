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

            if (ServiceLocator.TryGet<IChatProvider>(out var provider))
                Log($"live      : {provider.ProviderName} (online={provider.IsOnline})");
            else
                Log("live      : no IChatProvider registered — bootstrap has not run.");

            var chat = ChatSystem.Instance;
            if (chat != null && chat.IsChatOpen && chat.ActivePersona != null)
                Log($"talking to: {chat.ActivePersona.displayName}");
        }
    }
}
