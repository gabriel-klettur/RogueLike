using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.World
{
    public sealed partial class BuildingColliderDebugOverlay : MonoBehaviour
    {

        private void CleanupOrphanedVisualRoots()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(VISUAL_PREFIX)) continue;

                bool tracked = false;
                for (int j = 0; j < _visuals.Count; j++)
                {
                    if (_visuals[j] != null && _visuals[j].Host == child.gameObject)
                    {
                        tracked = true;
                        break;
                    }
                }

                if (tracked) continue;
                child.gameObject.SetActive(false);
                DestroyUnityObject(child.gameObject);
            }
        }

        private void SetAllDebugRootsActive(bool active)
        {
            SetVisualsActive(active);
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith(VISUAL_PREFIX))
                    child.gameObject.SetActive(active);
            }
        }

        private static Vector3 GetInverseLossyScale(Transform target)
        {
            Vector3 lossy = target != null ? target.lossyScale : Vector3.one;
            return new Vector3(
                Mathf.Abs(lossy.x) > 0.0001f ? 1f / lossy.x : 1f,
                Mathf.Abs(lossy.y) > 0.0001f ? 1f / lossy.y : 1f,
                Mathf.Abs(lossy.z) > 0.0001f ? 1f / lossy.z : 1f);
        }

        private static void EnsureSharedAssets()
        {
            if (s_whiteSprite == null)
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.hideFlags = HideFlags.HideAndDontSave;
                s_whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                s_whiteSprite.hideFlags = HideFlags.HideAndDontSave;
            }

            if (s_lineMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    s_lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            if (s_fillMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    s_fillMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        private static void DestroyUnityObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}