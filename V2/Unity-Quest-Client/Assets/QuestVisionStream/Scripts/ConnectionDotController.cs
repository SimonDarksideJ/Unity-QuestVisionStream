// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using Ethar.DebugDrawingBBox;
using QuestVisionStream.Core;
using QuestVisionStream.Services;
using UnityEngine;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// The at-a-glance connection indicator: a small head-locked dot at the top
    /// right of the view — green while the connection status is healthy, red
    /// otherwise. Deliberately the ONLY non-tag world visual outside the log
    /// window (it renders no text; all messaging lives in the detection HUD).
    /// Created and parented to the camera by <see cref="QuestVisionStreamBootstrap"/>.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ConnectionDotController : MonoBehaviour
    {
        private static readonly Color ConnectedColor = new Color(0.2f, 0.85f, 0.35f);
        private static readonly Color ProblemColor = new Color(0.9f, 0.25f, 0.2f);

        private IStatusService status;
        private Material dotMaterial;
        private bool? lastConnected;

        public void Initialize(IStatusService statusService)
        {
            status = statusService;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dot.GetComponent<Collider>());
            dot.name = "ConnectionDot";
            dot.transform.SetParent(transform, false);
            dot.transform.localPosition = new Vector3(0.28f, 0.20f, 1.2f);
            dot.transform.localScale = Vector3.one * 0.012f;

            var dotRenderer = dot.GetComponent<MeshRenderer>();
            dotMaterial = UnlitMaterialFactory.Create(); // URP + XR safe
            dotRenderer.sharedMaterial = dotMaterial;
            dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dotRenderer.receiveShadows = false;
            UnlitMaterialFactory.SetColor(dotMaterial, ProblemColor);
        }

        private void Update()
        {
            if (status == null)
            {
                return;
            }

            var connection = status.Model.Get(StatusModel.Fields.Connection);
            var connected = connection.HasValue && connection.Value.Severity == StatusSeverity.Ok;
            if (lastConnected == connected)
            {
                return;
            }

            lastConnected = connected;
            UnlitMaterialFactory.SetColor(dotMaterial, connected ? ConnectedColor : ProblemColor);
        }

        private void OnDestroy()
        {
            if (dotMaterial != null)
            {
                Destroy(dotMaterial);
            }
        }
    }
}
