using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Players
{
    /// <summary>
    /// Creates the <see cref="SpellDefinition"/> assets a player wave's variant RESERVATIONS
    /// name, and registers them in <c>SpellCatalog</c>.
    ///
    /// <para>Why it exists. A cast variant is chosen by SPELL KEY and by nothing else — there
    /// is no rotation for a reserved one, which is exactly the property that keeps a
    /// weapon-draw animation out of the rotation an ordinary fireball falls into. So an
    /// animation that no gameplay spell wants still needs a key, and
    /// <see cref="SpellType.AnimationProbe"/> is the project's answer to that: an inert spell
    /// whose executor is deliberately empty and whose whole job is that an animation can be
    /// selected and watched. The valkyrie's wave brought nine drawings of two weapon draws;
    /// two of them are what <c>weapon_toggle</c> plays, and the rest would have been
    /// unreachable art.</para>
    ///
    /// <para>DERIVED FROM THE SHIPPED DATA, not from a list kept here. The probes are whatever
    /// the <c>PlayerDefinition</c> assets say their variants reserve — see
    /// <see cref="ReservedSpellKeys"/> — so the next wave that ships an unpinned animation gets
    /// its probe by running this again rather than by somebody remembering to add a row. What
    /// CANNOT be derived is a loadout toggle: nothing in the data carries the
    /// <c>loadoutKey</c> a new verb should name, and only a person knows which weapon a verb
    /// is supposed to draw, so those are declared in <see cref="LoadoutToggles"/>.</para>
    ///
    /// <para>CREATION DEFAULTS, AUTHORED VALUES WIN. A spell that already exists is left
    /// exactly as it is. Same contract as <c>SpellExpansionSeeder</c>, the persona importer
    /// and <c>TilesetRulesetImporter</c>, and it is what makes the tool safe to re-run after a
    /// designer has retuned something.</para>
    ///
    /// <para>NO <c>Undo.RecordObject</c>, for the reason <c>BuildingPropImporter</c> recorded
    /// the hard way: assets created by a bulk tool land on the GLOBAL editor undo stack, and
    /// the first thing that pops it reverts them in memory to their empty creation state while
    /// the correct data sits on disk.</para>
    /// </summary>
    internal static class PlayerVariantSpellSeeder
    {
        private const string SpellFolder = "Assets/_Project/Data/Catalogs/Spells";
        private const string PlayerCatalogFolder = "Assets/_Project/Data/Catalogs/Players";
        private const string CatalogPath = "Assets/_Project/Data/Catalogs/SpellCatalog.asset";

        /// <summary>
        /// The weapon-draw verbs, one per loadout key that needs its own.
        ///
        /// <para><c>weapon_toggle</c> ships with <c>loadoutKey: armed</c>, so a character whose
        /// signature loadout is keyed <c>armed</c> needs nothing here — which is why the
        /// valkyrie's sword-and-shield set is keyed that way and her greatsword is not. A
        /// second weapon needs a second verb and no other change:
        /// <c>PlayerLoadoutController.ToggleLoadout</c> already puts a different key ON while
        /// one is worn rather than taking the old one off.</para>
        /// </summary>
        private static readonly (string spellKey, string loadoutKey, string displayName)[]
            LoadoutToggles =
            {
                ("weapon_toggle_greatsword", "greatsword", "Draw Greatsword"),
            };

        [MenuItem("Valkur/Players/Seed Variant Spells (Dry Run)")]
        public static void DryRun() => Run(apply: false);

        [MenuItem("Valkur/Players/Seed Variant Spells")]
        public static void Apply() => Run(apply: true);

        private static void Run(bool apply)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[PlayerVariantSpells] No SpellCatalog at {CatalogPath}.");
                return;
            }

            HashSet<string> known = KnownSpellKeys();
            var registered = new List<SpellDefinition>();
            if (catalog.AllSpells != null)
                foreach (SpellDefinition s in catalog.AllSpells)
                    if (s != null) registered.Add(s);

            var created = new List<string>();
            var alreadyThere = new List<string>();
            var unresolved = new List<string>();
            var nonSpellVerbs = new List<string>();

            foreach ((string spellKey, string loadoutKey, string displayName) in LoadoutToggles)
            {
                if (known.Contains(spellKey)) { alreadyThere.Add(spellKey); continue; }
                created.Add(spellKey);
                // Claimed immediately, even on a dry run: the reservation pass below reads the
                // same keys off the assets, and without this a toggle this pass is about to
                // create is reported as missing by the pass after it.
                known.Add(spellKey);
                if (!apply) continue;
                SpellDefinition made = CreateSpell(spellKey, displayName, SpellType.WeaponLoadout);
                made.audience = SpellAudience.Player;
                made.loadoutKey = loadoutKey;
                made.cooldownDuration = 0.8f;
                made.allowMovement = true;
                made.interruptible = true;
                made.maxInstances = 1;
                Save(made, spellKey);
                registered.Add(made);
            }

            foreach ((string spellKey, string animState) in ReservedSpellKeys())
            {
                if (known.Contains(spellKey)) { alreadyThere.Add(spellKey); continue; }
                if (!spellKey.StartsWith("anim_", System.StringComparison.Ordinal))
                {
                    // A reservation naming a real gameplay spell that does not exist is a data
                    // bug a generator must NOT paper over: only a person can say what the
                    // spell is supposed to DO, and inventing an inert one would make the
                    // reservation look wired while the animation never plays.
                    //
                    // But only on the CAST side. An ATTACK variant's key is not necessarily a
                    // spell at all: `PlayerController.PlayWorkSwing` looks a variant up by the
                    // HARVEST verb, so the dwarf's `harvest_chop`, `harvest_fish` and
                    // `harvest_mine` are correctly reserved and correctly absent from the spell
                    // catalog. Reporting those as missing spells is how a tool teaches people
                    // to ignore its output.
                    if (animState == "cast") unresolved.Add(spellKey);
                    else nonSpellVerbs.Add(spellKey);
                    continue;
                }
                created.Add(spellKey);
                known.Add(spellKey);
                if (!apply) continue;
                SpellDefinition made = CreateSpell(
                    spellKey, "Anim: " + Prettify(spellKey), SpellType.AnimationProbe);
                // audience None on purpose: a probe is not player content, and it sits in the
                // Spells Editor picker's "unassigned" tab instead of claiming to be a spell
                // somebody can learn. It is also what keeps it out of
                // SpellTreeSeeds' "player-castable but taught by no school" report.
                made.audience = SpellAudience.None;
                made.animState = animState;
                made.usesAttackAnimation = animState == "attack";
                made.cooldownDuration = 0.4f;
                made.allowOverlap = true;
                made.allowMovement = true;
                made.maxInstances = 1;
                Save(made, spellKey);
                registered.Add(made);
            }

            if (apply && created.Count > 0)
            {
                catalog.SetSpells(registered.ToArray());
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string verb = apply ? "created" : "would create";
            Debug.Log($"[PlayerVariantSpells] {verb} {created.Count} spell(s); " +
                      $"{alreadyThere.Count} already existed." +
                      (created.Count > 0 ? "\n  " + string.Join("\n  ", created) : ""));
            if (nonSpellVerbs.Count > 0)
            {
                // A log, not a warning: these are the expected shape, not a problem to fix.
                Debug.Log("[PlayerVariantSpells] Attack-variant reservations that name no " +
                          "spell — harvest verbs and the like, which PlayWorkSwing resolves " +
                          "by key rather than by casting:\n  " +
                          string.Join("\n  ", nonSpellVerbs));
            }
            if (unresolved.Count > 0)
            {
                Debug.LogError("[PlayerVariantSpells] Reservations naming a spell that does " +
                               "not exist and is not an anim_* probe. Author these by hand — " +
                               "a generator cannot know what they do:\n  " +
                               string.Join("\n  ", unresolved));
            }
        }

        /// <summary>
        /// Every reserved spell key on every shipped <see cref="PlayerDefinition"/>, with the
        /// state its variant list implies.
        ///
        /// <para>Read off the ASSETS rather than out of the wave manifest, which was the first
        /// attempt and is a worse idea twice over. The manifest is JSON and Unity ships no
        /// parser for it, so reading it meant a regex — and a lazy block match terminates on
        /// the first nested <c>]</c>, which in this schema is the <c>sprites</c> array that
        /// sits immediately BEFORE <c>spellKeys</c> in every entry. So it silently found zero
        /// reservations while reporting success, which is the shape of bug this repository
        /// records a dozen times. The assets carry the same reservations in a typed field, the
        /// importer has already validated them, and a loadout's own lists come along for
        /// free.</para>
        ///
        /// <para>The order that follows from this is import-then-seed: the frames importer
        /// writes the reservations, then this reads them. Both are idempotent, so a re-run in
        /// either order converges — but on a brand-new wave the import has to go first or
        /// there is nothing to read.</para>
        /// </summary>
        private static IEnumerable<(string spellKey, string animState)> ReservedSpellKeys()
        {
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:PlayerDefinition",
                                                             new[] { PlayerCatalogFolder }))
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def?.assetConfig == null) continue;

                foreach (var pair in Reservations(def.assetConfig))
                    if (seen.Add(pair.spellKey)) yield return pair;

                if (def.assetConfig.loadouts == null) continue;
                foreach (Loadout loadout in def.assetConfig.loadouts)
                {
                    if (loadout == null) continue;
                    foreach (var pair in Reservations(loadout))
                        if (seen.Add(pair.spellKey)) yield return pair;
                }
            }
        }

        private static IEnumerable<(string spellKey, string animState)>
            Reservations(EntityAssetConfig config)
        {
            foreach (var pair in FromAttack(config.attackVariants)) yield return pair;
            foreach (var pair in FromCast(config.castVariants)) yield return pair;
        }

        private static IEnumerable<(string spellKey, string animState)>
            Reservations(Loadout loadout)
        {
            foreach (var pair in FromAttack(loadout.attackVariants)) yield return pair;
            foreach (var pair in FromCast(loadout.castVariants)) yield return pair;
        }

        private static IEnumerable<(string spellKey, string animState)>
            FromAttack(List<AttackVariant> variants)
        {
            if (variants == null) yield break;
            foreach (AttackVariant v in variants)
            {
                if (v?.spellKeys == null) continue;
                foreach (string key in v.spellKeys)
                    if (!string.IsNullOrEmpty(key)) yield return (key, "attack");
            }
        }

        private static IEnumerable<(string spellKey, string animState)>
            FromCast(List<CastVariant> variants)
        {
            if (variants == null) yield break;
            foreach (CastVariant v in variants)
            {
                if (v?.spellKeys == null) continue;
                foreach (string key in v.spellKeys)
                    if (!string.IsNullOrEmpty(key)) yield return (key, "cast");
            }
        }

        private static HashSet<string> KnownSpellKeys()
        {
            var known = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:SpellDefinition",
                                                             new[] { SpellFolder }))
            {
                var s = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && !string.IsNullOrEmpty(s.spellKey)) known.Add(s.spellKey);
            }
            return known;
        }

        private static SpellDefinition CreateSpell(string spellKey, string displayName,
                                                   SpellType type)
        {
            var spell = ScriptableObject.CreateInstance<SpellDefinition>();
            spell.spellKey = spellKey;
            spell.displayName = displayName;
            spell.type = type;
            return spell;
        }

        private static void Save(SpellDefinition spell, string spellKey)
        {
            AssetDatabase.CreateAsset(spell, $"{SpellFolder}/{spellKey}.asset");
            EditorUtility.SetDirty(spell);
        }

        private static string Prettify(string spellKey)
        {
            string body = spellKey.Substring("anim_".Length).Replace('_', ' ');
            return body.Length == 0 ? body : char.ToUpperInvariant(body[0]) + body.Substring(1);
        }
    }
}
