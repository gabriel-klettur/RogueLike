using UnityEngine;
using Valkur.Core.Economy;
using Valkur.Core.Editors;

namespace Valkur.Gameplay.Editors.Economy
{
    /// <summary>
    /// Economy Editor — what it remembers between sessions.
    ///
    /// <para>The TAB, the selected vendor and the chart timeframe. All three are re-chosen on
    /// every open otherwise, and a tuning pass is exactly the workflow that reopens an editor
    /// twenty times: an author balancing the coin faucet who lands on the Market tab each time
    /// stops using the editor.</para>
    ///
    /// <para>Nothing here is destructive to restore — reopening on a tab only shows it. That
    /// matters because the workspace layer's rule is that no editor may reopen in a mode that
    /// destroys something, which is why Buildings refuses to restore Delete and Tile refuses
    /// its collider paint modes. This editor has no such mode: every field is undoable and
    /// nothing is applied by merely being looked at.</para>
    ///
    /// <para>The vendor is stored by KEY rather than by index. The list is rebuilt from the
    /// asset database on every open and its order can change — a re-import, a new vendor, a
    /// rename — and an index would silently select a DIFFERENT vendor than the one the author
    /// left open, which on a live-edit surface means their next keystroke edits the wrong
    /// asset. Same reasoning the Skills editor gives for storing a recipe id. A key that no
    /// longer resolves falls back to the first vendor, which is the safe failure.</para>
    ///
    /// <para>The FEED's on/off state is deliberately NOT here. It is a consent decision about
    /// this machine reaching the network, so it lives in PlayerPrefs
    /// (<c>BitcoinPriceService.EnabledPrefKey</c>) where it survives a workspace reset and is
    /// not carried around inside a document that describes panel layout.</para>
    /// </summary>
    public partial class EconomyRuntimeEditor : IProvidesWorkspaceState
    {
        private const string WS_TAB = "activeTab";
        private const string WS_VENDOR = "selectedVendorKey";
        private const string WS_TIMEFRAME = "btcTimeframe";

        public Transform WorkspaceRoot => _root != null ? _root.transform : null;

        public void CaptureWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;
            ws.SetInt(WS_TAB, (int)_tab);
            ws.SetInt(WS_TIMEFRAME, (int)_timeframe);

            var vendor = SelectedVendor;
            ws.SetString(WS_VENDOR, vendor != null ? vendor.vendorKey : string.Empty);
        }

        public void RestoreWorkspace(EditorWorkspace ws)
        {
            if (ws == null) return;

            // Clamped through the enum's own range rather than cast blind: a document written
            // by a build with more tabs than this one would otherwise select a tab that does
            // not exist, and the body would build nothing at all with no error.
            int tab = ws.GetInt(WS_TAB, (int)Tab.Market);
            _tab = tab >= 0 && tab <= (int)Tab.Bitcoin ? (Tab)tab : Tab.Market;

            int tf = ws.GetInt(WS_TIMEFRAME, (int)BitcoinTimeframe.Day1);
            _timeframe = tf >= 0 && tf <= (int)BitcoinTimeframe.Day1
                ? (BitcoinTimeframe)tf : BitcoinTimeframe.Day1;

            _pendingVendorKey = ws.GetString(WS_VENDOR, null);
        }

        /// <summary>
        /// The vendor key read from the workspace, held until <c>ResolveContent</c> has built
        /// the list it has to be matched against.
        ///
        /// <para>Restore runs before Activate, so the list is empty at the moment the key
        /// arrives — resolving it there would always miss and always fall back to the first
        /// vendor, which is the shape of a restore that looks implemented and does nothing.</para>
        /// </summary>
        private string _pendingVendorKey;

        /// <summary>
        /// Turn a remembered vendor key into an index, once the list exists. Called from
        /// <c>ResolveContent</c>.
        /// </summary>
        private void ApplyPendingVendorSelection()
        {
            if (string.IsNullOrEmpty(_pendingVendorKey)) return;

            for (int i = 0; i < _vendors.Count; i++)
            {
                if (_vendors[i] != null && _vendors[i].vendorKey == _pendingVendorKey)
                {
                    _selectedVendor = i;
                    break;
                }
            }
            _pendingVendorKey = null;
        }
    }
}
