using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>One selected thing: the object, and which domain owns it.</summary>
    internal readonly struct SelectionItem
    {
        public readonly ISelectionDomain Domain;
        public readonly GameObject       Go;

        public SelectionItem(ISelectionDomain domain, GameObject go)
        {
            Domain = domain;
            Go     = go;
        }

        public bool IsAlive => Domain != null && Go != null;
    }

    /// <summary>
    /// What the Selection tool currently has picked, across all three domains, in the order it
    /// was picked. Modelled directly on <c>BuildingSelectionSet</c> and for the same reasons —
    /// the LAST entry is the PRIMARY, so clicking a fourth thing makes it primary without
    /// unselecting the other three, and a re-click MOVES an existing member to the end rather
    /// than duplicating it.
    ///
    /// <para>MEMBERSHIP IS NOT ELIGIBILITY. A member can be deleted, destroyed or unloaded
    /// between the pick and the operation — a world swap tears every one of them down — and
    /// nothing tells this set. <see cref="Prune"/> is the answer and it runs before anything
    /// reads the set for a group operation, because a group move that walked a destroyed
    /// entry would throw halfway through its own undo record. Same rule
    /// <c>AlliedUnit.Live</c> and <c>BuildingSelectionSet</c> both record.</para>
    /// </summary>
    internal sealed class MultiSelectSet
    {
        private readonly List<SelectionItem> _items = new List<SelectionItem>(32);

        public int Count => _items.Count;

        /// <summary>Snapshot, oldest first, primary last. Never the backing list — a caller
        /// looping it while an operation removes members would skip entries silently.</summary>
        public IReadOnlyList<SelectionItem> Items => _items.ToArray();

        public SelectionItem Primary => _items.Count == 0 ? default : _items[_items.Count - 1];

        public bool Contains(GameObject go)
        {
            if (go == null) return false;
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Go == go) return true;
            return false;
        }

        /// <summary>Add as primary; an existing member is MOVED to the end rather than
        /// duplicated. True when it was not a member before.</summary>
        public bool Add(ISelectionDomain domain, GameObject go)
        {
            if (domain == null || go == null) return false;
            bool wasNew = !RemoveInternal(go);
            _items.Add(new SelectionItem(domain, go));
            return wasNew;
        }

        public bool Remove(GameObject go) => RemoveInternal(go);

        /// <summary>Toggle membership. True when it is selected AFTER the call.</summary>
        public bool Toggle(ISelectionDomain domain, GameObject go)
        {
            if (domain == null || go == null) return false;
            if (RemoveInternal(go)) return false;
            _items.Add(new SelectionItem(domain, go));
            return true;
        }

        public void Clear() => _items.Clear();

        /// <summary>Drop the entry holding <paramref name="go"/>, whichever domain owns it.
        /// True when one was there. Keyed on the OBJECT rather than on a (domain, object)
        /// pair: the same GameObject can only ever belong to one domain, and asking for both
        /// would let a caller fail to remove something by naming the wrong half.</summary>
        private bool RemoveInternal(GameObject go)
        {
            if (go == null) return false;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Go != go) continue;
                _items.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>Drop members whose object is gone. Returns how many were dropped.</summary>
        public int Prune()
        {
            int removed = 0;
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                if (_items[i].IsAlive) continue;
                _items.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        /// <summary>How many members belong to <paramref name="domainId"/>. Feeds the status
        /// line, which is the only thing on screen that says a group spans three files.</summary>
        public int CountIn(string domainId)
        {
            int n = 0;
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].Domain != null && _items[i].Domain.Id == domainId) n++;
            return n;
        }

        /// <summary>
        /// Re-point a member at a new object, keeping its position in the order.
        ///
        /// <para>Needed because a restored PARTICLE is genuinely a new GameObject — its
        /// domain destroys rather than deactivates — so an undo that did not re-point would
        /// leave the set holding a destroyed reference, which <see cref="Prune"/> would then
        /// silently drop. Buildings and lights come back as the same object and this is a
        /// no-op for them.</para>
        /// </summary>
        public void Repoint(GameObject oldGo, GameObject newGo)
        {
            if (newGo == null) return;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Go != oldGo) continue;
                _items[i] = new SelectionItem(_items[i].Domain, newGo);
                return;
            }
        }
    }
}
