using UnityEngine;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Diagnostic logging for the tile editor brush system.
    /// Extracted from TileEditorManager to isolate debug concerns.
    /// </summary>
    public static class TileEditorDiagnostics
    {
        public static void LogBrushDiagnostics(MonoBehaviour context, Tilemap tilemap, Vector3Int cellPos, TileBase tile)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== [TileEditor] BRUSH DIAGNOSTICS (first paint) ===");

            sb.AppendLine($"  tile={tile?.name ?? "NULL"} type={tile?.GetType().Name ?? "?"}");
            if (tile is Tile t)
            {
                var spr = t.sprite;
                sb.AppendLine($"  sprite={spr?.name ?? "NULL"} spriteNull={spr == null}");
                if (spr != null)
                {
                    sb.AppendLine($"  sprite.texture={spr.texture?.name ?? "NULL"} texNull={spr.texture == null}");
                    if (spr.texture != null)
                        sb.AppendLine($"  texSize={spr.texture.width}x{spr.texture.height} ppu={spr.pixelsPerUnit}");
                }
                sb.AppendLine($"  tile.color={t.color}");
            }

            sb.AppendLine($"  tilemap={tilemap.name} cellPos={cellPos}");
            var renderer = tilemap.GetComponent<TilemapRenderer>();
            if (renderer != null)
            {
                sb.AppendLine($"  renderer.enabled={renderer.enabled}");
                sb.AppendLine($"  sortingLayer={renderer.sortingLayerName} sortingOrder={renderer.sortingOrder}");
                var mat = renderer.sharedMaterial;
                sb.AppendLine($"  material={mat?.name ?? "NULL"} shader={mat?.shader?.name ?? "NULL"}");
            }
            else
            {
                sb.AppendLine("  renderer=NULL (no TilemapRenderer!)");
            }

            var light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType != null)
            {
                var lights = Object.FindObjectsOfType(light2DType);
                sb.AppendLine($"  Light2D count={lights.Length}");

                var ltProp = light2DType.GetProperty("lightType",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var ltField = light2DType.GetField("m_LightType",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var intProp = light2DType.GetProperty("intensity",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                foreach (var l in lights)
                {
                    var go = ((Component)l).gameObject;
                    string ltVal = "?";
                    if (ltProp != null)
                    {
                        try { ltVal = $"prop={ltProp.GetValue(l)} ({(int)ltProp.GetValue(l)})"; }
                        catch { ltVal = "prop-read-error"; }
                    }
                    else if (ltField != null)
                    {
                        try { ltVal = $"field={ltField.GetValue(l)} ({(int)ltField.GetValue(l)})"; }
                        catch { ltVal = "field-read-error"; }
                    }
                    else
                    {
                        ltVal = "NO_PROP_OR_FIELD";
                    }

                    string intVal = "?";
                    if (intProp != null)
                    {
                        try { intVal = intProp.GetValue(l)?.ToString(); }
                        catch { intVal = "read-error"; }
                    }

                    sb.AppendLine($"    Light2D: '{go.name}' active={go.activeInHierarchy} lightType={ltVal} intensity={intVal}");
                }
            }
            else
            {
                sb.AppendLine("  Light2D type NOT FOUND (URP 2D Renderer missing?)");
            }

            sb.AppendLine("=== END DIAGNOSTICS ===");
            Debug.Log(sb.ToString());
        }
    }
}
