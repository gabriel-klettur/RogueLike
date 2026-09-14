using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>sfx</c> command: hear any sound effect in the audio catalog on demand.
    ///
    /// <para>WHY THIS EXISTS. Most catalog sounds are tied to a moment that takes set-up to
    /// reach — a mine exploding, a revive at an altar, a rare pickup, a capstone in the
    /// grimoire — so without a probe a sound is checked only when somebody happens to cause
    /// its event, and a bad pick survives for weeks. It plays through
    /// <see cref="IAudioService.PlaySfxById"/>, the same path the game uses, so what is heard
    /// includes the entry's volume group.</para>
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>, in its own category.
    /// </summary>
    public partial class DevConsole
    {
        private const float SFX_PARADE_GAP_SECONDS = 0.6f;

        private Coroutine _sfxParade;

        private void RegisterSfxCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "sfx",
                Usage    = "sfx [<id> | buscar <texto> | todos [prefijo] | parar]",
                Help     = "play or list audio catalog sound effects (todos plays them one after another)",
                Category = "audio",
                Handler  = args => CmdSfx(args)
            });
        }

        private void CmdSfx(string[] args)
        {
            var catalog = Resources.Load<AudioCatalogSO>("AudioCatalog");
            if (catalog == null)
            {
                Log("AudioCatalog not found under Resources/.");
                return;
            }

            // args[0] is the command name itself.
            string first = args.Length > 1 ? args[1] : string.Empty;

            if (first.Length == 0)
            {
                Log($"{catalog.SfxEntries.Length} sound effects. 'sfx buscar spell_' lists ids, " +
                    "'sfx <id>' plays one, 'sfx todos spell_' plays a group, 'sfx parar' stops.");
                return;
            }

            if (first.Equals("buscar", StringComparison.OrdinalIgnoreCase))
            {
                string needle = args.Length > 2 ? args[2] : string.Empty;
                var ids = Matching(catalog, needle, contains: true);
                Log($"{ids.Count} match '{needle}': {string.Join(", ", ids)}");
                return;
            }

            if (first.Equals("parar", StringComparison.OrdinalIgnoreCase))
            {
                StopSfxParade();
                Log("Stopped.");
                return;
            }

            if (!ServiceLocator.TryGet<IAudioService>(out var audio) || audio == null)
            {
                Log("No audio service is running (enter Play Mode first).");
                return;
            }

            if (first.Equals("todos", StringComparison.OrdinalIgnoreCase))
            {
                string prefix = args.Length > 2 ? args[2] : string.Empty;
                var ids = Matching(catalog, prefix, contains: false);
                if (ids.Count == 0)
                {
                    Log($"No sound id starts with '{prefix}'.");
                    return;
                }
                StopSfxParade();
                _sfxParade = StartCoroutine(SfxParade(audio, catalog, ids));
                Log($"Playing {ids.Count} sounds. 'sfx parar' stops.");
                return;
            }

            if (!audio.HasSfx(first))
            {
                var near = Matching(catalog, first, contains: true);
                Log(near.Count > 0
                    ? $"'{first}' has no clip. Did you mean: {string.Join(", ", near)}"
                    : $"'{first}' has no clip. 'sfx buscar <texto>' lists ids.");
                return;
            }

            audio.PlaySfxById(first);
            var clip = catalog.GetSfxClip(first);
            Log($"Playing {first} ({clip.length:0.00}s, group {catalog.GetSfx(first).group}).");
        }

        private IEnumerator SfxParade(IAudioService audio, AudioCatalogSO catalog, List<string> ids)
        {
            foreach (string id in ids)
            {
                var clip = catalog.GetSfxClip(id);
                if (clip == null) continue;
                audio.PlaySfxById(id);
                Log($"  {id} ({clip.length:0.00}s)");
                // Real time: a hit-stop or the pause menu must not stall the parade.
                yield return new WaitForSecondsRealtime(clip.length + SFX_PARADE_GAP_SECONDS);
            }
            _sfxParade = null;
        }

        private void StopSfxParade()
        {
            if (_sfxParade == null) return;
            StopCoroutine(_sfxParade);
            _sfxParade = null;
        }

        private static List<string> Matching(AudioCatalogSO catalog, string text, bool contains)
        {
            var ids = new List<string>();
            foreach (var e in catalog.SfxEntries)
            {
                if (e == null || string.IsNullOrEmpty(e.id) || e.clip == null) continue;
                bool hit = contains
                    ? e.id.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                    : e.id.StartsWith(text, StringComparison.OrdinalIgnoreCase);
                if (hit) ids.Add(e.id);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }
    }
}
