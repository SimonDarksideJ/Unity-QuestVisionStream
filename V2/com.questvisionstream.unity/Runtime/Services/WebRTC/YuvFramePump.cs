// Copyright (c) Simon Jackson (SimonDarksideJ). All rights reserved.
// Licensed under the MIT License. See LICENSE in the repository root for license information.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QuestVisionStream.Services
{
    /// <summary>
    /// The camera→encoder frame path, carried over from the proven V1
    /// <c>PCAVideoStreamer</c>/<c>FrameSender</c> pipeline: blit the source into a
    /// stream-sized RT, convert RGB→I420 on the GPU (BT.601 limited range compute
    /// shader), read the three planes back asynchronously and hand them to the
    /// transport. A CPU fallback reads back RGB24 and lets the plugin convert.
    /// Never more than one readback in flight — a slow GPU drops frames instead of
    /// queueing them (latest-frame-wins at the edge, like the server does).
    /// </summary>
    public sealed class YuvFramePump : IDisposable
    {
        private const string ShaderResourcePath = "QuestVisionStream/RGBToYUV420";

        // Blit UVs for the vertical flip: scale (1,-1), offset (0,1) samples the
        // source bottom-up — free, no extra pass.
        private static readonly Vector2 FlipScale = new Vector2(1f, -1f);
        private static readonly Vector2 FlipOffset = new Vector2(0f, 1f);

        private readonly bool useGpu;
        private readonly bool flipVertically;
        private ComputeShader shader;
        private int kernel;
        private RenderTexture blitTexture;
        private RenderTexture yTexture;
        private RenderTexture uTexture;
        private RenderTexture vTexture;
        private int width;
        private int height;
        private bool readbackInFlight;

        public YuvFramePump(bool useGpuConversion, bool flipVertically = true)
        {
            this.flipVertically = flipVertically;
            shader = Resources.Load<ComputeShader>(ShaderResourcePath);
            useGpu = useGpuConversion && shader != null && SystemInfo.supportsComputeShaders;
            if (useGpu)
            {
                kernel = shader.FindKernel("CSMain");
            }
            else if (useGpuConversion)
            {
                Debug.LogWarning("[QVS:Pump] GPU YUV conversion unavailable; falling back to RGB24 readback.");
            }
        }

        /// <summary>Frames dropped because a previous readback was still in flight.</summary>
        public long DroppedFrames { get; private set; }

        public long PushedFrames { get; private set; }

        public void Configure(int streamWidth, int streamHeight)
        {
            if (width == streamWidth && height == streamHeight && blitTexture != null)
            {
                return;
            }

            ReleaseTextures();
            width = streamWidth;
            height = streamHeight;

            blitTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            blitTexture.Create();

            if (useGpu)
            {
                yTexture = CreatePlane(width, height);
                uTexture = CreatePlane(width / 2, height / 2);
                vTexture = CreatePlane(width / 2, height / 2);
            }
        }

        /// <summary>
        /// Blit + convert + readback one frame, delivering it to the transport when
        /// the GPU is done. Safe to call every frame; skips while a readback is in flight.
        /// </summary>
        public void PumpFrame(Texture source, IWebRTCTransportModule transport)
        {
            if (source == null || blitTexture == null || transport == null)
            {
                return;
            }

            if (readbackInFlight)
            {
                DroppedFrames++;
                return;
            }

            // Send the stream UPRIGHT: the GPU readback convention delivers frames
            // vertically flipped on-device (this was why the V1 server defaulted
            // QVS_FLIP_VERTICAL=true). Correcting at source is free here and keeps
            // the wire truthful — run the server with QVS_FLIP_VERTICAL=false.
            if (flipVertically)
            {
                Graphics.Blit(source, blitTexture, FlipScale, FlipOffset);
            }
            else
            {
                Graphics.Blit(source, blitTexture);
            }

            if (useGpu)
            {
                shader.SetTexture(kernel, "InputTexture", blitTexture);
                shader.SetTexture(kernel, "OutputY", yTexture);
                shader.SetTexture(kernel, "OutputU", uTexture);
                shader.SetTexture(kernel, "OutputV", vTexture);
                shader.Dispatch(kernel, (width + 7) / 8, (height + 7) / 8, 1);
                ReadbackYuv(transport);
            }
            else
            {
                ReadbackRgb(transport);
            }
        }

        private void ReadbackYuv(IWebRTCTransportModule transport)
        {
            readbackInFlight = true;
            var frameWidth = width;
            var frameHeight = height;
            sbyte[] yData = null;
            sbyte[] uData = null;
            sbyte[] vData = null;
            var pending = 3;
            var failed = false;

            void OnPlaneDone(AsyncGPUReadbackRequest request, Action<sbyte[]> assign)
            {
                if (request.hasError)
                {
                    failed = true;
                }
                else
                {
                    // sbyte[] straight out of the readback (same bytes) — the JNI
                    // bridge needs the runtime array type to actually be sbyte[].
                    assign(request.GetData<sbyte>().ToArray());
                }

                if (--pending > 0)
                {
                    return;
                }

                readbackInFlight = false;
                if (failed)
                {
                    return;
                }

                PushedFrames++;
                transport.PushFrameYuv(yData, uData, vData, frameWidth, frameHeight);
            }

            AsyncGPUReadback.Request(yTexture, 0, request => OnPlaneDone(request, data => yData = data));
            AsyncGPUReadback.Request(uTexture, 0, request => OnPlaneDone(request, data => uData = data));
            AsyncGPUReadback.Request(vTexture, 0, request => OnPlaneDone(request, data => vData = data));
        }

        private void ReadbackRgb(IWebRTCTransportModule transport)
        {
            readbackInFlight = true;
            var frameWidth = width;
            var frameHeight = height;

            AsyncGPUReadback.Request(blitTexture, 0, TextureFormat.RGB24, request =>
            {
                readbackInFlight = false;
                if (request.hasError)
                {
                    return;
                }

                var data = request.GetData<sbyte>();
                if (data.Length <= 0)
                {
                    return;
                }

                PushedFrames++;
                transport.PushFrameRgb(data.ToArray(), frameWidth, frameHeight);
            });
        }

        private static RenderTexture CreatePlane(int planeWidth, int planeHeight)
        {
            var texture = new RenderTexture(planeWidth, planeHeight, 0, RenderTextureFormat.R8)
            {
                enableRandomWrite = true
            };
            texture.Create();
            return texture;
        }

        private void ReleaseTextures()
        {
            if (blitTexture != null) { blitTexture.Release(); blitTexture = null; }
            if (yTexture != null) { yTexture.Release(); yTexture = null; }
            if (uTexture != null) { uTexture.Release(); uTexture = null; }
            if (vTexture != null) { vTexture.Release(); vTexture = null; }
        }

        public void Dispose() => ReleaseTextures();
    }
}
