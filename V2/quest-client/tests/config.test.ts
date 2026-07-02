/**
 * Signaling-URL resolution: tier priority, the unvalidated `?server=`
 * override, and the unbounded `/api/config` fetch that can hang boot forever.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { resolveSignalingUrl } from '../src/config';

function setPageUrl(url: string): void {
  window.history.replaceState(null, '', url);
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
  setPageUrl('/');
});

describe('tier priority (baselines)', () => {
  it('a valid ?server= override wins over everything', async () => {
    setPageUrl('/?server=wss://my-server.example:3000');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"server":"wss://other"}')));
    expect(await resolveSignalingUrl()).toBe('wss://my-server.example:3000');
  });

  it('uses /api/config when no override is present', async () => {
    setPageUrl('/');
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ server: 'wss://from-kv.example' }))),
    );
    expect(await resolveSignalingUrl()).toBe('wss://from-kv.example');
  });

  it('falls back to the dev default when nothing is configured', async () => {
    setPageUrl('/');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"server":""}')));
    expect(await resolveSignalingUrl()).toBe('ws://localhost:3000');
  });

  it('a failed /api/config fetch falls through (dev server 404)', async () => {
    setPageUrl('/');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('nope', { status: 404 })));
    expect(await resolveSignalingUrl()).toBe('ws://localhost:3000');
  });
});

describe('?server= validation', () => {
  it('rejects non-WebSocket schemes instead of dialing them', async () => {
    setPageUrl('/?server=javascript:alert(1)');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"server":""}')));
    const url = await resolveSignalingUrl();
    expect(url).toBe('ws://localhost:3000'); // invalid override must be skipped
  });

  it('rejects http(s) URLs (signaling is ws/wss only)', async () => {
    setPageUrl('/?server=https://attacker.example/collect');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"server":""}')));
    expect(await resolveSignalingUrl()).toBe('ws://localhost:3000');
  });

  it('rejects garbage that is not a URL at all', async () => {
    setPageUrl('/?server=not a url');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"server":""}')));
    expect(await resolveSignalingUrl()).toBe('ws://localhost:3000');
  });

  it('validates the /api/config value the same way', async () => {
    setPageUrl('/');
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ server: 'https://not-a-ws.example' }))),
    );
    expect(await resolveSignalingUrl()).toBe('ws://localhost:3000');
  });
});

describe('?server= confirmation (camera-redirect guard)', () => {
  it('asks the user before streaming to a non-localhost override target', async () => {
    setPageUrl('/?server=wss://someone-elses-server.example:3000');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"server":""}')));
    const confirm = vi.fn((_message?: string) => true);
    vi.stubGlobal('confirm', confirm);

    expect(await resolveSignalingUrl()).toBe('wss://someone-elses-server.example:3000');
    expect(confirm).toHaveBeenCalledTimes(1);
    expect(String(confirm.mock.calls[0]?.[0])).toContain('someone-elses-server.example');
  });

  it('falls through to the configured server when the user declines', async () => {
    setPageUrl('/?server=wss://attacker.example:3000');
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ server: 'wss://trusted.example' }))),
    );
    vi.stubGlobal('confirm', vi.fn(() => false));

    expect(await resolveSignalingUrl()).toBe('wss://trusted.example');
  });

  it('does not prompt for localhost dev targets', async () => {
    setPageUrl('/?server=ws://localhost:3000');
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"server":""}')));
    const confirm = vi.fn(() => true);
    vi.stubGlobal('confirm', confirm);

    expect(await resolveSignalingUrl()).toBe('ws://localhost:3000');
    expect(confirm).not.toHaveBeenCalled();
  });

  it('does not prompt for the /api/config-configured server (trusted tier)', async () => {
    setPageUrl('/');
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify({ server: 'wss://from-kv.example' }))),
    );
    const confirm = vi.fn(() => true);
    vi.stubGlobal('confirm', confirm);

    expect(await resolveSignalingUrl()).toBe('wss://from-kv.example');
    expect(confirm).not.toHaveBeenCalled();
  });
});

describe('config fetch timeout', () => {
  it('does not hang boot when /api/config never responds', async () => {
    setPageUrl('/');
    vi.useFakeTimers();
    // A fetch that honours its abort signal but otherwise never settles.
    vi.stubGlobal(
      'fetch',
      vi.fn(
        (_input: unknown, init?: { signal?: AbortSignal }) =>
          new Promise((_resolve, reject) => {
            init?.signal?.addEventListener('abort', () =>
              reject(new DOMException('aborted', 'AbortError')),
            );
          }),
      ),
    );

    let resolved: string | undefined;
    void resolveSignalingUrl().then((url) => {
      resolved = url;
    });

    await vi.advanceTimersByTimeAsync(10_000); // any sane timeout is well under 10s
    expect(resolved).toBe('ws://localhost:3000'); // boot must proceed on the fallback
  });
});
