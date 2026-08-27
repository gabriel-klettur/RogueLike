using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Enemies.FSM;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// Keeps <see cref="FSMBuiltInTransitions"/> honest.
    ///
    /// That registry is a hand-written mirror of what the state classes do, drawn in F12 as
    /// the read-only half of the graph. A hand-written mirror of code is worthless the moment
    /// someone adds a <c>ChangeState</c> call and forgets it — and worse than worthless,
    /// because the graph would then be confidently wrong instead of merely incomplete, which
    /// is the exact failure this whole feature exists to end.
    ///
    /// So the census is taken from the SOURCE, not from a second hand-written list: every
    /// <c>ChangeState(new SomeState())</c> in the production state machine is extracted by
    /// reading the .cs files, and the two sets must agree in BOTH directions — an undeclared
    /// coded edge fails, and a declared edge whose code was deleted fails too.
    ///
    /// The project has precedent for a test that reads its own source
    /// (<c>GameEditorManagerBootRaceWiringTests</c>), including the lesson that came with it:
    /// a test about code must not match text inside a comment. Hence <see cref="StripComments"/>.
    /// </summary>
    [TestFixture]
    public class FSMBuiltInTransitionRegistryTests
    {
        private const string StatesDir = "_Project/Scripts/Gameplay/Enemies/FSM/States";
        private const string MachineFile = "_Project/Scripts/Gameplay/Enemies/FSM/StateMachine.cs";
        private const string AutoCastFile = "_Project/Scripts/Gameplay/Enemies/NPCAutoCast.cs";

        /// <summary>
        /// <c>ChangeState(next)</c> inside <c>TryTakeAuthoredTransition</c> is the dispatcher
        /// that APPLIES an authored edge. It is not itself a coded edge, and counting it would
        /// mean the registry had to declare a transition to a variable.
        /// </summary>
        private static readonly Regex ChangeStateCall =
            new Regex(@"ChangeState\(\s*(?:[A-Za-z_]\w*\s*\?\?\s*)?new\s+(\w+)\s*\(", RegexOptions.Compiled);

        private static string Root => Path.GetDirectoryName(Application.dataPath) + "/Assets";

        private readonly struct Edge
        {
            public readonly string From, To, File;
            public Edge(string from, string to, string file) { From = from; To = to; File = file; }
            public string Key => From + ">" + To;
        }

        // ── The census ───────────────────────────────────────────────────────────

        private static List<Edge> CensusFromSource()
        {
            var edges = new List<Edge>();

            var statesDir = Path.Combine(Root, StatesDir);
            Assert.IsTrue(Directory.Exists(statesDir),
                $"State classes must live at {StatesDir}. If they moved, this test's census " +
                "is blind and the registry it guards is unguarded — update the path.");

            foreach (var path in Directory.GetFiles(statesDir, "*.cs"))
            {
                // A state class file is named for its class, and the class IS the FROM state.
                string from = Path.GetFileNameWithoutExtension(path);
                CollectInto(edges, path, from);
            }

            // Two edges are raised by the machine itself rather than from inside a state, so
            // their source is the wildcard: they can fire from whatever state is current.
            CollectInto(edges, Path.Combine(Root, MachineFile), FSMBuiltInTransitions.AnyState);
            CollectInto(edges, Path.Combine(Root, AutoCastFile), FSMBuiltInTransitions.AnyState);

            return edges;
        }

        private static void CollectInto(List<Edge> into, string path, string from)
        {
            if (!File.Exists(path)) return;
            string code = StripComments(File.ReadAllText(path));

            foreach (Match m in ChangeStateCall.Matches(code))
            {
                string to = m.Groups[1].Value;
                // A state constructed as an ARGUMENT to another state is not a transition —
                // e.g. new DamageState(duration, fromLeft, resumeState). Only the outermost
                // capture is a target, and the regex anchors on ChangeState( so that holds.
                var edge = new Edge(from, to, path);
                if (!into.Any(e => e.Key == edge.Key)) into.Add(edge);
            }
        }

        /// <summary>
        /// Removes // line comments, /* block */ comments and string literals before matching.
        /// String literals matter here because this very registry quotes
        /// "ChangeState(new ...)" inside its own documentation, and a census that counted
        /// prose would invent edges that do not exist.
        /// </summary>
        private static string StripComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            bool inLine = false, inBlock = false, inString = false, inChar = false, escaped = false;

            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                char next = i + 1 < src.Length ? src[i + 1] : '\0';

                if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
                if (inBlock) { if (c == '*' && next == '/') { inBlock = false; i++; } continue; }
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (inChar)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '\'') inChar = false;
                    continue;
                }

                if (c == '/' && next == '/') { inLine = true; i++; continue; }
                if (c == '/' && next == '*') { inBlock = true; i++; continue; }
                if (c == '"') { inString = true; continue; }
                if (c == '\'') { inChar = true; continue; }

                sb.Append(c);
            }
            return sb.ToString();
        }

        // ── The assertions ───────────────────────────────────────────────────────

        [Test]
        public void EveryCodedTransition_IsDeclaredInTheRegistry()
        {
            var census = CensusFromSource();
            var declared = new HashSet<string>(FSMBuiltInTransitions.All.Select(e => e.Key));

            var missing = census.Where(e => !declared.Contains(e.Key)).ToList();

            Assert.IsEmpty(missing,
                "These transitions exist in code but are not declared in FSMBuiltInTransitions, " +
                "so F12 draws a graph that is missing them:\n  " +
                string.Join("\n  ", missing.Select(e =>
                    $"{e.From} -> {e.To}   ({Path.GetFileName(e.File)})")) +
                "\n\nAdd them to FSMBuiltInTransitions._all with a Label and a Detail. The graph " +
                "is only worth trusting while this list is empty.");
        }

        [Test]
        public void EveryDeclaredTransition_StillExistsInCode()
        {
            var census = new HashSet<string>(CensusFromSource().Select(e => e.Key));

            // The resume edge is decided at runtime from a stored class name, so it is not
            // written as `new SomeState()` anywhere and the source census cannot see it.
            var stale = FSMBuiltInTransitions.All
                .Where(e => !e.DynamicTarget && !census.Contains(e.Key))
                .ToList();

            Assert.IsEmpty(stale,
                "These are declared in FSMBuiltInTransitions but no ChangeState call produces " +
                "them any more, so F12 draws edges the machine cannot take:\n  " +
                string.Join("\n  ", stale.Select(e => $"{e.From} -> {e.To}  ({e.Label})")));
        }

        [Test]
        public void TheAuthoredTransitionDispatcher_IsNotCountedAsACodedEdge()
        {
            // StateMachine.TryTakeAuthoredTransition ends in ChangeState(next) — applying a
            // DESIGNER'S edge. If the census ever picked that up it would demand the registry
            // declare a transition whose target is a variable, and the only way to satisfy it
            // would be to declare something false.
            var fromMachine = CensusFromSource()
                .Where(e => e.File.Replace('\\', '/').EndsWith("StateMachine.cs"))
                .Select(e => e.To)
                .ToList();

            CollectionAssert.DoesNotContain(fromMachine, "next");
            CollectionAssert.AreEquivalent(
                new[] { "DamageState", "UnconsciousState" }, fromMachine,
                "StateMachine itself raises exactly two transitions: the flinch and the death " +
                "drop. Anything else appearing here is a new machine-level edge that needs a " +
                "deliberate decision, not an automatic registry entry.");
        }

        [Test]
        public void FleeAndAlertChase_AreReachableOnlyFromAuthoredData()
        {
            // The load-bearing fact behind the whole honest-graph feature: the two halves of
            // the machine do not overlap. If code ever grows its own path into these states,
            // the graph's story ("dimmed edges are code, bright edges are yours") stops being
            // the whole truth and this test should be the thing that says so.
            var census = CensusFromSource();

            Assert.IsFalse(census.Any(e => e.To == "FleeState"),
                "FleeState just became reachable from code. It used to be reachable ONLY " +
                "through an authored transition, which is why deleting that transition in F12 " +
                "removed the behaviour entirely.");
            Assert.IsFalse(census.Any(e => e.To == "AlertChaseState"),
                "AlertChaseState just became reachable from code — same story as FleeState.");
        }

        [Test]
        public void EveryDeclaredEdge_CarriesALabelAndADetail()
        {
            foreach (var e in FSMBuiltInTransitions.All)
            {
                Assert.IsNotEmpty(e.Label, $"{e.Key} has no graph label");
                Assert.LessOrEqual(e.Label.Length, 32,
                    $"{e.Key}'s label '{e.Label}' will be truncated by the edge caption's width");
                Assert.IsNotEmpty(e.Detail,
                    $"{e.Key} has no Detail — the panel would show an edge with no explanation, " +
                    "which is the state this feature exists to end");
                Assert.IsNotEmpty(e.SourceFile, $"{e.Key} does not say where it lives");
            }
        }

        [Test]
        public void ForStates_SkipsEdgesWhoseEndpointsAreAbsent()
        {
            // A set that omits NPCCastState must not grow a dangling arrow to it.
            var minimal = new HashSet<string> { "IdleState", "ChaseState" };
            var drawn = FSMBuiltInTransitions.ForStates(minimal);

            Assert.IsTrue(drawn.All(e =>
                (e.From == FSMBuiltInTransitions.AnyState || minimal.Contains(e.From)) &&
                (e.To == FSMBuiltInTransitions.AnyState || minimal.Contains(e.To))),
                "ForStates returned an edge with an endpoint the set does not declare.");

            Assert.IsTrue(drawn.Any(e => e.From == "IdleState" && e.To == "ChaseState"),
                "the one edge both endpoints of which are present must survive");
            Assert.IsFalse(drawn.Any(e => e.To == "NPCCastState"),
                "NPCCastState is not in this set and must not be drawn into");
        }

        [Test]
        public void TheGraphShowsMoreCodedEdgesThanAuthoredOnes_WhichIsWhyThisExists()
        {
            // Not a vanity metric. If this ever inverts because the coded edges were migrated
            // into sets.json, the built-in layer has served its purpose and the assertion
            // should be revisited deliberately rather than silently passing forever.
            Assert.Greater(FSMBuiltInTransitions.All.Count, 10,
                "The registry has shrunk below the size that motivated it. If coded edges were " +
                "migrated to authored data, update this test and the class doc together.");
        }
    }
}
