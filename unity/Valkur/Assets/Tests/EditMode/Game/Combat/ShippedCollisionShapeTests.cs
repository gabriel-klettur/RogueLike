using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// The shipped collision data, read off disk, and the source seams that make it matter.
    ///
    /// <para>A hurtbox the bake never wrote is not an error anywhere: the entity silently falls
    /// back to one automatic capsule, which is plausible enough that nobody would notice the
    /// dragon's head has stopped being hittable again. So coverage is asserted here, per entity,
    /// against the frames its own definition references — the thing a new wave changes.</para>
    /// </summary>
    public class ShippedCollisionShapeTests
    {
        /// <summary>Share of an entity's halved frames that must carry a baked row.</summary>
        private const float MinCoverage = 0.95f;

        [Test]
        public void EveryHalvedFrame_OfEveryShippedEntity_HasABakedHurtbox()
        {
            int inspected = 0;
            var failures = new List<string>();

            foreach (var (key, owner, config) in ShippedConfigs())
            {
                var names = HalvedSpriteNames(owner);
                if (names.Count == 0) continue;
                inspected++;

                int covered = 0;
                foreach (string name in names)
                    if (config.collision?.FindFrame(name) != null) covered++;

                float coverage = covered / (float)names.Count;
                if (coverage < MinCoverage)
                    failures.Add($"{key}: {covered}/{names.Count} frames baked ({coverage:P0}). " +
                                 "Run Valkur > Entities > Bake Collision Shapes after importing art.");
            }

            Assert.That(inspected, Is.GreaterThan(0), "No shipped entity with halved frames was found — the check measured nothing.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void EveryBakedRow_IsAPlausibleBody()
        {
            int rows = 0;
            var failures = new List<string>();

            foreach (var (key, _, config) in ShippedConfigs())
            {
                var profile = config.collision;
                if (profile?.hurtShapeFrames == null) continue;

                foreach (var row in profile.hurtShapeFrames)
                {
                    rows++;
                    int valid = 0;
                    foreach (var s in row.shapes)
                    {
                        if (!s.IsValid) continue;
                        valid++;
                        if (Mathf.Abs(s.center.x) > 1.25f || Mathf.Abs(s.center.y) > 1.25f ||
                            s.size.x > 1.25f || s.size.y > 1.25f)
                            failures.Add($"{key}/{row.frame}: capsule outside its own frame {s.center} {s.size}");
                    }
                    if (valid < 1 || valid > EntityCollisionProfile.MaxShapesPerFrame)
                        failures.Add($"{key}/{row.frame}: {valid} capsules");
                }

                if (profile.HasAuthoredFootprint &&
                    (profile.footprintSize.x < 0.2f || profile.footprintSize.x > 4.5f ||
                     profile.footprintSize.y > profile.footprintSize.x + 0.001f))
                    failures.Add($"{key}: implausible footprint {profile.footprintSize}");
            }

            Assert.That(rows, Is.GreaterThan(0), "No baked rows at all: the shipped data was never baked.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void SpellProbe_FoldsEveryQuery_ToOneColliderPerEntity()
        {
            string src = Strip(Read("Gameplay", "Spells", "Debug", "SpellProbe.cs"));
            Assert.AreEqual(4, Regex.Matches(src, @"EntityHitFilter\.Collapse\(").Count,
                "Every multi-result query in SpellProbe must fold its result per entity, or a splash " +
                "hits a body once per hurtbox capsule.");
        }

        [Test]
        public void DirectDamageQueries_ResolveTheEntity_NotTheCollidersObject()
        {
            // These query Physics2D themselves rather than through SpellProbe. A hurtbox capsule
            // sits on a CHILD, so GetComponent<Health>() on the hit finds nothing and the blow
            // is lost — silently, which is the whole reason this is a guard.
            foreach (var parts in new[]
            {
                new[] { "Gameplay", "Combat", "Mechanics", "MeleeCombat.cs" },
                new[] { "Gameplay", "Combat", "Mechanics", "DashAbility.cs" },
                new[] { "Gameplay", "Combat", "Mechanics", "MouseTargetDetector.cs" },
                new[] { "Gameplay", "Combat", "Feedback", "ExplosionEffect.cs" },
            })
            {
                string src = Strip(Read(parts));
                StringAssert.DoesNotMatch(@"\b(hit|col|target)\.GetComponent<Health>\(\)", src, parts[parts.Length - 1]);
            }
        }

        // -- Helpers ------------------------------------------------------------------

        private static IEnumerable<(string key, ScriptableObject owner, EntityAssetConfig config)> ShippedConfigs()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:MonsterDefinition", new[] { "Assets/_Project/Data" }))
            {
                var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def?.assetConfig != null) yield return (def.monsterKey, def, def.assetConfig);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:PlayerDefinition", new[] { "Assets/_Project/Data" }))
            {
                var def = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (def?.assetConfig != null) yield return (def.playerKey, def, def.assetConfig);
            }
        }

        private static HashSet<string> HalvedSpriteNames(ScriptableObject owner)
        {
            var names = new HashSet<string>();
            var it = new SerializedObject(owner).GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (!(it.objectReferenceValue is Sprite sprite)) continue;
                if (Regex.IsMatch(sprite.name, @"_[ewEW]\d+$")) names.Add(sprite.name);
            }
            return names;
        }

        private static string Read(params string[] parts)
            => File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scripts", Path.Combine(parts)));

        private static string Strip(string src)
            => Regex.Replace(Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//[^\n]*", "");
    }
}
