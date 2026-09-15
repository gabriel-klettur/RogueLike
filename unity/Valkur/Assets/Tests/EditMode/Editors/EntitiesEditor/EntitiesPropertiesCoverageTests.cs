using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Editors.EntitiesEditor
{
    /// <summary>
    /// Every authorable field on a monster has somewhere to be authored.
    ///
    /// <para><b>The audit that produced this found nine of nineteen <c>MonsterDefinition</c>
    /// fields with no UI at all</b> — including <c>aiTuning</c>, nineteen fields of behaviour
    /// that another audit had just created for exactly this purpose, and <c>coinReward</c>, the
    /// coin faucet of the whole economy. They were authorable only from the Inspector, in the
    /// editor whose job is authoring entities.</para>
    ///
    /// <para>This reads the SOURCE of the panel rather than building it, for the reason every
    /// other source guard in this project does: uGUI performs no layout in EditMode, so a test
    /// that built the form would be asserting on rows whose size and visibility mean nothing.
    /// What can be checked without a frame is whether the field is mentioned at all — which is
    /// the failure that actually happened, and the one that comes back the next time a field is
    /// added to the data model and nowhere else.</para>
    ///
    /// <para>The exemption list is the interesting half: a field is allowed to be absent only
    /// with a stated reason, so adding one is a decision somebody wrote down rather than a row
    /// nobody noticed was missing.</para>
    /// </summary>
    public class EntitiesPropertiesCoverageTests
    {
        /// <summary>
        /// Fields deliberately not in the panel, and why.
        ///
        /// <para>Object references to other assets are shown as a NAME but not picked here:
        /// choosing a <c>LootTable</c> or a <c>NPCPersonaDefinition</c> is an asset-picker
        /// problem, and a text field that takes an asset path is the shape that silently stops
        /// resolving. They are still reported, so an author can see which one is wired.</para>
        /// </summary>
        private static readonly Dictionary<string, string> ExemptDefinitionFields =
            new Dictionary<string, string>
            {
                { "bossDefinition", "Reached through the 'Open Boss Editor' handoff button." },
                { "assetConfig",    "Authored in the Animation panel, which can show the art." },
            };

        private static readonly Dictionary<string, string> ExemptStatsFields =
            new Dictionary<string, string>
            {
                { "power", "Legacy XP fallback only; shown read-only beside the XP reward." },
            };

        private static string PanelSource()
        {
            string root = System.IO.Path.Combine(
                UnityEngine.Application.dataPath,
                "_Project/Scripts/Gameplay/Editors/Entities");
            Assert.That(System.IO.Directory.Exists(root), Is.True, root + " is missing.");

            var sb = new System.Text.StringBuilder();
            foreach (var file in System.IO.Directory.GetFiles(root, "*.cs"))
                sb.Append(System.IO.File.ReadAllText(file));
            return sb.ToString();
        }

        [Test]
        public void EveryMonsterDefinitionField_IsReachableFromThePanel()
        {
            string source = PanelSource();
            var missing = new List<string>();

            foreach (var f in typeof(MonsterDefinition)
                         .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (ExemptDefinitionFields.ContainsKey(f.Name)) continue;
                if (source.Contains("def." + f.Name)) continue;
                missing.Add(f.Name);
            }

            Assert.That(missing, Is.Empty,
                "MonsterDefinition fields the Entities editor never mentions:\n  " +
                string.Join("\n  ", missing) +
                "\n\nAdd a row, or add the field to ExemptDefinitionFields WITH the reason. " +
                "A field authorable only from the Inspector is a field the editor does not author.");
        }

        [Test]
        public void EveryEntityStatsField_IsReachableFromThePanel()
        {
            string source = PanelSource();
            var missing = new List<string>();

            foreach (var f in typeof(EntityStats)
                         .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (ExemptStatsFields.ContainsKey(f.Name)) continue;
                if (source.Contains("stats." + f.Name) || source.Contains("s." + f.Name)) continue;
                missing.Add(f.Name);
            }

            Assert.That(missing, Is.Empty,
                "EntityStats fields the Entities editor never mentions:\n  " +
                string.Join("\n  ", missing));
        }

        [Test]
        public void EveryAITuningField_HasARow()
        {
            string source = PanelSource();
            var missing = typeof(AIBehaviourTuning)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Select(f => f.Name)
                .Where(name => !source.Contains("aiTuning." + name))
                .ToList();

            Assert.That(missing, Is.Empty,
                "AI tuning dials with no row:\n  " + string.Join("\n  ", missing) +
                "\n\nThese decide whether a monster dodges, stands off, flanks or leashes. " +
                "The whole block had no UI until 2026-09-12.");
        }

        [Test]
        public void TheExemptionsAreRealFields_NotStaleNames()
        {
            // An exemption for a field that no longer exists is an exemption that quietly stops
            // covering anything -- and it would hide the NEXT field to take that name.
            var defFields = new HashSet<string>(typeof(MonsterDefinition)
                .GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name));
            foreach (var key in ExemptDefinitionFields.Keys)
                Assert.That(defFields.Contains(key), Is.True,
                    $"'{key}' is exempted but is not a MonsterDefinition field any more.");

            var statFields = new HashSet<string>(typeof(EntityStats)
                .GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name));
            foreach (var key in ExemptStatsFields.Keys)
                Assert.That(statFields.Contains(key), Is.True,
                    $"'{key}' is exempted but is not an EntityStats field any more.");
        }

        [Test]
        public void NoEditorTutorial_TeachesARetiredFunctionKey()
        {
            // The F-row toggles were retired on 2026-09-05 and the thirteen editor actions ship
            // UNBOUND. Four overlays still taught them, which is the loading-screen tips defect
            // in another costume: an author following the tutorial presses a key that does
            // nothing and nothing says why.
            string root = System.IO.Path.Combine(
                UnityEngine.Application.dataPath, "_Project/Scripts/Gameplay/Editors");
            var offenders = new List<string>();

            foreach (var file in System.IO.Directory.GetFiles(root, "*.cs",
                         System.IO.SearchOption.AllDirectories))
            {
                string text = System.IO.File.ReadAllText(file);
                for (int key = 2; key <= 12; key++)
                {
                    // A tutorial ROW, not any mention. The trailing comma is what separates
                    // ("F7", "Toggle ...") from ToString("F2") -- the first pass matched both
                    // and reported three float-format calls as dead-key tutorials, which is a
                    // guard that cries wolf and therefore gets switched off.
                    //
                    // F1 is deliberately outside the range: it is the debug HUD's level cycle
                    // and is still bound. So is the Tile editor's Shift+F8 perf probe, which
                    // is an overlay you flip while working rather than an editor toggle.
                    string row = "(\"F" + key + "\",";
                    if (text.Contains(row))
                        offenders.Add(System.IO.Path.GetFileName(file) + " -> F" + key);
                }
            }

            Assert.That(offenders, Is.Empty,
                "Editor tutorials still teaching a retired F-key toggle:\n  " +
                string.Join("\n  ", offenders) +
                "\n\nEvery runtime editor is opened from the General Editor on Escape.");
        }
    }
}
