// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ethar.DebugDrawingBBox.Editor
{
    /// <summary>
    /// One-click entry point for the drawing test: builds a fresh scene with a
    /// camera and the <see cref="DrawingTestHarness"/>, ready to Play. Lets us
    /// validate the box/marker/sphere/TMP drawing in the editor Game view without
    /// a device build.
    /// </summary>
    public static class DrawingTestMenu
    {
        [MenuItem("Tools/Ethar/Debug Drawing BBox/Open Drawing Test Scene")]
        public static void OpenDrawingTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var harness = new GameObject("QVS Drawing Test");
            harness.AddComponent<DrawingTestHarness>();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[QVS:DrawTest] Scene ready — press Play. Cyan/green/blue swatches = shader renders; magenta = broken shader; missing = not rendering.");
        }
    }
}
#endif
