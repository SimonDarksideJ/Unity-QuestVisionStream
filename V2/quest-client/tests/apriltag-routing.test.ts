import { describe, expect, it, vi } from 'vitest';
import type { ServiceActivationContext } from '@realitycollective/service-framework';
import { imagePointToViewport, type DetectedMarker } from '../src/apriltags/detector';
import { AprilTagRoutingService } from '../src/services/apriltag/AprilTagRoutingService';
import type { IAprilTagConfigService } from '../src/services/apriltag/IAprilTagConfigService';
import type { TagExit, TagObservation } from '../src/services/apriltag/types';

/** Minimal activation context — the framework constructor only stores fields. */
function makeContext(): ServiceActivationContext {
  return {
    name: 'apriltag-routing',
    priority: 0,
    config: {},
    manager: {} as never,
    scheduler: { subscribe: () => () => {}, emit: () => {}, dispose: () => {} },
    environment: { name: 'test', capabilities: new Set<string>(), hasCapability: () => false },
    signal: new AbortController().signal,
  } as unknown as ServiceActivationContext;
}

/** Config stub exposing only what the routing service reads. */
function makeConfig(
  tagTtlMs = 1000,
  opts: { knownTagsOnly?: boolean; known?: number[] } = {},
): IAprilTagConfigService {
  const known = new Set(opts.known ?? []);
  return {
    sampleWidth: 100,
    sampleHeight: 100,
    flipX: false,
    flipY: false,
    tagTtlMs,
    knownTagsOnly: opts.knownTagsOnly ?? false,
    tagInfo: (id: number) => ({ id, name: `T${id}`, color: '#abcdef' }),
    isKnownTag: (id: number) => known.has(id),
  } as unknown as IAprilTagConfigService;
}

function marker(id: number): DetectedMarker {
  return {
    id,
    hammingDistance: 0,
    corners: [
      { x: 10, y: 10 },
      { x: 90, y: 10 },
      { x: 90, y: 90 },
      { x: 10, y: 90 },
    ],
  };
}

function makeRouting(tagTtlMs = 1000): AprilTagRoutingService {
  return new AprilTagRoutingService(makeContext(), makeConfig(tagTtlMs));
}

describe('imagePointToViewport', () => {
  it('maps image top-left to viewport top-left (Y inverts by default)', () => {
    expect(imagePointToViewport(0, 0, 100, 100, false, false)).toEqual({ x: 0, y: 1 });
    expect(imagePointToViewport(100, 100, 100, 100, false, false)).toEqual({ x: 1, y: 0 });
  });

  it('applies flipX / flipY', () => {
    expect(imagePointToViewport(0, 0, 100, 100, true, false)).toEqual({ x: 1, y: 1 });
    expect(imagePointToViewport(0, 0, 100, 100, false, true)).toEqual({ x: 0, y: 0 });
  });
});

describe('AprilTagRoutingService lifecycle', () => {
  it('emits tagEnter then tagUpdate for a persisting tag', () => {
    const routing = makeRouting();
    const enters: TagObservation[] = [];
    const updates: TagObservation[] = [];
    routing.on('tagEnter', (o) => enters.push(o));
    routing.on('tagUpdate', (o) => updates.push(o));

    routing.ingest([marker(3)], 1000);
    routing.ingest([marker(3)], 1100);

    expect(enters).toHaveLength(1);
    expect(enters[0]).toMatchObject({ id: 3, name: 'T3', color: '#abcdef' });
    expect(enters[0]!.corners).toHaveLength(4);
    expect(updates).toHaveLength(1);
    expect(updates[0]!.firstSeenMs).toBe(1000);
  });

  it('emits tagExit once the TTL lapses with the tag unseen', () => {
    const routing = makeRouting(1000);
    const exits: TagExit[] = [];
    routing.on('tagExit', (e) => exits.push(e));

    routing.ingest([marker(7)], 1000);
    routing.ingest([], 1500); // within TTL — no exit yet
    expect(exits).toHaveLength(0);
    routing.ingest([], 2500); // now > lastSeen + TTL
    expect(exits).toEqual([{ id: 7, name: 'T7' }]);
  });

  it('runs a rule only for its matching tag/phase', () => {
    const routing = makeRouting();
    const run = vi.fn();
    routing.addRule({ id: 'r', on: 'tagEnter', tag: 3, run });

    routing.ingest([marker(4)], 1000); // different id — no fire
    expect(run).not.toHaveBeenCalled();
    routing.ingest([marker(3)], 1000); // matches
    expect(run).toHaveBeenCalledTimes(1);
    expect(run.mock.calls[0]![0]).toMatchObject({ id: 3, name: 'T3' });
  });

  it('removeRule stops a rule from firing', () => {
    const routing = makeRouting();
    const run = vi.fn();
    routing.addRule({ id: 'r', on: 'tagEnter', run });
    routing.removeRule('r');
    routing.ingest([marker(1)], 1000);
    expect(run).not.toHaveBeenCalled();
  });

  it('knownTagsOnly ignores unregistered ids (decoder false-positives)', () => {
    const routing = new AprilTagRoutingService(
      makeContext(),
      makeConfig(1000, { knownTagsOnly: true, known: [3] }),
    );
    const enters: TagObservation[] = [];
    routing.on('tagEnter', (o) => enters.push(o));

    routing.ingest([marker(3), marker(191)], 1000); // 191 is a stray id
    expect(enters.map((o) => o.id)).toEqual([3]);
  });
});
