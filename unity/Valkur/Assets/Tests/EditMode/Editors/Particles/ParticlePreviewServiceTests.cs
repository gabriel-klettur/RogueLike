using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Covers <see cref="ParticlePreviewService"/> — the off-screen RenderTexture pump behind the
    /// Particles Editor (F1) picker grid and View panel.
    ///
    /// WHY THIS FIXTURE EXISTS — the shipped bug it guards:
    ///   The first implementation mapped presets onto a FIXED pool of 24 slots with
    ///   <c>slotIdx = i % POOL_SIZE</c>. With 66 presets in the catalog, presets 0, 24 and 48 all
    ///   shared one RenderTexture, so most picker cells showed somebody else's effect — the thumbnail
    ///   you clicked was not the preset you placed. The service was rewritten to allocate one slot per
    ///   visible preset, and <see cref="GetPreviewTexture_SeventyVisiblePresets_ReturnsDistinctTexturePerPreset"/>
    ///   exists specifically so a modulo can never come back: it asserts 70 presets yield 70 distinct
    ///   RenderTextures AND that presets spaced exactly one historical pool-size apart never collide.
    ///
    /// The rest of the fixture pins the contract the picker relies on: unknown/null ids and the
    /// pre-Initialize state return null instead of a stale texture; re-filtering to the same list does
    /// not churn emitters (a rebuilt ParticleSystem restarts every thumbnail); narrowing parks slots and
    /// widening restores the very same textures; the documented MAX_POOL_SIZE ceiling refuses to serve a
    /// thumbnail rather than silently sharing one; Initialize is idempotent; and Shutdown releases every
    /// RenderTexture, destroys every GameObject and resets playback/zoom state (Domain Reload is OFF in
    /// this project, so leaked state survives into the next Play session).
    ///
    /// EditMode notes: the service is a plain C# class, so it is constructed directly with a throwaway
    /// GameObject as parent. Shutdown() uses DestroyImmediate outside play mode, so TearDown can rely on
    /// it. Cameras and RenderTextures are created for real; particle/URP warnings are ignored via
    /// LogAssert.ignoreFailingMessages, EXCEPT the overflow test which asserts a warning on purpose.
    /// </summary>
    [TestFixture]
    public class ParticlePreviewServiceTests
    {
        /// <summary>
        /// The size of the old fixed pool that caused the shared-thumbnail bug. Presets this far apart
        /// in the visible list are exactly the ones that used to collide.
        /// </summary>
        private const int HISTORICAL_POOL_SIZE = 24;

        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();

        private ParticlePreviewService _service;
        private GameObject _parentGo;

        // ── Fixture lifecycle ────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            // Particle material / URP camera setup logs assorted warnings in EditMode that have
            // nothing to do with anything asserted here.
            LogAssert.ignoreFailingMessages = true;

            _parentGo = new GameObject("PPrevServiceTestRoot");
            _sceneObjects.Add(_parentGo);

            _service = new ParticlePreviewService();
        }

        [TearDown]
        public void TearDown()
        {
            // Shutdown first: it owns the cameras / RTs / emitter GameObjects it created.
            _service?.Shutdown();
            _service = null;

            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _assets)
                if (so != null) Object.DestroyImmediate(so);
            _assets.Clear();

            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// Unity's engine-side error when <c>RenderTexture.Release()</c> runs while a still-enabled
        /// Camera holds that texture as its <c>targetTexture</c>. Shutdown() releases before detaching,
        /// so any test that leaves a preview camera enabled will see exactly this message.
        /// It is NOT suppressed by <c>LogAssert.ignoreFailingMessages</c> (it comes from native code),
        /// so the affected tests have to Expect it explicitly.
        /// </summary>
        private const string RT_RELEASE_ERROR = "Releasing render texture that is set as Camera.targetTexture!";

        // ── Builders ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A deliberately cheap looping preset: kind "aura" takes the plain ParticleSystem path
        /// (no lightning LineRenderer, no burst coroutine — coroutines do not run in EditMode).
        /// </summary>
        private ParticlePresetDefinition MakePreset(string id)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id          = id;
            def.displayName = id;
            def.type        = "aura";
            def.vfx = new ParticleVfxParams
            {
                kind     = "aura",
                loops    = true,
                emitRate = 1f,
                count    = 1,
                lifespan = 0.5f,
                speed    = 0.5f,
                sizeMin  = 0.05f,
                sizeMax  = 0.10f,
                burstIntervalSeconds = 0f,
            };
            _assets.Add(def);
            return def;
        }

        private List<ParticlePresetDefinition> MakePresets(int count, string prefix = "pprev_")
        {
            var list = new List<ParticlePresetDefinition>(count);
            for (int i = 0; i < count; i++) list.Add(MakePreset(prefix + i));
            return list;
        }

        // ── Reflection helpers (test-only introspection of the slot pool) ────────

        private static int ReadIntConst(string name)
        {
            var f = typeof(ParticlePreviewService).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, $"ParticlePreviewService.{name} is gone — the documented ceiling this test " +
                                 "pins no longer exists; update the test together with the service.");
            return (int)f.GetRawConstantValue();
        }

        private static IList ReadPool(ParticlePreviewService svc)
        {
            var f = typeof(ParticlePreviewService).GetField("_pool", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "ParticlePreviewService._pool is gone — the slot pool this test inspects was renamed.");
            return (IList)f.GetValue(svc);
        }

        /// <summary>Reads the cached ParticleSystem of a pool slot; null when the slot never had a preset.</summary>
        private static ParticleSystem ReadSlotParticleSystem(object slot)
        {
            var f = slot.GetType().GetField("Ps", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(f, "ThumbSlot.Ps is gone — the emitter-churn assertion needs updating.");
            return f.GetValue(slot) as ParticleSystem;
        }

        /// <summary>
        /// Asserts every id resolves to a live RenderTexture and that no two ids share one.
        /// Uses <c>rt != null</c> rather than Assert.IsNotNull: a released-but-not-cleared RT is
        /// Unity fake-null, which Assert.IsNotNull would happily accept.
        /// The failure message names the colliding pair, which is exactly the symptom of the modulo
        /// bug this fixture exists to prevent.
        /// </summary>
        private void AssertDistinctTextures(IReadOnlyList<ParticlePresetDefinition> presets)
        {
            var seen = new Dictionary<RenderTexture, string>();
            for (int i = 0; i < presets.Count; i++)
            {
                string id = presets[i].id;
                var rt = _service.GetPreviewTexture(id);
                Assert.IsTrue(rt != null, $"Preset '{id}' (index {i} of {presets.Count}) got no preview " +
                                          "RenderTexture — every visible preset must own a slot.");
                if (seen.TryGetValue(rt, out string other))
                    Assert.Fail($"Presets '{other}' and '{id}' share RenderTexture '{rt.name}'. " +
                                "Slots must never be reused across presets — this is the shared-thumbnail " +
                                "regression (slotIdx = i % POOL_SIZE).");
                seen[rt] = id;
            }
        }

        // ── The regression guard ─────────────────────────────────────────────────

        [Test]
        public void GetPreviewTexture_SeventyVisiblePresets_ReturnsDistinctTexturePerPreset()
        {
            var presets = MakePresets(70);
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(presets);

            AssertDistinctTextures(presets);

            // Pointed check on the exact pairs the old fixed 24-slot pool aliased together.
            for (int i = 0; i + HISTORICAL_POOL_SIZE < presets.Count; i++)
            {
                var a = _service.GetPreviewTexture(presets[i].id);
                var b = _service.GetPreviewTexture(presets[i + HISTORICAL_POOL_SIZE].id);
                Assert.AreNotSame(a, b,
                    $"Presets {i} and {i + HISTORICAL_POOL_SIZE} share a RenderTexture — presets exactly " +
                    "one historical pool-size apart collided under the old modulo mapping.");
            }
        }

        // ── Null / unknown / uninitialised lookups ───────────────────────────────

        [Test]
        public void GetPreviewTexture_UnknownId_ReturnsNull()
        {
            var presets = MakePresets(4);
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(presets);

            Assert.IsNull(_service.GetPreviewTexture("id_that_is_not_visible"),
                "An id with no slot must return null so the picker can draw a placeholder — " +
                "never another preset's texture.");
        }

        [Test]
        public void GetPreviewTexture_NullId_ReturnsNull()
        {
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(MakePresets(3));

            Assert.IsNull(_service.GetPreviewTexture(null),
                "A null id must be caught by the guard rather than reaching the dictionary lookup.");
        }

        [Test]
        public void GetPreviewTexture_EmptyId_ReturnsNull()
        {
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(MakePresets(3));

            Assert.IsNull(_service.GetPreviewTexture(string.Empty),
                "The empty string is the fallback key for presets with a null id — it must not hand a " +
                "texture to a caller that simply has no id.");
        }

        [Test]
        public void GetPreviewTexture_BeforeInitialize_ReturnsNull()
        {
            Assert.IsNull(_service.GetPreviewTexture("anything"),
                "Before Initialize there is no pool at all; the picker may query during its first " +
                "build pass and must get null instead of an exception.");
        }

        [Test]
        public void GetLargePreviewTexture_BeforeInitialize_ReturnsNull()
        {
            Assert.IsNull(_service.GetLargePreviewTexture(),
                "The large RT does not exist before Initialize — returning an uncreated handle would " +
                "leave the View panel bound to a dead texture.");
        }

        [Test]
        public void SetVisiblePresets_BeforeInitialize_IsIgnored()
        {
            var presets = MakePresets(3);

            Assert.DoesNotThrow(() => _service.SetVisiblePresets(presets),
                "SetVisiblePresets before Initialize must be a no-op, not a crash.");

            _service.Initialize(_parentGo.transform);

            Assert.IsNull(_service.GetPreviewTexture(presets[0].id),
                "A pre-Initialize SetVisiblePresets must not be retroactively honoured — the caller has " +
                "to publish the list again after Initialize.");
        }

        // ── Stability of the mapping ─────────────────────────────────────────────

        [Test]
        public void SetVisiblePresets_SameListTwice_KeepsSameTextureAndEmitterPerPreset()
        {
            var presets = MakePresets(6);
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(presets);

            var rtBefore = new RenderTexture[presets.Count];
            for (int i = 0; i < presets.Count; i++) rtBefore[i] = _service.GetPreviewTexture(presets[i].id);

            var pool = ReadPool(_service);
            var psBefore = new ParticleSystem[presets.Count];
            for (int i = 0; i < presets.Count; i++) psBefore[i] = ReadSlotParticleSystem(pool[i]);

            _service.SetVisiblePresets(presets);

            for (int i = 0; i < presets.Count; i++)
            {
                Assert.AreSame(rtBefore[i], _service.GetPreviewTexture(presets[i].id),
                    $"Re-publishing the identical list reallocated the RenderTexture for '{presets[i].id}'. " +
                    "The picker keeps RawImage.texture references across refreshes; churn leaves them dangling.");
                Assert.AreSame(psBefore[i], ReadSlotParticleSystem(pool[i]),
                    $"Slot {i} rebuilt its ParticleSystem for an unchanged preset. ApplyPreset must only run " +
                    "when the slot's preset actually changes, otherwise every refresh restarts all thumbnails.");
            }
        }

        [Test]
        public void SetVisiblePresets_NarrowedThenWidened_ParksThenRestoresSameTextures()
        {
            var all = MakePresets(6);
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(all);

            var original = new RenderTexture[all.Count];
            for (int i = 0; i < all.Count; i++) original[i] = _service.GetPreviewTexture(all[i].id);

            // Narrow the filter down to the first two presets.
            _service.SetVisiblePresets(all.GetRange(0, 2));

            for (int i = 0; i < 2; i++)
                Assert.AreSame(original[i], _service.GetPreviewTexture(all[i].id),
                    $"Preset '{all[i].id}' stayed visible, so its texture must not move when the filter narrows.");

            for (int i = 2; i < all.Count; i++)
                Assert.IsNull(_service.GetPreviewTexture(all[i].id),
                    $"Preset '{all[i].id}' is filtered out; its slot must be parked and the lookup must " +
                    "return null rather than a slot now driven by a different preset.");

            // Widen back to the full list.
            _service.SetVisiblePresets(all);

            for (int i = 0; i < all.Count; i++)
                Assert.AreSame(original[i], _service.GetPreviewTexture(all[i].id),
                    $"Widening the filter must reuse the existing slot for '{all[i].id}' — the pool grows " +
                    "but never reallocates textures it already owns.");

            AssertDistinctTextures(all);
        }

        [Test]
        public void SetVisiblePresets_EmptyList_LeavesNoPresetWithATexture()
        {
            var presets = MakePresets(4);
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(presets);

            _service.SetVisiblePresets(new List<ParticlePresetDefinition>());

            for (int i = 0; i < presets.Count; i++)
                Assert.IsNull(_service.GetPreviewTexture(presets[i].id),
                    $"After an empty filter no preset may still resolve to a texture ('{presets[i].id}' did).");

            Assert.DoesNotThrow(() => _service.Tick(),
                "Ticking with zero active slots must be a safe no-op — the round-robin modulo would " +
                "divide by zero otherwise.");
        }

        // ── Awkward input ────────────────────────────────────────────────────────

        [Test]
        public void SetVisiblePresets_ListWithNullEntries_SkipsNullsAndKeepsRemainderDistinct()
        {
            var real = MakePresets(4, "nullmix_");
            var mixed = new List<ParticlePresetDefinition>
            {
                null, real[0], null, real[1], real[2], null, real[3], null
            };

            _service.Initialize(_parentGo.transform);

            Assert.DoesNotThrow(() => _service.SetVisiblePresets(mixed),
                "A destroyed/missing catalog entry arrives as null and must be skipped, not dereferenced.");

            AssertDistinctTextures(real);
        }

        [Test]
        public void SetVisiblePresets_DuplicateIds_DoesNotThrowAndMapsIdToASingleSlot()
        {
            var a  = MakePreset("dup_unique_a");
            var d1 = MakePreset("dup_shared");
            var d2 = MakePreset("dup_shared");   // same id, different asset
            var b  = MakePreset("dup_unique_b");

            _service.Initialize(_parentGo.transform);

            Assert.DoesNotThrow(
                () => _service.SetVisiblePresets(new List<ParticlePresetDefinition> { a, d1, d2, b }),
                "Two catalog entries sharing an id must not throw — the id→slot map has to use the " +
                "indexer, not Add() (Add throws ArgumentException on a duplicate key).");

            var dup = _service.GetPreviewTexture("dup_shared");
            Assert.IsTrue(dup != null, "The duplicated id must still resolve to exactly one texture.");
            Assert.AreNotSame(dup, _service.GetPreviewTexture("dup_unique_a"),
                "A duplicated id must not collapse onto a neighbouring preset's slot.");
            Assert.AreNotSame(dup, _service.GetPreviewTexture("dup_unique_b"),
                "A duplicated id must not collapse onto a neighbouring preset's slot.");
            Assert.AreNotSame(_service.GetPreviewTexture("dup_unique_a"),
                              _service.GetPreviewTexture("dup_unique_b"),
                "Unique presets must keep their own slots even when a duplicate id sits between them.");
        }

        [Test]
        public void SetVisiblePresets_PresetWithNullId_DoesNotThrowAndYieldsNoTexture()
        {
            var nameless = MakePreset("temp");
            nameless.id = null;
            var normal = MakePreset("nullid_neighbour");

            _service.Initialize(_parentGo.transform);

            Assert.DoesNotThrow(
                () => _service.SetVisiblePresets(new List<ParticlePresetDefinition> { nameless, normal }),
                "A preset asset with an unset id must not break the whole visible list.");

            Assert.IsNull(_service.GetPreviewTexture(null),
                "A null id is unaddressable — it must not become reachable via the empty-string fallback key.");
            Assert.IsTrue(_service.GetPreviewTexture("nullid_neighbour") != null,
                "One malformed preset must not cost its neighbours their thumbnails.");
        }

        [Test]
        public void SetVisiblePresets_UnicodeAndVeryLongIds_ResolveToTheirOwnTextures()
        {
            // Accent + CJK + an astral-plane surrogate pair: preset ids come from designer-authored
            // assets and are used verbatim as dictionary keys.
            var unicode = MakePreset("héroe_火_🔥_preset");
            var longId  = MakePreset(new StringBuilder().Insert(0, "muy_largo_", 500).ToString());
            var plain   = MakePreset("plain_ascii_preset");

            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(new List<ParticlePresetDefinition> { unicode, longId, plain });

            Assert.IsTrue(_service.GetPreviewTexture(unicode.id) != null,
                "Non-ASCII ids must round-trip through the id→slot dictionary unchanged (no normalisation).");
            Assert.IsTrue(_service.GetPreviewTexture(longId.id) != null,
                "A very long id must not be truncated on the way into the map.");
            Assert.AreNotSame(_service.GetPreviewTexture(unicode.id), _service.GetPreviewTexture(longId.id),
                "Unusual ids must still get their own slots.");
            Assert.AreNotSame(_service.GetPreviewTexture(plain.id), _service.GetPreviewTexture(unicode.id),
                "Unusual ids must still get their own slots.");
        }

        // ── The documented ceiling ───────────────────────────────────────────────

        [Test]
        public void SetVisiblePresets_BeyondMaxPoolSize_WarnsAndLeavesOverflowWithoutTexture()
        {
            int max     = ReadIntConst("MAX_POOL_SIZE");
            int visible = max + 2;

            var presets = MakePresets(visible, "ceiling_");
            _service.Initialize(_parentGo.transform);

            LogAssert.Expect(LogType.Warning, new Regex(
                $@"\[ParticlePreviewService\] {visible} presets visible but only {max} preview slots exist"));

            _service.SetVisiblePresets(presets);

            // Everything up to the ceiling still gets its own, unshared texture.
            AssertDistinctTextures(presets.GetRange(0, max));

            for (int i = max; i < visible; i++)
                Assert.IsNull(_service.GetPreviewTexture(presets[i].id),
                    $"Preset '{presets[i].id}' is past the {max}-slot ceiling. The documented behaviour is " +
                    "NO thumbnail — borrowing another preset's picture is the exact bug this service was " +
                    "rewritten to remove.");
        }

        // ── Initialize / Shutdown lifecycle ──────────────────────────────────────

        [Test]
        public void Initialize_CalledTwice_KeepsFirstParentAndSameLargeTexture()
        {
            _service.Initialize(_parentGo.transform);
            var largeFirst = _service.GetLargePreviewTexture();
            int childrenAfterFirst = _parentGo.transform.childCount;
            Assert.Greater(childrenAfterFirst, 0, "Sanity: Initialize must create the large emitter + camera.");

            var secondParent = new GameObject("PPrevServiceSecondRoot");
            _sceneObjects.Add(secondParent);

            _service.Initialize(secondParent.transform);

            Assert.AreSame(largeFirst, _service.GetLargePreviewTexture(),
                "A second Initialize must not reallocate the large RenderTexture — the View panel holds a " +
                "reference to the first one.");
            Assert.AreEqual(childrenAfterFirst, _parentGo.transform.childCount,
                "A second Initialize must not duplicate cameras/emitters under the original parent.");
            Assert.AreEqual(0, secondParent.transform.childCount,
                "A second Initialize must early-out, so nothing may be parented to the new transform — " +
                "objects created there would outlive Shutdown, which only knows the first parent's children.");
        }

        [Test]
        public void Shutdown_BeforeInitialize_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.Shutdown(),
                "Shutdown runs from the editor's Deactivate(), which can fire without a prior Activate().");
        }

        [Test]
        public void Shutdown_CalledTwice_DoesNotThrow()
        {
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(MakePresets(3));

            _service.Shutdown();

            Assert.DoesNotThrow(() => _service.Shutdown(),
                "Deactivate() and OnDestroy() both call Shutdown; the second pass must not re-release " +
                "already-destroyed RenderTextures.");
        }

        [Test]
        public void Shutdown_AfterInitialize_ReleasesTexturesAndDestroysSceneObjects()
        {
            var presets = MakePresets(3);
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(presets);
            _service.SetSelectedPreset(presets[0].id, presets[0]);

            var thumbRt = _service.GetPreviewTexture(presets[0].id);
            var largeRt = _service.GetLargePreviewTexture();
            Assert.IsTrue(thumbRt != null, "Sanity: the preset must own a thumbnail before Shutdown.");
            Assert.IsTrue(largeRt != null, "Sanity: the large RT must exist before Shutdown.");

            // Selecting a preset enables the large camera, so releasing its RT trips Unity's
            // engine-side complaint. Expected here rather than ignored: if the service ever starts
            // detaching targetTexture before releasing, this line tells us to drop the Expect.
            LogAssert.Expect(LogType.Error, RT_RELEASE_ERROR);

            _service.Shutdown();

            Assert.IsNull(_service.GetPreviewTexture(presets[0].id),
                "After Shutdown the id→slot map must be empty, not pointing at released textures.");
            Assert.IsNull(_service.GetLargePreviewTexture(),
                "After Shutdown the large preview accessor must report null.");

            // Unity's overloaded == is the only way to observe a destroyed object.
            Assert.IsTrue(thumbRt == null, "The thumbnail RenderTexture must be released AND destroyed — " +
                                           "a surviving RT is VRAM leaked on every F1 open/close cycle.");
            Assert.IsTrue(largeRt == null, "The large RenderTexture must be released AND destroyed.");
            Assert.AreEqual(0, _parentGo.transform.childCount,
                "Every emitter and camera GameObject must be destroyed by Shutdown; leftovers keep " +
                "simulating particles off-screen forever.");
        }

        [Test]
        public void Shutdown_AfterStateChanges_ResetsPlaybackAndZoomToDefaults()
        {
            _service.Initialize(_parentGo.transform);
            _service.Pause();
            _service.SetSpeedMultiplier(0.25f);
            _service.SetZoom(3f);

            _service.Shutdown();

            Assert.IsFalse(_service.IsPaused,
                "Shutdown must clear the paused flag — Domain Reload is OFF, so a stale 'paused' would make " +
                "the next F1 session open with frozen thumbnails.");
            Assert.AreEqual(1f, _service.SpeedMultiplier, 1e-4f,
                "Shutdown must restore the default speed multiplier for the next session.");
            Assert.AreEqual(1f, _service.LargeOrthoZoom, 1e-4f,
                "Shutdown must restore the auto-fit zoom baseline for the next session.");
        }

        // ── Selection ────────────────────────────────────────────────────────────

        [Test]
        public void SetSelectedPreset_NullDefinition_DoesNotThrowAndKeepsLargeTexture()
        {
            _service.Initialize(_parentGo.transform);
            var presets = MakePresets(2);
            _service.SetVisiblePresets(presets);
            _service.SetSelectedPreset(presets[0].id, presets[0]);

            Assert.DoesNotThrow(() => _service.SetSelectedPreset(null, null),
                "Clearing the selection (deleted preset, empty filter) passes a null definition and must " +
                "not throw.");
            Assert.IsTrue(_service.GetLargePreviewTexture() != null,
                "Clearing the selection must not release the large RT — the View panel keeps the same " +
                "texture bound and simply stops being redrawn.");
            Assert.DoesNotThrow(() => _service.Tick(),
                "Ticking with a cleared selection must not dereference the null definition.");

            // That Tick pointed a thumb camera at a slot RT, so shut down inside the test body where
            // the resulting engine error can be expected — TearDown has no LogAssert scope of its own.
            LogAssert.Expect(LogType.Error, RT_RELEASE_ERROR);
            _service.Shutdown();
        }

        [Test]
        public void SetSelectedPreset_BeforeInitialize_DoesNotThrow()
        {
            var preset = MakePreset("selected_before_init");

            Assert.DoesNotThrow(() => _service.SetSelectedPreset(preset.id, preset),
                "The editor can restore its last selection before Activate() has initialised the service.");
            Assert.IsNull(_service.GetLargePreviewTexture(),
                "A pre-Initialize selection must not conjure a large RT.");
        }

        // ── Playback + zoom controls ─────────────────────────────────────────────

        [Test]
        public void SetZoom_OutOfRangeValues_ClampToDocumentedBounds()
        {
            _service.Initialize(_parentGo.transform);

            _service.SetZoom(100f);
            Assert.AreEqual(4f, _service.LargeOrthoZoom, 1e-4f,
                "Zoom must clamp at 4.0; an unclamped value collapses the ortho size and shows nothing.");

            _service.SetZoom(0.0001f);
            Assert.AreEqual(0.25f, _service.LargeOrthoZoom, 1e-4f,
                "Zoom must clamp at 0.25; a near-zero zoom divides the ortho size towards infinity.");

            _service.SetZoom(-5f);
            Assert.AreEqual(0.25f, _service.LargeOrthoZoom, 1e-4f,
                "A negative zoom must clamp to the minimum, never mirror the preview.");
        }

        [Test]
        public void ZoomIn_ThenZoomOut_ReturnsToStartingZoom()
        {
            _service.Initialize(_parentGo.transform);

            _service.ZoomIn();
            Assert.Greater(_service.LargeOrthoZoom, 1f, "ZoomIn must increase the zoom factor.");

            _service.ZoomOut();
            Assert.AreEqual(1f, _service.LargeOrthoZoom, 1e-4f,
                "ZoomIn followed by ZoomOut must land back on the auto-fit baseline — the two steps have to " +
                "be exact inverses (multiply/divide), not add/subtract.");
        }

        [Test]
        public void ResetZoom_AfterRepeatedZoomIn_ReturnsToAutoFitBaseline()
        {
            _service.Initialize(_parentGo.transform);

            for (int i = 0; i < 20; i++) _service.ZoomIn();
            Assert.AreEqual(4f, _service.LargeOrthoZoom, 1e-4f,
                "Repeated ZoomIn must saturate at the documented maximum instead of growing without bound.");

            _service.ResetZoom();
            Assert.AreEqual(1f, _service.LargeOrthoZoom, 1e-4f,
                "ResetZoom must return to the 1.0 auto-fit baseline.");
        }

        [Test]
        public void SetSpeedMultiplier_ZeroOrNegative_ClampsToPositiveMinimum()
        {
            _service.Initialize(_parentGo.transform);

            _service.SetSpeedMultiplier(0f);
            Assert.Greater(_service.SpeedMultiplier, 0f,
                "A zero multiplier would stall the round-robin accumulator forever — it must be floored.");

            _service.SetSpeedMultiplier(-3f);
            Assert.Greater(_service.SpeedMultiplier, 0f,
                "A negative multiplier would drive the accumulator backwards — it must be floored.");
        }

        [Test]
        public void TogglePause_CalledTwice_ReturnsToRunningAndReportsNewState()
        {
            _service.Initialize(_parentGo.transform);
            Assert.IsFalse(_service.IsPaused, "Sanity: the preview starts running.");

            Assert.IsTrue(_service.TogglePause(),
                "TogglePause must return the NEW state so the toolbar button can relabel itself.");
            Assert.IsTrue(_service.IsPaused, "The returned value and the property must agree.");

            Assert.IsFalse(_service.TogglePause(), "The second toggle must report 'running' again.");
            Assert.IsFalse(_service.IsPaused, "The second toggle must resume the round-robin.");
        }

        [Test]
        public void Resume_AfterPause_ClearsPausedFlag()
        {
            _service.Initialize(_parentGo.transform);

            _service.Pause();
            Assert.IsTrue(_service.IsPaused, "Pause must set the flag.");

            _service.Resume();
            Assert.IsFalse(_service.IsPaused, "Resume must clear the flag set by Pause.");
        }

        // ── Tick robustness ──────────────────────────────────────────────────────

        [Test]
        public void Tick_BeforeInitialize_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _service.Tick(),
                "The owning MonoBehaviour's Update() runs whether or not the editor was activated; Tick " +
                "must early-out on an uninitialised service.");
        }

        [Test]
        public void Tick_AfterShutdown_DoesNotThrow()
        {
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(MakePresets(3));
            _service.Shutdown();

            Assert.DoesNotThrow(() => _service.Tick(),
                "One more Update() can land after Deactivate(); Tick must not touch the destroyed cameras.");
        }

        [Test]
        public void Tick_WhilePaused_DoesNotThrowAndKeepsPresetMapping()
        {
            var presets = MakePresets(4);
            _service.Initialize(_parentGo.transform);
            _service.SetVisiblePresets(presets);
            var before = _service.GetPreviewTexture(presets[2].id);

            _service.Pause();
            for (int i = 0; i < 3; i++) _service.Tick();

            Assert.AreSame(before, _service.GetPreviewTexture(presets[2].id),
                "Pausing must only freeze the round-robin; it must never reshuffle which slot a preset owns.");
        }
    }
}
