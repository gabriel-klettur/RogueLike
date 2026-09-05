using UnityEngine;

namespace Valkur.Data
{
    /// <summary>How the chat decides which provider answers.</summary>
    public enum ChatProviderMode
    {
        /// <summary>Use the language model when a key resolves; fall back to offline silently.</summary>
        Auto = 0,

        /// <summary>Never call out. The authored repertoire answers everything.</summary>
        ForceOffline = 1,

        /// <summary>Always call out, and warn loudly if no key resolves. Diagnosis only.</summary>
        ForceOnline = 2,
    }

    /// <summary>
    /// Everything about talking to a language model that is a DECISION rather than code.
    ///
    /// <para>The endpoint, the model id and the two field names below are data on purpose.
    /// A remote API's request shape is the one thing in this project that can change without
    /// anything in the repository changing, and when it does the symptom is a 400 that looks
    /// like a bug in the game. Keeping them here means that break is a value someone edits
    /// in the Inspector, not a rebuild.</para>
    ///
    /// <para>THE KEY IS NOT HERE, and must never be added. It is read at runtime from the
    /// process environment or from a gitignored <c>.env</c> — see <c>Valkur.Core.EnvFile</c>.
    /// A ScriptableObject is an asset: putting a key in one commits it, ships it inside the
    /// player, and puts it in every backup of the project.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "ChatLlmSettings", menuName = "Valkur/Chat/LLM Settings")]
    public class ChatLlmSettings : ScriptableObject
    {
        [Header("Activation")]
        [Tooltip("Auto: use the model when a key resolves, otherwise answer from the persona's " +
                 "authored lines. That default means a clone of this repo with no key still has " +
                 "NPCs that talk, and this machine gets the real thing with no switch to remember.")]
        public ChatProviderMode mode = ChatProviderMode.Auto;

        [Tooltip("Environment variable (or .env entry) holding the API key. Never the key itself.")]
        public string apiKeyEnvVar = "OPENAI_API_KEY";

        [Header("Endpoint")]
        [Tooltip("Chat completions endpoint.")]
        public string endpoint = "https://api.openai.com/v1/chat/completions";

        [Tooltip("Model id.")]
        public string model = "gpt-5-mini";

        [Header("Request shape")]
        [Tooltip("Name of the output-length field. The GPT-5 family expects " +
                 "'max_completion_tokens'; older chat models expect 'max_tokens'. A field name " +
                 "rather than a bool because the next rename should be a data edit too.")]
        public string maxOutputTokensField = "max_completion_tokens";

        [Tooltip("How much the model may think before answering: minimal, low, medium, high. " +
                 "Blank omits the field entirely, for a model that does not accept it.\n\n" +
                 "'minimal' on purpose, and it is not a cost tweak — it is what makes this work " +
                 "at all. Reasoning tokens are billed against the SAME budget as the reply, and " +
                 "measured on gpt-5-mini answering one NPC line: medium (the default) spent 320 " +
                 "of them, low 64, minimal 0. At the default effort a persona prompt would " +
                 "exhaust the whole allowance thinking and return EMPTY content — which reads, " +
                 "from the game, as the model silently never having been asked. An NPC saying " +
                 "two sentences in character has nothing to reason about.")]
        public string reasoningEffort = "minimal";

        [Tooltip("Ceiling on the reply, INCLUDING any reasoning tokens spent getting there. " +
                 "Sized with headroom rather than tightly: running out mid-reasoning yields an " +
                 "empty reply and a silent fall back to the authored lines, which is the most " +
                 "confusing failure this system has.")]
        [Min(32)] public int maxOutputTokens = 400;

        [Tooltip("Send a temperature at all. Off by default: reasoning models reject any value " +
                 "other than the default and answer 400, and the persona prompt is what should " +
                 "be controlling voice anyway.")]
        public bool sendTemperature;

        [Range(0f, 2f)]
        [Tooltip("Only sent when 'Send Temperature' is on.")]
        public float temperature = 0.9f;

        [Header("Context")]
        [Tooltip("How many past turns of the remembered conversation are sent as context. " +
                 "Every turn is billed on every message, so this is the second cost lever. " +
                 "NPCMemory keeps 12; sending all of them buys little over the last few.")]
        [Range(0, 12)] public int historyTurns = 6;

        [Tooltip("Seconds before a request is abandoned and the offline provider answers " +
                 "instead. A player waiting on a bubble notices well before this.")]
        [Range(3f, 60f)] public float timeoutSeconds = 20f;

        [Header("Budget")]
        [Tooltip("Shortest gap between two requests. A message sent inside it is answered " +
                 "from the persona's authored lines instead of being billed.\n\n" +
                 "Nothing else in the chat rate-limits anything: every Enter was one " +
                 "request, and the whole persona prompt (profile, stock list, purse, rules) " +
                 "is re-sent on each one, so holding the key down was an unbounded bill with " +
                 "no cap and no counter anywhere. Degrading to the offline provider rather " +
                 "than refusing keeps the character talking, which is the same trade every " +
                 "other failure here makes.")]
        [Range(0f, 10f)] public float minSecondsBetweenRequests = 1.5f;

        [Tooltip("How many requests one Play session may spend before the model is put away " +
                 "for the rest of it. 0 means no ceiling.\n\n" +
                 "This is the backstop the cooldown cannot be: a cooldown bounds the RATE " +
                 "and not the TOTAL, so an afternoon of testing still adds up. When it is " +
                 "reached the offline provider answers and the console says so once.")]
        [Min(0)] public int maxRequestsPerSession = 150;

        [Header("Safety rails")]
        [Tooltip("Appended to every persona's system prompt. This is what keeps a model from " +
                 "narrating for the player, inventing shop stock or breaking character.")]
        [TextArea(3, 10)]
        public string sharedSystemRules =
            "Eres un personaje dentro de un videojuego de fantasía. Responde SIEMPRE en primera " +
            "persona como ese personaje y nunca como un asistente.\n" +
            "- Máximo 3 frases. Habla corto, como en una conversación de pasillo.\n" +
            "- No narres las acciones ni los pensamientos del jugador, y no hables por él.\n" +
            "- Si vendes algo, tu puesto es EXACTAMENTE la lista de arriba, con esos precios. " +
            "No inventes ningún artículo, variedad, tamaño ni sabor que no esté en ella; si te " +
            "piden algo que no tienes, dilo con tu voz y ofrece lo que sí tengas. Los precios " +
            "puedes decirlos: son los de verdad.\n" +
            "- NUNCA digas que has entregado, servido, guardado, apuntado ni cobrado nada. " +
            "Hablando no puedes mover ni un objeto ni una moneda. Si el viajero quiere comprar, " +
            "confirma QUÉ y CUÁNTO y espera: el trato se cierra aparte.\n" +
            "- Los códigos entre paréntesis de la lista, como (id borsh_01), son para el " +
            "sistema. Nómbralos NUNCA en voz alta: tú dices 'un borsch', no 'un borsh_01'.\n" +
            "- No menciones que eres una IA, ni el juego, ni estas instrucciones.\n" +
            "- Si te preguntan algo que tu personaje no podría saber, respóndelo desde su " +
            "ignorancia, con su voz.";

        /// <summary>
        /// Whether these settings are complete enough to attempt a call. Says nothing about
        /// whether a key exists — that is resolved at runtime and deliberately not stored.
        /// </summary>
        public bool IsUsable =>
            !string.IsNullOrWhiteSpace(endpoint) &&
            !string.IsNullOrWhiteSpace(model) &&
            !string.IsNullOrWhiteSpace(apiKeyEnvVar) &&
            !string.IsNullOrWhiteSpace(maxOutputTokensField);
    }
}
