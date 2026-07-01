/**
 * Cloudflare Pages Function — GET /api/config
 *
 * Returns the signaling server URL for THIS deployment, read from the Pages
 * project environment variable `QVS_SIGNALING_URL`.
 *
 * Edit it ONLINE in the Cloudflare dashboard:
 *   Workers & Pages → <project> → Settings → Variables and Secrets
 *   (set it separately for Production and Preview environments).
 * Changes take effect immediately — no rebuild or redeploy — because the client
 * fetches this endpoint at startup.
 *
 * Response: { "server": "wss://your-host:3000" }  (empty string when unset).
 *
 * This file lives in functions/ (not src/) so it is compiled by Cloudflare's
 * Pages Functions runtime, not by the client's Vite/TypeScript build.
 */
export function onRequestGet(context) {
  const server = (context && context.env && context.env.QVS_SIGNALING_URL) || '';
  return new Response(JSON.stringify({ server }), {
    headers: {
      'content-type': 'application/json; charset=utf-8',
      'cache-control': 'no-store',
    },
  });
}
