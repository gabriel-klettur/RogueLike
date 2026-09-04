using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Editor.Chat
{
    /// <summary>
    /// Builds the shipped chat personas from
    /// <c>tools/chat/generated/chat_personas_manifest.json</c>, which
    /// <c>tools/chat/build_persona_manifest.py</c> produces from the seven personas
    /// Valkur shipped in Python.
    ///
    /// Two assets per character, for the reason <see cref="PersonaProfileDefinition"/>
    /// documents, plus the by-name rows of <see cref="ChatAssignmentCatalog"/>.
    ///
    /// <para><b>Re-running is safe and is the point.</b> The plain menu item fills only
    /// fields that are still empty, so a greeting or a dialogue line a designer rewrote
    /// in the Inspector survives every later import — the same "creation defaults, an
    /// authored value always wins" contract <c>TilesetRulesetImporter</c> uses for
    /// terrain names. The Overwrite variant is the escape hatch for when the manifest
    /// itself is the thing that changed.</para>
    ///
    /// <para>It uses <see cref="EditorUtility.SetDirty"/> and never
    /// <c>Undo.RecordObject</c>. A bulk import that records undo puts every created
    /// asset on the GLOBAL editor undo stack, and the first thing to pop it reverts them
    /// all in memory while the good data sits on disk — that is what happened to 193
    /// building templates, and the note in CLAUDE.md exists because of it.</para>
    /// </summary>
    public static class ChatPersonaImporter
    {
        private const string MANIFEST_RELATIVE_PATH = "tools/chat/generated/chat_personas_manifest.json";
        private const string PERSONA_DIR = "Assets/_Project/Data/ChatPersonas";
        private const string PROFILE_DIR = "Assets/_Project/Data/ChatPersonas/Profiles";
        private const string CATALOG_PATH = "Assets/_Project/Resources/Chat/ChatAssignmentCatalog.asset";

        [MenuItem("Valkur/Chat/Import Personas")]
        public static void Import() => Run(overwriteAuthored: false);

        [MenuItem("Valkur/Chat/Import Personas (Overwrite Authored)")]
        public static void ImportOverwrite()
        {
            if (!EditorUtility.DisplayDialog(
                    "Overwrite authored persona fields?",
                    "Every greeting, dialogue line, tone and profile field will be replaced by " +
                    "the manifest, discarding anything edited in the Inspector.\n\n" +
                    "The plain 'Import Personas' fills only empty fields and is what you " +
                    "normally want.",
                    "Overwrite", "Cancel"))
                return;

            Run(overwriteAuthored: true);
        }

        private static void Run(bool overwriteAuthored)
        {
            string manifestPath = ResolveManifestPath();
            if (!File.Exists(manifestPath))
            {
                Debug.LogError(
                    $"[ChatPersonaImporter] No manifest at '{manifestPath}'. " +
                    "Run: python tools/chat/build_persona_manifest.py");
                return;
            }

            if (!(MiniJsonRuntime.Deserialize(File.ReadAllText(manifestPath)) is Dictionary<string, object> root))
            {
                Debug.LogError($"[ChatPersonaImporter] '{manifestPath}' is not a JSON object.");
                return;
            }

            EnsureDirectory(PERSONA_DIR);
            EnsureDirectory(PROFILE_DIR);

            var personasById = new Dictionary<string, NPCPersonaDefinition>();
            int created = 0, updated = 0;

            foreach (var entry in AsList(root, "personas"))
            {
                if (!(entry is Dictionary<string, object> row)) continue;

                string personaId = Str(row, "personaId");
                if (string.IsNullOrEmpty(personaId)) continue;

                var profile = LoadOrCreate<PersonaProfileDefinition>(
                    $"{PROFILE_DIR}/{personaId}_profile.asset", ref created);
                ApplyProfile(profile, AsDict(row, "profile"), personaId, overwriteAuthored);
                EditorUtility.SetDirty(profile);

                var persona = LoadOrCreate<NPCPersonaDefinition>(
                    $"{PERSONA_DIR}/{personaId}.asset", ref created);
                ApplyPersona(persona, row, profile, personaId, overwriteAuthored);
                EditorUtility.SetDirty(persona);

                personasById[personaId] = persona;
                updated++;
            }

            ApplyCatalog(AsList(root, "assignments"), personasById);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[ChatPersonaImporter] {updated} personas imported " +
                $"({created} assets created, overwriteAuthored={overwriteAuthored}).");
        }

        // ── Persona ─────────────────────────────────────────────────────────

        private static void ApplyPersona(
            NPCPersonaDefinition persona, Dictionary<string, object> row,
            PersonaProfileDefinition profile, string personaId, bool force)
        {
            // Identity is never "authored" in the Inspector sense — it is the key the
            // whole subsystem joins on, so it is always written.
            persona.personaId = personaId;
            persona.profile = profile;

            SetIfEmpty(ref persona.displayName, Str(row, "displayName"), force);
            SetIfEmpty(ref persona.role, Str(row, "role"), force, defaultValue: "generic");
            SetIfEmpty(ref persona.greeting, Str(row, "greeting"), force);
            SetIfEmpty(ref persona.tone, Str(row, "tone"), force);

            // chatRange defaults to 10 on a fresh asset, and the shipped answer is 2-3.
            // "Still at the default" is the only signal available for a float, so treat
            // the default as unauthored.
            float chatRange = Flt(row, "chatRange");
            if (chatRange > 0f && (force || Mathf.Approximately(persona.chatRange, 10f)))
                persona.chatRange = chatRange;

            var style = AsDict(row, "style");
            if (style != null && (force || persona.maxSentences == 3))
                persona.maxSentences = Mathf.Max(1, (int)Flt(style, "maxSentences"));
            if (style != null)
            {
                SetIfEmpty(ref persona.verbosity, Str(style, "verbosity"), force, defaultValue: "medium");
                // A bool has no "unauthored" state, so it follows the manifest only on
                // creation or on an explicit overwrite.
                if (force || persona.dialogueLines.Count == 0)
                    persona.useEmoji = Bl(style, "useEmoji", fallback: true);
            }

            ReplaceListIfEmpty(persona.allowedItemTypes, Strings(row, "allowedItemTypes"), force);
            ReplaceListIfEmpty(persona.dialogueLines, Strings(row, "dialogueLines"), force);

            var limits = AsDict(row, "discountLimits");
            if (limits != null && (force || persona.discountLimits.Count == 0))
            {
                persona.discountLimits.Clear();
                foreach (var kv in limits)
                {
                    persona.discountLimits.Add(new NPCPersonaDefinition.DiscountEntry
                    {
                        itemKey = kv.Key,
                        maxDiscount = Mathf.Clamp01(ToFloat(kv.Value)),
                    });
                }
            }
        }

        // ── Profile ─────────────────────────────────────────────────────────

        private static void ApplyProfile(
            PersonaProfileDefinition profile, Dictionary<string, object> src,
            string personaId, bool force)
        {
            profile.personaId = personaId;
            if (src == null) return;

            SetIfEmpty(ref profile.origin, Str(src, "origin"), force);
            SetIfEmpty(ref profile.background, Str(src, "background"), force);
            ReplaceListIfEmpty(profile.goals, Strings(src, "goals"), force);
            ReplaceListIfEmpty(profile.boundaries, Strings(src, "boundaries"), force);

            var humour = AsDict(src, "humour");
            if (humour != null)
            {
                profile.humour.enabled = Bl(humour, "enabled", fallback: true);
                SetIfEmpty(ref profile.humour.frequency, Str(humour, "frequency"), force, "sometimes");
                SetIfEmpty(ref profile.humour.style, Str(humour, "style"), force);
                ReplaceListIfEmpty(profile.humour.topics, Strings(humour, "topics"), force);
                ReplaceListIfEmpty(profile.humour.examples, Strings(humour, "examples"), force);
            }

            var traits = AsDict(src, "traits");
            if (traits != null)
            {
                ReplaceListIfEmpty(profile.traits.positive, Strings(traits, "positive"), force);
                ReplaceListIfEmpty(profile.traits.negative, Strings(traits, "negative"), force);
                ReplaceListIfEmpty(profile.traits.quirks, Strings(traits, "quirks"), force);
            }

            var speech = AsDict(src, "speech");
            if (speech != null)
            {
                SetIfEmpty(ref profile.speech.register, Str(speech, "register"), force, "casual");
                SetIfEmpty(ref profile.speech.punctuation, Str(speech, "punctuation"), force);
                SetIfEmpty(ref profile.speech.flirtStyle, Str(speech, "flirtStyle"), force);
                ReplaceListIfEmpty(profile.speech.slang, Strings(speech, "slang"), force);
                ReplaceListIfEmpty(profile.speech.emojiPalette, Strings(speech, "emojiPalette"), force);
                ReplaceListIfEmpty(profile.speech.fillerWords, Strings(speech, "fillerWords"), force);
                ReplaceListIfEmpty(profile.speech.catchphrases, Strings(speech, "catchphrases"), force);
            }

            var knowledge = AsDict(src, "knowledge");
            if (knowledge != null)
            {
                SetIfEmpty(ref profile.knowledge.catalogPolicy, Str(knowledge, "catalogPolicy"), force);
                ReplaceListIfEmpty(profile.knowledge.domain, Strings(knowledge, "domain"), force);
                ReplaceListIfEmpty(profile.knowledge.allowedTypes, Strings(knowledge, "allowedTypes"), force);
                ReplaceListIfEmpty(profile.knowledge.tabooTopics, Strings(knowledge, "tabooTopics"), force);
                ReplaceListIfEmpty(profile.knowledge.localLore, Strings(knowledge, "localLore"), force);
            }

            var moods = AsDict(src, "moods");
            if (moods != null)
            {
                profile.moods.enabled = Bl(moods, "enabled", fallback: true);
                SetIfEmpty(ref profile.moods.baseline, Str(moods, "baseline"), force, "neutral");
                ReplaceListIfEmpty(profile.moods.triggersUp, Strings(moods, "triggersUp"), force);
                ReplaceListIfEmpty(profile.moods.triggersDown, Strings(moods, "triggersDown"), force);
            }

            var negotiation = AsDict(src, "negotiation");
            if (negotiation != null)
            {
                SetIfEmpty(ref profile.negotiation.style, Str(negotiation, "style"), force);
                ReplaceListIfEmpty(profile.negotiation.phrases, Strings(negotiation, "phrases"), force);
            }

            var smallTalk = AsDict(src, "smallTalk");
            if (smallTalk != null)
            {
                ReplaceListIfEmpty(profile.smallTalk.topicsPreferred, Strings(smallTalk, "topicsPreferred"), force);
                ReplaceListIfEmpty(profile.smallTalk.topicsAvoid, Strings(smallTalk, "topicsAvoid"), force);
                ReplaceListIfEmpty(profile.smallTalk.examples, Strings(smallTalk, "examples"), force);
            }
        }

        // ── Catalog ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rewrites the by-name rows wholesale. Unlike the persona assets there is nothing
        /// to preserve here: a row is a pure (entity name → persona) mapping generated
        /// from the same source, and a stale row is exactly the drift this import exists
        /// to remove.
        /// </summary>
        private static void ApplyCatalog(
            List<object> assignments, Dictionary<string, NPCPersonaDefinition> personasById)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ChatAssignmentCatalog>(CATALOG_PATH);
            if (catalog == null)
            {
                Debug.LogError($"[ChatPersonaImporter] No ChatAssignmentCatalog at '{CATALOG_PATH}'.");
                return;
            }

            catalog.assignments.Clear();
            foreach (var entry in assignments)
            {
                if (!(entry is Dictionary<string, object> row)) continue;

                string entityName = Str(row, "entityName");
                string personaId = Str(row, "personaId");
                if (string.IsNullOrEmpty(entityName)) continue;
                if (!personasById.TryGetValue(personaId, out var persona)) continue;

                catalog.assignments.Add(new ChatAssignmentCatalog.ChatAssignment
                {
                    entityName = entityName,
                    persona = persona,
                });
            }

            catalog.RebuildLookup();
            EditorUtility.SetDirty(catalog);
        }

        // ── Asset plumbing ──────────────────────────────────────────────────

        private static T LoadOrCreate<T>(string path, ref int created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created++;
            return asset;
        }

        private static void EnsureDirectory(string assetDir)
        {
            if (AssetDatabase.IsValidFolder(assetDir)) return;

            string parent = Path.GetDirectoryName(assetDir)?.Replace('\\', '/');
            string leaf = Path.GetFileName(assetDir);
            if (!string.IsNullOrEmpty(parent)) EnsureDirectory(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// The manifest lives in the repository, not in the Unity project — it is built by
        /// a Python tool and read by nothing at runtime. <c>Application.dataPath</c> ends
        /// at <c>&lt;repo&gt;/unity/Valkur/Assets</c>, so the root is three levels up.
        /// </summary>
        private static string ResolveManifestPath()
        {
            var dir = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 3 && dir?.Parent != null; i++) dir = dir.Parent;
            return Path.Combine(dir?.FullName ?? Application.dataPath, MANIFEST_RELATIVE_PATH)
                       .Replace('\\', '/');
        }

        // ── Authored-value guards ───────────────────────────────────────────

        private static void SetIfEmpty(ref string field, string value, bool force, string defaultValue = null)
        {
            if (string.IsNullOrEmpty(value)) return;
            bool unauthored = string.IsNullOrWhiteSpace(field) || field == defaultValue;
            if (force || unauthored) field = value;
        }

        private static void ReplaceListIfEmpty(List<string> field, List<string> values, bool force)
        {
            if (field == null || values == null || values.Count == 0) return;
            if (!force && field.Count > 0) return;

            field.Clear();
            field.AddRange(values);
        }

        // ── MiniJson accessors ──────────────────────────────────────────────

        private static Dictionary<string, object> AsDict(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        private static List<object> AsList(Dictionary<string, object> d, string key) =>
            (d != null && d.TryGetValue(key, out var v) ? v as List<object> : null) ?? new List<object>();

        private static string Str(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as string ?? "" : "";

        private static float Flt(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? ToFloat(v) : 0f;

        private static bool Bl(Dictionary<string, object> d, string key, bool fallback) =>
            d != null && d.TryGetValue(key, out var v) && v is bool b ? b : fallback;

        private static List<string> Strings(Dictionary<string, object> d, string key)
        {
            var list = d != null && d.TryGetValue(key, out var v) ? v as List<object> : null;
            return list == null
                ? new List<string>()
                : list.Select(x => x as string).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        private static float ToFloat(object value)
        {
            switch (value)
            {
                case double d: return (float)d;
                case float f: return f;
                case long l: return l;
                case int i: return i;
                default: return 0f;
            }
        }
    }
}
