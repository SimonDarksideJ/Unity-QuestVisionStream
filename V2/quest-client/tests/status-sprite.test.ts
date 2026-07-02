/**
 * StatusSpriteSystem (new): the in-AR rendering of the StatusModel — the DOM
 * status panel is invisible inside an immersive session, so a head-locked
 * text sprite (parented to the persistent player head entity) shows the
 * current headline while anything needs attention, and hides when healthy.
 */
import * as THREE from 'three';
import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@iwsdk/core', () => {
  class SystemBase {
    world: unknown;
    scene: unknown;
    init(): void {}
    update(): void {}
    destroy(): void {}
  }
  return { createSystem: () => SystemBase };
});

import { StatusModel } from '../src/ui/status';
import { StatusSpriteSystem } from '../src/systems/StatusSpriteSystem';
import { stubCanvas2d } from './helpers';

stubCanvas2d();

function makeSystem(model: StatusModel) {
  const head = new THREE.Group();
  const SystemCtor = StatusSpriteSystem as unknown as new () => object;
  const system = new SystemCtor() as {
    world: unknown;
    init(): void;
    destroy(): void;
  };
  system.world = { playerHeadEntity: { object3D: head } };
  (system as { model?: StatusModel }).model = model; // test seam: inject the model
  system.init();
  return { system, head };
}

describe('StatusModel.headline', () => {
  it('is null when everything is healthy (connection ready)', () => {
    const model = new StatusModel();
    model.set('camera', 'active');
    model.set('signaling', 'connected');
    model.set('connection', 'ready');
    expect(model.headline()).toBeNull();
  });

  it('prioritizes camera errors over everything else', () => {
    const model = new StatusModel();
    model.set('connection', 'failed');
    model.set('camera', 'error — camera unavailable');
    expect(model.headline()).toContain('camera');
  });

  it('reports connection problems and in-progress states', () => {
    const model = new StatusModel();
    model.set('connection', 'failed');
    expect(model.headline()).toContain('failed');

    const connecting = new StatusModel();
    connecting.set('connection', 'connecting');
    expect(connecting.headline()).toContain('connecting');
  });

  it('reports a paused quality gate when otherwise connected', () => {
    const model = new StatusModel();
    model.set('connection', 'ready');
    model.set('quality', 'paused (too dark)');
    expect(model.headline()).toContain('paused');
  });
});

describe('StatusSpriteSystem', () => {
  beforeEach(() => {
    // fresh model per test via makeSystem
  });

  it('parents a sprite to the player head and shows the current headline', () => {
    const model = new StatusModel();
    model.set('connection', 'connecting');
    const { head } = makeSystem(model);

    const hud = head.children[0];
    expect(hud).toBeDefined();
    const sprite = hud!.children.find((c) => c instanceof THREE.Sprite);
    expect(sprite).toBeDefined();
    expect(hud!.visible).toBe(true);
  });

  it('hides when the model reports healthy and reappears on failure', () => {
    const model = new StatusModel();
    model.set('connection', 'connecting');
    const { head } = makeSystem(model);
    const hud = head.children[0]!;

    model.set('connection', 'ready');
    expect(hud.visible).toBe(false);

    model.set('connection', 'failed');
    expect(hud.visible).toBe(true);
  });

  it('updates the sprite texture when the headline changes', () => {
    const model = new StatusModel();
    model.set('connection', 'connecting');
    const { head } = makeSystem(model);
    const sprite = head.children[0]!.children.find(
      (c) => c instanceof THREE.Sprite,
    ) as THREE.Sprite;
    const before = sprite.material.map;

    model.set('connection', 'failed');
    expect(sprite.material.map).not.toBe(before); // re-rendered label
  });

  it('detaches and disposes on destroy', () => {
    const model = new StatusModel();
    model.set('connection', 'connecting');
    const { system, head } = makeSystem(model);
    const sprite = head.children[0]!.children.find(
      (c) => c instanceof THREE.Sprite,
    ) as THREE.Sprite;
    const textureDisposed = vi.fn();
    sprite.material.map!.addEventListener('dispose', textureDisposed);

    system.destroy();

    expect(head.children).toHaveLength(0);
    expect(textureDisposed).toHaveBeenCalledTimes(1);
    // and further model changes must not resurrect anything
    model.set('connection', 'failed');
    expect(head.children).toHaveLength(0);
  });
});
