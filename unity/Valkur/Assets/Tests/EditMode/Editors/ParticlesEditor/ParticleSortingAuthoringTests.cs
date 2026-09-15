using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Valkur.UIKit;
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.Editors.ParticlesEditor
{
    /// <summary>
    /// The AUTHORING loop for a preset's depth, end to end: F1 Properties row →
    /// <see cref="ParticlePresetFieldWriter"/> → <see cref="ParticleVfxParams.sortingLayer"/>
    /// → <see cref="ParticleEmitter.ApplyPreset"/> → <see cref="ParticleSystemRenderer"/>.
    ///
    /// <see cref="ParticleEmitterSortingAndAmbientTests"/> already pins the LAST link of that
    /// chain — that a preset which authors a layer reaches the renderer with it, and that one
    /// which authors nothing still lands on VFX at order 0. This fixture pins the links
    /// BEFORE it, because a value nothing can set is exactly as useless as a value nothing
    /// re-applies. Both halves have already failed in this game once:
    ///
    ///   • the vegetation presets were put on ObjectsLow, which the project draws BELOW
    ///     WallsBottom, so every falling leaf hid behind every building wall (BuildingObject
    ///     assigns WallsBottom / WallsTop by the instance's z offset);
    ///   • and the only way to move them off it was to open the .asset in the Inspector,
    ///     because the Properties panel offered no depth row at all.
    ///
    /// There is NO sorting layer between WallsBottom and Entities, so "behind the player" and
    /// "in front of a building body" cannot both be true through layers alone. That is
    /// precisely why the choice has to belong to the person authoring the preset — the right
    /// answer is per-preset and no hardcoded layer can be it.
    ///
    /// Nothing here hardcodes a layer NAME LIST. Every expectation about which layers exist,
    /// and about which of them draws in front of which, is derived from
    /// <see cref="SortingLayer.layers"/> at run time, so the suite survives an edit to
    /// ProjectSettings > Tags and Layers instead of turning red for it. Only two names are
    /// spelled out — <see cref="SortingConfig.LAYER_WALLS_BOTTOM"/> and
    /// <see cref="SortingConfig.LAYER_OBJECTS_LOW"/>, through the constants, never as
    /// literals — because the regression IS about those two.
    ///
    /// EditMode notes: <c>OnPresetPropertyChanged</c> deliberately early-returns outside Play
    /// Mode so a fixture cannot dirty a real .asset. These tests therefore drive the two
    /// halves it joins — <c>RebuildPresetPropertyForm</c> for what the panel OFFERS, and
    /// <c>ParticlePresetFieldWriter.TrySetField</c> for what an edit STORES — and push the
    /// result through <c>ApplyPreset</c> themselves.
    /// </summary>
    [TestFixture]
    public partial class ParticleSortingAuthoringTests
    {
        /// <summary>
        /// Row keys, built from the field names rather than typed out: renaming
        /// <see cref="ParticleVfxParams.sortingLayer"/> must break the build here, not
        /// silently turn every assertion below into "the row is missing".
        /// </summary>
        private const string VFX = "vfx.";
        private static readonly string KEY_LAYER = VFX + nameof(ParticleVfxParams.sortingLayer);
        private static readonly string KEY_ORDER = VFX + nameof(ParticleVfxParams.sortingOrder);
        private static readonly string KEY_FUDGE = VFX + nameof(ParticleVfxParams.sortingFudge);

        /// <summary>
        /// Parked in the field before an edit that is expected to be REFUSED, so "the field
        /// still holds this" proves the refusal did not half-apply. Deliberately a real layer
        /// name (resolved at run time) rather than a marker string: the invariant under test is
        /// that a rejected write leaves behind data the emitter can still resolve.
        /// </summary>
        private string _priorLayer;

        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _trackedAssets = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            // Both halves of this fixture log in EditMode without anything being wrong:
            // building TMP rows outside a running Canvas, and building a ParticleSystem whose
            // renderer/material chatter would fail an assertion that passed.
            LogAssert.ignoreFailingMessages = true;

            _priorLayer = ProjectLayerNames()[0];
            ClearSortingLayerVerdictCache();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _trackedAssets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _trackedAssets.Clear();

            ClearSingletonInstance<ParticlesRuntimeEditor>();

            // Domain Reload is OFF and the only reset hook on this ledger is a
            // [RuntimeInitializeOnLoadMethod], which never fires in EditMode. Leaving it
            // populated would make ParticleEmitterSortingAndAmbientTests' warn-once test
            // order-dependent on whether this fixture ran first.
            ClearSortingLayerVerdictCache();

            LogAssert.ignoreFailingMessages = false;
        }

        // ═══════════════════════════════════════════════════════════════════ fixtures

        /// <summary>
        /// A minimal continuous emitter preset. Everything this fixture never asserts on is
        /// held constant, so a failure can only be about depth.
        /// </summary>
        private ParticlePresetDefinition MakePreset(string id, string layer = "",
                                                    int order = 0, float fudge = 0f)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _trackedAssets.Add(def);
            def.id = id;
            def.displayName = id;
            def.type = "aura";
            def.vfx = new ParticleVfxParams
            {
                kind = "aura",
                loops = true,
                emitRate = 10f,
                count = 6,
                lifespan = 0.5f,
                speed = 1f,
                sizeMin = 0.1f,
                sizeMax = 0.2f,
                sortingLayer = layer,
                sortingOrder = order,
                sortingFudge = fudge,
                color = Color.white,
                colors = new[] { Color.white },
            };
            return def;
        }

        private ParticlePresetCatalog MakeCatalogWith(ParticlePresetDefinition def)
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            _trackedAssets.Add(catalog);
            catalog.SetPresets(new List<ParticlePresetDefinition> { def });
            return catalog;
        }

        private ParticleEmitter CreateEmitter(string name = "DepthAuthoringTestEmitter")
        {
            var go = new GameObject(name);
            _sceneObjects.Add(go);
            return go.AddComponent<ParticleEmitter>();
        }

        /// <summary>
        /// Runs the real <c>RebuildPresetPropertyForm</c> against a real
        /// <see cref="PropertyForm"/> and hands back the form it filled.
        ///
        /// A fresh editor and a fresh form per call, on purpose: <c>PropertyForm.Clear</c> uses
        /// <c>Object.Destroy</c>, which is deferred (and complains) in EditMode, so rebuilding
        /// twice into one form would leave the first build's rows in the hierarchy. Building
        /// once into an empty form keeps that whole question out of the assertions.
        /// </summary>
        private PropertyForm BuildFormFor(ParticlePresetDefinition def)
        {
            var editor = CreateEditor();
            SetFieldValue(editor, "_catalog", MakeCatalogWith(def));

            var form = CreateStandaloneForm();

            // UIRefs is a struct, so it has to be boxed, poked and written back — assigning
            // through the FieldInfo directly would mutate a copy and the editor would still
            // see a null form.
            var uiField = FindField(editor, "_ui");
            Assert.IsNotNull(uiField, "ParticlesRuntimeEditor._ui is gone — this fixture " +
                                      "injects the Properties form through it.");
            object boxedUi = uiField.GetValue(editor);
            var formField = boxedUi.GetType().GetField("PresetPropsForm");
            Assert.IsNotNull(formField, "UIRefs.PresetPropsForm is gone — the Properties form " +
                                        "is no longer reachable from the editor's UI refs.");
            formField.SetValue(boxedUi, form);
            uiField.SetValue(editor, boxedUi);

            TestReflection.Invoke(editor, "RebuildPresetPropertyForm", def.id);
            return form;
        }

        /// <summary>
        /// A PropertyForm under a real Canvas. The Canvas ancestor is what keeps the TMP rows
        /// from throwing as they initialise — the EditMode gotcha every UI fixture in this
        /// suite pays for once.
        /// </summary>
        private PropertyForm CreateStandaloneForm()
        {
            var canvasGo = new GameObject("DepthAuthoringTestCanvas");
            _sceneObjects.Add(canvasGo);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(canvasGo.transform, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            return PropertyForm.Create(root.transform, "PresetPropsForm");
        }

        private ParticlesRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<ParticlesRuntimeEditor>();

            var go = new GameObject("TestParticlesEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();

            // Mirrors ParticlesEditorLifecycleTests / ParticlesPresetAutosaveTests so all three
            // fixtures build the editor the same way.
            TestReflection.Invoke(editor, "OnSingletonAwake");
            StubPreviewService(editor);
            return editor;
        }

        /// <summary>
        /// Copied from <see cref="ParticlesEditorLifecycleTests"/> so the fixtures stay in
        /// lockstep: marks the preview service initialised and fills its pool with empty slots,
        /// so nothing in EditMode reaches for a Camera or a RenderTexture.
        /// </summary>
        private static void StubPreviewService(ParticlesRuntimeEditor editor)
        {
            var serviceField = FindField(editor, "_previewService");
            var service = serviceField?.GetValue(editor);
            if (service == null) return;

            var serviceType = service.GetType();
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;

            serviceType.GetField("_initialized", bf)?.SetValue(service, true);

            var pool = serviceType.GetField("_pool", bf)?.GetValue(service) as Array;
            if (pool == null) return;
            var thumbSlotType = serviceType.GetNestedType("ThumbSlot", BindingFlags.NonPublic);
            if (thumbSlotType == null) return;
            for (int i = 0; i < pool.Length; i++)
                if (pool.GetValue(i) == null)
                    pool.SetValue(Activator.CreateInstance(thumbSlotType), i);
        }

        // ═══════════════════════════════════════════════════ form / dropdown readers

        /// <summary>
        /// The form's key → widget map. Private by design (the form owns its rows), so the only
        /// way to ask "does a row for this key exist" is through it.
        /// </summary>
        private static IDictionary FormFields(PropertyForm form)
        {
            var f = typeof(PropertyForm).GetField("_fields",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "PropertyForm._fields is gone — this fixture reads the form's " +
                                "row keys through it.");
            var dict = f.GetValue(form) as IDictionary;
            Assert.IsNotNull(dict, "PropertyForm._fields must be a dictionary keyed by row key.");
            return dict;
        }

        private static List<string> FormFieldKeys(PropertyForm form)
        {
            var keys = new List<string>();
            foreach (var k in FormFields(form).Keys) keys.Add((string)k);
            return keys;
        }

        private static Component FormField(PropertyForm form, string key)
        {
            var fields = FormFields(form);
            Assert.IsTrue(fields.Contains(key),
                $"The Properties form offers no row keyed '{key}'. Rows present: " +
                string.Join(", ", FormFieldKeys(form)));
            return (Component)fields[key];
        }

        private static TMP_Dropdown LayerDropdown(PropertyForm form)
        {
            var dd = FormField(form, KEY_LAYER) as TMP_Dropdown;
            Assert.IsTrue(dd != null, "The Sorting Layer row must be a dropdown.");
            Assert.Greater(dd.options.Count, 0, "The Sorting Layer dropdown offers nothing.");
            return dd;
        }

        /// <summary>
        /// Splits the dropdown's entries into the ones that name a project sorting layer (in
        /// the order offered) and the ones that do not — the second list is where the "unset"
        /// entry lands, whatever it happens to be labelled.
        /// </summary>
        private static void SplitOptions(TMP_Dropdown dd, List<string> layers, List<string> others)
        {
            var names = new List<string>(ProjectLayerNames());
            for (int i = 0; i < dd.options.Count; i++)
            {
                string text = ParticlePresetFieldWriter.LayerNameFromLabel(dd.options[i].text);
                if (names.Contains(text)) layers.Add(text);
                else others.Add(text);
            }
        }

        private static int RequireOptionIndex(TMP_Dropdown dd, string label)
        {
            for (int i = 0; i < dd.options.Count; i++)
                if (ParticlePresetFieldWriter.LayerNameFromLabel(dd.options[i].text) == label) return i;
            Assert.Fail($"The Sorting Layer dropdown offers no entry '{label}'. Offered: " +
                        string.Join(", ", OptionLabels(dd)));
            return -1;
        }

        /// <summary>The layer the row is currently SHOWING, with its position label stripped.</summary>
        private static string SelectedLayer(TMP_Dropdown dd) =>
            ParticlePresetFieldWriter.LayerNameFromLabel(dd.options[dd.value].text);

        private static string[] OptionLabels(TMP_Dropdown dd)
        {
            var labels = new string[dd.options.Count];
            for (int i = 0; i < dd.options.Count; i++) labels[i] = dd.options[i].text;
            return labels;
        }

        // ═══════════════════════════════════════════════════════════ writer helpers

        private static string WriteLayerIndex(ParticlePresetDefinition def, int index,
                                              out bool ok, out string error)
        {
            ok = ParticlePresetFieldWriter.TrySetField(def, KEY_LAYER, index, out error);
            return def.vfx.sortingLayer;
        }

        /// <summary>
        /// The invariant no failure path may break: whatever ends up in sortingLayer, the
        /// emitter has to be able to resolve it without falling back. Empty is fine (it means
        /// VFX by design); a real layer name is fine; a number rendered as text, or a name the
        /// project does not define, is a preset that boots into the fallback with a warning and
        /// draws in front of the whole world.
        /// </summary>
        private static void AssertLayerFieldIsResolvable(ParticlePresetDefinition def, string when)
        {
            string stored = def.vfx.sortingLayer;
            if (string.IsNullOrEmpty(stored)) return;

            Assert.GreaterOrEqual(DrawOrderIndex(stored), 0,
                $"sortingLayer holds '{stored}' {when}, which is not a layer this project " +
                "defines. ParticleEmitter would warn once and draw the effect on VFX — in " +
                "front of the player, the walls and the canopy — with nothing on screen " +
                "explaining why.");
        }

        // ═══════════════════════════════════════════════════ sorting-layer helpers

        private static string[] ProjectLayerNames()
        {
            var layers = SortingLayer.layers;
            var names = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++) names[i] = layers[i].name;
            return names;
        }

        /// <summary>
        /// Position in <see cref="SortingLayer.layers"/>, which IS the draw order — later in
        /// the array draws in front. The live list is the authority; SortingConfig's constants
        /// only supply the two names this fixture's regression is about.
        /// </summary>
        private static int DrawOrderIndex(string layerName)
        {
            var layers = SortingLayer.layers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i].name == layerName) return i;
            return -1;
        }

        private static int RequireDrawOrderIndex(string layerName)
        {
            int i = DrawOrderIndex(layerName);
            Assert.GreaterOrEqual(i, 0,
                $"Sorting layer '{layerName}' is not defined in ProjectSettings > Tags and " +
                "Layers. SortingConfig names it, so either the layer was deleted or the " +
                "constant was renamed — and half this game's draw order is built on it.");
            return i;
        }

        /// <summary>The layer immediately in front of <paramref name="reference"/>.</summary>
        private static string FirstLayerAbove(string reference)
        {
            int i = RequireDrawOrderIndex(reference);
            var layers = SortingLayer.layers;
            Assert.Less(i + 1, layers.Length,
                $"'{reference}' is the frontmost sorting layer, so nothing can be authored in " +
                "front of it — the regression this fixture pins would be unfixable.");
            return layers[i + 1].name;
        }

        // ══════════════════════════════════════════════════════ reflection plumbing

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        /// <summary>
        /// The warn-once ledger inside ParticleEmitter.Colors.cs. It is a session-lived static
        /// and Domain Reload is OFF, so a fixture that leaves it dirty changes the behaviour of
        /// every fixture that runs after it.
        /// </summary>
        private static void ClearSortingLayerVerdictCache()
        {
            var f = typeof(ParticleEmitter).GetField(
                "_sortingLayerVerdicts", BindingFlags.NonPublic | BindingFlags.Static);
            (f?.GetValue(null) as IDictionary)?.Clear();
        }

        private static FieldInfo FindField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static void SetFieldValue(object obj, string name, object value)
            => FindField(obj, name)?.SetValue(obj, value);

        // ══════════════════════════════════════════════════════════ emitter readers

        private static ParticleSystemRenderer RootRendererOf(ParticleEmitter emitter)
        {
            // EnsureParticleSystem always names the root child "Particles"; composite layers
            // are "Layer_0", "Layer_1", ...
            var t = emitter.transform.Find("Particles");
            Assert.IsTrue(t != null,
                "ApplyPreset must have built the root ParticleSystem child 'Particles'.");
            var r = t.GetComponent<ParticleSystemRenderer>();
            Assert.IsTrue(r != null, "The root ParticleSystem must carry a renderer.");
            return r;
        }
    }
}
