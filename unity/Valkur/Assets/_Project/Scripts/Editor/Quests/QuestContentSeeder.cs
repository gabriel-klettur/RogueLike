using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.EditorTools.Quests
{
    /// <summary>
    /// Creates the shipped quests and the catalogue that points at them.
    ///
    /// <para><b>Creation defaults, authored value wins.</b> A re-run fills only the
    /// fields of a quest that are still empty and never overwrites prose a designer
    /// rewrote in the Inspector — the same contract <c>TilesetRulesetImporter</c>,
    /// the persona importer and the progression seeder use. The overwrite variant is
    /// a separate menu item so it cannot be reached by accident.</para>
    ///
    /// <para><b>No <c>Undo.RecordObject</c>, deliberately.</b> A bulk asset tool that
    /// records its creations puts them on the GLOBAL editor undo stack, and the first
    /// thing to pop that stack reverts them all in memory while the correct data sits
    /// on disk — which is how 193 building templates were emptied once. Assets an
    /// operator RE-RUNS are dirtied with <c>EditorUtility.SetDirty</c> and nothing
    /// else.</para>
    ///
    /// <para>The catalogue lives under <c>Resources/Quests/</c> because
    /// <c>QuestService</c> is <c>AddComponent</c>-ed by the boot sequence and has no
    /// inspector slot; the quests themselves live in <c>Data/Catalogs/Quests/</c>,
    /// outside <c>Resources</c>, because they are reached through the catalogue's own
    /// references and putting 10 more assets into the build-everything folder buys
    /// nothing.</para>
    /// </summary>
    public static class QuestContentSeeder
    {
        private const string QuestFolder   = "Assets/_Project/Data/Catalogs/Quests";
        private const string CatalogFolder = "Assets/_Project/Resources/Quests";
        private const string CatalogPath   = CatalogFolder + "/QuestCatalog.asset";

        [MenuItem("Valkur/Quests/Seed Quest Content")]
        public static void Seed() => Run(overwriteAuthored: false);

        [MenuItem("Valkur/Quests/Seed Quest Content (Overwrite Authored)")]
        public static void SeedOverwrite()
        {
            if (!EditorUtility.DisplayDialog(
                    "Sobrescribir quests",
                    "Esto reescribe TODOS los campos de las 10 quests enviadas, incluida la prosa " +
                    "que alguien haya editado a mano en el Inspector.\n\n¿Continuar?",
                    "Sobrescribir", "Cancelar"))
                return;
            Run(overwriteAuthored: true);
        }

        [MenuItem("Valkur/Quests/Report Quest Content")]
        public static void Report()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<QuestCatalog>(CatalogPath);
            if (catalog == null) { Debug.Log("[QuestContentSeeder] No hay catálogo en " + CatalogPath); return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[QuestContentSeeder] {catalog.quests.Count} quests en el catálogo:");
            foreach (var q in catalog.quests)
            {
                if (q == null) { sb.AppendLine("  (entrada nula)"); continue; }
                sb.AppendLine($"  {q.questId,-24} nivel {q.requiredLevel,2}  " +
                              $"{q.objectives.Length} objetivos  " +
                              $"da: {q.giverPersonaId}  entrega: {q.turnInPersonaId}  " +
                              $"{q.xpReward} xp / {q.coinReward} monedas");
            }
            Debug.Log(sb.ToString());
        }

        private static void Run(bool overwriteAuthored)
        {
            EnsureFolder(QuestFolder);
            EnsureFolder(CatalogFolder);

            var blueprints = Blueprints();
            var assets = new List<QuestDefinition>(blueprints.Count);
            int created = 0, updated = 0;

            foreach (var bp in blueprints)
            {
                string path = $"{QuestFolder}/{bp.questId}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
                if (asset == null)
                {
                    asset = ScriptableObject.CreateInstance<QuestDefinition>();
                    Apply(asset, bp, overwriteEverything: true);
                    AssetDatabase.CreateAsset(asset, path);
                    created++;
                }
                else
                {
                    Apply(asset, bp, overwriteAuthored);
                    EditorUtility.SetDirty(asset);
                    updated++;
                }
                assets.Add(asset);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<QuestCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<QuestCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            // The catalogue is REBUILT rather than appended to: a quest renamed or
            // dropped from the blueprint list would otherwise stay referenced forever,
            // and the save layer resolves ids through exactly this list.
            catalog.quests.Clear();
            catalog.quests.AddRange(assets);
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[QuestContentSeeder] {created} quests creadas, {updated} revisadas, " +
                      $"{catalog.quests.Count} en el catálogo ({CatalogPath}).");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf   = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// Copies a blueprint onto an asset. Numbers and structure are ALWAYS written —
        /// a generator that cannot propagate its own retune is not a generator — while
        /// prose is written only when the field is still empty, unless the caller asked
        /// for the overwrite variant. The same split the crafting importer uses, and for
        /// the same reason: the balance belongs to the generator, the words belong to
        /// whoever last wrote them.
        /// </summary>
        private static void Apply(QuestDefinition asset, QuestDefinition bp, bool overwriteEverything)
        {
            asset.questId              = bp.questId;
            asset.objectives           = bp.objectives;
            asset.giverPersonaId       = bp.giverPersonaId;
            asset.turnInPersonaId      = bp.turnInPersonaId;
            asset.requiredLevel        = bp.requiredLevel;
            asset.recommendedLevel     = bp.recommendedLevel;
            asset.prerequisiteQuestIds = bp.prerequisiteQuestIds;
            asset.questLine            = bp.questLine;
            asset.xpReward             = bp.xpReward;
            asset.skillPointReward     = bp.skillPointReward;
            asset.arcanePointReward    = bp.arcanePointReward;
            asset.coinReward           = bp.coinReward;
            asset.itemRewards          = bp.itemRewards;
            asset.itemRewardCounts     = bp.itemRewardCounts;

            if (overwriteEverything || string.IsNullOrWhiteSpace(asset.displayName))
                asset.displayName = bp.displayName;
            if (overwriteEverything || string.IsNullOrWhiteSpace(asset.description))
                asset.description = bp.description;
            if (overwriteEverything || string.IsNullOrWhiteSpace(asset.hookLine))
                asset.hookLine = bp.hookLine;
            if (overwriteEverything || string.IsNullOrWhiteSpace(asset.completionLine))
                asset.completionLine = bp.completionLine;

            asset.name = bp.questId;
        }

        // ── The shipped ten ────────────────────────────────────────────────────

        private static ObjectiveEntry Obj(ObjectiveKind kind, string target, int count,
                                          string description, bool consume = false)
            => new ObjectiveEntry
            {
                kind = kind, targetId = target, count = Mathf.Max(1, count),
                description = description, consumeOnComplete = consume,
            };

        /// <summary>
        /// Ten quests, deliberately spread across LENGTH and across OBJECTIVE KIND —
        /// every one of the nine kinds is exercised by at least one of them, which is
        /// what makes this content a test of the layer as well as a thing to play.
        ///
        /// <para>The archetypes are the ones D&amp;D standardised: the cellar vermin
        /// that teaches a new player the verbs, the smith's fetch, the pack with a
        /// named alpha, the feast, the courier round, the tomb delve, the vigil, the
        /// stolen spellbook, the merchant's contract, and the dragon at the end of
        /// everything.</para>
        /// </summary>
        private static List<QuestDefinition> Blueprints()
        {
            var list = new List<QuestDefinition>();

            // ── 1. XS — el arranque. Un objetivo, un tipo, cinco minutos. ──────
            list.Add(Make(
                id: "q_despensa_plaga",
                name: "Plaga en la despensa",
                line: "gatita",
                giver: "vendor_cheff_gatita", turnIn: "vendor_cheff_gatita",
                reqLevel: 0, recLevel: 1,
                desc: "Algo pequeño y con demasiados dientes se ha instalado entre los sacos de " +
                      "harina de Gatita. Ella no piensa entrar ahí con un cucharón.",
                hook: "Ay, por fin alguien con botas. Hay retoños metidos en mi despensa y me " +
                      "están royendo la harina. Saca seis y te doy de comer una semana.",
                done: "¡Seis! Y sin romper nada. Toma, esto te lo has ganado con creces.",
                objectives: new[]
                {
                    Obj(ObjectiveKind.KillCount, "barbol_baby", 6, "Echa 6 retoños de la despensa"),
                },
                xp: 120, coins: 40,
                items: new[] { "locro" }, itemCounts: new[] { 2 }));

            // ── 2. XS/S — el recado del herrero. Recolectar y ENTREGAR. ────────
            list.Add(Make(
                id: "q_yunque_frio",
                name: "El yunque frío",
                line: "smith",
                giver: "vendor_blacksmith_smith", turnIn: "vendor_blacksmith_smith",
                reqLevel: 0, recLevel: 2,
                desc: "La fragua de Smith lleva dos días apagada. No le falta oficio: le falta " +
                      "mineral y le falta carbón.",
                hook: "Un yunque frío no es un yunque, es un pisapapeles de doscientos kilos. " +
                      "Tráeme doce de mineral de hierro y seis de carbón y verás lo que sale.",
                done: "Escucha ese chisporroteo. Eso es una fragua contenta. Llévate esto.",
                objectives: new[]
                {
                    Obj(ObjectiveKind.Collect, "iron_ore", 12, "Consigue 12 de mineral de hierro", consume: true),
                    Obj(ObjectiveKind.Collect, "coal_chunk", 6, "Consigue 6 trozos de carbón", consume: true),
                },
                xp: 180, coins: 80,
                items: new[] { "iron_ingot" }, itemCounts: new[] { 2 }));

            // ── 3. S — la manada y su alfa. Chusma primero, nombre después. ────
            list.Add(Make(
                id: "q_manada_alfa",
                name: "La manada y su alfa",
                line: "pavel",
                giver: "vendor_lumberjack_pavel", turnIn: "vendor_lumberjack_pavel",
                reqLevel: 2, recLevel: 4,
                desc: "Pavel ya no baja al claro del este. Dice que los barboles se mueven " +
                      "juntos, y que uno de ellos es del tamaño de su cabaña.",
                hook: "No es que sean muchos. Es que uno de ellos manda. Adelgaza la manada y " +
                      "luego busca al grande: mientras respire, volverán.",
                done: "Ya se oye el hacha otra vez en el claro. Gracias, de verdad.",
                prereqs: new[] { "q_despensa_plaga" },
                objectives: new[]
                {
                    Obj(ObjectiveKind.KillCount, "barbol", 10, "Adelgaza la manada: 10 barboles"),
                    Obj(ObjectiveKind.KillCount, "barbol_muscle", 1, "Derrota al Coloso que la manda"),
                },
                xp: 400, coins: 150, skillPoints: 1));

            // ── 4. M — el banquete. Tres recetas distintas + un ingrediente. ───
            list.Add(Make(
                id: "q_banquete_siete_fuegos",
                name: "El banquete de los siete fuegos",
                line: "gatita",
                giver: "vendor_cheff_gatita", turnIn: "vendor_cheff_gatita",
                reqLevel: 3, recLevel: 5,
                desc: "Gatita ha prometido un banquete que no puede cocinar sola. Tres platos, " +
                      "tres fuegos, y carne suficiente para el cuarto.",
                hook: "Prometí un banquete y ahora tengo tres ollas y dos manos. Cocina un locro, " +
                      "unas empanadas y unos churros con chocolate, y tráeme cuatro de carne. " +
                      "Del resto me encargo yo.",
                done: "Huele a fiesta. Anda, siéntate: esta paella es tuya.",
                prereqs: new[] { "q_despensa_plaga" },
                objectives: new[]
                {
                    Obj(ObjectiveKind.Craft, "locro", 1, "Cocina un locro"),
                    Obj(ObjectiveKind.Craft, "empanadas_argentinas", 1, "Cocina unas empanadas"),
                    Obj(ObjectiveKind.Craft, "churros_con_chocolate", 1, "Cocina churros con chocolate"),
                    Obj(ObjectiveKind.Collect, "beef", 4, "Lleva 4 de carne a la cocina", consume: true),
                },
                xp: 500, coins: 200,
                items: new[] { "paella" }, itemCounts: new[] { 1 }));

            // ── 5. M — la ronda del correo. Hablar y viajar, sin combate. ──────
            list.Add(Make(
                id: "q_cartas_caminos",
                name: "Cartas para los caminos",
                line: "abigail",
                giver: "vendor_banker_abigail", turnIn: "vendor_banker_abigail",
                reqLevel: 2, recLevel: 3,
                desc: "Tres cartas, tres firmas y un camino largo. Abigail no cierra un libro " +
                      "de cuentas hasta que cada deudor lo mira a los ojos.",
                hook: "Necesito tres firmas y un par de botas que no sean las mías. Habla con " +
                      "Pavel, con Valeria y con Roberto, y pásate por el bosque de camino: " +
                      "quiero saber si el puente sigue en pie.",
                done: "Tres firmas y un puente en pie. Un buen día para los libros.",
                objectives: new[]
                {
                    Obj(ObjectiveKind.Talk, "vendor_lumberjack_pavel", 1, "Consigue la firma de Pavel"),
                    Obj(ObjectiveKind.Talk, "vendor_alchemist_valeria", 1, "Consigue la firma de Valeria"),
                    Obj(ObjectiveKind.Talk, "vendor_mague_roberto", 1, "Consigue la firma de Roberto"),
                    Obj(ObjectiveKind.Reach, "Forest", 1, "Comprueba el puente del bosque"),
                },
                xp: 350, coins: 250));

            // ── 6. L — la cripta. Viajar, limpiar y traerse algo de abajo. ─────
            list.Add(Make(
                id: "q_cripta_colina",
                name: "La cripta bajo la colina",
                line: "felipondor",
                giver: "npc_barbol_brother_felipondor", turnIn: "npc_barbol_brother_felipondor",
                reqLevel: 5, recLevel: 7,
                desc: "Bajo la colina hay una puerta que lleva cerrada más tiempo del que nadie " +
                      "recuerda. Felipondor sabe lo que hay dentro, y por eso no baja él.",
                hook: "Hay algo ahí abajo que lleva mi apellido y no me enorgullece. Baja, límpialo, " +
                      "y súbeme cinco trozos de obsidiana de las paredes: quiero saber de qué está " +
                      "hecha esa oscuridad.",
                done: "Obsidiana. Claro que sí. Ahora ya sé qué es lo que nos mira desde abajo.",
                prereqs: new[] { "q_manada_alfa" },
                objectives: new[]
                {
                    Obj(ObjectiveKind.Reach, "dungeon", 1, "Encuentra la entrada de la cripta"),
                    Obj(ObjectiveKind.KillCount, "barbol_oscuro", 8, "Limpia la cripta: 8 barboles oscuros"),
                    Obj(ObjectiveKind.Collect, "obsidian_chunk", 5, "Arranca 5 trozos de obsidiana", consume: true),
                },
                xp: 900, coins: 400, skillPoints: 1,
                items: new[] { "spellbook_simple" }, itemCounts: new[] { 1 }));

            // ── 7. M — la vigilia. Aguantar en un sitio mientras llegan. ───────
            list.Add(Make(
                id: "q_vigilia_altar",
                name: "Vigilia en el altar",
                line: "felipondor",
                giver: "npc_barbol_brother_felipondor", turnIn: "npc_barbol_brother_felipondor",
                reqLevel: 6, recLevel: 8,
                desc: "Lo que se abrió en la cripta no se ha vuelto a cerrar. Alguien tiene que " +
                      "estar en el bosque cuando salgan, y tiene que seguir de pie tres minutos " +
                      "después.",
                hook: "No te pido que ganes. Te pido que sigas ahí cuando dejen de venir. Tres " +
                      "minutos en pie en el bosque, y quince de ellos en el suelo.",
                done: "Sigues de pie. Eso ya es más de lo que consiguió el anterior.",
                prereqs: new[] { "q_cripta_colina" },
                objectives: new[]
                {
                    Obj(ObjectiveKind.Reach, "Forest", 1, "Sube al claro del bosque"),
                    Obj(ObjectiveKind.Survive, "", 180, "Aguanta en pie 180 segundos"),
                    Obj(ObjectiveKind.KillCount, "", 15, "Derriba 15 enemigos durante la vigilia"),
                },
                xp: 700, coins: 300));

            // ── 8. M — el grimorio. Practicar, recuperar, cobrarse la deuda. ───
            list.Add(Make(
                id: "q_grimorio_robado",
                name: "El grimorio robado",
                line: "roberto",
                giver: "vendor_mague_roberto", turnIn: "vendor_mague_roberto",
                reqLevel: 4, recLevel: 6,
                desc: "A Roberto le han vaciado la estantería. Sabe quién ha sido, porque el " +
                      "ladrón lleva su misma cara.",
                hook: "Me han robado un libro y, lo que es peor, me lo ha robado alguien que sabe " +
                      "leerlo. Practica el fuego hasta que te salga sin pensar, recupera el " +
                      "grimorio, y hazme el favor de encontrarte con tres magos oscuros por el camino.",
                done: "Está entero. Todavía huele a mi mesa. Toma, esto es más útil que el oro.",
                objectives: new[]
                {
                    Obj(ObjectiveKind.CastSpell, "fireball", 10, "Lanza 10 bolas de fuego"),
                    Obj(ObjectiveKind.KillCount, "dark_mague", 3, "Derrota a 3 magos oscuros"),
                    Obj(ObjectiveKind.Collect, "spellbook_simple", 1, "Recupera el grimorio", consume: true),
                },
                xp: 650, coins: 200, arcanePoints: 2));

            // ── 9. S/M — la deuda. La única quest que mide el bolsillo. ────────
            list.Add(Make(
                id: "q_deuda_abigail",
                name: "La deuda de la casa Abigail",
                line: "abigail",
                giver: "vendor_banker_abigail", turnIn: "vendor_banker_abigail",
                reqLevel: 3, recLevel: 5,
                desc: "Abigail no presta a quien no ha ahorrado nunca. Su contrato empieza " +
                      "demostrándole que sabes qué es una moneda.",
                hook: "Antes de firmar contigo quiero verte con quinientas monedas en la mano — " +
                      "en la mano, no en la memoria — y tres pepitas de oro sobre mi mesa. " +
                      "Quien gasta todo lo que gana no es un cliente, es un agujero.",
                done: "Quinientas, y tres pepitas. Tienes crédito en esta casa. Y esto, por las molestias.",
                prereqs: new[] { "q_cartas_caminos" },
                objectives: new[]
                {
                    Obj(ObjectiveKind.EarnCoins, "", 500, "Reúne 500 monedas y consérvalas"),
                    Obj(ObjectiveKind.Collect, "gold_nugget", 3, "Deja 3 pepitas de oro sobre la mesa", consume: true),
                },
                xp: 300, coins: 0,
                items: new[] { "ruby_raw" }, itemCounts: new[] { 1 }));

            // ── 10. XL — el dragón. La capstone: nivel, cadena y cinco pasos. ──
            list.Add(Make(
                id: "q_dragon_fosa_roja",
                name: "El dragón de la Fosa Roja",
                line: "fosa_roja",
                giver: "vendor_blacksmith_smith", turnIn: "vendor_blacksmith_smith",
                reqLevel: 10, recLevel: 15,
                desc: "Smith lleva media vida guardando el acero equivocado para el enemigo " +
                      "correcto. La Fosa Roja tiene dueño, y el dueño tiene alas.",
                hook: "Te lo voy a decir una sola vez: no vas a poder con eso hoy. Sube de nivel, " +
                      "quítale al gigante lo que guarda, aguanta el aliento de la Fosa y tráeme " +
                      "un corazón de magma. Entonces hablamos de la espada.",
                done: "Nadie había traído uno de esos a esta fragua. Nadie. Toma — es tuya desde " +
                      "hace mucho, solo que no lo sabías.",
                prereqs: new[] { "q_manada_alfa", "q_cripta_colina", "q_grimorio_robado" },
                objectives: new[]
                {
                    Obj(ObjectiveKind.ReachLevel, "", 12, "Alcanza el nivel 12"),
                    Obj(ObjectiveKind.KillCount, "barbol_gigante", 1, "Derrota al Barbol Gigante"),
                    Obj(ObjectiveKind.Survive, "", 240, "Sobrevive 240 segundos en la Fosa"),
                    Obj(ObjectiveKind.KillCount, "red_dragon", 1, "Mata al Dragón Rojo"),
                    Obj(ObjectiveKind.Collect, "magma_heart", 1, "Arranca su corazón de magma", consume: true),
                },
                xp: 5000, coins: 1500, skillPoints: 3, arcanePoints: 3,
                items: new[] { "knight_longsword" }, itemCounts: new[] { 1 }));

            return list;
        }

        private static QuestDefinition Make(
            string id, string name, string line,
            string giver, string turnIn,
            int reqLevel, int recLevel,
            string desc, string hook, string done,
            ObjectiveEntry[] objectives,
            int xp, int coins,
            int skillPoints = 0, int arcanePoints = 0,
            string[] prereqs = null,
            string[] items = null, int[] itemCounts = null)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.questId              = id;
            q.displayName          = name;
            q.questLine            = line;
            q.giverPersonaId       = giver;
            q.turnInPersonaId      = turnIn;
            q.requiredLevel        = reqLevel;
            q.recommendedLevel     = recLevel;
            q.description          = desc;
            q.hookLine             = hook;
            q.completionLine       = done;
            q.objectives           = objectives ?? Array.Empty<ObjectiveEntry>();
            q.prerequisiteQuestIds = prereqs ?? Array.Empty<string>();
            q.xpReward             = xp;
            q.coinReward           = coins;
            q.skillPointReward     = skillPoints;
            q.arcanePointReward    = arcanePoints;
            q.itemRewards          = items ?? Array.Empty<string>();
            q.itemRewardCounts     = itemCounts ?? Array.Empty<int>();
            return q;
        }
    }
}
