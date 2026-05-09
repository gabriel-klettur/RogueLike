using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// Pins the zone-name validation contract — empty / oversize / illegal
    /// characters / duplicate-in-slot all surface a specific
    /// <see cref="MapEditorZoneNameValidator.Result"/> + a status-bar
    /// message. The exclude-name path lets rename "rename to itself"
    /// safely.
    /// </summary>
    [TestFixture]
    public class MapEditorZoneNameValidatorTests
    {
        private GameObject _zonesGo;
        private ZoneManager _zones;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _zonesGo = new GameObject("ValidatorZones");
            _zones = _zonesGo.AddComponent<ZoneManager>();
            _zones.AddZone("Forest", new Vector2Int(0, 0),  editableInTileEditor: true);
            _zones.AddZone("Dungeon", new Vector2Int(50, 0), editableInTileEditor: true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_zonesGo != null) Object.DestroyImmediate(_zonesGo);
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Validate_FreshName_Ok()
        {
            var r = MapEditorZoneNameValidator.Validate(
                "NewArea", _zones, excludeName: null, out var trimmed, out _);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.Ok, r);
            Assert.AreEqual("NewArea", trimmed);
        }

        [Test]
        public void Validate_TrimsLeadingTrailingWhitespace()
        {
            var r = MapEditorZoneNameValidator.Validate(
                "   Castle   ", _zones, excludeName: null, out var trimmed, out _);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.Ok, r);
            Assert.AreEqual("Castle", trimmed);
        }

        [Test]
        public void Validate_EmptyOrWhitespace_Empty()
        {
            var r1 = MapEditorZoneNameValidator.Validate(
                "", _zones, excludeName: null, out _, out var msg1);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.Empty, r1);
            Assert.IsNotEmpty(msg1, "Empty name must produce a user-facing message.");

            var r2 = MapEditorZoneNameValidator.Validate(
                "    ", _zones, excludeName: null, out _, out _);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.Empty, r2);
        }

        [Test]
        public void Validate_TooLong_TooLong()
        {
            string longName = new string('z', MapEditorZoneNameValidator.MaxLength + 1);
            var r = MapEditorZoneNameValidator.Validate(
                longName, _zones, excludeName: null, out _, out var msg);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.TooLong, r);
            Assert.That(msg, Does.Contain("too long"),
                "Error message must mention the length cap so the user can shorten the name.");
        }

        [Test]
        public void Validate_IllegalCharacters_InvalidCharacters()
        {
            var r = MapEditorZoneNameValidator.Validate(
                "bad/name", _zones, excludeName: null, out _, out _);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.InvalidCharacters, r);
        }

        [Test]
        public void Validate_AcceptsLettersDigitsUnderscoreHyphenSpaces()
        {
            var r = MapEditorZoneNameValidator.Validate(
                "Cave-Of_The 7th Hill", _zones, excludeName: null, out _, out _);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.Ok, r);
        }

        [Test]
        public void Validate_DuplicateInSlot_DuplicateInSlot()
        {
            var r = MapEditorZoneNameValidator.Validate(
                "Forest", _zones, excludeName: null, out _, out var msg);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.DuplicateInSlot, r);
            Assert.That(msg, Does.Contain("Forest"),
                "Error message must include the offending name so the user knows what to rename.");
        }

        [Test]
        public void Validate_DuplicateCheckIsCaseInsensitive()
        {
            var r = MapEditorZoneNameValidator.Validate(
                "FOREST", _zones, excludeName: null, out _, out _);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.DuplicateInSlot, r,
                "ZoneManager keys are OrdinalIgnoreCase, so case-only collisions must reject too — " +
                "otherwise the user could ship two zones distinguishable only by case.");
        }

        [Test]
        public void Validate_ExcludeName_AllowsRenameToSelf()
        {
            // "Forest" already exists; renaming Forest → Forest (or "forest"
            // case-fold) must NOT be a duplicate failure when excludeName
            // tells the validator to ignore that one slot.
            var r = MapEditorZoneNameValidator.Validate(
                "forest", _zones, excludeName: "Forest", out _, out _);
            Assert.AreEqual(MapEditorZoneNameValidator.Result.Ok, r,
                "Excluding the original name lets a rename-to-itself path validate cleanly.");
        }
    }
}
