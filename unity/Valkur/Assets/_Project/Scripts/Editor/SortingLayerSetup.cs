using UnityEditor;
using UnityEngine;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor utility to verify and create the required Sorting Layers for the project.
    /// Maps to Python's Z_LAYERS and Layer enum for proper render ordering.
    /// 
    /// Run via menu: Valkur > Setup > Verify Sorting Layers
    /// </summary>
    public static class SortingLayerSetup
    {
        private static readonly string[] RequiredSortingLayers = new[]
        {
            "Background",
            "Ground",
            "FloorDecals",
            "ObjectsLow",
            "WallsBottom",
            "Entities",
            "Decorations",
            "WallsTop",
            "ObjectsHigh",
            "Overhead",
            "UIWorld"
        };

        [MenuItem("Valkur/Setup/Verify Sorting Layers")]
        public static void VerifySortingLayers()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var sortingLayers = tagManager.FindProperty("m_SortingLayers");

            int added = 0;
            foreach (string layerName in RequiredSortingLayers)
            {
                if (!SortingLayerExists(sortingLayers, layerName))
                {
                    AddSortingLayer(sortingLayers, layerName);
                    added++;
                }
            }

            if (added > 0)
            {
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[SortingLayerSetup] Added {added} sorting layers. Total required: {RequiredSortingLayers.Length}.");
            }
            else
            {
                Debug.Log("[SortingLayerSetup] All required sorting layers already exist.");
            }
        }

        private static bool SortingLayerExists(SerializedProperty sortingLayers, string name)
        {
            for (int i = 0; i < sortingLayers.arraySize; i++)
            {
                var element = sortingLayers.GetArrayElementAtIndex(i);
                var nameProperty = element.FindPropertyRelative("name");
                if (nameProperty != null && nameProperty.stringValue == name)
                    return true;
            }
            return false;
        }

        private static void AddSortingLayer(SerializedProperty sortingLayers, string name)
        {
            sortingLayers.InsertArrayElementAtIndex(sortingLayers.arraySize);
            var newLayer = sortingLayers.GetArrayElementAtIndex(sortingLayers.arraySize - 1);
            newLayer.FindPropertyRelative("name").stringValue = name;
            newLayer.FindPropertyRelative("uniqueID").intValue = GenerateUniqueID(sortingLayers);
        }

        private static int GenerateUniqueID(SerializedProperty sortingLayers)
        {
            int maxId = 0;
            for (int i = 0; i < sortingLayers.arraySize; i++)
            {
                var element = sortingLayers.GetArrayElementAtIndex(i);
                var idProp = element.FindPropertyRelative("uniqueID");
                if (idProp != null && idProp.intValue > maxId)
                    maxId = idProp.intValue;
            }
            return maxId + 1;
        }
    }
}
