/**
 * DetectionRenderSystem: capture-pose placement (C3 — tags must be placed
 * against the camera pose from ~capture time, not the pose at reply arrival),
 * tag lifecycle/disposal, and dedup baselines.
 */
import * as THREE from 'three';
import { afterEach, describe, expect, it, vi } from 'vitest';

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
  it('places against the pose from ~capture time, not the arrival-time pose', async () => {
    vi.useFakeTimers({ toFake: ['performance', 'setTimeout', 'Date'] });
    const camera = makeCamera();
    const { system, detection, scene } = makeSystem(camera);

    // Capture-time pose: looking straight ahead from x=0.
    poseCamera(camera, 0, 0);
    system.update();
    const reference = makeCamera();
    poseCamera(reference, 0, 0);
    const expected = expectedPlacement(reference, 0.5, 0.5);

    // Round-trip latency elapses; the head has moved substantially by the
    // time the server's detections arrive.
    await vi.advanceTimersByTimeAsync(200);
    poseCamera(camera, 1, 0.8);
    system.update();

    detection.emit('detections', payload());

    expect(scene.children).toHaveLength(1);
    const placed = scene.children[0]!.position;
    expect(placed.distanceTo(expected)).toBeLessThan(1e-6);
  });
});

describe('tag lifecycle baselines', () => {
  it('per-class dedup places one tag per label', () => {
    const camera = makeCamera();
    const { system, detection, scene } = makeSystem(camera);

    detection.emit('detections', payload('cup'));
    detection.emit('detections', payload('cup'));
    detection.emit('detections', payload('plant'));

    expect(scene.children).toHaveLength(2);
  });

  it('clear() empties the scene, disposes GPU textures, and resets dedup', () => {
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
    detection.emit('detections', payload('cup')); // dedup was reset
    expect(scene.children).toHaveLength(1);
  });
});
