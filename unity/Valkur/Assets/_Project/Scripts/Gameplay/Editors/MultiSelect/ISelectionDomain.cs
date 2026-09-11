using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// One kind of placed world content the Selection tool can pick up — buildings, particle
    /// emitters, authored lights — behind a single verb set.
    ///
    /// <para>EVERY IMPLEMENTATION DELEGATES TO ITS OWN EDITOR and owns no persistence of its
    /// own. That is the whole point of the layer: the Buildings editor already knows what a
    /// placement is, which fields a duplicate has to carry, when a save is refused inside an
    /// interior and how its anti-wipe guard reads a falling count. A domain here that placed
    /// or wrote content itself would be a second implementation of all of that, and the two
    /// would drift on the first field either one grows.</para>
    ///
    /// <para>WHY AN ADAPTER RATHER THAN AN INTERFACE ON <c>BuildingObject</c> / <c>ParticleEmitter</c>
    /// / the light: the pick rectangle is an EDITOR decision, not a property of the object. A
    /// light's grab box is not its illumination radius — one is how far it reaches, the other
    /// is how big a target the author needs — so putting <c>TryGetRect</c> on the domain type
    /// would be asserting something about it that only an editor has an opinion on.</para>
    ///
    /// <para>NOTHING HERE RECORDS UNDO. A group operation spans three domains and must be ONE
    /// undo step, so <c>MultiSelectRuntimeEditor</c> owns the <c>UndoStack</c> entry and calls
    /// these as its do/undo bodies. Each domain's own editor keeps its own history untouched;
    /// this tool's history is separate and is the only one that can span all three.</para>
    /// </summary>
    internal interface ISelectionDomain
    {
        /// <summary>Stable key used in status text, filters and the workspace record.</summary>
        string Id { get; }

        /// <summary>What the author sees on the filter chip. PLURAL, because a chip names a
        /// kind of thing rather than one of them.</summary>
        string Label { get; }

        /// <summary>
        /// The same noun for exactly one. DECLARED, never derived from <see cref="Label"/>.
        ///
        /// <para>Deriving it by dropping the final "s" is right for "edificios" and wrong for
        /// "luces", which is "luz" — measured live, the count line read "1 luce". A rule that
        /// is correct for two of three labels and silently wrong for the third is worse than
        /// three declarations, because the wrong one only appears when a selection happens to
        /// hold exactly one of that kind.</para>
        /// </summary>
        string LabelSingular { get; }

        /// <summary>The outline colour for this domain. One hue per domain is the only thing
        /// on screen saying WHAT a selected box is, since all three draw the same shape.</summary>
        Color Tint { get; }

        /// <summary>False when this domain's editor is absent from the scene. The tool then
        /// says so on its chip instead of silently selecting nothing.</summary>
        bool Available { get; }

        /// <summary>Everything selectable right now. Snapshot into the caller's buffer.</summary>
        void Collect(List<GameObject> buffer);

        /// <summary>The world-space box the author clicks and drag-selects with.</summary>
        bool TryGetRect(GameObject go, out Rect rect);

        /// <summary>
        /// A short name for one placement, for the selected-items list.
        ///
        /// <para>It has to identify THIS one among several of the same kind stacked on the
        /// same spot — which is the whole reason the list exists — so it carries the
        /// placement's own id or preset rather than just the domain's noun.</para>
        /// </summary>
        string Describe(GameObject go);

        /// <summary>Where this item is, for computing a group's relative layout.</summary>
        Vector3 PositionOf(GameObject go);

        /// <summary>Put it there. No undo, no save — the caller owns both.</summary>
        void MoveTo(GameObject go, Vector3 worldPos);

        /// <summary>
        /// Remove it and return a token that <see cref="Restore"/> can rebuild it from. The
        /// token is domain-private and deliberately opaque: a building comes back by being
        /// re-activated, an emitter by being rebuilt from a captured recipe, a light from a
        /// <c>LightSnapshot</c>, and no caller should be able to tell those apart.
        /// </summary>
        object Delete(GameObject go);

        /// <summary>Undo a <see cref="Delete"/>. Returns the live object, which may be a new
        /// instance (particles are genuinely destroyed) — so the caller must re-point at it.</summary>
        GameObject Restore(object token);

        /// <summary>Place a copy at <paramref name="worldPos"/>, carrying everything a
        /// placement is in this domain.</summary>
        GameObject Duplicate(GameObject go, Vector3 worldPos);

        /// <summary>
        /// Snapshot BY VALUE for the clipboard, and rebuild from that snapshot.
        ///
        /// <para>Distinct from <see cref="Duplicate"/>, which copies from a LIVE object. A
        /// clipboard outlives its source: the author copies a lamppost, deletes it, and pastes
        /// it back. A live reference would paste whatever the source had become by then, or
        /// throw once it was gone — the rule the Buildings clipboard already records.</para>
        ///
        /// <para>Also distinct from <see cref="Delete"/>'s token, which is only required to be
        /// restorable ONCE and in place: a building's delete token is the deactivated object
        /// itself, which cannot be pasted twice or pasted somewhere else.</para>
        /// </summary>
        object CaptureForClipboard(GameObject go);

        /// <summary>Build a fresh placement from a clipboard token. Called once per paste, so
        /// it must mint new identity (a new instance id, a new GUID) rather than reuse the
        /// source's — two placements under one id are one record on disk.</summary>
        GameObject SpawnFromClipboard(object token, Vector3 worldPos);

        /// <summary>
        /// The sprites this item draws, for the paste ghost, appended to
        /// <paramref name="buffer"/>. A domain that draws no sprite adds nothing and the ghost
        /// falls back to a translucent box in the domain colour — which is the honest picture
        /// for a light or an emitter, neither of which has a silhouette to preview.
        /// </summary>
        void CollectGhostSprites(GameObject go, List<SpriteRenderer> buffer);

        /// <summary>Drop a duplicate that is being rolled back. Distinct from
        /// <see cref="Delete"/> because it needs no token: the copy never reached the file.</summary>
        void DiscardDuplicate(GameObject go);

        /// <summary>
        /// Open this item's OWN editor, focused on this item, and arm Escape there to come
        /// back to <paramref name="returnTo"/>. False when that editor is not in the scene,
        /// so the tool can say so instead of a double-click doing nothing.
        ///
        /// <para>THE FOCUS IS APPLIED AFTER THE OPEN, in every implementation. Activation
        /// seeds the target editor's own mode and inspector, so a selection written first is
        /// overwritten by it and the author lands in a panel pointed at nothing — the same
        /// ordering constraint <c>GameEditorManager.OpenExclusive(target, returnTo)</c>
        /// carries for the return pointer itself.</para>
        /// </summary>
        bool OpenEditorFor(GameObject go, GameEditorManager.IGameEditor returnTo);

        /// <summary>
        /// Write this domain's file, once, at the end of a group operation.
        /// <paramref name="afterDeletion"/> tells the editor that a falling record count is
        /// expected, which its anti-wipe guard needs in order not to refuse the write.
        /// </summary>
        void Persist(bool afterDeletion);
    }
}
