// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using QuestVisionStream.Core;
using QuestVisionStream.Services;
using UnityEngine;

namespace QuestVisionStream.Client
{
    /// <summary>
    /// In-headset status HUD, ported from the WebXR client's StatusSpriteSystem
    /// intent: a head-locked headline showing the single highest-priority problem
    /// (hidden entirely when healthy) plus a small green/red connection dot.
    /// Created and parented to the camera by the bootstrap.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class StatusHudController : MonoBehaviour
    {
        private IStatusService status;
        private TextMesh headline;
        private Material dotMaterial;
        private string lastHeadline = string.Empty;

        public void Initialize(IStatusService statusService)
        {
            status = statusService;

            // Headline text — ~1.2 m out, slightly below gaze, hidden when healthy.
            var headlineObject = new GameObject("Headline");
            headlineObject.transform.SetParent(transform, false);
            headlineObject.transform.localPosition = new Vector3(0, -0.18f, 1.2f);
            headline = headlineObject.AddComponent<TextMesh>();
            headline.characterSize = 0.02f;
            headline.fontSize = 48;
            headline.anchor = TextAnchor.MiddleCenter;
            headline.alignment = TextAlignment.Center;
            headline.color = new Color(1f, 0.55f, 0.35f);
            headline.text = string.Empty;

            // Connection dot — top-right of the view.
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
        }

        private void Update()
        {
            if (status == null)
            {
                return;
            }

            // Headline: re-render only on change; hide entirely when healthy.
            var current = status.Model.Headline() ?? string.Empty;
            if (current != lastHeadline)
            {
                lastHeadline = current;
                headline.text = current;
            }

            var connection = status.Model.Get(StatusModel.Fields.Connection);
            var connected = connection.HasValue &&
                            connection.Value.Severity == StatusSeverity.Ok;
            UnlitMaterialFactory.SetColor(dotMaterial, connected ? new Color(0.2f, 0.85f, 0.35f) : new Color(0.9f, 0.25f, 0.2f));
        }
    }
}
