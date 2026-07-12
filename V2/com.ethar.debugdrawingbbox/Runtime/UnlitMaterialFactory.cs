// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using UnityEngine;

namespace Ethar.DebugDrawingBBox
{
    /// <summary>
    /// Builds an unlit material that actually renders the world-space detection
    /// visuals (LineRenderer outlines, marker quads, tag spheres, the status dot)
    /// on the ACTIVE render pipeline.
    ///
    /// Those visuals were originally created with <c>Shader.Find("Sprites/Default")</c>.
    /// That built-in shader does not render on 3D renderers under URP with
    /// single-pass-instanced stereo (the exact symptom seen on device: the geometry
    /// is computed and "drawn" every frame per the logs, yet nothing is visible,
    /// while the uGUI HUD — which uses URP-compatible UI shaders — shows fine).
    /// URP's own Unlit shader renders correctly in stereo; the built-in shaders
    /// remain as fallbacks so the editor, the built-in pipeline and edit-mode tests
    /// still work.
    /// </summary>
    public static class UnlitMaterialFactory
    {
        /// <summary>Resources path (no extension) of the shipped URP/Unlit material asset.</summary>
        public const string ResourceMaterialPath = "QuestVisionStream/DetectionUnlit";

        private static bool shaderLogged;

        /// <summary>A fresh unlit material that renders on the device build.</summary>
        public static Material Create()
        {
            // Prefer a MATERIAL ASSET shipped in Resources. This is the crux of the
            // device fix: shipping a material asset is what forces the URP/Unlit
            // shader variants — including the XR single-pass-instanced (stereo)
            // variant — into the build. A material built purely from Shader.Find has
            // no variants collected at build time, so on device the draw falls back
            // to the magenta error shader even though Shader.Find succeeds. (The
            // editor keeps every variant, which is why it renders fine there.)
            var source = Resources.Load<Material>(ResourceMaterialPath);
            if (source != null)
            {
                if (!shaderLogged)
                {
                    shaderLogged = true;
                    Debug.Log($"[QVS:Render] Detection material from Resources '{ResourceMaterialPath}' (shader '{source.shader.name}')");
                }

                return new Material(source);
            }

            // Fallback: editor before the asset is generated, edit-mode tests, or the
            // built-in pipeline. Renders in-editor; may strip on device (hence the asset).
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            if (!shaderLogged)
            {
                shaderLogged = true;
                Debug.LogWarning($"[QVS:Render] Resources material '{ResourceMaterialPath}' MISSING — fell back to Shader.Find('{(shader != null ? shader.name : "<null>")}'). This is the config that strips on device; run Tools > Ethar > Debug Drawing BBox > Regenerate Detection Material.");
            }

            return new Material(shader);
        }

        /// <summary>
        /// Tint a material whichever colour property its shader exposes — URP Unlit
        /// uses <c>_BaseColor</c>, the built-in sprite/unlit shaders use <c>_Color</c>.
        /// Setting the absent one is harmless.
        /// </summary>
        public static void SetColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }
    }
}
