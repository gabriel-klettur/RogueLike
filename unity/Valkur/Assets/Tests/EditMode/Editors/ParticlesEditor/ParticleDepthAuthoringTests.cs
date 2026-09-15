using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.ParticlesEditor
{
    /// <summary>
    /// The two halves of authoring a placed emitter's DEPTH: the numbered dropdown, which is
    /// how an author reads the stack, and the Y-sort, which is how an emitter is ordered
    /// against the buildings sharing its layer.
    ///
    /// <para>What is pinned here is the part that fails SILENTLY. A dropdown whose labels drift
    /// out of step with its values stores the neighbour of the layer that was clicked and
    /// nothing throws; a Y-sort that replaced the authored order instead of adding to it would
    /// collapse every layer of a composite onto one value and look merely "a bit off".</para>
    /// </summary>
    [TestFixture]
    public class ParticleDepthAuthoringTests
    {
        // ── The numbered dropdown ────────────────────────────────────────────

        [Test]
        public void Labels_AreIndexAligned_WithTheStoredNames()
        {
            var names  = ParticlePresetFieldWriter.SortingLayerNames("");
            var labels = ParticlePresetFieldWriter.SortingLayerLabels("");

            Assert.That(labels.Length, Is.EqualTo(names.Length),
                "the form reports the index of the row picked and it is resolved against the NAME list");

            for (int i = 0; i < names.Length; i++)
                Assert.That(labels[i], Does.Contain(names[i]),
                    $"label {i} must name the layer it stores");
        }

        [Test]
        public void EveryLabel_CarriesItsPositionInTheDrawStack()
        {
            var labels = ParticlePresetFieldWriter.SortingLayerLabels("");
            var stack  = SortingLayer.layers;

            Assert.That(stack.Length, Is.GreaterThan(0));
            for (int i = 0; i < stack.Length; i++)
                Assert.That(labels[i], Does.StartWith(i.ToString()),
                    "the number is what says whether a pick lands in front of the player");
        }

        /// <summary>
        /// The stored value stays the NAME. A number in the label is display; a number in the
        /// asset would move every placed emitter's depth the day somebody inserts a layer in
        /// ProjectSettings, silently.
        /// </summary>
        [Test]
        public void PickingARow_StoresTheName_NeverTheNumber()
        {
            var names = ParticlePresetFieldWriter.SortingLayerNames("");
            int idx   = Array.IndexOf(names, "Entities");
            Assert.That(idx, Is.GreaterThanOrEqualTo(0), "this project ships an Entities layer");

            var v = new ParticleVfxParams();
            Assert.That(ParticlePresetFieldWriter.TrySetField(v, "vfx.sortingLayer", idx, out string err),
                Is.True, err);
            Assert.That(v.sortingLayer, Is.EqualTo("Entities"));
        }

        /// <summary>A layer the project no longer defines keeps its row so the author can see
        /// what the asset says — but it has no position in a stack it is not in.</summary>
        [Test]
        public void AMissingLayer_IsLabelledRatherThanNumbered()
        {
            var labels = ParticlePresetFieldWriter.SortingLayerLabels("NoSuchLayerXYZ");
            Assert.That(labels[labels.Length - 1], Does.Contain("NoSuchLayerXYZ"));
            Assert.That(labels[labels.Length - 1], Does.Not.StartWith((labels.Length - 1).ToString()));
        }

        // ── Y-sort ───────────────────────────────────────────────────────────

        [Test]
        public void YSort_IsOffByDefault()
        {
            Assert.That(new ParticleVfxParams().ySort, Is.False,
                "nothing already authored may change depth on import");
        }

        [Test]
        public void YSort_RoundTripsThroughTheInstanceConfigJson()
        {
            var v = new ParticleVfxParams { ySort = true, sortingLayer = "PropsL4", sortingOrder = 3 };
            string json = ParticleVfxParamsJson.Write(v);

            Assert.That(json, Does.Contain("ySort"),
                "a value that differs from the default must be written, or it is lost on reload");

            var back = ParticleVfxParamsJson.Read(
                Valkur.Gameplay.World.MiniJsonRuntime.Deserialize(json) as System.Collections.Generic.Dictionary<string, object>);

            Assert.That(back.ySort, Is.True);
            Assert.That(back.sortingLayer, Is.EqualTo("PropsL4"));
            Assert.That(back.sortingOrder, Is.EqualTo(3));
        }

        /// <summary>
        /// The authored order is a NUDGE, not a value that is thrown away — that is what keeps
        /// a composite's layers ordered against each other, since they share one Y and differ
        /// only in the order each block authored.
        /// </summary>
        [Test]
        public void TheAuthoredOrder_IsAddedToTheYTerm_NotReplacedByIt()
        {
            var go = new GameObject("ysort-probe");
            try
            {
                go.transform.position = new Vector3(0f, 12.5f, 0f);

                var a = MakeRenderer(go, "a", 0);
                var b = MakeRenderer(go, "b", 7);

                var binder = go.AddComponent<ParticleYSort>();
                binder.Rebind();

                int yTerm = SortingConfig.YToSortingOrder(12.5f);
                Assert.That(a.sortingOrder, Is.EqualTo(yTerm));
                Assert.That(b.sortingOrder, Is.EqualTo(yTerm + 7),
                    "collapsing both onto one value would leave sortingFudge as the only tie-break");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// The same formula the buildings use, so an emitter and the house it stands beside are
        /// ordered by the same rule rather than by two that agree today.
        /// </summary>
        [Test]
        public void MovingTheEmitter_ReordersIt_ByTheSameRuleAsABuilding()
        {
            var go = new GameObject("ysort-probe");
            try
            {
                go.transform.position = new Vector3(0f, 10f, 0f);
                var r = MakeRenderer(go, "r", 0);
                var binder = go.AddComponent<ParticleYSort>();
                binder.Rebind();

                int high = r.sortingOrder;

                // Further DOWN the screen must draw in FRONT: a larger order.
                go.transform.position = new Vector3(0f, 4f, 0f);
                binder.ApplyNow();

                Assert.That(r.sortingOrder, Is.GreaterThan(high));
                Assert.That(r.sortingOrder, Is.EqualTo(SortingConfig.YToSortingOrder(4f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// Edit Mode never calls LateUpdate, so a binder that only ordered from there would
        /// leave every placement drawing at its authored order until something moved it — and
        /// this whole fixture would be measuring nothing.
        /// </summary>
        [Test]
        public void Rebind_OrdersImmediately_WithoutWaitingForAFrame()
        {
            var go = new GameObject("ysort-probe");
            try
            {
                go.transform.position = new Vector3(0f, 3f, 0f);
                var r = MakeRenderer(go, "r", 0);
                r.sortingOrder = 0;

                go.AddComponent<ParticleYSort>().Rebind();

                Assert.That(r.sortingOrder, Is.EqualTo(SortingConfig.YToSortingOrder(3f)));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        /// <summary>
        /// The emitter is the only thing that attaches the binder, and only for a PLACED
        /// emitter. A source check because the gate lives inside a private method reached from
        /// two apply paths, and because deleting it is invisible until a preview emitter
        /// vanishes behind its own background.
        /// </summary>
        [Test]
        public void TheBinder_IsGatedOnAPlacedEmitter()
        {
            var refresh = typeof(ParticleEmitter).GetMethod("RefreshYSort",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(refresh, Is.Not.Null, "the emitter owns the depth policy");

            string src = System.IO.File.ReadAllText(System.IO.Path.Combine(
                Application.dataPath, "_Project/Scripts/Gameplay/VFX/ParticleEmitter.cs"));

            int gate = src.IndexOf("RefreshYSort", StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(-1));
            Assert.That(src, Does.Contain("GetComponent<PersistedParticleInstance>()"),
                "a preview emitter and a spell's effect have no world position to be ordered against");
        }

        private static SpriteRenderer MakeRenderer(GameObject parent, string name, int order)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            var r = child.AddComponent<SpriteRenderer>();
            r.sortingOrder = order;
            return r;
        }
    }
}
