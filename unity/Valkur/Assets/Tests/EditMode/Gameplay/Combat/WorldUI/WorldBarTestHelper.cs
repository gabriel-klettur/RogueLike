using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
namespace Valkur.Tests.EditMode.Gameplay.Combat.WorldUI
{
    internal static class WorldBarTestHelper
    {
        /// <summary>
        /// Unity does not call <c>Awake</c> on a component added in Edit Mode, so a driver has to
        /// be started by hand. Kept from the original fixture, which needed it for the same reason.
        /// </summary>
        public static void InvokeAwake(Component c) => Invoke(c, "Awake");

        public static void InvokeOnEnable(Component c) => Invoke(c, "OnEnable");

        public static void InvokeLateUpdate(Component c) => Invoke(c, "LateUpdate");

        public static void Invoke(Component c, string method)
        {
            if (c == null) return;
            var m = c.GetType().GetMethod(method,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            m?.Invoke(c, null);
        }

        /// <summary>Every renderer the rig owns, and nothing else on the entity.</summary>
        public static List<SpriteRenderer> BarRenderers(GameObject go)
        {
            var found = new List<SpriteRenderer>();
            var root = go.transform.Find("WorldBars");
            if (root == null) return found;
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                found.Add(sr);
            return found;
        }

        /// <summary>An entity with a measurable body, the way a spawned creature has one.</summary>
        public static GameObject MakeEntity(string name, float bodyWidth = 1.2f, float bodyHeight = 1.86f)
        {
            var go = new GameObject(name);
            var sr = go.AddComponent<SpriteRenderer>();
            int w = Mathf.Max(1, Mathf.RoundToInt(bodyWidth * 16f));
            int h = Mathf.Max(1, Mathf.RoundToInt(bodyHeight * 16f));
            var tex = new Texture2D(w, h) { hideFlags = HideFlags.HideAndDontSave };
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 16f,
                                      0, SpriteMeshType.FullRect);
            sr.sprite.hideFlags = HideFlags.HideAndDontSave;
            return go;
        }
    }
}
