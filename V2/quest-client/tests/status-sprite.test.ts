/**
 * StatusSpriteSystem: the in-AR rendering of the StatusModel — the DOM status
 * panel is invisible inside an immersive session, so a head-locked HUD (a fixed
 * multi-line status panel + a green/red connection dot), parented to the
 * persistent player head entity, shows status on-headset.
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

  /** The panel is the sprite with a texture map; the dot is the mapless one. */
  const findPanel = (hud: THREE.Object3D) =>
    hud.children.find((c) => c instanceof THREE.Sprite && (c as THREE.Sprite).material.map) as
      | THREE.Sprite
      | undefined;
  const findDot = (hud: THREE.Object3D) =>
    hud.children.find((c) => c instanceof THREE.Sprite && !(c as THREE.Sprite).material.map) as
      | THREE.Sprite
      | undefined;

  it('parents a status panel and a connection dot to the player head', () => {
    const model = new StatusModel();
    model.set('connection', 'connecting');
    const { head } = makeSystem(model);

    const hud = head.children[0];
    expect(hud).toBeDefined();
    const sprites = hud!.children.filter((c) => c instanceof THREE.Sprite);
    expect(sprites).toHaveLength(2); // panel + dot
    expect(findPanel(hud!)).toBeDefined();
    expect(findDot(hud!)).toBeDefined();
    expect(hud!.visible).toBe(true); // fixed panel — always shown
  });

  it('turns the connection dot green when connected, red otherwise', () => {
    const model = new StatusModel();
    model.set('connection', 'connecting');
    const { head } = makeSystem(model);
    const dot = findDot(head.children[0]!)!;

    model.set('connection', 'ready');
    expect(dot.material.color.getHexString()).toBe('39d353'); // green

    model.set('connection', 'failed');
    expect(dot.material.color.getHexString()).toBe('ff5b52'); // red
  });

  it('re-renders the panel texture when a status line changes', () => {
    const model = new StatusModel();
    model.set('connection', 'connecting');
    const { head } = makeSystem(model);
    const panel = findPanel(head.children[0]!)!;
    const before = panel.material.map;

    model.set('connection', 'failed');
    expect(panel.material.map).not.toBe(before); // re-rendered panel
  });

  it('detaches and disposes on destroy', () => {
    const model = new StatusModel();
    model.set('connection', 'connecting');
    const { system, head } = makeSystem(model);
    const panel = findPanel(head.children[0]!)!;
    const textureDisposed = vi.fn();
    panel.material.map!.addEventListener('dispose', textureDisposed);

    system.destroy();

    expect(head.children).toHaveLength(0);
    expect(textureDisposed).toHaveBeenCalledTimes(1);
    // and further model changes must not resurrect anything
    model.set('connection', 'failed');
    expect(head.children).toHaveLength(0);
  });
});
