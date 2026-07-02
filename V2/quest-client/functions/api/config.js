/**
 * Cloudflare Pages Function — GET /api/config
 *
 * Returns the signaling server URL for THIS deployment. Resolved in order:
 *
 *   1. KV binding `QVS_CONFIG`, key `signaling_url`  ← LIVE, edit with NO redeploy.
 *   2. Env var `QVS_SIGNALING_URL`                    ← simple, but changing it
 *                                                        requires a redeploy
 *                                                        (Pages bakes env vars into
 *                                                        the deployment).
 *
 * Response: { "server": "wss://your-host:3000" }  (empty string when unset).
 *
 * To change the server while the app is live, use option 1: bind a KV namespace
 * as `QVS_CONFIG` (one-time — the binding itself needs one deploy to wire up),
 * then edit the `signaling_url` value in the dashboard or with
 * `wrangler kv key put`. KV values are read at request time, so the change takes
 * effect within seconds — just reload ("kick") the app. See
 * Documentation/Deployment-Cloudflare-Pages.md §4.
 *
 * This file lives in functions/ (not src/) so it is run by Cloudflare's Pages
 * Functions runtime, not the client's Vite/TypeScript build.
 */
export async function onRequestGet(context) {
  const env = context && context.env ? context.env : {};
  let server = '';

  // 1) KV binding — live, no redeploy needed to change the value.
  try {
    if (env.QVS_CONFIG && typeof env.QVS_CONFIG.get === 'function') {
      const v = await env.QVS_CONFIG.get('signaling_url');
      if (v) server = v;
    }
  } catch (_) {
    // KV not bound / transient error — fall through to the env var.
  }

  // 2) Plain env var fallback (change requires a redeploy).
  if (!server && env.QVS_SIGNALING_URL) server = env.QVS_SIGNALING_URL;

  return new Response(JSON.stringify({ server }), {
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
    },
  });
}
