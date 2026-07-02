/**
 * PoseHistory (new in this pass): the capture-pose groundwork for the P0
 * "pose-freeze" gap — detections must be placed against the camera pose at
 * (approximately) capture time, not wherever the head points when the reply
 * arrives after the full round-trip.
 */
import * as THREE from 'three';
import { describe, expect, it } from 'vitest';
import { PoseHistory, unprojectThroughSnapshot } from '../src/rendering/PoseHistory';

function cameraAt(x: number, ry = 0): THREE.PerspectiveCamera {
  const camera = new THREE.PerspectiveCamera(70, 4 / 3, 0.01, 100);
  camera.position.set(x, 1.6, 0);
  camera.rotation.set(0, ry, 0);
  camera.updateMatrixWorld(true);
  camera.updateProjectionMatrix();
  return camera;
}

describe('PoseHistory', () => {
  it('returns the snapshot nearest in time', () => {
    const history = new PoseHistory();
    history.record(0, cameraAt(0));
    history.record(100, cameraAt(1));
    history.record(200, cameraAt(2));

    const snap = history.lookup(90)!;
    expect(snap.timeMs).toBe(100);
    expect(new THREE.Vector3().setFromMatrixPosition(snap.matrixWorld).x).toBe(1);
  });

  it('returns null when empty', () => {
    expect(new PoseHistory().lookup(123)).toBeNull();
  });

  it('evicts snapshots older than maxAgeMs', () => {
    const history = new PoseHistory(500);
    history.record(0, cameraAt(0));
    history.record(1000, cameraAt(1));
    expect(history.size).toBe(1);
    expect(history.lookup(0)!.timeMs).toBe(1000); // only the fresh one remains
  });

  it('snapshots are immutable copies, not live references', () => {
    const history = new PoseHistory();
    const camera = cameraAt(0);
    history.record(0, camera);
    camera.position.set(9, 9, 9);
    camera.updateMatrixWorld(true);
    const pos = new THREE.Vector3().setFromMatrixPosition(history.lookup(0)!.matrixWorld);
    expect(pos.x).toBe(0); // must not have followed the camera
  });
});

describe('unprojectThroughSnapshot', () => {
  it('matches THREE unproject through the live camera for the same pose', () => {
    const camera = cameraAt(0.5, 0.3);
    const history = new PoseHistory();
    history.record(0, camera);
    const snap = history.lookup(0)!;

    const center = { x: 0.3, y: 0.7 };
    const distance = 2;

    // Reference: the current placeOnRay math through the live camera.
    const ndc = new THREE.Vector3(center.x * 2 - 1, center.y * 2 - 1, 0.5).unproject(camera);
    const origin = camera.getWorldPosition(new THREE.Vector3());
    const expected = origin
      .clone()
      .add(ndc.sub(origin).normalize().multiplyScalar(distance));

    const actual = unprojectThroughSnapshot(snap, center, distance);
    expect(actual.distanceTo(expected)).toBeLessThan(1e-6);
  });
});
