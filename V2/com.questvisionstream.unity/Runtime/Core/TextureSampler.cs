// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestVisionStream.Core
{
    /// <summary>
    /// Asynchronous CPU sampling of any texture at a fixed size: blit → RGBA32 RT →
    /// AsyncGPUReadback → <c>Color32[]</c>. Shared by the image qualifier (tiny
    /// frames) and the AprilTag detector (stream-sized frames). Never more than
    /// one readback in flight per sampler — callers that tick faster simply skip.
    /// </summary>
    public sealed class TextureSampler : IDisposable
    {
        private RenderTexture target;
        private int width;
        private int height;
        private bool inFlight;

        /// <summary>
        /// Request a sample; <paramref name="onPixels"/> fires on the main thread when
        /// the GPU delivers. Returns false when skipped (readback in flight / no source).
        /// </summary>
        public bool TrySample(Texture source, int sampleWidth, int sampleHeight, Action<Color32[], int, int> onPixels)
        {
            if (source == null || inFlight || sampleWidth <= 0 || sampleHeight <= 0)
            {
                return false;
            }

            if (target == null || width != sampleWidth || height != sampleHeight)
            {
                Release();
                width = sampleWidth;
                height = sampleHeight;
                target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                target.Create();
            }

            Graphics.Blit(source, target);
            inFlight = true;

            AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
            {
                inFlight = false;
                if (request.hasError || target == null)
                {
                    return;
                }

                var data = request.GetData<Color32>();
                if (data.Length < width * height)
                {
                    return;
                }

                onPixels?.Invoke(data.ToArray(), width, height);
            });

            return true;
        }

        private void Release()
        {
            if (target != null)
            {
                target.Release();
                target = null;
            }
        }

        public void Dispose() => Release();
    }
}
