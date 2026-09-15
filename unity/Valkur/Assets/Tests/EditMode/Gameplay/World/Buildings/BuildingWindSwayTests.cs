using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Gameplay.World.Buildings
{
    /// <summary>
    /// Which buildings sway: trees and flora by their folder, nothing built, and an explicit
    /// override in either direction. Derived from the category so a newly imported tree sways
    /// with no data edit — the same reason it files itself under the Trees tab with none.
    /// </summary>
    [TestFixture]
    public class BuildingWindSwayTests
    {
        private static BuildingTemplateData Template(string assetPath, int windSway = 0)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.assetPath = assetPath;
            t.windSway  = windSway;
            return t;
        }

        [Test]
        public void TreesAndFlora_Sway_ByTheirFolder()
        {
            Assert.IsTrue(BuildingWindSway.Resolve(Template("Buildings/nature/tree_oak_large")));
            Assert.IsTrue(BuildingWindSway.Resolve(Template("Buildings/vegetation/willow")));
            Assert.IsTrue(BuildingWindSway.Resolve(Template("Buildings/gardens/bush_round")));
            Assert.IsTrue(BuildingWindSway.Resolve(Template("Buildings/nature/mushroom_cluster")));
        }

        [Test]
        public void ThingsBuilt_DoNot()
        {
            Assert.IsFalse(BuildingWindSway.Resolve(Template("Buildings/houses/curse_house_topdown")));
            Assert.IsFalse(BuildingWindSway.Resolve(Template("Buildings/lights/lamp_post_ornate")));
            Assert.IsFalse(BuildingWindSway.Resolve(null));
        }

        [Test]
        public void TheOverride_WinsInEitherDirection()
        {
            Assert.IsTrue(BuildingWindSway.Resolve(Template("Buildings/military/tent", windSway: 1)), "A tent flaps.");
            Assert.IsFalse(BuildingWindSway.Resolve(Template("Buildings/nature/tree_petrified", windSway: -1)), "A stone tree does not.");
        }
    }
}
