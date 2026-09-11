using System.Collections.Generic;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// The set of placed buildings the Buildings editor currently has selected, in the
    /// order they were picked. The LAST one is the PRIMARY — the one the inspector, the
    /// split handle and the resize handle work on — so "click a fourth building" makes it
    /// primary without unselecting the other three.
    ///
    /// <para>Pure and ordered on purpose. The editor used to have ONE selection field
    /// (<c>_activeBuilding</c>) written from eleven places; a multi-selection bolted onto
    /// that as a second list would have been two answers to "what is selected" that the
    /// eleven writers keep in step by hand. This type is the set, <c>_activeBuilding</c>
    /// stays the primary, and <c>BuildingsRuntimeEditor.Selection</c> reconciles the two
    /// once per frame from ONE place rather than at every writer.</para>
    ///
    /// <para>Membership is not eligibility: a member may have been deleted (the editor
    /// deletes by <c>SetActive(false)</c>) or destroyed (undoing an erase) since it was
    /// picked, and nothing tells the set. <see cref="Prune"/> is the answer, and it is
    /// called before anything reads the set for a group operation — a group delete that
    /// walked a destroyed entry would throw halfway through its own undo record.</para>
    /// </summary>
    public sealed class BuildingSelectionSet
    {
        private readonly List<BuildingObject> _items = new List<BuildingObject>();

        public int Count => _items.Count;

        /// <summary>Snapshot, oldest first, primary last. Never the backing list.</summary>
        public IReadOnlyList<BuildingObject> Items => _items.ToArray();

        /// <summary>The most recently picked member, or null when empty.</summary>
        public BuildingObject Primary => _items.Count == 0 ? null : _items[_items.Count - 1];

        public bool Contains(BuildingObject b) => b != null && _items.Contains(b);

        /// <summary>
        /// Add <paramref name="b"/> as the primary. An existing member is MOVED to the end
        /// rather than duplicated, so "click a building already in the group" promotes it.
        /// Returns true when the building was not a member before.
        /// </summary>
        public bool Add(BuildingObject b)
        {
            if (b == null) return false;
            bool wasNew = !_items.Remove(b);
            _items.Add(b);
            return wasNew;
        }

        public bool Remove(BuildingObject b) => b != null && _items.Remove(b);

        /// <summary>Toggle membership. Returns true when <paramref name="b"/> is selected
        /// AFTER the call.</summary>
        public bool Toggle(BuildingObject b)
        {
            if (b == null) return false;
            if (_items.Remove(b)) return false;
            _items.Add(b);
            return true;
        }

        public void Clear() => _items.Clear();

        /// <summary>Keep only <paramref name="b"/> (or nothing, for null). This is what
        /// switching from Multiple to Simple scope does: the primary survives, the rest
        /// are dropped.</summary>
        public void CollapseTo(BuildingObject b)
        {
            _items.Clear();
            if (b != null) _items.Add(b);
        }

        /// <summary>
        /// Drop every member that is destroyed or deactivated. Returns how many went.
        /// Deactivated counts as gone here — unlike the allied-unit registry, where a
        /// deactivated member is merely ineligible — because <c>SetActive(false)</c> IS
        /// this editor's delete, and a selection holding a deleted building would move,
        /// copy and re-delete a thing the author can no longer see.
        /// </summary>
        public int Prune()
        {
            int removed = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var b = _items[i];
                if (b == null || !b.gameObject.activeInHierarchy)
                {
                    _items.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }
    }
}
