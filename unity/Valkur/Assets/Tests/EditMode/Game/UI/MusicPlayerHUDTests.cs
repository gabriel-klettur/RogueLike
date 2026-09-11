using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Infrastructure;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// The music panel, built and driven in Edit Mode through a fake audio service. Replaces the
    /// old panel's two fixtures, which reached into private fields by reflection and pinned a
    /// "hidden on a fresh install" default that made the panel impossible to discover.
    ///
    /// <para>PlayerPrefs are MACHINE state: the panel's keys are snapshotted in SetUp and put
    /// back in TearDown, so running the suite never opens or moves the developer's own panel.</para>
    /// </summary>
    public class MusicPlayerHUDTests
    {
        private static readonly string[] PrefKeys =
        {
            "valkur.musichud.hidden", "valkur.musichud.expanded",
            "valkur.musichud.dock.right", "valkur.musichud.dock.bottom",
        };

        private sealed class FakeAudio : IAudioService, IMusicSignalSource
        {
            public bool Playing, Paused, Playlist = true;
            public float Volume = 0.6f;
            public float Time;
            public AudioClip Clip;
            public string Id = "pepitoria_theme_3", Title = "Pepitoria Theme 3";
            public float SeekedTo = -1f;
            public int Skips, Backs, Pauses, Resumes;
            public float[] Signal;

            public event System.Action<string, string, float, int> OnTrackChanged;
            public void RaiseTrackChanged() => OnTrackChanged?.Invoke(Id, Title, 117f, 4);

            public bool IsMusicPlaying => Playing && !Paused;
            public bool IsMusicPaused => Paused;
            public float MusicVolume => Volume;
            public bool HasActivePlaylist => Playlist;
            public AudioClip CurrentMusicClip => Clip;
            public string CurrentTrackTitle => Title;
            public string CurrentTrackId => Id;
            public float CurrentTrackBpm => 117f;
            public int CurrentTrackBeatsPerBar => 4;
            public float CurrentTrackBeatOffsetSec => 0f;
            public float[] CurrentTrackBeatTimes => null;
            public string CurrentTrackKey => "A minor";
            public float CurrentMusicTime => Time;

            public int MusicSignalSampleRate => 48000;
            public int ReadMusicSignal(float[] dest)
            {
                if (Signal == null || !IsMusicPlaying) return 0;
                int n = Mathf.Min(dest.Length, Signal.Length);
                System.Array.Copy(Signal, Signal.Length - n, dest, 0, n);
                return n;
            }

            public void SetMusicVolume(float v) => Volume = v;
            public void PauseMusic() { Paused = true; Pauses++; }
            public void ResumeMusic() { Paused = false; Resumes++; }
            public void SkipToNextTrack() => Skips++;
            public void SkipToPreviousTrack() => Backs++;
            public void SeekMusic(float s) => SeekedTo = s;

            public void PlayMusic(AudioClip c) { }
            public void PlayMusic(AudioClip c, float f) { }
            public void CrossfadeTo(AudioClip c, float d = 0.6f) { }
            public void PlayMusicByTrackId(string id, float f = -1f) { }
            public void StopMusic() { }
            public void StopMusic(float f) { }
            public void PlaySFX(AudioClip c, float v = 1f) { }
            public void PlaySFXAtPosition(AudioClip c, Vector3 p, float v = 1f) { }
            public void PlaySfxById(string id, float v = 1f) { }
            public bool HasSfx(string id) => false;
            public void PlaySfxRandom(string[] ids, float v = 1f) { }
            public void SetSFXVolume(float v) { }
            public void SetAmbientVolume(float v) { }
            public void EnableAmbient(string[] ids, float mn, float mx) { }
            public void DisableAmbient() { }
            public void StartPlaylist(AudioClip[] t, float i = 120f, bool s = true) { }
            public void StopPlaylist() { }
            public bool GetMusicSpectrumData(float[] b, int ch = 0, FFTWindow w = FFTWindow.BlackmanHarris) => false;
            public bool GetMusicOutputData(float[] b, int ch = 0) => false;
            public void OnZoneChanged(string z, string l = null, string b = null) { }
            public void EnterGameAudio() { }
            public void PlayMenuMusic() { }
            public void TransitionMenuToGame() { }
            public void ApplySettings() { }
        }

        private readonly Dictionary<string, object> _savedPrefs = new Dictionary<string, object>();
        private float _savedVolume;
        private FakeAudio _audio;
        private MusicPlayerHUD _hud;
        private readonly List<Object> _cleanup = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            foreach (var k in PrefKeys)
            {
                if (!PlayerPrefs.HasKey(k)) continue;
                _savedPrefs[k] = k.Contains("dock") ? (object)PlayerPrefs.GetFloat(k) : PlayerPrefs.GetInt(k);
            }
            foreach (var k in PrefKeys) PlayerPrefs.DeleteKey(k);
            _savedVolume = GameSettings.Instance.musicVolume;
            _audio = new FakeAudio { Clip = AudioClip.Create("test_song", 48000 * 180, 1, 48000, false) };
            _cleanup.Add(_audio.Clip);
            ServiceLocator.Register<IAudioService>(_audio);
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Unregister<IAudioService>();
            foreach (var o in _cleanup) if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();
            if (_hud != null) Object.DestroyImmediate(_hud.gameObject);
            foreach (var k in PrefKeys) PlayerPrefs.DeleteKey(k);
            foreach (var kv in _savedPrefs)
            {
                if (kv.Value is float f) PlayerPrefs.SetFloat(kv.Key, f);
                else PlayerPrefs.SetInt(kv.Key, (int)kv.Value);
            }
            _savedPrefs.Clear();
            PlayerPrefs.Save();
            GameSettings.Instance.musicVolume = _savedVolume;
            GameSettings.Instance.Save();
        }

        private MusicPlayerHUD Build()
        {
            _hud = MusicPlayerHUD.Create(null);
            return _hud;
        }

        private void Tick(int frames = 1, float dt = 1f / 60f)
        {
            for (int i = 0; i < frames; i++) _hud.Tick(dt);
        }

        private void PlaySong(float time = 106f)
        {
            _audio.Playing = true;
            _audio.Time = time;
        }

        // -- Grid and contract ------------------------------------------------------------

        [Test]
        public void EveryRectInThePixelSpace_SitsOnWholeTexels_AtUnitScale()
        {
            Build();
            PlaySong();
            _hud.SetExpanded(true);
            Tick(3);
            foreach (var rt in _hud.PixelSpace.GetComponentsInChildren<RectTransform>(true))
            {
                if (rt == _hud.PixelSpace || rt.parent == null) continue;
                // The tooltip positions itself from floats and places whole texels itself; the
                // motes are a single graphic that snaps its own quads.
                Vector2 p = rt.anchoredPosition, s = rt.sizeDelta;
                if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.zero)
                {
                    Assert.AreEqual(Mathf.Round(p.x), p.x, 1e-4f, rt.name + " x");
                    Assert.AreEqual(Mathf.Round(p.y), p.y, 1e-4f, rt.name + " y");
                    Assert.AreEqual(Mathf.Round(s.x), s.x, 1e-4f, rt.name + " w");
                    Assert.AreEqual(Mathf.Round(s.y), s.y, 1e-4f, rt.name + " h");
                }
                // A window grows; it never stretches. The old panel ran at localScale (0.56, 0.87).
                Assert.AreEqual(Vector3.one, rt.localScale, rt.name + " is scaled.");
            }
        }

        [Test]
        public void EveryNineSlice_FitsItsBorders()
        {
            // A 9-slice smaller than its two borders is squashed by uGUI onto half texels: the
            // groove shipped that way on the first build (3 texels tall, a 2+2 border) and was the
            // one row of the plaque off the pixel grid in the live capture.
            Build();
            _hud.SetExpanded(true);
            Tick(2);
            foreach (var img in _hud.PixelSpace.GetComponentsInChildren<Image>(true))
            {
                if (img.type != Image.Type.Sliced || img.sprite == null) continue;
                var b = img.sprite.border;
                var s = img.rectTransform.sizeDelta;
                Assert.LessOrEqual(b.x + b.z, s.x, $"{img.name}: borders {b} wider than {s}");
                Assert.LessOrEqual(b.y + b.w, s.y, $"{img.name}: borders {b} taller than {s}");
            }
        }

        [Test]
        public void TheCanvas_FollowsTheHudContract()
        {
            Build();
            var scaler = _hud.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler);
            Assert.AreEqual(new Vector2(HudLayout.ReferenceWidth, HudLayout.ReferenceHeight), scaler.referenceResolution);
            Assert.AreEqual(HudLayout.Match, scaler.matchWidthOrHeight);
            Assert.AreEqual(HudLayout.MusicSortingOrder, _hud.Canvas.sortingOrder);
            Assert.AreEqual(MusicPlayerHUD.ObjectName, _hud.gameObject.name, "The Buildings editor finds the panel by this name.");
        }

        [Test]
        public void NoTwoWidgets_Overlap()
        {
            Build();
            PlaySong();
            Tick(2);
            var parts = new List<RectTransform>
            {
                _hud.PreviousKey.Root, _hud.PlayKey.Root, _hud.NextKey.Root, _hud.MuteKey.Root,
                _hud.Volume.Root, _hud.ResonanceKey.Root, _hud.CloseKey.Root, _hud.Groove.Root,
                _hud.MedallionRim.rectTransform,
            };
            foreach (var name in new[] { "Title", "TimeNow", "TimeTotal", "VolumeLabel", "Zone" })
                parts.Add((RectTransform)_hud.PixelSpace.Find(name));
            for (int i = 0; i < parts.Count; i++)
                for (int j = i + 1; j < parts.Count; j++)
                {
                    var a = Rect(parts[i]);
                    var b = Rect(parts[j]);
                    Assert.IsFalse(a.Overlaps(b), $"{parts[i].name} {a} overlaps {parts[j].name} {b}");
                }
        }

        private static Rect Rect(RectTransform rt) => new Rect(rt.anchoredPosition, rt.sizeDelta);

        [Test]
        public void TheResonance_NeverReachesTheTopRightColumn()
        {
            // At the reference resolution one texel is two canvas units. The old expanded panel
            // was 603 x 500 px and covered 63 % of the minimap.
            var style = MusicHudStyle.Active;
            int scale = PlayerHudStyle.Active.HudPixelScaleFor((int)HudLayout.ReferenceWidth, (int)HudLayout.ReferenceHeight);
            float top = style.dockBottom + style.HeightTexels(true) * scale;
            Assert.LessOrEqual(top, HudLayout.BottomReserved);
        }

        [Test]
        public void TheDefaultDock_ClearsTheTray()
        {
            Assert.GreaterOrEqual(MusicHudStyle.Active.dockBottom,
                16f + Valkur.UIKit.HUDIconBar.BUTTON_SIZE, "The panel would sit on the tray's buttons.");
        }

        [Test]
        public void ThePlaque_IsExactlyAsWideAsTheTray_UnderIt()
        {
            // The tray holds inventory, spells and music: three 80-unit buttons 6 apart. Same
            // width and the same right margin make the plaque and the tray one column.
            var style = MusicHudStyle.Active;
            int scale = PlayerHudStyle.Active.HudPixelScaleFor((int)HudLayout.ReferenceWidth, (int)HudLayout.ReferenceHeight);
            Assert.AreEqual(3 * Valkur.UIKit.HUDIconBar.BUTTON_SIZE + 2 * 6f, style.widthTexels * scale);
            // Game windows open to the left of the panel from this declared width.
            Assert.AreEqual(HudLayout.MusicPanelWidth, style.widthTexels * scale);
            Assert.AreEqual(16f, style.dockRight, "The tray's own edge inset.");
        }

        [Test]
        public void TheTrayIcon_ShipsInTheStyle_NotThroughAssetDatabase()
        {
            Assert.IsNotNull(MusicHudStyle.Active.trayIcon,
                "Resources/UI/MusicHudStyle.asset has no tray icon: in a build the tray shows a grey square.");
        }

        // -- What it says ----------------------------------------------------------------------

        [Test]
        public void APlayingTrack_ShowsItsTitle_ZoneAndPositionInTheList()
        {
            Build();
            PlaySong(106.4f);
            Tick(2);
            Assert.AreEqual("PEPITORIA THEME 3", _hud.TitleText);
            StringAssert.StartsWith("PEPITORIA", _hud.ZoneText);
            StringAssert.Contains("/13", _hud.ZoneText, "Pepitoria has thirteen tracks in the shipped catalog.");
            Assert.AreEqual("1:46", _hud.TimeNowText);
            Assert.AreEqual("3:00", _hud.TimeTotalText);
            Assert.AreEqual(MusicSigil.Town, MusicTrackInfo.SigilOf("Pepitoria"));
        }

        [Test]
        public void NothingPlaying_SaysSo_AndTheTransportCannotBePressed()
        {
            Build();
            Tick(2);
            Assert.AreEqual(MusicHudText.Idle, _hud.TitleText);
            Assert.IsFalse(_hud.PlayKey.Enabled);
            Assert.AreEqual("-:--", _hud.TimeNowText);
        }

        [Test]
        public void Paused_ReadsEnPausa_AndOffersPlay()
        {
            Build();
            PlaySong();
            _audio.Paused = true;
            Tick(2);
            Assert.AreEqual(MusicHudText.Paused, _hud.StatusText);
            Assert.AreEqual("MusicHud_play", _hud.PlayKey.Glyph.sprite.name);
            _hud.PlayKey.Press();
            Assert.AreEqual(1, _audio.Resumes);
        }

        [Test]
        public void NoPlaylist_DisablesSkip_ButNotPlay()
        {
            _audio.Playlist = false;
            Build();
            PlaySong();
            Tick(2);
            Assert.IsFalse(_hud.NextKey.Enabled);
            Assert.IsFalse(_hud.PreviousKey.Enabled);
            Assert.IsTrue(_hud.PlayKey.Enabled);
            _hud.NextKey.Press();
            Assert.AreEqual(0, _audio.Skips);
        }

        // -- Volume ---------------------------------------------------------------------------

        [Test]
        public void PickingANotch_SetsTheSharedMusicVolume()
        {
            Build();
            PlaySong();
            Tick();
            _hud.Volume.Pick(4);
            Assert.AreEqual(0.5f, _audio.Volume, 1e-4f);
            Assert.AreEqual(0.5f, GameSettings.Instance.musicVolume, 1e-4f,
                "The pause and main menus read the same field; the panel must write it.");
        }

        [Test]
        public void Mute_Remembers_AndUnmuteRefillsTheNotchesOneByOne()
        {
            _audio.Volume = 0.75f;
            Build();
            PlaySong();
            Tick(2);
            _hud.MuteKey.Press();
            Assert.AreEqual(0f, _audio.Volume, 1e-4f);
            Tick(2);
            Assert.AreEqual(0, _hud.Volume.LitNow);
            Assert.AreEqual(MusicHudText.Muted, _hud.StatusText);

            _hud.MuteKey.Press();
            Assert.AreEqual(0.75f, _audio.Volume, 1e-4f);
            Tick(1, 0.02f);
            Assert.Less(_hud.Volume.LitNow, _hud.Volume.Target, "Unmuting should refill, not snap.");
            Tick(30, 0.02f);
            Assert.AreEqual(_hud.Volume.Target, _hud.Volume.LitNow);
            Assert.AreEqual(6, _hud.Volume.Target);
        }

        [Test]
        public void AQuietVolume_NeverLooksMuted()
        {
            Build();
            Assert.AreEqual(1, _hud.Volume.NotchesFor(0.02f));
            Assert.AreEqual(0, _hud.Volume.NotchesFor(0f));
        }

        // -- Events -----------------------------------------------------------------------------

        [Test]
        public void ANewTrack_LightsTheMedallion_AndReleasesNotes()
        {
            Build();
            PlaySong(0f);
            Tick(2);
            Assert.AreEqual(0, _hud.Motes.Alive, "Binding to a song already playing is not news.");
            _audio.Id = "pepitoria_theme_4";
            _audio.Title = "Pepitoria Theme 4";
            _audio.RaiseTrackChanged();
            Tick();
            Assert.Greater(_hud.Motes.Alive, 0);
            Assert.LessOrEqual(_hud.Motes.Alive, MusicHudStyle.Active.notesOnTrack);
            Assert.Greater(_hud.MedallionFlash, 0.5f);
            Assert.IsTrue(_hud.ShineActive);
            Tick(90);
            Assert.AreEqual(0, _hud.Motes.Alive, "Motes die within a second.");
            Assert.AreEqual(0f, _hud.MedallionFlash);
        }

        [Test]
        public void AtRest_NothingMoves()
        {
            Build();
            PlaySong();
            Tick(240);
            Assert.AreEqual(0, _hud.Motes.Alive, "A panel that sparkles at rest says nothing when it matters.");
        }

        [Test]
        public void ATrackChangedWhileClosed_IsNotAnnouncedWhenItOpens()
        {
            Build();
            PlaySong();
            _hud.SetHidden(true);
            Tick(30);
            _audio.Id = "pepitoria_theme_5";
            _audio.Title = "Pepitoria Theme 5";
            _audio.RaiseTrackChanged();
            Tick(2);
            _hud.SetHidden(false);
            Tick(2);
            Assert.AreEqual("PEPITORIA THEME 5", _hud.TitleText);
            Assert.AreEqual(0, _hud.Motes.Alive);
        }

        [Test]
        public void Seeking_MovesTheSongOnRelease_AndMarksWhereItLanded()
        {
            Build();
            PlaySong(10f);
            Tick(2);
            _hud.Groove.BeginSeek(0.5f);
            Assert.AreEqual(-1f, _audio.SeekedTo, "Streaming audio stutters when seeked every drag frame.");
            _hud.Groove.Commit(0.5f);
            Assert.AreEqual(90f, _audio.SeekedTo, 0.01f);
            Assert.Greater(_hud.Motes.Alive, 0);
        }

        [Test]
        public void Skipping_SendsSparksTheWayOfTheJump()
        {
            Build();
            PlaySong();
            Tick(2);
            _hud.NextKey.Press();
            Assert.AreEqual(1, _audio.Skips);
            Assert.Greater(_hud.Motes.Alive, 0);
        }

        // -- Window ---------------------------------------------------------------------------

        [Test]
        public void AFreshInstall_ShowsThePanel_AndClosingIsRemembered()
        {
            Build();
            Assert.IsFalse(_hud.Hidden, "Hidden by default, a player never learns the panel exists.");
            _hud.CloseKey.Press();
            Assert.IsTrue(_hud.Hidden);
            Object.DestroyImmediate(_hud.gameObject);
            Build();
            Assert.IsTrue(_hud.Hidden);
            _hud.Toggle();
            Assert.IsFalse(_hud.Hidden);
        }

        [Test]
        public void Closed_ItFades_AndThenDoesNoWork()
        {
            Build();
            PlaySong();
            Tick(2);
            _hud.SetHidden(true);
            Tick(30);
            Assert.AreEqual(0f, _hud.Alpha);
            int frames = _hud.WorkFrames;
            _audio.Time = 150f;
            Tick(20);
            Assert.AreEqual(frames, _hud.WorkFrames);
        }

        [Test]
        public void OpeningAndClosing_Fades_RatherThanJumping()
        {
            Build();
            Tick(30);
            _hud.SetHidden(true);
            Tick(1, 0.02f);
            Assert.That(_hud.Alpha, Is.GreaterThan(0f).And.LessThan(1f));
        }

        [Test]
        public void Resonance_GrowsUpward_AndItsKeyStaysDown()
        {
            Build();
            int h = _hud.SizeTexels.y;
            _hud.SetExpanded(true);
            Assert.AreEqual(h + MusicHudStyle.Active.resonanceTexels, _hud.SizeTexels.y);
            Assert.IsTrue(_hud.ResonanceKey.Latched);
            Assert.IsTrue(_hud.ResonanceKey.Pressed);
            _hud.ResonanceKey.Press();
            Assert.IsFalse(_hud.Expanded);
        }

        [Test]
        public void Resonance_ShowsTheMusic_EvenWhenMuted()
        {
            _audio.Volume = 0f;
            var s = new float[1024];
            for (int i = 0; i < s.Length; i++) s[i] = 0.3f * Mathf.Sin(2f * Mathf.PI * 440f * i / 48000f);
            _audio.Signal = s;
            Build();
            PlaySong();
            _hud.SetExpanded(true);
            Tick(30);
            int band = _hud.ResonanceGraphic.TallestBand;
            Assert.GreaterOrEqual(band, 0, "The old analyser read the signal after the volume and drew nothing muted.");
            Assert.Greater(_hud.ResonanceGraphic.BlocksIn(band), 5);
            Assert.Greater(_hud.ResonanceGraphic.PhraseMarks, 3, "The shipped catalog carries beat onsets for every track.");
        }

        [Test]
        public void BeingHit_PutsTheSongInTheBackground_ForAWhile()
        {
            Build();
            Tick(30);
            _hud.NotePlayerHit();
            Assert.AreEqual(MusicHudStyle.Active.combatAlpha, _hud.AlphaTarget, 1e-4f);
            Tick(Mathf.CeilToInt(MusicHudStyle.Active.combatHoldSeconds * 60f) + 5);
            Assert.AreEqual(1f, _hud.AlphaTarget);
        }

        [Test]
        public void TheConsoleProbe_NamesEachSource_AndDrivesTheWindow()
        {
            var s = new float[1024];
            for (int i = 0; i < s.Length; i++) s[i] = 0.2f * Mathf.Sin(i * 0.1f);
            _audio.Signal = s;
            Build();
            PlaySong();
            Tick(2);
            string report = _hud.RunCommand(new[] { "music" });
            StringAssert.Contains("Pepitoria Theme 3", report);
            StringAssert.Contains("BPM", report);
            StringAssert.Contains("envolvente 128", report);
            StringAssert.Contains("viva", report);
            _hud.RunCommand(new[] { "music", "cerrar" });
            Assert.IsTrue(_hud.Hidden);
            _hud.RunCommand(new[] { "music", "resonancia" });
            Assert.IsTrue(_hud.Expanded);
        }

        [TestCase("MusicPlayPause")]
        [TestCase("MusicNext")]
        [TestCase("MusicPrevious")]
        public void TheMusicKeys_ShipUnbound_Assignable_AndHarmless(string name)
        {
            var asset = Valkur.Core.Input.InputService.Initialize()?.Asset;
            Assert.IsNotNull(asset);
            var map = asset.FindActionMap(Valkur.Core.Input.InputActionCatalog.MapGameplay, throwIfNotFound: true);
            var action = map.FindAction(name, throwIfNotFound: false);
            Assert.IsNotNull(action, $"{name} is missing from ValkurInputActions.");
            // One EMPTY slot: ApplyBindingOverride writes into a slot and cannot create one, so an
            // action with no binding at all could not be assigned from the Controls editor.
            var slots = new List<UnityEngine.InputSystem.InputBinding>();
            foreach (var b in action.bindings) if (!b.isComposite) slots.Add(b);
            Assert.AreEqual(1, slots.Count, $"{name} needs exactly one bindable slot.");
            Assert.IsTrue(string.IsNullOrEmpty(slots[0].effectivePath), $"{name} ships bound to {slots[0].effectivePath}.");
            var descriptor = Valkur.Core.Input.InputActionCatalog.Find(Valkur.Core.Input.InputActionCatalog.MapGameplay, name);
            Assert.IsNotNull(descriptor, $"{name} has no catalog descriptor.");
            Assert.IsFalse(descriptor.ReachesDamage);
        }

        // -- Art --------------------------------------------------------------------------------

        [Test]
        public void ThePauseGlyph_IsTwoBars()
        {
            var art = MusicHudArt.Get(PlayerHudStyle.Active);
            Assert.IsTrue(art.TryGetPieceRect("pause", out var r));
            Assert.AreEqual(new Vector2Int(3, 5), r.size);
            for (int y = 0; y < r.height; y++)
            {
                Assert.Greater(art.Atlas.GetPixel(r.x, r.y + y).a, 0.5f, "left bar");
                Assert.Less(art.Atlas.GetPixel(r.x + 1, r.y + y).a, 0.5f, "gap");
                Assert.Greater(art.Atlas.GetPixel(r.x + 2, r.y + y).a, 0.5f, "right bar");
            }
        }

        [Test]
        public void TheSkipGlyphs_CarryAFullHeightBar()
        {
            // The old panel drew the bar as a 3x6 tick at the top: FillRect's arguments were swapped.
            var art = MusicHudArt.Get(PlayerHudStyle.Active);
            Assert.IsTrue(art.TryGetPieceRect("prev", out var prev));
            Assert.IsTrue(art.TryGetPieceRect("next", out var next));
            for (int y = 0; y < prev.height; y++)
            {
                Assert.Greater(art.Atlas.GetPixel(prev.x, prev.y + y).a, 0.5f, "previous: bar on the left");
                Assert.Greater(art.Atlas.GetPixel(next.x + next.width - 1, next.y + y).a, 0.5f, "next: bar on the right");
            }
        }

        [Test]
        public void TheStone_HasNoGold()
        {
            // Gold is importance in this HUD. The plaque is ambience.
            var theme = PlayerHudStyle.Active;
            var tex = MusicHudArt.BakePlaque(128, 42, 0, theme);
            try
            {
                foreach (var c in tex.GetPixels())
                {
                    if (c.a < 0.5f) continue;
                    Assert.IsFalse(c.r > c.b + 0.25f && c.g > c.b + 0.15f, $"Gold-looking pixel {c} in the plaque.");
                }
            }
            finally { Object.DestroyImmediate(tex); }
        }
    }
}
