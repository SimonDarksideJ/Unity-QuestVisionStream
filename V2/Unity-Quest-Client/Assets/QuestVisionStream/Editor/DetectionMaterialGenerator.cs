// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

#if UNITY_EDITOR
using System.IO;
using QuestVisionStream.Core;
using UnityEditor;
using UnityEngine;

namespace QuestVisionStream.Client.Editor
{
    /// <summary>
    /// Ensures a URP/Unlit material asset exists in a Resources folder. This is the
    /// device fix for the invisible/magenta detection visuals: shipping the material
    /// as an ASSET forces the URP/Unlit shader variants (crucially the XR
    /// single-pass-instanced stereo variant) into the player build's shader set, so
    /// the runtime materials produced by <see cref="UnlitMaterialFactory"/> render
    /// instead of falling back to the magenta error shader.
    ///
    /// Runs automatically when the editor loads/recompiles; also available via the
    /// menu. Safe to re-run — it only creates the asset when missing.
    /// </summary>
    [InitializeOnLoad]
    public static class DetectionMaterialGenerator
    {
        private const string ResourceDirectory = "Assets/QuestVisionStream/Resources/QuestVisionStream";
        private static readonly string MaterialAssetPath = $"{ResourceDirectory}/DetectionUnlit.mat";

        static DetectionMaterialGenerator()
        {
            // Defer: asset creation is not allowed during the InitializeOnLoad phase.
            EditorApplication.delayCall += EnsureMaterialExists;
        }

        [MenuItem("Tools/QuestVisionStream/Regenerate Detection Material")]
        public static void EnsureMaterialExists()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath) != null)
            {
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[QVS] 'Universal Render Pipeline/Unlit' shader not found — cannot generate the detection material. Is URP installed?");
                return;
            }

            if (!Directory.Exists(ResourceDirectory))
            {
                Directory.CreateDirectory(ResourceDirectory);
            }

            var material = new Material(shader) { name = "DetectionUnlit" };
            AssetDatabase.CreateAsset(material, MaterialAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[QVS] Generated {MaterialAssetPath} — URP/Unlit variants will now ship in device builds (loaded at runtime as '{UnlitMaterialFactory.ResourceMaterialPath}'). Commit this asset.");
        }
    }
}
#endif
