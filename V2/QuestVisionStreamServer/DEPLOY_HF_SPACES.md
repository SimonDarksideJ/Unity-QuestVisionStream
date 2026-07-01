# Deploy: Hugging Face Spaces (free-GPU cloud fallback)

Cloud fallback when you want inference off your own hardware. HF Spaces offers
free GPU tiers; Cloudflare has **no** free GPU (usable only for signaling/TURN,
not inference), which is why Spaces is the cloud target.

## Approach

Use a **Docker Space** (not the default Gradio SDK) so the WebRTC/WebSocket ports
are under your control.

1. Create a Space → SDK: **Docker**.
2. Add this server's files (`Dockerfile`, `requirements*.txt`, `questvisionstream/`).
3. Spaces exposes a single public port (`7860` by convention). Set:
   - `QVS_PORT=7860` (signaling on the public port), or run signaling on 7860 and
     drop the separate health port.
4. Set the Space to a GPU hardware tier for real-time inference.

## Networking caveat (important)

Spaces sits behind Hugging Face's proxy, which terminates TLS and does **not**
give you host networking. WebRTC **media** across the public internet will need a
**TURN** relay:

```
QVS_ENABLE_TURN=true
QVS_TURN_URLS=turn:<your-turn>:3478
QVS_TURN_USERNAME=<user>
QVS_TURN_CREDENTIAL=<pass>
```

(TURN can be a cheap coturn instance or a managed TURN provider. This is the one
piece Cloudflare *can* help with — a TURN/relay endpoint — even though it can't
host the inference.)

## Signaling URL for the client

Point the WebXR client at the Space over WSS:
`https://<space>.hf.space/?server=wss://<space>.hf.space` (adjust to how you
expose the WebSocket path).
