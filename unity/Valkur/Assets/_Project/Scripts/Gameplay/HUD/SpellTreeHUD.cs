using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The character sheet's GRIMOIRE tab: the schools of magic drawn as constellations, what
    /// each teaches, and what the character may buy next with arcane points.
    ///
    /// It is a separate panel from the talents tree for the same reason the data is separate:
    /// a talent is a number and a spell is a verb, they cost different currencies, and putting
    /// them in one list makes the player compare "+5 % melee damage" against "unlock Meteor
    /// Shower" as if those were the same kind of choice.
    ///
    /// A school the character has no affinity for is shown, not hidden — it just costs more,
    /// and the rail says so. Hiding it would turn a class into a wall; charging for it turns a
    /// class into a tendency, which is the design <see cref="SpellTree"/> records.
    ///
    /// <para><b>Rebuilt 2026-09-12</b> (<c>.github/GRIMOIRE_BEAUTY_AUDIT_2026-09-12.md</c>).
    /// What it replaced was a list of <c>UnityEngine.UI.Text</c> in Arial on a translucent
    /// black rectangle, which drew none of the 71 icons every node resolves and none of the 62
    /// prerequisite chains they declare — while the Spells editor had been drawing exactly this
    /// board, for the author, in the same assembly, all along. The scaffold is SHARED with it
    /// (<c>SpellGraphLayout</c> places the nodes, <c>SpellGraphSprites</c> draws the sockets);
    /// what is ours is the player's reading of it: six states, the frontier of what is
    /// reachable, and a card that says every reason a node is shut.</para>
    /// </summary>
    public sealed partial class SpellTreeHUD : SingletonMonoBehaviour<SpellTreeHUD>
    {
        [SerializeField] private KnownSpells grimoire;
        [SerializeField] private int playerLevel = 1;

        private int _activeSchool;
        private SpellNode _selected;

        private readonly List<SpellLock> _locks = new List<SpellLock>(4);
        private readonly StringBuilder _sb = new StringBuilder(160);

        /// <summary>What the board last drew, so a refresh can tell what CHANGED and emit for
        /// it. Without it the panel can see that a number moved and never why — the defect the
        /// old bars over an entity's head had, for the same reason.</summary>
        private readonly Dictionary<string, GrimoireNodeState> _lastStates =
            new Dictionary<string, GrimoireNodeState>();

        private int _lastPoints = -1;

        public bool IsOpen { get; private set; }
        public int ActiveSchool => _activeSchool;

        /// <summary>The node the card is showing, or null. Selection, never a purchase.</summary>
        public SpellNode Selected => _selected;

        protected override bool Persist => false;

        // ── Binding ───────────────────────────────────────────────────────

        public void Bind(KnownSpells value, int level)
        {
            Unbind();
            grimoire = value;
            playerLevel = level;
            if (grimoire != null) grimoire.OnLoadoutChanged += Refresh;
            if (IsOpen) Refresh();
        }

        private void Unbind()
        {
            if (grimoire != null) grimoire.OnLoadoutChanged -= Refresh;
        }

        private void AutoResolve()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            Bind(player.GetComponent<KnownSpells>(),
                 player.GetComponent<Experience>()?.Level ?? 1);
        }

        // ── Open / close ──────────────────────────────────────────────────

        public void Open()
        {
            EnsureBuilt();
            if (grimoire == null) AutoResolve();

            IsOpen = true;
            if (_root != null) _root.SetActive(true);

            // The panel opens on the school the character has an affinity for, not on index 0.
            // Nine schools and one of them is yours: opening on somebody else's is a first
            // frame that says nothing about this character.
            _activeSchool = PreferredSchool();
            _selected = null;

            SeedStateCache();
            Refresh();
            BeginOpenMotion();
            GrimoireAudio.Play(GrimoireSound.Open, _style);
        }

        public void Close()
        {
            IsOpen = false;
            if (_motes != null) _motes.Clear();
            if (_root != null) _root.SetActive(false);
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void SelectSchool(int index)
        {
            int next = Mathf.Max(0, index);
            bool changed = next != _activeSchool;
            _activeSchool = next;
            _selected = null;

            SeedStateCache();
            Refresh();

            if (!changed) return;
            EmitSchoolSwap();
            GrimoireAudio.Play(GrimoireSound.School, _style);
        }

        /// <summary>Shows a node in the card. Does NOT buy it — the card's button does.</summary>
        public void SelectNode(SpellNode node)
        {
            if (_selected == node) return;
            _selected = node;
            RefreshCard();
            RefreshBoardSelection();
            GrimoireAudio.Play(GrimoireSound.Select, _style);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        protected override void OnSingletonAwake() => EnsureBuilt();

        protected override void OnDestroy()
        {
            Unbind();
            base.OnDestroy();
        }

        private void Update()
        {
            if (!IsOpen) return;

            float dt = Time.unscaledDeltaTime;
            TickOpenMotion(dt);
            TickSpirit(dt);
            TickNavigation();
            TickBoard(dt);
            if (_motes != null) _motes.Tick(dt);
        }

        // ── Model helpers ─────────────────────────────────────────────────

        private SpellTree ActiveTree()
        {
            if (grimoire == null || grimoire.Trees.Count == 0) return null;
            int index = Mathf.Clamp(_activeSchool, 0, grimoire.Trees.Count - 1);
            return grimoire.Trees[index];
        }

        /// <summary>
        /// The school to open on: the first the character has an affinity for, else the first
        /// there is. Affinity is the only fact in the data that says "this one is yours".
        /// </summary>
        private int PreferredSchool()
        {
            if (grimoire == null) return 0;
            var trees = grimoire.Trees;
            for (int i = 0; i < trees.Count; i++)
                if (trees[i] != null && trees[i].HasAffinity(grimoire.ClassKey)) return i;
            return 0;
        }

        private GrimoireNodeState StateOf(SpellTree tree, SpellNode node)
        {
            if (node == null || grimoire == null) return GrimoireNodeState.Malformed;
            _locks.Clear();
            bool learned = grimoire.IsNodeLearned(node);
            if (!learned) grimoire.CollectLockReasons(tree, node, playerLevel, _locks);
            return GrimoireNodeStatus.Resolve(learned, _locks);
        }

        /// <summary>Every reason the node is shut, on one line, in the player's language.</summary>
        private string ReasonOf(SpellTree tree, SpellNode node)
        {
            if (node == null || grimoire == null) return string.Empty;
            if (grimoire.IsNodeLearned(node)) return GrimoireText.Known;

            _locks.Clear();
            if (grimoire.CollectLockReasons(tree, node, playerLevel, _locks))
                return GrimoireText.Available;

            return GrimoireText.Reasons(_locks, _sb);
        }

        /// <summary>
        /// The FIRST reason only, for the caption under a node on the board.
        ///
        /// <para>The board and the card answer different questions and this is where that got
        /// decided. Captured live, the full composite — "Level 6 · Needs Dash" — wrapped to
        /// three lines under every node and the block fell across the socket below it; before
        /// that, unwrapped, three captions printed on top of each other. One short phrase fits
        /// on one line at a readable size, and it is the most structural of the reasons because
        /// the model collects them in the order the player can act on. The card, which has a
        /// column to itself, keeps the whole list.</para>
        /// </summary>
        private string ShortReasonOf(SpellTree tree, SpellNode node)
        {
            if (node == null || grimoire == null) return string.Empty;
            if (grimoire.IsNodeLearned(node)) return GrimoireText.Known;

            _locks.Clear();
            if (grimoire.CollectLockReasons(tree, node, playerLevel, _locks))
                return GrimoireText.Available;

            return _locks.Count > 0 ? GrimoireText.Reason(_locks[0]) : string.Empty;
        }

        /// <summary>Test seam — the active school as text, one line per node.</summary>
        public string ComputeListText()
        {
            var tree = ActiveTree();
            if (tree == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                sb.Append(node.ResolveDisplayName());
                sb.Append(" (");
                sb.Append(grimoire.ResolveCost(tree, node));
                sb.Append("): ");
                sb.Append(ReasonOf(tree, node));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Records what every node in the active school looks like RIGHT NOW, without emitting.
        /// Called when the panel opens and when the school changes, so the first refresh after
        /// either does not read a whole school as "everything just happened".
        /// </summary>
        private void SeedStateCache()
        {
            _lastStates.Clear();
            _lastPoints = grimoire != null ? grimoire.AvailablePoints : -1;

            var tree = ActiveTree();
            if (tree == null) return;
            foreach (var node in tree.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.nodeId)) continue;
                _lastStates[node.nodeId] = StateOf(tree, node);
            }
        }
    }
}
