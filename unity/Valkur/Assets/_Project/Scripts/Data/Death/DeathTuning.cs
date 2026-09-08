using UnityEngine;

namespace Valkur.Data
{
    /// <summary>
    /// Where a spirit that cannot reach an altar is put back on its feet.
    ///
    /// <para>The mode exists because "no altar reachable" is not one situation but four, and
    /// they want different answers: a world nobody has placed an altar in, an interior with no
    /// altar of its own, an altar walled off by geometry, and a player who simply gave up. What
    /// they share is that the run must not END there — a death with no exit is the one failure
    /// a player cannot work around, and the shipped build had exactly that.</para>
    /// </summary>
    public enum DeathRescueMode
    {
        /// <summary>Never auto-revive. Only legitimate with an altar guaranteed reachable.</summary>
        None = 0,

        /// <summary>The last position checkpoint the save layer wrote. The best answer when one exists.</summary>
        LastCheckpoint = 1,

        /// <summary>Where the body fell. Always available, and keeps the corpse's loot in reach.</summary>
        DeathPosition = 2,

        /// <summary>The active zone's own spawn point. Survives an interior whose corpse is unreachable.</summary>
        ZoneSpawn = 3,
    }

    /// <summary>How the spirit is shown the way back.</summary>
    public enum SpiritPathMode
    {
        /// <summary>No path at all.</summary>
        None = 0,

        /// <summary>A straight rasterised line. A magical compass — it cuts through walls.</summary>
        StraightLine = 1,

        /// <summary>A real walkable route through <c>PathFinder</c>. Only honest while the spirit is solid.</summary>
        Pathfound = 2,
    }

    /// <summary>
    /// Every judgement the death-and-revive flow makes, in one asset the Death Editor can reach.
    ///
    /// <para><b>Why this exists.</b> The flow shipped with its decisions spread across four
    /// kinds of home and one of them was fatal: a hard-coded <c>templateId 249</c> written by
    /// hand in TWO files that had to agree, three <c>[SerializeField]</c> timings on a component
    /// nothing can inspect (it is <c>AddComponent</c>-ed onto a bare GameObject, the same
    /// unreachable-field defect as <c>ChatSystem._catalog</c>), a layer mask built in code, and
    /// an XP penalty on a third component. The audit of 2026-09-07 measured the consequence and
    /// it is sharper than "nobody placed an altar": the stone arch WAS placed, in <c>lobby</c> at
    /// (693, 815), carrying template <b>197</b> — while the code looked for <b>249</b>. Both ids
    /// are the same sprite, <c>Buildings/portals/portal_stone_arch</c>, and differ only in
    /// <c>splitRatio</c>; the instance even overrides its ratio to 249's 0.195, so somebody placed
    /// it AS the altar. So the binder bound nothing, the trail drew nothing and <c>Revive()</c>
    /// was never called by anybody, while the altar stood there looking exactly right. Nothing
    /// failed, every number was internally consistent, and only the composition was wrong — the
    /// shape <c>SPAWNER_COORDINATE_SPACE_DRIFT</c> already records. A LIST rather than one id is
    /// the fix, because two templates over one sprite is a normal state of the catalogue.</para>
    ///
    /// <para><b>It is DESIGN-TIME state, deliberately not saved per run</b>, exactly like
    /// <see cref="EconomyTuning"/>. A save carries whether the player is currently dead; it does
    /// not carry how long the fade takes. Retuning changes every existing save, which is correct
    /// for a balance change.</para>
    ///
    /// <para><b>Every consumer must be null-safe and fall back to these defaults</b>, and the
    /// defaults ARE the values the code carried before this asset existed — so a project with no
    /// asset, an EditMode test and a build with a stripped <c>Resources/</c> all behave exactly
    /// as they did. A tuning layer that changes behaviour by being absent is worse than none.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "DeathTuning", menuName = "Valkur/Death/Death Tuning")]
    public sealed class DeathTuning : ScriptableObject
    {
        /// <summary>
        /// Path under <c>Resources/</c>. Under Resources because every reader —
        /// <c>DeathSequenceController</c>, <c>SpiritAltarPathHighlighter</c>,
        /// <c>SpiritWorldGrayscale</c>, <c>PlayerDeathDropSystem</c> — is
        /// <c>AddComponent</c>-ed onto a bare GameObject by <c>GameplaySceneSetup</c> and has no
        /// inspector slot to be wired from. Same reason <c>ProgressionCatalog</c> lives there.
        /// </summary>
        public const string ResourcePath = "Death/DeathTuning";

        // ── Flow ─────────────────────────────────────────────────────────────

        [Header("Ritmo de la muerte")]
        [Tooltip("Pausa dramatica entre morir y poder mover el espiritu. Por debajo de ~0.3 s " +
                 "el jugador no llega a leer que ha muerto; por encima de ~1.2 s la pausa se " +
                 "lee como que el juego se ha colgado.")]
        [Range(0f, 3f)] public float dyingFlashDuration = 0.6f;

        [Tooltip("Cuanto tarda el mundo en drenarse a gris al morir.")]
        [Range(0f, 5f)] public float grayscaleFadeIn = 1.5f;

        [Tooltip("Cuanto tarda el color en volver al revivir. Es tambien lo que dura el " +
                 "ReviveRoutine antes de devolver el control, asi que subirlo alarga la espera.")]
        [Range(0f, 5f)] public float grayscaleFadeOut = 1.0f;

        // ── Spirit ───────────────────────────────────────────────────────────

        [Header("Forma espiritu")]
        [Tooltip("Multiplicador de velocidad mientras eres espiritu. Por encima de 1 el camino " +
                 "de vuelta es un tramite; por debajo, un castigo. 1 = igual que vivo.")]
        [Range(0.25f, 3f)] public float spiritSpeedMultiplier = 1.35f;

        [Tooltip("Si el espiritu atraviesa muros y edificios. Debe ir de la mano con el modo de " +
                 "camino: una linea recta que cruza paredes por las que el espiritu NO pasa es " +
                 "una ruta que promete algo que no se puede andar, que es como se envio.")]
        public bool spiritPassesThroughWalls = true;

        [Tooltip("Segundos maximos en forma espiritu antes de que el rescate actue. 0 = sin " +
                 "limite. Es la red que hace imposible el callejon sin salida.")]
        [Range(0f, 600f)] public float spiritTimeLimitSeconds = 90f;

        [Tooltip("Si el espiritu es visible para los NPC. Apagado (lo normal) los monstruos lo " +
                 "ignoran; encendido, la vuelta al altar es una huida.")]
        public bool spiritIsTargetable;

        // ── Altars ───────────────────────────────────────────────────────────

        [Header("Altares")]
        [Tooltip("Plantillas de edificio que cuentan como altar de resurreccion. Era un 249 " +
                 "escrito a mano en dos ficheros que tenian que coincidir y nada lo comprobaba. " +
                 "197 y 249 son el MISMO arco de piedra: dos plantillas del mismo sprite que solo " +
                 "difieren en splitRatio, y el mundo enviado tiene colocada la 197 mientras el " +
                 "codigo buscaba la 249. Las dos van en la lista, o el fallo vuelve en cuanto " +
                 "alguien coloque la otra.")]
        public int[] altarTemplateIds = { 197, 249 };

        [Tooltip("Segundos que el vinculador busca altares antes de rendirse. Al rendirse ahora " +
                 "AVISA en pantalla en vez de escribir un warning que nadie lee.")]
        [Range(5f, 300f)] public float altarSearchTimeout = 60f;

        [Tooltip("Radio extra alrededor de la huella del altar que cuenta como 'dentro'. A 0 el " +
                 "jugador tiene que pisar el rectangulo exacto del edificio.")]
        [Range(0f, 4f)] public float altarActivationPadding = 0.75f;

        // ── Rescue ───────────────────────────────────────────────────────────

        [Header("Rescate (garantia de no-callejon-sin-salida)")]
        [Tooltip("Donde revive un espiritu que no puede llegar a ningun altar.")]
        public DeathRescueMode rescueMode = DeathRescueMode.LastCheckpoint;

        [Tooltip("Segundos de espera cuando NO existe ningun altar cargado, antes de rescatar. " +
                 "Corto a proposito: sin altar no hay nada que esperar.")]
        [Range(1f, 120f)] public float rescueDelayWithoutAltar = 12f;

        [Tooltip("Fraccion de vida con la que revive un rescate. Menor que 1 para que el rescate " +
                 "siga siendo peor que llegar al altar, que revive al maximo.")]
        [Range(0.05f, 1f)] public float rescueHpFraction = 0.35f;

        // ── Path ─────────────────────────────────────────────────────────────

        [Header("Camino al altar")]
        public SpiritPathMode pathMode = SpiritPathMode.StraightLine;

        [Tooltip("Segundos entre recalculos del camino.")]
        [Range(0.05f, 2f)] public float pathUpdateInterval = 0.25f;

        [Tooltip("Color de las baldosas del camino al altar.")]
        public Color pathTint = new Color(1f, 0.95f, 0.2f, 0.9f);

        [Tooltip("Tope de baldosas dibujadas. Un altar a 300 unidades pintaria 300 sprites; el " +
                 "tope recorta por el extremo LEJANO, asi que lo que se ve es siempre el tramo " +
                 "pegado al jugador, que es el unico que puede andar ahora.")]
        [Range(8, 600)] public int pathMaxMarkers = 140;

        [Tooltip("Dibujar tambien un rastro hacia el cadaver. Sin el, el jugador revive y no " +
                 "tiene forma de saber donde dejo su inventario y sus monedas.")]
        public bool showCorpseCompass = true;

        [Tooltip("Color del rastro al cadaver. Distinto del camino al altar a proposito: son " +
                 "dos destinos y un solo color los convierte en uno.")]
        public Color corpseTint = new Color(0.95f, 0.35f, 0.35f, 0.85f);

        // ── Corpse and loot ──────────────────────────────────────────────────

        [Header("Cadaver y botin")]
        [Tooltip("Soltar el inventario al morir.")]
        public bool dropInventory = true;

        [Tooltip("Soltar las monedas al morir.")]
        public bool dropCoins = true;

        [Tooltip("Fraccion de la bolsa que se cae. 1 = todo. Por debajo de 1 el jugador conserva " +
                 "un colchon y morir deja de ser una perdida total.")]
        [Range(0f, 1f)] public float coinLossFraction = 1f;

        [Tooltip("Segundos que el cadaver sobrevive tras revivir. 0 = desaparece al revivir, y con " +
                 "el desaparece la brujula: el rastro al botin cuelga del cadaver, asi que a 0 el " +
                 "jugador se levanta en el altar sin nada que le diga donde dejo la bolsa. Ese es " +
                 "justo el paseo para el que existe.")]
        [Range(0f, 600f)] public float corpseLingerSeconds = 120f;

        [Tooltip("Al morir de nuevo, retirar los objetos de la muerte ANTERIOR. Sin esto se " +
                 "acumulan indefinidamente por el mundo.")]
        public bool cleanupPreviousDrops = true;

        // ── Cost ─────────────────────────────────────────────────────────────

        [Header("Coste de morir")]
        [Tooltip("Fraccion de la XP del nivel actual que se pierde al revivir.")]
        [Range(0f, 1f)] public float xpLossFraction = 0.10f;

        [Tooltip("Si la penalizacion puede bajarte de nivel.")]
        public bool xpLossCanDelevel;

        [Tooltip("Si el 'resurrect' de la consola tambien cobra el coste. Apagado: es un cheat " +
                 "de autor, y cobrarlo era un defecto real — los dos eventos existen justo para " +
                 "distinguir un revivir de verdad de este.")]
        public bool cheatRevivePaysCost;

        // ── Persistence ──────────────────────────────────────────────────────

        [Header("Persistencia")]
        [Tooltip("Guardar el estado de muerte. Apagado, morir + salir + recargar DESHACE la " +
                 "muerte entera y devuelve el inventario: el coste pasa a ser opcional para el " +
                 "jugador. Encendido, la partida vuelve en forma espiritu donde la dejaste.")]
        public bool persistDeathState = true;

        // ── Audio ────────────────────────────────────────────────────────────

        [Header("Audio")]
        [Tooltip("Id de SFX al morir. Vacio o no presente en el catalogo = silencio, sin warning: " +
                 "se consulta con HasSfx antes de pedirlo.")]
        public string sfxDeath = "player_death";

        [Tooltip("Id de SFX al entrar en forma espiritu.")]
        public string sfxSpiritEnter = "spirit_enter";

        [Tooltip("Id de SFX al revivir.")]
        public string sfxRevive = "player_revive";

        // ── Resolution ───────────────────────────────────────────────────────

        private static DeathTuning s_cached;
        private static bool s_looked;

        /// <summary>
        /// Domain Reload is OFF, so the cache is dropped on subsystem registration with a plain
        /// <c>stsfld</c> — the only reset shape <c>DomainReloadStaticResetTests</c> recognises.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>
        /// The shipped tuning, or a throwaway instance carrying the defaults. Never null, so no
        /// caller has to branch — and the defaults are what the code did before the asset existed.
        /// </summary>
        public static DeathTuning Active
        {
            get
            {
                if (!s_looked)
                {
                    s_cached = Resources.Load<DeathTuning>(ResourcePath);
                    s_looked = true;
                }
                if (s_cached == null)
                {
                    s_cached = CreateInstance<DeathTuning>();
                    // HideAndDontSave: this instance is never an asset, and without it an
                    // EditMode run that touches the death flow leaves a ScriptableObject Unity
                    // reports as leaked, on a fixture that has nothing to do with it.
                    s_cached.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_cached;
            }
        }

        /// <summary>Drops the cache so the next read re-resolves. Called after the editor writes the asset.</summary>
        public static void InvalidateCache()
        {
            s_cached = null;
            s_looked = false;
        }

        /// <summary>
        /// True when <paramref name="templateId"/> is one of the altar templates.
        ///
        /// <para>The single answer to that question, asked by the binder, the grayscale exemption
        /// and the editor. It used to be a literal in two files that had to agree, which is the
        /// same failure mode as a hand-maintained <c>LAYER_COUNT</c>.</para>
        /// </summary>
        public bool IsAltarTemplate(int templateId)
        {
            if (altarTemplateIds == null) return false;
            for (int i = 0; i < altarTemplateIds.Length; i++)
                if (altarTemplateIds[i] == templateId) return true;
            return false;
        }

        /// <summary>Adds an altar template id if it is not already listed. Returns false when it was.</summary>
        public bool AddAltarTemplate(int templateId)
        {
            if (IsAltarTemplate(templateId)) return false;
            int n = altarTemplateIds?.Length ?? 0;
            var next = new int[n + 1];
            for (int i = 0; i < n; i++) next[i] = altarTemplateIds[i];
            next[n] = templateId;
            altarTemplateIds = next;
            return true;
        }

        /// <summary>Removes an altar template id. Returns false when it was not listed.</summary>
        public bool RemoveAltarTemplate(int templateId)
        {
            if (!IsAltarTemplate(templateId)) return false;
            int n = altarTemplateIds.Length;
            var kept = new System.Collections.Generic.List<int>(n);
            for (int i = 0; i < n; i++)
                if (altarTemplateIds[i] != templateId) kept.Add(altarTemplateIds[i]);
            altarTemplateIds = kept.ToArray();
            return true;
        }
    }
}
