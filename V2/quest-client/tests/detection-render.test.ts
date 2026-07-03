/**
 * DetectionRenderSystem: capture-pose placement (C3 — boxes must be placed
 * against the camera pose from ~capture time, not the pose at reply arrival),
 * box lifecycle/disposal, and per-frame (ephemeral) redraw behaviour.
 */
import * as THREE from 'three';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { toRenderBatch } from '@questvisionstream/client';

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

import { IDetectionService } from '@questvisionstream/client';
import { AppConfig } from '../src/config';
import { DetectionRenderSystem } from '../src/systems/DetectionRenderSystem';
import { FakeDetectionService, installServiceManager, stubCanvas2d } from './helpers';

stubCanvas2d();

function makeCamera(): THREE.PerspectiveCamera {
  const camera = new THREE.PerspectiveCamera(70, 4 / 3, 0.01, 100);
  camera.position.set(0, 1.6, 0);
  camera.updateMatrixWorld(true);
  camera.updateProjectionMatrix();
  return camera;
}

function poseCamera(camera: THREE.PerspectiveCamera, x: number, ry: number): void {
  camera.position.set(x, 1.6, 0);
  camera.rotation.set(0, ry, 0);
  camera.updateMatrixWorld(true);
}

function makeSystem(camera: THREE.PerspectiveCamera) {
  const detection = new FakeDetectionService();
  installServiceManager(new Map<unknown, unknown>([[IDetectionService, detection]]));
  const scene = new THREE.Scene();
  // The real base-class constructor takes ECS wiring args; the mocked one is
  // parameterless — cast at the seam so typecheck (against real types) passes.
  const SystemCtor = DetectionRenderSystem as unknown as new () => object;
  const system = new SystemCtor() as {
    world: unknown;
    scene: THREE.Scene;
    init(): void;
    update(): void;
    clear(): void;
    destroy(): void;
  };
  system.world = { camera };
  system.scene = scene;
  system.init();
  return { system, detection, scene };
}

// bbox centered at (320, 240) in a 640x480 frame → normalized center (0.5, 0.5).
const payload = (label = 'cup') => ({
  type: 'detections',
  frame: 1,
  width: 640,
  height: 480,
  detections: [{ label, conf: 0.9, bbox: [300, 200, 340, 280] }],
});

/** The system's documented ray math, computed through a reference camera. */
function expectedPlacement(camera: THREE.PerspectiveCamera, cx: number, cy: number): THREE.Vector3 {
  const ndc = new THREE.Vector3(cx * 2 - 1, cy * 2 - 1, 0.5).unproject(camera);
  const origin = camera.getWorldPosition(new THREE.Vector3());
  return origin
    .clone()
    .add(ndc.sub(origin).normalize().multiplyScalar(AppConfig.placementDistanceMeters));
}

afterEach(() => {
  vi.useRealTimers();
});

describe('capture-pose placement (C3)', () => {
  it('places the box against the pose from ~capture time, not the arrival-time pose', async () => {
    vi.useFakeTimers({ toFake: ['performance', 'setTimeout', 'Date'] });
    const camera = makeCamera();
    const { system, detection, scene } = makeSystem(camera);

    // Capture-time pose: looking straight ahead from x=0.
    poseCamera(camera, 0, 0);
    system.update();

    // What a correct capture-pose placement yields: the four box corners
    // unprojected through a reference camera posed identically to capture time.
    const reference = makeCamera();
    poseCamera(reference, 0, 0);
    const { x, y, w, h } = toRenderBatch(payload() as never, { invertY: AppConfig.invertY })
      .detections[0]!.rect;
    const expectedCorners = [
      { x, y },
      { x: x + w, y },
      { x: x + w, y: y + h },
      { x, y: y + h },
    ].map((p) => expectedPlacement(reference, p.x, p.y));

    // Round-trip latency elapses; the head has moved substantially by the time
    // the server's detections arrive. Placement must ignore this arrival pose.
    await vi.advanceTimersByTimeAsync(200);
    poseCamera(camera, 1, 0.8);
    system.update();

    detection.emit('detections', payload());

    expect(scene.children).toHaveLength(1);
    const outline = scene.children[0]!.children.find(
      (c) => c instanceof THREE.LineLoop,
    ) as THREE.LineLoop;
    const positions = outline.geometry.getAttribute('position') as THREE.BufferAttribute;
    expect(positions.count).toBe(4);
    for (let i = 0; i < 4; i++) {
      const corner = new THREE.Vector3().fromBufferAttribute(positions, i);
      expect(corner.distanceTo(expectedCorners[i]!)).toBeLessThan(1e-6);
    }
  });
});

class FakeXrEmitter {
  private readonly listeners = new Map<string, Set<() => void>>();
  addEventListener(event: string, handler: () => void): void {
    const set = this.listeners.get(event) ?? new Set<() => void>();
    set.add(handler);
    this.listeners.set(event, set);
  }
  removeEventListener(event: string, handler: () => void): void {
    this.listeners.get(event)?.delete(handler);
  }
  dispatch(event: string): void {
    for (const handler of this.listeners.get(event) ?? []) handler();
  }
}

describe('recenter cleanup', () => {
  it('clears all tags when the XR reference space resets (user recentered)', () => {
    const camera = makeCamera();
    const detection = new FakeDetectionService();
    installServiceManager(new Map<unknown, unknown>([[IDetectionService, detection]]));

    const xr = new FakeXrEmitter() as FakeXrEmitter & {
      getReferenceSpace: () => FakeXrEmitter;
    };
    const referenceSpace = new FakeXrEmitter();
    xr.getReferenceSpace = () => referenceSpace;

    const scene = new THREE.Scene();
    const SystemCtor = DetectionRenderSystem as unknown as new () => object;
    const system = new SystemCtor() as { world: unknown; scene: THREE.Scene; init(): void };
    system.world = { camera, renderer: { xr } };
    system.scene = scene;
    system.init();

    detection.emit('detections', payload('cup'));
    expect(scene.children).toHaveLength(1);

    // Session starts, then the user recenters: every world-anchored tag is
    // now misplaced relative to the new reference space — clear them.
    xr.dispatch('sessionstart');
    referenceSpace.dispatch('reset');

    expect(scene.children).toHaveLength(0);
    detection.emit('detections', payload('cup')); // next frame redraws cleanly
    expect(scene.children).toHaveLength(1);
  });
});

describe('box lifecycle baselines', () => {
  it('redraws per frame (ephemeral): each payload replaces the previous boxes', () => {
    const camera = makeCamera();
    const { detection, scene } = makeSystem(camera);

    detection.emit('detections', payload('cup'));
    expect(scene.children).toHaveLength(1);

    // A new frame REPLACES the previous boxes (no cross-frame accumulation or
    // dedup) — the scene always shows "what's seen right now".
    detection.emit('detections', payload('cup'));
    expect(scene.children).toHaveLength(1);

    // Every detection in a frame gets its own box — including two of the same
    // class (no per-class dedup).
    detection.emit('detections', {
      type: 'detections',
      frame: 2,
      width: 640,
      height: 480,
      detections: [
        { label: 'cup', conf: 0.9, bbox: [300, 200, 340, 280] },
        { label: 'cup', conf: 0.8, bbox: [10, 10, 60, 70] },
        { label: 'plant', conf: 0.7, bbox: [100, 100, 150, 170] },
      ],
    });
    expect(scene.children).toHaveLength(3);
  });

  it('clear() empties the scene and disposes GPU textures', () => {
    const camera = makeCamera();
    const { system, detection, scene } = makeSystem(camera);

    detection.emit('detections', payload('cup'));
    const sprite = scene.children[0]!.children.find(
      (c) => c instanceof THREE.Sprite,
    ) as THREE.Sprite;
    const textureDisposed = vi.fn();
    sprite.material.map!.addEventListener('dispose', textureDisposed);

    system.clear();

    expect(scene.children).toHaveLength(0);
    expect(textureDisposed).toHaveBeenCalledTimes(1); // C4: the leaked CanvasTexture
    detection.emit('detections', payload('cup')); // redraws after a clear
    expect(scene.children).toHaveLength(1);
  });
});
