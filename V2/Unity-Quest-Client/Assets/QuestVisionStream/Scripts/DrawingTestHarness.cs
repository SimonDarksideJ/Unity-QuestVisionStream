// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using QuestVisionStream.Core;
using TMPro;
using UnityEngine;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// A self-contained drawing test board. Drop it on a GameObject in an empty
    /// scene (or use <c>Tools ▸ QuestVisionStream ▸ Open Drawing Test Scene</c>) and
    /// press Play in the editor Game view — no device build needed.
    ///
    /// It lays out, side by side, the exact primitives the detection renderers use
    /// (LineRenderer outline, marker quad, tag sphere, TMP label) plus a row of
    /// cubes each on a DIFFERENT shader. So a rendering fault reproduces instantly
    /// and legibly here:
    ///   - a swatch that shows its assigned colour  → that shader renders,
    ///   - a swatch that shows MAGENTA/pink         → that shader's variant is broken,
    ///   - a swatch that shows nothing              → not rendering at all.
    /// Colours are deliberately non-magenta so the pink error shader is unmistakable.
    /// </summary>
    [AddComponentMenu("QuestVisionStream/Drawing Test Harness")]
    public sealed class DrawingTestHarness : MonoBehaviour
    {
        private void Start()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camObject = new GameObject("Test Camera") { tag = "MainCamera" };
                cam = camObject.AddComponent<Camera>();
                camObject.AddComponent<AudioListener>();
            }

            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -4f), Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.15f); // dark: colours and pink both pop

            // --- Row 1: the same shader on a plain cube, several shaders compared ---
            var shaderNames = new[]
            {
                "Universal Render Pipeline/Unlit", // what UnlitMaterialFactory targets
                "Universal Render Pipeline/Lit",   // known-good URP sanity swatch
                "Sprites/Default",                 // the original detection-visual shader
                "Unlit/Color",                     // built-in fallback
            };
            var swatchColours = new[] { Color.cyan, Color.green, Color.yellow, new Color(1f, 0.55f, 0f) };

            var x = -2.7f;
            for (var i = 0; i < shaderNames.Length; i++)
            {
                var shader = Shader.Find(shaderNames[i]);
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = new Vector3(x, 0.7f, 0f);
                cube.transform.localScale = Vector3.one * 0.55f;
                var material = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
                Tint(material, swatchColours[i]);
                cube.GetComponent<MeshRenderer>().sharedMaterial = material;

                var shortName = shaderNames[i].Substring(shaderNames[i].LastIndexOf('/') + 1);
                MakeLabel(new Vector3(x, 1.25f, 0f), $"{shortName}\n{(shader != null ? "found" : "NULL")}");
                Debug.Log($"[QVS:DrawTest] Shader.Find(\"{shaderNames[i]}\") = {(shader != null ? shader.name : "NULL")}");
                x += 1.8f;
            }

            // --- Row 2: the ACTUAL detection-visual paths, via UnlitMaterialFactory ---

            // LineRenderer box (the ephemeral-box outline path)
            var lineObject = new GameObject("TestLineBox");
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.widthMultiplier = 0.03f;
            var lineMaterial = UnlitMaterialFactory.Create();
            UnlitMaterialFactory.SetColor(lineMaterial, Color.red);
            line.material = lineMaterial;
            var boxCentre = new Vector3(-2.0f, -1.0f, 0f);
            line.positionCount = 5;
            line.SetPositions(new[]
            {
                boxCentre + new Vector3(-0.5f, -0.35f, 0f),
                boxCentre + new Vector3(0.5f, -0.35f, 0f),
                boxCentre + new Vector3(0.5f, 0.35f, 0f),
                boxCentre + new Vector3(-0.5f, 0.35f, 0f),
                boxCentre + new Vector3(-0.5f, -0.35f, 0f),
            });
            MakeLabel(boxCentre + new Vector3(0f, 0.7f, 0f), "LineRenderer\n(factory)");

            // Center-marker quad
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.position = new Vector3(-0.4f, -1.0f, 0f);
            quad.transform.localScale = Vector3.one * 0.4f;
            var quadMaterial = UnlitMaterialFactory.Create();
            UnlitMaterialFactory.SetColor(quadMaterial, Color.green);
            quad.GetComponent<MeshRenderer>().sharedMaterial = quadMaterial;
            MakeLabel(new Vector3(-0.4f, -0.5f, 0f), "Quad\n(factory)");

            // Anchored-tag sphere
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = new Vector3(1.0f, -1.0f, 0f);
            sphere.transform.localScale = Vector3.one * 0.4f;
            var sphereMaterial = UnlitMaterialFactory.Create();
            UnlitMaterialFactory.SetColor(sphereMaterial, new Color(0.3f, 0.5f, 1f));
            sphere.GetComponent<MeshRenderer>().sharedMaterial = sphereMaterial;
            MakeLabel(new Vector3(1.0f, -0.5f, 0f), "Sphere\n(factory)");

            // TMP label sample (the label path we plan to migrate to)
            MakeLabel(new Vector3(2.4f, -1.0f, 0f), "TMP text\nperson 82%");
        }

        private static void MakeLabel(Vector3 position, string text)
        {
            var labelObject = new GameObject("Label");
            labelObject.transform.position = position;
            var tmp = labelObject.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 3f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(6f, 3f);
        }

        private static void Tint(Material material, Color color)
        {
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
