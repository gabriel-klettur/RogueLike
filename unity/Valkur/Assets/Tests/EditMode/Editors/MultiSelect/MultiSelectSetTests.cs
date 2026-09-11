using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Editors.MultiSelect
{
    /// <summary>
    /// The cross-domain selection set: order, primary, pruning and re-pointing.
    ///
    /// <para>Reached by REFLECTION because <c>MultiSelectSet</c> and <c>ISelectionDomain</c> are
    /// <c>internal</c> to <c>Valkur.Gameplay</c>, and they are internal on purpose — nothing
    /// outside the tool has any business holding a selection. An <c>InternalsVisibleTo</c> would
    /// open the whole assembly to the test one for three types.</para>
    ///
    /// <para>The audit that prompted these found <b>zero tests</b> across thirteen new files.
    /// Two of its findings were exactly what a fixture catches: a toggle that only worked once,
    /// and a count line that printed the same number twice.</para>
    /// </summary>
    [TestFixture]
    public class MultiSelectSetTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        private static System.Type SetType =>
            System.Type.GetType("Valkur.Gameplay.Editors.MultiSelect.MultiSelectSet, Valkur.Gameplay");
        private static System.Type DomainType =>
            System.Type.GetType("Valkur.Gameplay.Editors.MultiSelect.ISelectionDomain, Valkur.Gameplay");

        private object   _set;
        private object   _domain;
        private readonly System.Collections.Generic.List<GameObject> _spawned =
            new System.Collections.Generic.List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            Assert.NotNull(SetType,    "MultiSelectSet not found — did the namespace move?");
            Assert.NotNull(DomainType, "ISelectionDomain not found — did the namespace move?");
            _set = System.Activator.CreateInstance(SetType, nonPublic: true);

            // The set never calls into a domain; it only stores the reference. Any live
            // implementation will do, and the Buildings one needs no scene to be constructed.
            var buildingDomain = System.Type.GetType(
                "Valkur.Gameplay.Editors.MultiSelect.BuildingSelectionDomain, Valkur.Gameplay");
            Assert.NotNull(buildingDomain);
            _domain = System.Activator.CreateInstance(buildingDomain, nonPublic: true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GameObject Obj(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        private bool  Add(GameObject go)      => (bool)SetType.GetMethod("Add", Any).Invoke(_set, new object[] { _domain, go });
        private bool  Toggle(GameObject go)   => (bool)SetType.GetMethod("Toggle", Any).Invoke(_set, new object[] { _domain, go });
        private bool  Remove(GameObject go)   => (bool)SetType.GetMethod("Remove", Any).Invoke(_set, new object[] { go });
        private bool  Contains(GameObject go) => (bool)SetType.GetMethod("Contains", Any).Invoke(_set, new object[] { go });
        private int   Prune()                 => (int)SetType.GetMethod("Prune", Any).Invoke(_set, null);
        private int   Count                   => (int)SetType.GetProperty("Count", Any).GetValue(_set);
        private int   CountIn(string id)      => (int)SetType.GetMethod("CountIn", Any).Invoke(_set, new object[] { id });
        private void  Repoint(GameObject a, GameObject b) => SetType.GetMethod("Repoint", Any).Invoke(_set, new object[] { a, b });

        private GameObject PrimaryGo()
        {
            var primary = SetType.GetProperty("Primary", Any).GetValue(_set);
            return (GameObject)primary.GetType().GetField("Go", Any).GetValue(primary);
        }

        // ── Order and primary ────────────────────────────────────────────────

        [Test]
        public void TheLastPicked_IsPrimary()
        {
            var a = Obj("a"); var b = Obj("b"); var c = Obj("c");
            Add(a); Add(b); Add(c);
            Assert.AreEqual(3, Count);
            Assert.AreSame(c, PrimaryGo(), "The primary is what a drag anchors on and what a paste lands under.");
        }

        [Test]
        public void ReAdding_PromotesRatherThanDuplicating()
        {
            var a = Obj("a"); var b = Obj("b");
            Add(a); Add(b);
            bool wasNew = Add(a);

            Assert.IsFalse(wasNew, "Re-adding an existing member is a promotion, not an addition.");
            Assert.AreEqual(2, Count, "Clicking a member twice must not put it in the set twice.");
            Assert.AreSame(a, PrimaryGo(),
                "Clicking something already selected makes it primary — that is what lets an " +
                "author choose the anchor of a group they already built.");
        }

        [Test]
        public void Toggle_ReportsMembershipAfterTheCall()
        {
            var a = Obj("a");
            Assert.IsTrue(Toggle(a),  "First toggle selects.");
            Assert.IsTrue(Contains(a));
            Assert.IsFalse(Toggle(a), "Second toggle deselects.");
            Assert.IsFalse(Contains(a));
            Assert.AreEqual(0, Count);
        }

        // ── Membership is not eligibility ────────────────────────────────────

        [Test]
        public void Prune_DropsDestroyedMembers_AndKeepsTheRest()
        {
            var a = Obj("a"); var b = Obj("b"); var c = Obj("c");
            Add(a); Add(b); Add(c);

            Object.DestroyImmediate(b);
            _spawned.Remove(b);

            Assert.AreEqual(1, Prune(), "Exactly the destroyed member is dropped.");
            Assert.AreEqual(2, Count);
            Assert.IsTrue(Contains(a));
            Assert.IsTrue(Contains(c));
            Assert.AreSame(c, PrimaryGo(), "Pruning the middle must not disturb the primary.");
        }

        [Test]
        public void Items_IsASnapshot_NotTheBackingList()
        {
            var a = Obj("a"); var b = Obj("b");
            Add(a); Add(b);

            var items = SetType.GetProperty("Items", Any).GetValue(_set) as System.Collections.IList;
            Assert.AreEqual(2, items.Count);

            Remove(a);

            Assert.AreEqual(2, items.Count,
                "A caller looping the selection while an operation removes members would skip " +
                "entries silently if this were the live list — the AlliedUnit.Live rule.");
            Assert.AreEqual(1, Count);
        }

        // ── Re-pointing after a destructive undo ─────────────────────────────

        [Test]
        public void Repoint_SwapsTheObject_AndKeepsItsPlaceInTheOrder()
        {
            var a = Obj("a"); var b = Obj("b"); var c = Obj("c");
            Add(a); Add(b); Add(c);

            var rebuilt = Obj("b-rebuilt");
            Repoint(b, rebuilt);

            Assert.IsTrue(Contains(rebuilt));
            Assert.IsFalse(Contains(b));
            Assert.AreEqual(3, Count);
            Assert.AreSame(c, PrimaryGo(),
                "A restored emitter is a NEW GameObject — its domain destroys rather than " +
                "deactivates — so re-pointing must not reorder the group around it.");
        }

        [Test]
        public void Repoint_OfSomethingNotInTheSet_ChangesNothing()
        {
            var a = Obj("a");
            Add(a);
            Repoint(Obj("stranger"), Obj("replacement"));
            Assert.AreEqual(1, Count);
            Assert.IsTrue(Contains(a));
        }

        // ── Per-domain counts feed the panel ─────────────────────────────────

        [Test]
        public void CountIn_AnswersPerDomain()
        {
            var a = Obj("a"); var b = Obj("b");
            Add(a); Add(b);
            Assert.AreEqual(2, CountIn("building"));
            Assert.AreEqual(0, CountIn("particle"),
                "The per-domain count is the only thing on screen saying a group spans three files.");
        }

        // ── Every domain declares its own singular ───────────────────────────

        /// <summary>
        /// Caught live, not here: the count line read <b>"1 luce"</b>.
        ///
        /// <para>The singular was being derived by dropping the final "s", which is right for
        /// "edificios" and "particulas" and wrong for "luces" — the Spanish singular is "luz".
        /// A rule correct for two of three labels is worse than three declarations, because
        /// the wrong one surfaces only when a selection happens to hold exactly one light.</para>
        /// </summary>
        [Test]
        public void EveryDomain_DeclaresASingular_ThatIsNotJustThePluralMinusItsS()
        {
            var asm = SetType.Assembly;
            var domainTypes = asm.GetTypes()
                .Where(t => !t.IsInterface && !t.IsAbstract && DomainType.IsAssignableFrom(t))
                .ToList();

            Assert.IsNotEmpty(domainTypes, "No ISelectionDomain implementations found.");

            foreach (var dt in domainTypes)
            {
                var d        = System.Activator.CreateInstance(dt, nonPublic: true);
                string plural   = (string)dt.GetProperty("Label", Any).GetValue(d);
                string singular = (string)dt.GetProperty("LabelSingular", Any).GetValue(d);
                string id       = (string)dt.GetProperty("Id", Any).GetValue(d);

                Assert.IsNotEmpty(plural,   $"{dt.Name} has no chip label.");
                Assert.IsNotEmpty(singular, $"{dt.Name} has no singular — '1 {plural}' would reach the panel.");
                Assert.IsNotEmpty(id,       $"{dt.Name} has no id; the workspace filter key needs one.");

                Assert.AreEqual(singular, singular.ToLowerInvariant(),
                    $"{dt.Name}'s singular is used mid-sentence, so it is lower case.");
                Assert.AreNotEqual(plural.ToLowerInvariant(), singular,
                    $"{dt.Name} declares the same word for one and for many.");
            }
        }

        [Test]
        public void DomainIds_AreUnique()
        {
            var asm = SetType.Assembly;
            var ids = asm.GetTypes()
                .Where(t => !t.IsInterface && !t.IsAbstract && DomainType.IsAssignableFrom(t))
                .Select(t => (string)t.GetProperty("Id", Any)
                    .GetValue(System.Activator.CreateInstance(t, nonPublic: true)))
                .ToList();

            CollectionAssert.AllItemsAreUnique(ids,
                "The id keys the workspace filter and the per-domain count; two domains sharing " +
                "one would toggle each other.");
        }
    }
}
