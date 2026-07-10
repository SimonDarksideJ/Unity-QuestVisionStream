// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System.Threading.Tasks;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace QuestVisionStream.Client
{
    /// <summary>
    /// Depth-anchored ("advanced") mode enabler: ensures the room has been scanned so
    /// the anchored renderer can raycast real geometry for true per-object depth.
    ///
    /// The scene DATA comes through AR Foundation (<see cref="ARMeshManager"/> — the
    /// Meta OpenXR provider surfaces the scanned room mesh with colliders in the SAME
    /// tracking space as the detection camera, so <c>AnchoredTagRenderModule</c>'s
    /// existing <c>Physics.Raycast</c> hits it unchanged). The scan is only TRIGGERED
    /// through the Meta SDK (<see cref="OVRScene.RequestSpaceSetup"/>), a system UI
    /// operation. Flow (the requested workflow):
    ///   1. ensure the USE_SCENE permission,
    ///   2. check for scene geometry,
    ///   3. if none, launch Space Setup, then re-check — looping until it appears.
    ///
    /// The simple/quick mode never calls this — it keeps the fixed-depth fallback.
    /// NOTE: first integration cut — needs on-device verification (scene mesh
    /// availability, collider baking, permission timing).
    /// </summary>
    [AddComponentMenu("")]
    public sealed class RoomScanController : MonoBehaviour
    {
        public enum ScanState { Idle, RequestingPermission, WaitingForScene, Scanning, Ready, Unavailable }

        private const string ScenePermission = "com.oculus.permission.USE_SCENE";

        public ScanState State { get; private set; } = ScanState.Idle;

        private GameObject managerHost;
        private ARMeshManager meshManager;
        private bool running;

        public void Initialize(XROrigin origin)
        {
            // Scene managers live under the XR Origin (trackables are parented there),
            // disabled until the advanced mode actually asks for a scan.
            managerHost = new GameObject("QVS_SceneManagers");
            managerHost.transform.SetParent(origin != null ? origin.transform : transform, false);

            meshManager = managerHost.AddComponent<ARMeshManager>();

            // Collider-only mesh chunk template: the room mesh becomes Physics geometry
            // for the anchored raycast, without drawing over the passthrough view.
            var chunk = new GameObject("QVS_SceneMeshChunk");
            chunk.AddComponent<MeshFilter>();
            chunk.AddComponent<MeshCollider>();
            chunk.SetActive(false);
            meshManager.meshPrefab = chunk.GetComponent<MeshFilter>();

            managerHost.SetActive(false);
        }

        /// <summary>
        /// Ensure scene geometry is present, scanning if needed. Safe to call on every
        /// switch into the anchored mode; it no-ops once ready or while already running.
        /// </summary>
        public async void EnsureSceneReady()
        {
            if (running || State == ScanState.Ready || meshManager == null)
            {
                return;
            }

            running = true;
            try
            {
                if (!await EnsurePermission())
                {
                    SetState(ScanState.Unavailable, "scene permission denied");
                    return;
                }

                if (managerHost != null)
                {
                    managerHost.SetActive(true); // start meshing
                }

                SetState(ScanState.WaitingForScene, "loading existing scene");
                if (await WaitForScene(5f))
                {
                    SetState(ScanState.Ready, "scene geometry available");
                    return;
                }

                // No scene for this room yet — launch the system Space Setup scan.
                SetState(ScanState.Scanning, "prompting room scan (Space Setup)");
                await OVRScene.RequestSpaceSetup();

                SetState(ScanState.WaitingForScene, "loading scanned scene");
                SetState(await WaitForScene(10f) ? ScanState.Ready : ScanState.Unavailable,
                    State == ScanState.Ready ? "scene geometry available" : "no scene geometry after scan");
            }
            catch (System.Exception e)
            {
                SetState(ScanState.Unavailable, $"error: {e.Message}");
            }
            finally
            {
                running = false;
            }
        }

        private async Task<bool> WaitForScene(float seconds)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (HasSceneGeometry())
                {
                    return true;
                }

                await Task.Delay(250);
            }

            return HasSceneGeometry();
        }

        private bool HasSceneGeometry() => meshManager != null && meshManager.meshes.Count > 0;

        private static async Task<bool> EnsurePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(ScenePermission))
            {
                return true;
            }

            Permission.RequestUserPermission(ScenePermission);

            // No reliable grant callback across OS versions — poll briefly.
            for (var i = 0; i < 60 && !Permission.HasUserAuthorizedPermission(ScenePermission); i++)
            {
                await Task.Delay(250);
            }

            return Permission.HasUserAuthorizedPermission(ScenePermission);
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        private void SetState(ScanState next, string reason)
        {
            State = next;
            Debug.Log($"[QVS:RoomScan] {next} — {reason}");
        }
    }
}
