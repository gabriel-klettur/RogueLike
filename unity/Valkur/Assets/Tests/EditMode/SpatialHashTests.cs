using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode
{
    public class SpatialHashTests
    {
        [Test]
        public void Insert_And_QueryRadius_FindsNearbyItems()
        {
            var hash = new SpatialHash<string>(2f);
            hash.Insert("A", new Vector2(0, 0));
            hash.Insert("B", new Vector2(1, 0));
            hash.Insert("C", new Vector2(10, 10));

            var results = new List<(string item, Vector2 pos)>();
            hash.QueryRadius(Vector2.zero, 2f, results);

            Assert.AreEqual(2, results.Count);
            var names = new HashSet<string>();
            foreach (var r in results) names.Add(r.item);
            Assert.IsTrue(names.Contains("A"));
            Assert.IsTrue(names.Contains("B"));
            Assert.IsFalse(names.Contains("C"));
        }

        [Test]
        public void QueryRadius_EmptyHash_ReturnsNothing()
        {
            var hash = new SpatialHash<int>(2f);
            var results = new List<(int item, Vector2 pos)>();
            hash.QueryRadius(Vector2.zero, 5f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void Clear_RemovesAllEntries()
        {
            var hash = new SpatialHash<string>(2f);
            hash.Insert("A", Vector2.zero);
            hash.Insert("B", Vector2.one);
            hash.Clear();

            var results = new List<(string item, Vector2 pos)>();
            hash.QueryRadius(Vector2.zero, 100f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void QueryRadius_ExactBoundary_IncludesItem()
        {
            var hash = new SpatialHash<string>(2f);
            hash.Insert("A", new Vector2(3f, 0f));

            var results = new List<(string item, Vector2 pos)>();
            hash.QueryRadius(Vector2.zero, 3f, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("A", results[0].item);
        }

        [Test]
        public void QueryRadius_JustOutsideBoundary_ExcludesItem()
        {
            var hash = new SpatialHash<string>(2f);
            hash.Insert("A", new Vector2(3.01f, 0f));

            var results = new List<(string item, Vector2 pos)>();
            hash.QueryRadius(Vector2.zero, 3f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void Insert_NegativeCoordinates_WorksCorrectly()
        {
            var hash = new SpatialHash<string>(2f);
            hash.Insert("A", new Vector2(-5f, -5f));
            hash.Insert("B", new Vector2(-4.5f, -5f));

            var results = new List<(string item, Vector2 pos)>();
            hash.QueryRadius(new Vector2(-5f, -5f), 1f, results);
            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void MultipleInserts_SameCell_AllReturned()
        {
            var hash = new SpatialHash<int>(10f);
            for (int i = 0; i < 20; i++)
                hash.Insert(i, new Vector2(0.1f * i, 0f));

            var results = new List<(int item, Vector2 pos)>();
            hash.QueryRadius(Vector2.zero, 5f, results);
            Assert.AreEqual(20, results.Count);
        }

        [Test]
        public void LargeCellSize_StillFiltersCorrectly()
        {
            var hash = new SpatialHash<string>(100f);
            hash.Insert("near", new Vector2(1, 1));
            hash.Insert("far", new Vector2(50, 50));

            var results = new List<(string item, Vector2 pos)>();
            hash.QueryRadius(Vector2.zero, 2f, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("near", results[0].item);
        }
    }
}
