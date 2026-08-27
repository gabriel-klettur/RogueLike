using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// Pins FSM COVERAGE against the shipped data — <c>StreamingAssets/FSM/sets.json</c>,
    /// <c>StreamingAssets/FSM/assignments.json</c> and every <see cref="MonsterDefinition"/>
    /// asset in the project. Read-only: this fixture never writes to
    /// <c>StreamingAssets/FSM/</c>.
    ///
    /// Every failure this file catches is SILENT in game. A monster that resolves to no set
    /// does not error — <see cref="FSMRuntimeFactory"/> returns false and
    /// <c>FSMMonsterBrain</c> boots a bare hard-coded <c>IdleState</c> with no transitions
    /// and no allowed-state guard, so the monster simply stands there looking like a design
    /// choice. Eight of the nineteen shipped monsters were in exactly that state when this
    /// fixture was written (<c>mon1</c>, <c>barbol_brother_felipondor</c> and the six
    /// <c>vendor_*</c>), and nothing in the project said so.
    ///
    /// NOTHING here hardcodes how many sets, monsters or assignments exist. Every expectation
    /// is derived from the files at run time, because the whole point is to keep holding as
    /// sets are added and monsters are re-assigned.
    /// </summary>
    [TestFixture]
    public class ShippedFSMCoverageTests
    {
        // ── Shipped data, re-read for every test ────────────────────────────────

        private Dictionary<string, ShippedSet> _sets;
        private Dictionary<string, string> _byArchetype;
        private Dictionary<string, string> _byEid;
        private List<MonsterDefinition> _monsters;

        [SetUp]
        public void SetUp()
        {
            _sets        = LoadSets();
            _byArchetype = LoadAssignmentTable("by_archetype");
            _byEid       = LoadAssignmentTable("by_eid");
            _monsters    = LoadShippedMonsters();

            // Guards, not expectations: an empty corpus would make every assertion below
            // pass vacuously, which is the one outcome worse than a red test.
            Assert.IsNotEmpty(_sets,
                "sets.json declared no sets at all — every monster would boot the hard-coded " +
                "IdleState fallback.");
            Assert.IsNotEmpty(_monsters,
                "No MonsterDefinition asset with a monsterKey was found — this fixture would " +
                "assert nothing.");
        }

        // ── 1. Coverage ─────────────────────────────────────────────────────────

        /// <summary>
        /// Every shipped monster must resolve to a set that exists, through the order
        /// <c>FSMRuntimeFactory.TryBuildForEntity</c> documents: <c>by_eid</c>, then
        /// <c>by_archetype</c>, then <c>MonsterDefinition.fsmSet</c>.
        ///
        /// <c>by_eid</c> is keyed by PLACEMENT id (the GUID an F5 placement keeps across a
        /// save/load), not by monster key, so it can rescue one placed entity and never an
        /// archetype — a monster is covered only if <c>by_archetype</c> or its own
        /// <c>fsmSet</c> names a real set. That layer is still validated, for set-id
        /// existence, by <see cref="EverySetIdReferencedAnywhere_ExistsInSetsJson"/>.
        ///
        /// The failure message NAMES the monsters, because that list is the actionable
        /// output — "8 monsters uncovered" tells an author nothing they can act on.
        /// </summary>
        [Test]
        public void EveryShippedMonster_ResolvesToARealSet()
        {
            var uncovered = new List<string>();

            foreach (var def in _monsters)
            {
                if (TryResolveSetForMonster(def, out _, out _)) continue;

                string reason;
                if (_byArchetype.TryGetValue(def.monsterKey, out string archSetId))
                    reason = $"by_archetype names '{archSetId}', which does not exist in sets.json";
                else if (string.IsNullOrEmpty(def.fsmSet))
                    reason = "no by_archetype entry, and MonsterDefinition.fsmSet is empty";
                else
                    reason = "no by_archetype entry, and MonsterDefinition.fsmSet = " +
                             $"'{def.fsmSet}' names no set that exists";

                uncovered.Add($"  • {def.monsterKey} — {reason}");
            }

            Assert.IsEmpty(uncovered,
                "These monsters resolve to NO FSM set and boot the hard-coded IdleState " +
                "fallback — no transitions, no allowed-state guard, no diagnostic in game:\n" +
                string.Join("\n", uncovered) +
                "\nFix by assigning them in F12 (writes by_archetype) or by pointing their " +
                "MonsterDefinition.fsmSet at a set that exists.");
        }

        // ── 2. Dangling set ids ─────────────────────────────────────────────────

        /// <summary>
        /// Every set id named by <c>by_archetype</c>, <c>by_eid</c> or a
        /// <c>MonsterDefinition.fsmSet</c> must exist in <c>sets.json</c>.
        ///
        /// A typo'd set id is indistinguishable from no assignment at all: the factory warns
        /// once per session and drops to the same hard-coded boot, so a monster somebody
        /// carefully assigned behaves exactly like one nobody ever touched. Renaming a set in
        /// F12 without re-pointing its assignments produces this, and it survives every
        /// round trip because both files are individually well-formed.
        /// </summary>
        [Test]
        public void EverySetIdReferencedAnywhere_ExistsInSetsJson()
        {
            var dangling = new List<string>();

            foreach (var kv in _byArchetype)
                if (!_sets.ContainsKey(kv.Value))
                    dangling.Add($"  • assignments.json by_archetype['{kv.Key}'] → '{kv.Value}'");

            foreach (var kv in _byEid)
                if (!_sets.ContainsKey(kv.Value))
                    dangling.Add($"  • assignments.json by_eid['{kv.Key}'] → '{kv.Value}'");

            foreach (var def in _monsters)
                if (!string.IsNullOrEmpty(def.fsmSet) && !_sets.ContainsKey(def.fsmSet))
                    dangling.Add($"  • {def.monsterKey}.asset MonsterDefinition.fsmSet → '{def.fsmSet}'");

            Assert.IsEmpty(dangling,
                "These references name a set that does not exist in sets.json. Each one " +
                "silently degrades to the hard-coded IdleState boot:\n" +
                string.Join("\n", dangling) +
                "\nKnown set ids: " + string.Join(", ", _sets.Keys.OrderBy(k => k)));
        }

        // ── 3. Initial state ────────────────────────────────────────────────────

        /// <summary>
        /// Every set's <c>initial</c> must name a node the set itself declares, and that
        /// node's class must resolve to an instantiable <see cref="IState"/>.
        ///
        /// <c>initial</c> names a NODE, and the node's <c>class</c> (falling back to its own
        /// id) names the C# type — the two are not the same string for anything authored in
        /// F12, where "Add Node" writes an id like <c>state_1</c> with an empty class. A set
        /// whose initial node was deleted or renamed still loads, still parses, and still
        /// lists its states; it only fails at spawn, as a single warning, for every monster
        /// assigned to it.
        ///
        /// Instantiation is checked through <see cref="FSMRuntimeFactory.CreateState"/> —
        /// the production reflection path, so a state that compiles but has lost its public
        /// parameterless constructor is caught here rather than in the console at run time.
        /// </summary>
        [Test]
        public void EverySet_InitialState_IsDeclaredAndInstantiable()
        {
            var problems = new List<string>();

            foreach (var set in _sets.Values.OrderBy(s => s.Id))
            {
                if (string.IsNullOrEmpty(set.InitialNodeId))
                {
                    problems.Add($"  • set '{set.Id}' declares no 'initial' node.");
                    continue;
                }

                if (!set.StateClassByNodeId.ContainsKey(set.InitialNodeId))
                {
                    problems.Add(
                        $"  • set '{set.Id}': initial '{set.InitialNodeId}' is not one of its " +
                        $"own states ({string.Join(", ", set.StateClassByNodeId.Keys)}).");
                    continue;
                }

                string cls = set.StateClassByNodeId[set.InitialNodeId];
                if (FSMRuntimeFactory.CreateState(cls) == null)
                    problems.Add(
                        $"  • set '{set.Id}': initial node '{set.InitialNodeId}' resolves to " +
                        $"class '{cls}', which is not an instantiable IState.");
            }

            Assert.IsEmpty(problems,
                "A set whose initial state cannot be built drops every monster assigned to it " +
                "onto the hard-coded IdleState boot:\n" + string.Join("\n", problems));
        }

        // ── 4. Transition endpoints ─────────────────────────────────────────────

        /// <summary>
        /// Every authored transition's <c>from</c> and <c>to</c> must name a state the set
        /// declares. <c>'*'</c> is legal as a <c>from</c> and means Any State.
        ///
        /// An edge pointing at a node that no longer exists is not an error anywhere: the
        /// factory ignores an unresolvable <c>to</c> with one warning, and an unresolvable
        /// <c>from</c> is worse — the edge is built, installed, and then never matches the
        /// current state, so the machine simply never takes it. Deleting a node in F12
        /// presents itself as an ordinary undoable edit and leaves exactly this behind.
        /// </summary>
        [Test]
        public void EveryTransition_EndpointsAreDeclaredStates()
        {
            var problems = new List<string>();

            foreach (var set in _sets.Values.OrderBy(s => s.Id))
            {
                foreach (var t in set.Transitions)
                {
                    if (t.From != AnyStateWildcard && !set.StateClassByNodeId.ContainsKey(t.From))
                        problems.Add(
                            $"  • set '{set.Id}', transition {t.Describe()}: 'from' names " +
                            $"'{t.From}', which the set does not declare.");

                    if (!set.StateClassByNodeId.ContainsKey(t.To))
                        problems.Add(
                            $"  • set '{set.Id}', transition {t.Describe()}: 'to' names " +
                            $"'{t.To}', which the set does not declare.");
                }
            }

            Assert.IsEmpty(problems,
                "These edges reference states their set does not declare. They are dropped or " +
                "never match, in silence:\n" + string.Join("\n", problems));
        }

        // ── 5. Guards ───────────────────────────────────────────────────────────

        /// <summary>
        /// Every authored guard must survive <see cref="FSMCondition.Parse"/>.
        ///
        /// This is the assertion that inverts intent rather than losing it. Parse returns
        /// null both for an EMPTY guard — a deliberate unconditional edge — and for a
        /// MALFORMED one; only the <c>error</c> out-parameter separates them. Anything that
        /// checks the returned condition instead of the error therefore reads a typo as
        /// "no guard", i.e. an edge that fires on the first frame it is evaluated instead of
        /// never. <c>hp_pct &lt; 0.25</c> mistyped is not a flee that never triggers; it is a
        /// monster that flees immediately, forever.
        ///
        /// The guard slot has three spellings: <c>guard</c> is what the F12 Transition tab
        /// writes, <c>when</c> and <c>condition</c> are the seed generator's names for it.
        /// Read all three, exactly as the factory does.
        /// </summary>
        [Test]
        public void EveryTransitionGuard_Parses()
        {
            var problems = new List<string>();

            foreach (var set in _sets.Values.OrderBy(s => s.Id))
            {
                foreach (var t in set.Transitions)
                {
                    if (string.IsNullOrWhiteSpace(t.RawGuard)) continue; // deliberate uncond. edge

                    FSMCondition.Parse(t.RawGuard, out string error);
                    if (error != null)
                        problems.Add(
                            $"  • set '{set.Id}', transition {t.Describe()}: guard " +
                            $"\"{t.RawGuard}\" — {error}");
                }
            }

            Assert.IsEmpty(problems,
                "An unparseable guard is treated as NO guard, which makes the edge " +
                "unconditional — the opposite of what was authored:\n" +
                string.Join("\n", problems));
        }

        // ── 6. NPCCastState ↔ autoCast ──────────────────────────────────────────

        /// <summary>
        /// <see cref="NPCCastState"/> and <c>MonsterDefinition.autoCast</c> must agree, in
        /// both directions, for every monster.
        ///
        /// A caster whose set omits NPCCastState never casts, loudly-but-uselessly:
        /// <c>NPCAutoCast</c> pushes the state with <c>FSM.ChangeState(new NPCCastState())</c>
        /// and <c>StateMachine.ChangeState</c> refuses it against the allowed-state whitelist
        /// built from the set's own node list (only Death / Damage / Unconscious bypass that
        /// whitelist). The refusal warns once per From&gt;To pair for the whole session, so
        /// the reader sees one line and a monster that walks up and does nothing.
        ///
        /// The converse is dead weight of the same shape as an unused <c>castSheets</c>: a
        /// set that declares NPCCastState but is assigned to a monster that can never have
        /// <c>NPCAutoCast</c> attached, so the node reads as a capability the monster does
        /// not have and the next author to look at the graph believes it.
        ///
        /// "Can cast" is NOT the <c>autoCast</c> flag — see
        /// <see cref="CanEverEnterCastState"/>. Reading the flag alone is wrong in both
        /// directions: <c>EntitySetup.ConfigureBoss</c> attaches NPCAutoCast to any
        /// definition carrying a <c>bossDefinition</c> WITHOUT consulting the flag (which is
        /// why <c>barbol_boss</c> ships <c>autoCast: 0</c> and still casts every few seconds
        /// through <c>BossConfigurator.ApplyPhaseAutoCast</c>), and
        /// <c>ConfigureMonsterAutoCast</c> also returns when <c>autoCastList</c> is empty, so
        /// a raised flag with no spells attaches nothing either.
        /// </summary>
        [Test]
        public void NPCCastState_IsDeclaredExactlyForAutoCastingMonsters()
        {
            var castersWithoutTheState = new List<string>();
            var nonCastersWithTheState = new List<string>();

            foreach (var def in _monsters)
            {
                if (!TryResolveSetForMonster(def, out var set, out string source)) continue;

                bool declares = set.DeclaresStateClass(nameof(NPCCastState));
                bool canCast  = CanEverEnterCastState(def, out string reason);

                if (canCast && !declares)
                    castersWithoutTheState.Add(
                        $"  • {def.monsterKey} ({reason}) → set '{set.Id}' via {source}, " +
                        "which declares no NPCCastState node.");
                else if (!canCast && declares)
                    nonCastersWithTheState.Add(
                        $"  • {def.monsterKey} ({reason}) → set '{set.Id}' via {source}, " +
                        "which declares an NPCCastState node it can never enter.");
            }

            Assert.IsEmpty(castersWithoutTheState,
                "These monsters auto-cast but resolve to a set with no NPCCastState node. " +
                "StateMachine.ChangeState refuses the push against the allowed-state " +
                "whitelist, so they silently never cast:\n" +
                string.Join("\n", castersWithoutTheState));

            Assert.IsEmpty(nonCastersWithTheState,
                "These monsters do not auto-cast, yet their set declares NPCCastState. " +
                "NPCAutoCast is the only thing that enters it and EntitySetup attaches it to " +
                "neither of these, so the node is unreachable and misdescribes " +
                "the monster:\n" + string.Join("\n", nonCastersWithTheState));
        }


        /// <summary>
        /// Mirrors the two places that actually attach <c>NPCAutoCast</c>, which is the only
        /// component that ever pushes an entity into <c>NPCCastState</c>. Reading
        /// <c>autoCast</c> on its own is wrong in BOTH directions: it misses the boss path,
        /// which ignores the flag entirely, and it accepts a flag set with an empty list,
        /// which attaches nothing.
        /// </summary>
        private static bool CanEverEnterCastState(MonsterDefinition def, out string reason)
        {
            if (def.bossDefinition != null)
            {
                reason = "boss - ConfigureBoss attaches NPCAutoCast regardless of autoCast";
                return true;
            }
            if (def.autoCast && def.autoCastList != null && def.autoCastList.Length > 0)
            {
                reason = $"autoCast with {def.autoCastList.Length} spell(s)";
                return true;
            }
            reason = def.autoCast
                ? "autoCast = true but autoCastList is empty, so nothing is attached"
                : "autoCast = false and not a boss";
            return false;
        }

        // ── Resolution, mirroring FSMRuntimeFactory ─────────────────────────────

        private const string AnyStateWildcard = "*";

        /// <summary>
        /// The archetype-level half of <c>FSMRuntimeFactory.TryBuildForEntity</c>'s order:
        /// <c>by_archetype</c>, then <c>MonsterDefinition.fsmSet</c>. <c>by_eid</c> is
        /// deliberately absent — it is keyed by placement id, so it can never answer the
        /// question "does this archetype have a brain?".
        ///
        /// A <c>by_archetype</c> entry naming a set that does not exist does NOT fall
        /// through to <c>fsmSet</c>: the factory returns false outright on that path, and a
        /// resolver kinder than production would report coverage the monster does not have —
        /// which is the precise failure this fixture exists to catch, papered over by the
        /// fixture itself.
        /// </summary>
        private bool TryResolveSetForMonster(MonsterDefinition def, out ShippedSet set, out string source)
        {
            set = null;
            source = null;

            if (!string.IsNullOrEmpty(def.monsterKey) &&
                _byArchetype.TryGetValue(def.monsterKey, out string archSetId))
            {
                if (_sets.TryGetValue(archSetId, out set))
                {
                    source = "by_archetype";
                    return true;
                }
                set = null;
                return false;
            }

            if (!string.IsNullOrEmpty(def.fsmSet) && _sets.TryGetValue(def.fsmSet, out set))
            {
                source = "MonsterDefinition.fsmSet";
                return true;
            }

            set = null;
            return false;
        }

        // ── Loading ─────────────────────────────────────────────────────────────

        private static string FsmDataPath(string fileName) =>
            Path.Combine(Application.streamingAssetsPath, "FSM", fileName);

        private static Dictionary<string, object> ReadJsonRoot(string fileName)
        {
            string path = FsmDataPath(fileName);
            Assert.IsTrue(File.Exists(path),
                $"Shipped FSM data '{fileName}' is missing at {path}. Without it every monster " +
                "boots the hard-coded IdleState fallback.");

            var root = MiniJsonRuntime.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>;
            Assert.IsNotNull(root, $"'{fileName}' did not parse as a JSON object.");
            return root;
        }

        private static Dictionary<string, ShippedSet> LoadSets()
        {
            var root = ReadJsonRoot("sets.json");
            var result = new Dictionary<string, ShippedSet>(System.StringComparer.Ordinal);

            var sets = root.TryGetValue("sets", out var o) ? o as List<object> : null;
            Assert.IsNotNull(sets, "sets.json has no 'sets' array.");

            foreach (var item in sets)
            {
                if (!(item is Dictionary<string, object> dict)) continue;

                string id = AsStr(dict, "id");
                if (string.IsNullOrEmpty(id)) continue;

                result[id] = new ShippedSet(
                    id,
                    AsStr(dict, "initial"),
                    ExtractStateClassMap(dict),
                    ExtractTransitions(dict));
            }
            return result;
        }

        private static Dictionary<string, string> LoadAssignmentTable(string tableKey)
        {
            var root = ReadJsonRoot("assignments.json");
            var result = new Dictionary<string, string>(System.StringComparer.Ordinal);

            var table = root.TryGetValue(tableKey, out var o) ? o as Dictionary<string, object> : null;
            if (table == null) return result; // an absent or empty table is legitimate

            foreach (var kv in table)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                    result[kv.Key] = kv.Value.ToString();

            return result;
        }

        /// <summary>
        /// Every <see cref="MonsterDefinition"/> asset in the project that carries a
        /// monsterKey — the same corpus <c>FSMSeedGenerator.LoadMonstersForArchetypes</c>
        /// walks, so "shipped monster" means the same thing here as it does to the tool that
        /// writes the assignments. A key-less asset in the monsters folder is not a monster
        /// the runtime can ever look up, so it is skipped rather than reported.
        /// </summary>
        private static List<MonsterDefinition> LoadShippedMonsters()
        {
            var result = new List<MonsterDefinition>();
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(MonsterDefinition)}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
                if (def != null && !string.IsNullOrEmpty(def.monsterKey))
                    result.Add(def);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.monsterKey, b.monsterKey));
            return result;
        }

        // ── JSON shape helpers, matching FSMRuntimeFactory's own reading ────────

        private static string AsStr(Dictionary<string, object> dict, string key) =>
            dict.TryGetValue(key, out var v) && v != null ? v.ToString() : null;

        /// <summary>
        /// node id → C# class name. A node names its class in <c>class</c>; when that is
        /// empty the id doubles as the class name, which is how the seeded
        /// <c>Monster_Default</c> set works (its node ids ARE the type names).
        /// </summary>
        private static Dictionary<string, string> ExtractStateClassMap(Dictionary<string, object> setDict)
        {
            var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
            if (!setDict.TryGetValue("states", out var statesObj)) return map;
            if (!(statesObj is List<object> states)) return map;

            foreach (var s in states)
            {
                if (!(s is Dictionary<string, object> d)) continue;

                string id = AsStr(d, "id");
                if (string.IsNullOrEmpty(id)) continue;

                string cls = AsStr(d, "class");
                map[id] = string.IsNullOrEmpty(cls) ? id : cls;
            }
            return map;
        }

        private static List<ShippedTransition> ExtractTransitions(Dictionary<string, object> setDict)
        {
            var result = new List<ShippedTransition>();
            if (!setDict.TryGetValue("transitions", out var raw)) return result;
            if (!(raw is List<object> list)) return result;

            foreach (var item in list)
            {
                if (!(item is Dictionary<string, object> d)) continue;

                string from = AsStr(d, "from");
                string to   = AsStr(d, "to");
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) continue;

                result.Add(new ShippedTransition(
                    AsStr(d, "id"),
                    from,
                    to,
                    AsStr(d, "guard") ?? AsStr(d, "when") ?? AsStr(d, "condition")));
            }
            return result;
        }

        // ── Read-only snapshots of the authored model ───────────────────────────

        private sealed class ShippedSet
        {
            public string Id { get; }
            public string InitialNodeId { get; }
            public Dictionary<string, string> StateClassByNodeId { get; }
            public List<ShippedTransition> Transitions { get; }

            public ShippedSet(string id, string initialNodeId,
                              Dictionary<string, string> stateClassByNodeId,
                              List<ShippedTransition> transitions)
            {
                Id = id;
                InitialNodeId = initialNodeId;
                StateClassByNodeId = stateClassByNodeId;
                Transitions = transitions;
            }

            /// <summary>
            /// True when any node in this set resolves to the given C# class. Asks about the
            /// CLASS, never the node id — a node authored in F12 is called <c>state_1</c>
            /// while its class field carries the real type name.
            /// </summary>
            public bool DeclaresStateClass(string className) =>
                StateClassByNodeId.Values.Any(c => string.Equals(c, className, System.StringComparison.Ordinal));
        }

        private sealed class ShippedTransition
        {
            public string Id { get; }
            public string From { get; }
            public string To { get; }
            public string RawGuard { get; }

            public ShippedTransition(string id, string from, string to, string rawGuard)
            {
                Id = id;
                From = from;
                To = to;
                RawGuard = rawGuard;
            }

            /// <summary>Names the edge the way the F12 graph shows it, so a failure can be
            /// found by eye without counting rows in the JSON.</summary>
            public string Describe()
            {
                var sb = new StringBuilder();
                sb.Append('\'').Append(From).Append("' → '").Append(To).Append('\'');
                if (!string.IsNullOrEmpty(Id)) sb.Append(" [").Append(Id).Append(']');
                return sb.ToString();
            }
        }
    }
}
