using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.Enemies;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Configures entity GameObjects from ScriptableObject definitions.
    /// Delegates sprite setup to EntitySpriteHelper and prefab creation to ProjectilePrefabFactory.
    /// </summary>
    public static partial class EntitySetup
    {
        private static readonly int PlayerLayer = SafeNameToLayer("Player");
        private static readonly int NPCLayer = SafeNameToLayer("NPC");

        private static int SafeNameToLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
                Debug.LogWarning($"[EntitySetup] Layer '{layerName}' not found in TagManager! Falling back to Default (0).");
            return layer == -1 ? 0 : layer;
        }

        public static void ConfigurePlayer(GameObject go, PlayerDefinition def)
        {
            // Backwards-compatible all-at-once entry point. Tests call this
            // directly; the runtime spawn path in GameplaySceneSetup steps
            // through the same work via the ConfigurePlayer* helpers below
            // so each chunk can yield to the loading screen between calls.
            ConfigurePlayerVisuals(go, def);
            ConfigurePlayerCombat(go, def);
            ConfigurePlayerSpells(go);
            ConfigurePlayerStats(go, def);
            ConfigurePlayerHUD();

            Debug.Log($"[EntitySetup] Player configured: key={def.playerKey}, HP={def.maxStrength}, MP={def.maxIntelligence}, ATK={def.basicAttack}, SPD={def.basicSpeed}");
        }

        // ── Stepwise player configuration ───────────────────────────────────────
        // Each helper handles one self-contained chunk of player setup so the
        // bootstrap can yield between them and feed sub-stage labels to the
        // loading screen. The split deliberately separates the heavy steps —
        // animation rebinding, spell catalog scan, HUD creation — into their
        // own reports so the user sees the bar advance instead of a single
        // "Spawning player" hang.

        /// <summary>Tags + layer + sprite/material wiring + animator rebind.</summary>
        internal static void ConfigurePlayerVisuals(GameObject go, PlayerDefinition def)
        {
            go.layer = PlayerLayer;
            go.tag = "Player";

            var spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
            bool appliedDataDrivenVisuals = EntityAnimationBinder.ApplyPlayerVisuals(go, def);
            if (!appliedDataDrivenVisuals)
                EntitySpriteHelper.EnsurePlayerSprite(spriteRenderer);
            EntitySpriteHelper.EnsureUnlitMaterial(spriteRenderer);

            // Only for a character that declares one. Adding the component unconditionally
            // would put an inert MonoBehaviour on four of the five players and make
            // "does this character have loadouts?" a question you answer by reading its
            // config anyway.
            if (appliedDataDrivenVisuals &&
                def.assetConfig.loadouts != null && def.assetConfig.loadouts.Count > 0)
            {
                var loadouts = go.GetComponent<PlayerLoadoutController>();
                if (loadouts == null)
                    loadouts = go.AddComponent<PlayerLoadoutController>();
                loadouts.Initialize(def.assetConfig);
            }
        }

        /// <summary>Health, movement speed, melee combat, dash ability.</summary>
        internal static void ConfigurePlayerCombat(GameObject go, PlayerDefinition def)
        {
            // Python parity: selected class defines player max HP from max_strength.
            InitHealth(go, def.maxStrength);
            InitPlayerMovement(go, def.basicSpeed);
            InitPlayerCombat(go, def);
        }

        /// <summary>Spell catalog scan + per-spell registration in the spell book.</summary>
        internal static void ConfigurePlayerSpells(GameObject go)
        {
            InitPlayerSpells(go);
        }

        /// <summary>Mana/XP/inventory + death/spirit flow + register.</summary>
        internal static void ConfigurePlayerStats(GameObject go, PlayerDefinition def)
        {
            InitPlayerStats(go, def);
            InitSharedVisuals(go);
            InitSpiritDeathFlow(go);

            // The single reader of the interact key: works a registered interactable if one
            // is in range, and otherwise opens a conversation. Without it
            // InputService.Gameplay.Interact has no reader at all, which is the state the
            // project shipped in.
            if (go.GetComponent<Interaction.PlayerInteractionController>() == null)
                go.AddComponent<Interaction.PlayerInteractionController>();

            InitPlayerProgression(go, def);

            // Register the player BEFORE building HUDs so any UI singleton's
            // Start() (e.g. InventoryUI) sees a populated EntityRegistry.Player
            // on its first ResolvePlayerRefs call. Otherwise the UI starts in
            // an unwired state and only catches up once the user opens it.
            EntityRegistry.RegisterPlayer(go);
        }

        /// <summary>InventoryUI + HUDIconBar + CombatRangeVisualizer. The action bar is built by
        /// HUDManager, beside the player panel, once the player exists.</summary>
        /// <summary>
        /// What this creature's bar frame says about it.
        ///
        /// <para>Elite is not an authored flag - no such field exists on <c>MonsterDefinition</c> -
        /// so it is derived from the one fact that already means "this one is harder than its
        /// kind": the level the SPAWN was stamped with is above the definition's own. That is
        /// exactly what <c>SpawnerTemplateData.levelBonus</c> and <c>scaleWithPlayerLevel</c>
        /// produce, so a deep camp's guards are framed as elites without anybody authoring the
        /// fact twice.</para>
        /// </summary>
        internal static Valkur.Core.UI.WorldBarRank ResolveBarRank(GameObject go, MonsterDefinition def)
        {
            if (def == null) return Valkur.Core.UI.WorldBarRank.Normal;
            if (def.bossDefinition != null) return Valkur.Core.UI.WorldBarRank.Boss;
            int spawned = SpawnLevel.Of(go, def);
            return spawned > def.level
                ? Valkur.Core.UI.WorldBarRank.Elite
                : Valkur.Core.UI.WorldBarRank.Normal;
        }

        internal static void ConfigurePlayerHUD()
        {
            EnsureInventoryUI();
            EnsureHUDIconBar();
            EnsureCraftingPanelUI();
            EnsureCombatRangeVisualizer();
        }

        public static void ConfigureMonster(GameObject go, MonsterDefinition def)
        {
            EntityColliderConfigurator.ApplyLayerRecursively(go, NPCLayer);
            go.tag = "Monster";

            var brain = go.GetComponent<FSMMonsterBrain>();
            if (brain != null) brain.Initialize(def);

            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.SetTargetLayers(1 << PlayerLayer);
                combat.SetSlashVfxColor(new Color(0.2f, 0.9f, 0.3f, 0.8f));
            }

            var spriteRenderer = go.GetComponentInChildren<SpriteRenderer>();
            bool appliedDataDrivenVisuals = EntityAnimationBinder.ApplyMonsterVisuals(go, def);
            if (!appliedDataDrivenVisuals)
                EntitySpriteHelper.EnsureMonsterSprite(spriteRenderer);
            EntitySpriteHelper.EnsureUnlitMaterial(spriteRenderer);
            EntityColliderConfigurator.ConfigureNpcBodyCollider(go, spriteRenderer);
            // hp / defense / meleeDamage are the three stats MonsterDefinition.level scales;
            // everything else is read straight off def.stats on purpose. Level <= 1 — every
            // shipped monster today — returns the authored struct unchanged.
            var scaled = def.GetScaledStats(SpawnLevel.Of(go, def));
            InitHealth(go, scaled.hp);

            // Defensive stats are pushed onto the live components here because Health owns
            // the damage seam and does not know about MonsterDefinition. Until this
            // existed, `defense` was authored on every shipped hostile (5 or 10), asserted
            // by a test, shown in the F5 panel — and read by no gameplay code at all, so a
            // designer tuning it changed nothing.
            var monsterHealth = go.GetComponent<Health>();
            if (monsterHealth != null)
            {
                monsterHealth.SetDefense(scaled.defense);
                monsterHealth.SetResistances(def.stats.resistances);
            }

            if (go.GetComponent<FloatingDamageSpawner>() == null)
                go.AddComponent<FloatingDamageSpawner>();

            var statusMgr = go.GetComponent<StatusEffectManager>();
            if (statusMgr == null) statusMgr = go.AddComponent<StatusEffectManager>();
            statusMgr.SetImmunities(def.stats.statusImmunities);

            // Hit flash + knockback. Nothing attached this before, which is why
            // NPCs took damage without ever flashing white.
            if (go.GetComponent<CombatFeedback>() == null)
                go.AddComponent<CombatFeedback>();

            // Tints the sprite gray as the monster dies (Python's death_tint_system).
            // Auto-subscribes to Health.OnDeath in its own OnEnable; just adding the
            // component is enough — InitHealth above ran first so Health is present.
            if (go.GetComponent<GrayscaleDeath>() == null)
                go.AddComponent<GrayscaleDeath>();

            ConfigureFactionAndThreat(go, def);
            ConfigureMonsterAutoCast(go, def);
            ConfigureBoss(go, def);
            ConfigureChat(go, def);

            // Minimap dot — reflection, because Gameplay may not reference UI. The type follows
            // the FACTION: every NPC used to be registered as a Monster, so the six vendors in
            // town were six red enemy dots under their own gold markers. ConfigureFactionAndThreat
            // ran above, so the side is already resolvable here. Two literal calls rather than a
            // ternary, because MinimapDotNameContractTests reads the literal to check the name.
            if (EntityFaction.SideOf(go) == FactionSide.Neutral)
                ConfigureMinimapDot(go, "NPC", new Color(0.95f, 0.90f, 0.70f, 1f));
            else
                ConfigureMinimapDot(go, "Monster", new Color(0.96f, 0.28f, 0.22f, 1f));

            var npcBar = go.GetComponent<WorldHealthBar>();
            if (npcBar == null) npcBar = go.AddComponent<WorldHealthBar>();
            npcBar.SetRank(ResolveBarRank(go, def));

            var ySort = go.GetComponent<YSortEntity>();
            if (ySort == null) ySort = go.AddComponent<YSortEntity>();
            ySort.ZLayerBase = SortingConfig.Z_ENTITY;

            // The ground under a monster: its sun shadow and contact blob, and the dust its
            // strides kick up. Same two components the player gets in InitSharedVisuals.
            World.Sky.SunShadowCaster.Attach(spriteRenderer, withBlob: true);
            if (go.GetComponent<World.Ambience.FootstepEmitter>() == null)
                go.AddComponent<World.Ambience.FootstepEmitter>();

            EntityRegistry.RegisterMonster(go);
            Valkur.Core.VerboseLog.Log(Valkur.Core.VerboseLog.Category.Bootstrap,
                () => $"[EntitySetup] Monster configured: {def.displayName}, HP={scaled.hp}");
        }

        // ── Private helpers ──

        private static void InitHealth(GameObject go, int maxHp)
        {
            var health = go.GetComponent<Health>();
            if (health == null) return;

            health.Initialize(maxHp);

            // The post-hit grace defaults to 0 on the component so that dozens of EditMode
            // tests which call TakeDamage twice in one method keep measuring what they
            // think they measure — Time.time does not advance inside an EditMode test.
            // Wiring it HERE is what makes it live in the game, for player and monster
            // alike: without it five monsters at cooldown 1 land five separate hits in the
            // same frame and a pack burst-deletes the player with no counterplay window.
            health.SetPostHitGrace(Health.RecommendedGraceSeconds);
        }

        // Adds the two components that drive the spirit/altar revive flow on
        // the player. Idempotent — safe to call on prefabs that already carry
        // them (the GetComponent guards skip in that case).
        private static void InitSpiritDeathFlow(GameObject go)
        {
            if (go.GetComponent<PlayerSpiritState>()   == null) go.AddComponent<PlayerSpiritState>();
            if (go.GetComponent<PlayerSpiritVisuals>() == null) go.AddComponent<PlayerSpiritVisuals>();
        }

        private static void InitPlayerMovement(GameObject go, float speed)
        {
            var controller = go.GetComponent<PlayerController>();
            if (controller != null) controller.SetMoveSpeed(speed);
        }

        /// <summary>
        /// Installs the whole progression stack and hands it the class definition.
        ///
        /// It runs LAST on purpose, after Health, Mana, MeleeCombat and the spell book
        /// exist. PlayerStats refuses to push into a component whose Initialize has not
        /// run — otherwise the spawn order, not the class definition, would decide the
        /// character's hit points — and PlayerProgression's spell sync keeps the
        /// definitions already registered in the book, so it needs that book populated.
        ///
        /// Every one of these is AddComponent-ed rather than serialized on the prefab,
        /// which is why ProgressionCatalog is loaded from Resources by path: a
        /// [SerializeField] on a component built this way has no way to be filled, which
        /// is exactly how ChatSystem's catalog stayed null for the life of the project.
        /// </summary>
        private static void InitPlayerProgression(GameObject go, PlayerDefinition def)
        {
            if (go.GetComponent<PlayerStats>() == null)
                go.AddComponent<PlayerStats>();

            if (go.GetComponent<TimedBuffSource>() == null)
                go.AddComponent<TimedBuffSource>();

            if (go.GetComponent<EquipmentStatSource>() == null)
                go.AddComponent<EquipmentStatSource>();

            var progression = go.GetComponent<PlayerProgression>();
            if (progression == null) progression = go.AddComponent<PlayerProgression>();

            progression.Configure(def);
        }

        private static void InitPlayerCombat(GameObject go, PlayerDefinition def)
        {
            var combat = go.GetComponent<MeleeCombat>();
            if (combat != null)
            {
                combat.Initialize(def.basicAttack, 0.5f, 1.5f);
                combat.SetTargetLayers(1 << NPCLayer);
            }

            var dash = go.GetComponent<DashAbility>();
            if (dash == null) dash = go.AddComponent<DashAbility>();
            dash.SetTargetLayers(1 << NPCLayer);

            // Click-to-target. The selector adds its own MouseTargetDetector if the HUD
            // has not built one yet, so the mask is set here rather than left to whichever
            // of the two runs first — a detector with mask 0 sees nothing at all.
            var detector = go.GetComponent<MouseTargetDetector>();
            if (detector == null) detector = go.AddComponent<MouseTargetDetector>();
            detector.SetDetectableLayers(1 << NPCLayer);

            if (go.GetComponent<PlayerTargetSelector>() == null)
                go.AddComponent<PlayerTargetSelector>();

            // The single reader of the stance key. On the player rather than on a system
            // object because it is a player gesture, and it dies with them.
            if (go.GetComponent<PlayerStanceToggle>() == null)
                go.AddComponent<PlayerStanceToggle>();
        }

        /// <summary>
        /// Cached reference set by GameplaySceneSetup before player spawn.
        /// </summary>
        private static SpellCatalog _spellCatalog;

        /// <summary>
        /// Set the spell catalog before ConfigurePlayer so all spells are available.
        /// Called from GameplaySceneSetup.
        /// </summary>
        public static void SetSpellCatalog(SpellCatalog catalog)
        {
            _spellCatalog = catalog;
        }

        private static void InitPlayerSpells(GameObject go)
        {
            var caster = go.GetComponent<SpellCaster>();
            if (caster == null) return;

            caster.SetTargetLayers(1 << NPCLayer);
            ProjectilePrefabFactory.EnsureFireballPrefab(caster);

            // Primary: use the injected SpellCatalog
            SpellDefinition[] allSpells = null;
            if (_spellCatalog != null && _spellCatalog.Count > 0)
            {
                allSpells = _spellCatalog.AllSpells;
            }

            // Fallback: scan via AssetDatabase in editor, Resources in build
            if (allSpells == null || allSpells.Length == 0)
            {
#if UNITY_EDITOR
                var guids = UnityEditor.AssetDatabase.FindAssets("t:SpellDefinition", new[] { "Assets/_Project/Data/Catalogs/Spells" });
                var list = new System.Collections.Generic.List<SpellDefinition>(guids.Length);
                foreach (var guid in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var def = UnityEditor.AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
                    if (def != null) list.Add(def);
                }
                allSpells = list.ToArray();
                if (allSpells.Length > 0)
                    Debug.Log($"[EntitySetup] Loaded {allSpells.Length} spells via AssetDatabase fallback.");
#else
                allSpells = Resources.LoadAll<SpellDefinition>("Catalogs/Spells");
#endif
            }

            if (allSpells == null || allSpells.Length == 0)
            {
                Debug.LogWarning("[EntitySetup] No SpellDefinition assets found! Falling back to fireball only.");
                caster.SetSpell(0, ProjectilePrefabFactory.GetFireballSpell());
                return;
            }

            int registered = 0;
            foreach (var spell in allSpells)
            {
                if (spell == null || string.IsNullOrEmpty(spell.spellKey)) continue;
                caster.RegisterSpell(spell.spellKey, spell);
                registered++;
            }

            // Slot 0 holds the fireball for backward compatibility. It is the catalog ASSET when
            // the book has one: the code-built fallback has no icon, no element and none of the
            // authored tuning, and the old action bar drew exactly that nameless copy in its
            // first slot. The fallback stays for a catalog that failed to load.
            var fireball = caster.GetSpellByKey("fireball");
            if (fireball == null) fireball = ProjectilePrefabFactory.GetFireballSpell();
            if (fireball != null)
                caster.SetSpell(0, fireball);

            Debug.Log($"[EntitySetup] Registered {registered}/{allSpells.Length} spells in spell book.");
        }

        // ── Monster auto-cast wiring ────────────────────────────────────────────
        // Reads MonsterDefinition.autoCast + autoCastList and:
        //   1. Ensures a SpellCaster + NPCAutoCast on the monster GameObject.
        //   2. Looks up each spell key in the SpellCatalog (set on the player path
        //      via SetSpellCatalog) — silently skips unknown keys with a warning.
        //   3. Drops the resolved SpellDefinitions into SpellCaster slots 0..N-1
        //      (capped at SlotCount; everything above lands only in the spell book).
        //   4. Wipes any inspector-authored NPCAutoCast entries and replaces them
        //      with one entry per registered slot using the default period/jitter.
        //
        // Default period 3 s + jitter 0.5 s mirrors the Python AutoCastComponent
        // legacy single-entry path (period_s = 2.0 with similar jitter); 3 s is
        // slightly more conservative for first-port balancing and matches the
        // pre-existing inspector default on NPCAutoCast.
        //
        // No-op for monsters where autoCast is false or autoCastList is empty,
        // so existing melee-only NPCs are not affected.
        /// <summary>
        /// Publishes the two facts every targeting decision needs: whose side this entity is on,
        /// and how far its grudges reach.
        ///
        /// <para><c>stats.faction</c> was authored on all twenty-five shipped definitions and
        /// reached no AI decision — it fed the loot roll, the respawn system and one
        /// <c>SetContext</c> nobody read. Copying it onto <see cref="EntityFaction"/> is what
        /// turns it into the thing it always looked like.</para>
        ///
        /// <para>The threat range is DERIVED from the monster's own aggro ring rather than
        /// authored, because a second number that has to stay in step with the first is a
        /// number that eventually will not. Twice the ring: far enough that being shot from
        /// outside it still earns an answer (which is what the retaliation edge is for), close
        /// enough that one arrow from a rooftop cannot own the monster forever.</para>
        /// </summary>
        internal static void ConfigureFactionAndThreat(GameObject go, MonsterDefinition def)
        {
            if (go == null) return;

            var faction = go.GetComponent<EntityFaction>();
            if (faction == null) faction = go.AddComponent<EntityFaction>();
            faction.SetAuthoredFaction(def != null ? def.stats.faction : null);

            var threat = go.GetComponent<ThreatMemory>();
            if (threat == null) threat = go.AddComponent<ThreatMemory>();
            threat.SetMaxRange(def != null ? def.stats.aggroRange * ThreatRangeFactor : 0f);
        }

        /// <summary>Threat reach as a multiple of the aggro ring. See ConfigureFactionAndThreat.</summary>
        private const float ThreatRangeFactor = 2f;

        internal static void ConfigureMonsterAutoCast(GameObject go, MonsterDefinition def)
        {
            if (def == null || !def.autoCast) return;
            if (def.autoCastList == null || def.autoCastList.Length == 0) return;

            if (_spellCatalog == null)
            {
                Debug.LogWarning($"[EntitySetup] Monster '{def.monsterKey}' has autoCast enabled " +
                                 "but no SpellCatalog was injected via SetSpellCatalog. Skipping.");
                return;
            }

            var caster = go.GetComponent<SpellCaster>();
            if (caster == null) caster = go.AddComponent<SpellCaster>();
            caster.SetTargetLayers(1 << PlayerLayer);
            caster.SetFreeCastWithoutMana(true);
            ProjectilePrefabFactory.EnsureFireballPrefab(caster);

            var auto = go.GetComponent<NPCAutoCast>();
            if (auto == null) auto = go.AddComponent<NPCAutoCast>();
            auto.Clear();

            int registered = 0;
            int slotCount  = caster.SlotCount;
            for (int i = 0; i < def.autoCastList.Length; i++)
            {
                string key = def.autoCastList[i];
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (!_spellCatalog.TryGet(key, out var spell) || spell == null)
                {
                    Debug.LogWarning($"[EntitySetup] Monster '{def.monsterKey}' references unknown " +
                                     $"spell '{key}' in autoCastList — skipping.");
                    continue;
                }

                // Always register in the spell book so TryCastByKey works even when
                // the spell falls outside the slot count.
                caster.RegisterSpell(spell.spellKey, spell);

                if (registered < slotCount)
                {
                    caster.SetSpell(registered, spell);
                    auto.AddEntry(BuildAutoCastEntry(registered, spell));
                    registered++;
                }
            }

            Valkur.Core.VerboseLog.Log(Valkur.Core.VerboseLog.Category.Bootstrap,
                () => $"[EntitySetup] Monster '{def.monsterKey}' auto-cast: " +
                      $"{registered}/{def.autoCastList.Length} spell(s) wired.");
        }

        /// <summary>
        /// One auto-cast entry, sized from the SPELL rather than from a constant.
        ///
        /// <para>Every monster used to get <c>periodSeconds: 3f</c>, hard-coded, whatever it was
        /// casting. Two clocks then ran with nothing relating them: a <c>war_cry</c> on a 20 s
        /// cooldown was ATTEMPTED every three seconds and refused six times out of seven, while
        /// a <c>fireball</c> on 0.4 s was held back to a seventh of the rate its own data asks
        /// for. The cooldown is the ability's authored rate limit — it is the right period, and
        /// deriving it here means retuning a spell retunes every monster that carries it.</para>
        ///
        /// <para><see cref="MinAutoCastPeriod"/> is the floor for a spell with no cooldown at
        /// all, so a 0 does not turn into a cast attempt every frame.</para>
        ///
        /// <para>The distance gate comes from the spell's own <c>range</c> for the same reason:
        /// <c>NPCAutoCast.castRange</c> is a single global 8 units, so before this a monster
        /// holding a 16-unit <c>seeking_shard</c> refused to fire it past 8, and one holding a
        /// self-centred nova tried to cast it from across the street. A spell that authors no
        /// range keeps the global.</para>
        /// </summary>
        private static NPCAutoCast.AutoCastEntry BuildAutoCastEntry(int slot, SpellDefinition spell)
        {
            float period = Mathf.Max(MinAutoCastPeriod, spell.cooldownDuration);

            return new NPCAutoCast.AutoCastEntry
            {
                spellSlot     = slot,
                periodSeconds = period,

                // Proportional, not constant: a 0.5 s jitter on a 20 s ability is no
                // de-synchronisation at all, and on a 0.4 s one it is longer than the period.
                periodJitter  = period * AutoCastJitterFraction,

                // Staggered by slot so a monster with four spells does not open the fight by
                // rolling all four in the same frame and picking whichever the loop reached
                // first. It is a fight opening, not a queue.
                initialDelaySeconds = slot * AutoCastSlotStagger,

                maxDistance = spell.range > 0f ? spell.range : 0f,
                minDistance = 0f,
                hpLossStep  = 0f,
            };
        }

        /// <summary>Floor on an auto-cast period. A spell with cooldown 0 would otherwise be
        /// attempted every frame.</summary>
        private const float MinAutoCastPeriod = 0.75f;

        /// <summary>Jitter as a fraction of the period, so it scales with the ability.</summary>
        private const float AutoCastJitterFraction = 0.2f;

        /// <summary>Seconds of opening stagger per slot index.</summary>
        private const float AutoCastSlotStagger = 0.6f;

        // ── Boss wiring ──────────────────────────────────────────────────────
        // MonsterDefinition.bossDefinition is the single opt-in flag: when set,
        // this monster becomes a boss. Before this method, BossConfigurator /
        // BossPhaseController were constructed in exactly one place in the whole
        // project — the F-key Boss Editor's preview sandbox — so no spawned
        // monster, including barbol_boss (2000 HP, xpReward 500), ever got
        // phases, a phase-driven spell rotation, phase music, or the boss
        // health bar.
        //
        // SpellCaster + NPCAutoCast are ensured HERE rather than relying on
        // ConfigureMonsterAutoCast above: a boss's spell rotation normally
        // lives entirely on BossDefinition.phases[i].autoCastList, and the
        // shipped bosses leave the base MonsterDefinition.autoCast false (see
        // barbol_boss.asset) — so ConfigureMonsterAutoCast no-ops for them and
        // BossConfigurator.Awake would otherwise resolve a null SpellCaster/
        // NPCAutoCast, permanently no-opping ConfigureRotation
        // (BossConfigurator.cs's `if (... _autoCast == null || _caster == null)
        // return;` guard). Component add order mirrors
        // BossEditorPreviewSandbox.Spawn so every Awake() finds its siblings:
        // SpellCaster -> BossPhaseController (needs Health, already added by
        // InitHealth above) -> NPCAutoCast (needs SpellCaster) ->
        // BossBeatChoreographer -> BossCueDispatcher (before BossConfigurator
        // so its Awake finds the dispatcher) -> BossConfigurator (needs
        // BossPhaseController).
        internal static void ConfigureBoss(GameObject go, MonsterDefinition def)
        {
            if (def == null || def.bossDefinition == null) return;

            var caster = go.GetComponent<SpellCaster>();
            if (caster == null) caster = go.AddComponent<SpellCaster>();
            caster.SetTargetLayers(1 << PlayerLayer);
            caster.SetFreeCastWithoutMana(true);
            ProjectilePrefabFactory.EnsureFireballPrefab(caster);

            if (go.GetComponent<BossPhaseController>() == null)
                go.AddComponent<BossPhaseController>();

            if (go.GetComponent<NPCAutoCast>() == null)
                go.AddComponent<NPCAutoCast>();

            if (go.GetComponent<BossBeatChoreographer>() == null)
                go.AddComponent<BossBeatChoreographer>();

            if (go.GetComponent<BossCueDispatcher>() == null)
                go.AddComponent<BossCueDispatcher>();

            var configurator = go.GetComponent<BossConfigurator>();
            if (configurator == null) configurator = go.AddComponent<BossConfigurator>();

            configurator.SetDefinition(def.bossDefinition, _spellCatalog);
            // Rebuild BossPhaseController's phase list from the BossDefinition
            // right away (not deferred to Start()): BossPhaseController.Awake
            // + OnEnable already ran synchronously inside the AddComponent call
            // above and registered with BossHealthBarHUD using its own default
            // placeholder phases — ConfigurePhasesFromDefinition swaps those for
            // the real authored ones before anything can render a frame.
            configurator.ConfigurePhasesFromDefinition();

            // The entry-phase spell rotation, music and chart are primed by
            // BossConfigurator.Start() (see that method), which Unity guarantees
            // to run before this same GameObject's first Update() — i.e. before
            // NPCAutoCast.Update could ever consider firing a cast — so nothing
            // further is needed here.
            var phases = def.bossDefinition.phases;

            if (_spellCatalog == null)
            {
                Debug.LogWarning($"[EntitySetup] Boss '{def.monsterKey}' configured before a " +
                                 "SpellCatalog was injected via SetSpellCatalog — phase spell " +
                                 "keys will not resolve until the catalog is set.");
            }

            Valkur.Core.VerboseLog.Log(Valkur.Core.VerboseLog.Category.Bootstrap,
                () => $"[EntitySetup] Boss configured: {def.monsterKey} -> '{def.bossDefinition.name}', " +
                      $"{(phases != null ? phases.Length : 0)} phase(s).");
        }

    }
}
