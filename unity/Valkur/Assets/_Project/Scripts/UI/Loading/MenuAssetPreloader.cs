using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Valkur.Core.Boot;

namespace Valkur.UI.Loading
{
    /// <summary>
    /// Pages the world's heavy sprite trees into memory while the player is still deciding
    /// what to play.
    ///
    /// <para><b>The whole idea is that the menu is idle time nobody is measuring.</b>
    /// Measured on this project: a first boot of the gameplay scene costs 5 649 ms and a
    /// second one in the same session 3 115 ms — the 2 534 ms difference is first-touch
    /// asset paging, paid once per launch. Touching the same trees from the menu costs
    /// 1 315 ms of wall clock that nobody is looking at, and took the first boot to
    /// <b>3 748 ms</b>. Seventy-five per cent of the penalty, moved to where it is free.</para>
    ///
    /// <para><b>What it deliberately does NOT do.</b> It does not build the world. Loading
    /// the gameplay scene additively behind the menu would move another ~1.4 s, and it was
    /// rejected on its cost rather than its size: the boot sequence does not merely load
    /// data, it STARTS SYSTEMS — spawners fire, NPCs walk, the day clock runs, autosave
    /// arms. A world alive behind a menu needs the sequence split into build and activate
    /// phases and two scenes' worth of singletons arbitrated, for a gain smaller than this
    /// file's. Paging assets has none of that: nothing is instantiated, nothing ticks, and
    /// abandoning it half-done costs nothing.</para>
    ///
    /// <para><b>It must never make the menu worse.</b> It holds a frame for at most
    /// <see cref="BudgetMs"/>, yields, and stops outright the moment a load begins. The
    /// references are kept so <c>Resources.UnloadUnusedAssets</c> cannot undo the work
    /// before the boot uses it — these are assets the gameplay scene loads anyway, so the
    /// session's peak memory is unchanged; only the ORDER moved.</para>
    /// </summary>
    public class MenuAssetPreloader : MonoBehaviour
    {
        /// <summary>
        /// How long one frame of preloading may take. Smaller than a chunk's average cost
        /// (~23 ms) on purpose: the check happens BETWEEN chunks, so this bounds how far
        /// past the budget a frame can run rather than being the frame's length.
        /// </summary>
        private const float BudgetMs = 8f;

        private const string MenuSceneName = "MainMenu";

        private static MenuAssetPreloader _instance;
        private static bool _completed;

        /// <summary>Kept alive so UnloadUnusedAssets cannot page them back out.</summary>
        private static readonly System.Collections.Generic.List<Object> _pinned =
            new System.Collections.Generic.List<Object>(6000);

        /// <summary>How many of the manifest's paths have been paged in this session.</summary>
        public static int PathsLoaded { get; private set; }

        public static int PathsTotal { get; private set; }

        public static bool IsComplete => _completed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryStartFor(SceneManager.GetActiveScene().name);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            => TryStartFor(scene.name);

        private static void TryStartFor(string sceneName)
        {
            if (_completed || _instance != null) return;
            if (!string.Equals(sceneName, MenuSceneName, System.StringComparison.OrdinalIgnoreCase)) return;

            var go = new GameObject("[MenuAssetPreloader]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MenuAssetPreloader>();
        }

        private void Start() => StartCoroutine(Preload());

        private IEnumerator Preload()
        {
            // One frame of grace so the menu draws before anything is paged.
            yield return null;

            var paths = new System.Collections.Generic.List<string>(MenuPreloadManifest.Paths());
            PathsTotal = paths.Count;

            var budget = LoadFrameBudget.Start(BudgetMs);
            var watch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < paths.Count; i++)
            {
                // The player pressed something: their load is the priority, and a
                // half-finished preload is still worth exactly what it paged.
                if (LoadingScreenController.IsShowing) break;

                try
                {
                    var loaded = Resources.LoadAll<Sprite>(paths[i]);
                    if (loaded != null && loaded.Length > 0) _pinned.AddRange(loaded);
                }
                catch (System.Exception ex)
                {
                    // A missing folder is not worth failing a menu over.
                    Debug.LogWarning($"[MenuAssetPreloader] '{paths[i]}' no se pudo precargar: {ex.Message}");
                }

                PathsLoaded = i + 1;

                if (budget.Spent) { yield return null; budget.Restart(); }
            }

            watch.Stop();
            _completed = PathsLoaded >= PathsTotal;
            Debug.Log($"[MenuAssetPreloader] {PathsLoaded}/{PathsTotal} carpetas, " +
                      $"{_pinned.Count} sprites en {watch.Elapsed.TotalMilliseconds:F0} ms" +
                      (_completed ? "." : " (interrumpido: el jugador arranco antes)."));

            if (_instance == this) _instance = null;
            Destroy(gameObject);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Domain Reload is off: the pinned list would otherwise carry a previous
            // session's destroyed assets, and the sceneLoaded handler would fire twice.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _pinned.Clear();
            _instance = null;
            _completed = false;
            PathsLoaded = 0;
            PathsTotal = 0;
        }
    }
}
