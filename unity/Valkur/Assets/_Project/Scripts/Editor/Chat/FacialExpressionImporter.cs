using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Chat
{
    /// <summary>
    /// Fills each persona's <see cref="NPCPersonaDefinition.faces"/> from the drawings in
    /// that character's own <c>facial/</c> folder.
    ///
    /// <para>THE CONVENTION IS THE INPUT, NOT A RUNTIME PATH. A character's faces live
    /// beside its other art, under <c>Art/**/&lt;character&gt;/facial/</c>, named
    /// <c>&lt;anything&gt;_&lt;expression&gt;.png</c>. Loading them at runtime by path would
    /// mean moving nine PNGs per character under <c>Resources/</c>, which CLAUDE.md keeps
    /// minimal on purpose. Importing them onto the asset instead costs nothing at runtime and
    /// makes the wiring VISIBLE in the Inspector — a character with no faces is a thing you
    /// can see without running the game.</para>
    ///
    /// <para>CREATION DEFAULTS, AUTHORED VALUE WINS — the same contract
    /// <c>TilesetRulesetImporter</c>, <c>ChatPersonaImporter</c> and the progression seeder
    /// use. An entry already pointing at a sprite is never rewritten, so re-running this
    /// after a designer has swapped one face by hand cannot undo them. The overwrite variant
    /// is a separate menu item.</para>
    ///
    /// <para>Deliberately no <c>Undo.RecordObject</c>: this is a bulk asset-import tool, and
    /// CLAUDE.md records what recording 193 template creations onto the global undo stack
    /// cost the last time — the EditMode suite popped it and reverted every one of them in
    /// memory while the good data sat on disk.</para>
    /// </summary>
    public static class FacialExpressionImporter
    {
        private const string PERSONA_DIR = "Assets/_Project/Data/ChatPersonas";
        private const string ART_ROOT = "Assets/_Project/Art";
        private const string FACIAL_FOLDER = "facial";

        [MenuItem("Valkur/Chat/Import Facial Expressions")]
        public static void Import() => Run(overwrite: false);

        [MenuItem("Valkur/Chat/Import Facial Expressions (Overwrite Authored)")]
        public static void ImportOverwrite()
        {
            if (!EditorUtility.DisplayDialog(
                    "Overwrite authored faces?",
                    "Every persona's face list will be rebuilt from its facial/ folder, " +
                    "discarding any sprite assigned by hand in the Inspector.",
                    "Rebuild", "Cancel"))
                return;

            Run(overwrite: true);
        }

        private static void Run(bool overwrite)
        {
            var folders = FindFacialFolders();
            if (folders.Count == 0)
            {
                Debug.LogWarning($"[FacialExpressionImporter] No '{FACIAL_FOLDER}/' folder " +
                                 $"anywhere under {ART_ROOT}. Nothing to import.");
                return;
            }

            var personas = LoadPersonas();
            if (personas.Count == 0)
            {
                Debug.LogWarning($"[FacialExpressionImporter] No NPCPersonaDefinition under " +
                                 $"{PERSONA_DIR}. Run 'Valkur > Chat > Import Personas' first.");
                return;
            }

            var report = new StringBuilder();
            int wired = 0;

            foreach (string folder in folders)
            {
                var faces = ReadFaces(folder, report);
                if (faces.Count == 0) continue;

                // The character a folder belongs to is its PARENT folder's name —
                // ".../gatita_chanchita/facial" is Gatita's. Matched against the persona's
                // display name and id rather than against a hand-kept table, because the one
                // thing a folder rename must not silently do is unhook a character's face.
                string characterFolder = Path.GetFileName(Path.GetDirectoryName(folder)) ?? "";
                var matches = MatchPersonas(personas, characterFolder);

                if (matches.Count == 0)
                {
                    report.AppendLine($"  SKIP {characterFolder}/{FACIAL_FOLDER} — " +
                                      "no persona whose name or id contains that folder name.");
                    continue;
                }
                if (matches.Count > 1)
                {
                    report.AppendLine($"  SKIP {characterFolder}/{FACIAL_FOLDER} — ambiguous, " +
                                      $"matches {string.Join(", ", matches.Select(m => m.personaId))}. " +
                                      "Refusing rather than guessing which character owns the art.");
                    continue;
                }

                if (ApplyTo(matches[0], faces, overwrite, report)) wired++;
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[FacialExpressionImporter] {folders.Count} facial/ folder(s), " +
                      $"{wired} persona(s) updated.\n{report}");
        }

        // ── Discovery ───────────────────────────────────────────────────────

        private static List<string> FindFacialFolders()
        {
            var found = new List<string>();
            string abs = Path.GetFullPath(ART_ROOT);
            if (!Directory.Exists(abs)) return found;

            foreach (string dir in Directory.GetDirectories(abs, FACIAL_FOLDER, SearchOption.AllDirectories))
                found.Add(ToAssetPath(dir));

            found.Sort(System.StringComparer.Ordinal);
            return found;
        }

        private static List<NPCPersonaDefinition> LoadPersonas()
        {
            var list = new List<NPCPersonaDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:NPCPersonaDefinition", new[] { PERSONA_DIR }))
            {
                var persona = AssetDatabase.LoadAssetAtPath<NPCPersonaDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (persona != null) list.Add(persona);
            }
            return list;
        }

        /// <summary>
        /// Every persona whose display name or id contains <paramref name="characterFolder"/>,
        /// or whose name the folder contains.
        ///
        /// Both directions, because the two naming schemes meet in the middle: the folder is
        /// <c>gatita_chanchita</c>, the display name is <c>Gatita</c> and the id is
        /// <c>vendor_cheff_gatita</c>. More than one match is refused by the caller — a wrong
        /// character's face is worse than none.
        /// </summary>
        private static List<NPCPersonaDefinition> MatchPersonas(
            List<NPCPersonaDefinition> personas, string characterFolder)
        {
            string folder = characterFolder.ToLowerInvariant();
            var hits = new List<NPCPersonaDefinition>();

            foreach (var persona in personas)
            {
                string name = (persona.displayName ?? "").ToLowerInvariant();
                string id = (persona.personaId ?? "").ToLowerInvariant();

                bool match =
                    (name.Length > 2 && (folder.Contains(name) || name.Contains(folder))) ||
                    (id.Length > 2 && (folder.Contains(id) || id.Contains(folder)));

                if (match) hits.Add(persona);
            }
            return hits;
        }

        // ── Reading ─────────────────────────────────────────────────────────

        /// <summary>
        /// The sprites in <paramref name="folder"/>, keyed by the expression their filename
        /// ends in. A file whose trailing token names no expression is reported and skipped —
        /// silently ignoring it is how a typo becomes a face that never appears.
        /// </summary>
        private static Dictionary<FacialExpression, Sprite> ReadFaces(string folder, StringBuilder report)
        {
            var faces = new Dictionary<FacialExpression, Sprite>();

            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;

                string stem = Path.GetFileNameWithoutExtension(path);
                int underscore = stem.LastIndexOf('_');
                string token = underscore >= 0 ? stem.Substring(underscore + 1) : stem;

                if (!FacialExpressionFallback.TryParse(token, out FacialExpression expression))
                {
                    report.AppendLine($"  ?    {stem} — '{token}' names no expression, skipped.");
                    continue;
                }

                if (faces.ContainsKey(expression))
                {
                    report.AppendLine($"  DUP  {stem} — {expression} already taken by " +
                                      $"{faces[expression].name}, skipped.");
                    continue;
                }

                faces[expression] = sprite;
            }
            return faces;
        }

        // ── Writing ─────────────────────────────────────────────────────────

        private static bool ApplyTo(
            NPCPersonaDefinition persona, Dictionary<FacialExpression, Sprite> faces,
            bool overwrite, StringBuilder report)
        {
            if (persona.faces == null) persona.faces = new List<NPCPersonaDefinition.FacialSprite>();
            if (overwrite) persona.faces.Clear();

            int added = 0, kept = 0;

            foreach (var pair in faces.OrderBy(p => p.Key))
            {
                int existing = persona.faces.FindIndex(f => f.expression == pair.Key);
                if (existing >= 0 && persona.faces[existing].sprite != null)
                {
                    kept++;
                    continue;
                }

                var entry = new NPCPersonaDefinition.FacialSprite
                {
                    expression = pair.Key,
                    sprite = pair.Value,
                };

                if (existing >= 0) persona.faces[existing] = entry;
                else persona.faces.Add(entry);
                added++;
            }

            // The fallback portrait is what a persona shows when the chain runs out, and the
            // neutral face is the only honest default for it. Only filled when empty, for the
            // same reason every other field here is.
            if (persona.portrait == null && faces.TryGetValue(FacialExpression.Neutral, out Sprite neutral))
                persona.portrait = neutral;

            if (added == 0 && kept == faces.Count)
            {
                report.AppendLine($"  ok   {persona.displayName} — already had all {kept}.");
                return false;
            }

            EditorUtility.SetDirty(persona);
            report.AppendLine($"  WIRE {persona.displayName} — {added} added" +
                              (kept > 0 ? $", {kept} left as authored" : "") + ".");
            return true;
        }

        private static string ToAssetPath(string absolute)
        {
            string normalized = absolute.Replace('\\', '/');
            int index = normalized.IndexOf("/Assets/", System.StringComparison.Ordinal);
            return index >= 0 ? normalized.Substring(index + 1) : normalized;
        }
    }
}
