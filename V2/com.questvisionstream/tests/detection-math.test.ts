/**
 * DetectionMath: coordinate-transform baselines (these lock in behaviour that
 * was verified correct in review) plus the new invertX mirror support.
 */
import { describe, expect, it } from 'vitest';
import { DetectionDeduper, normalizeDetection, toRenderBatch } from '../src/rendering/DetectionMath';
import type { Detection } from '../src/services/detection/types';

const det = (bbox: [number, number, number, number]): Detection => ({
  label: 'cup',
  conf: 0.9,
  bbox,
});

describe('normalizeDetection baselines', () => {
  it('normalizes center and rect (invertY on, the server default)', () => {
    // 100x100 frame, box (10,10)-(30,30): center (20,20) → (0.2, 1-0.2)
    const { center, rect } = normalizeDetection(det([10, 10, 30, 30]), 100, 100);
    expect(center).toEqual({ x: 0.2, y: 0.8 });
    // rect top-left after v-flip: ry = 1 - 0.1 - 0.2 = 0.7
    expect(rect).toEqual({ x: 0.1, y: 0.7, w: 0.2, h: 0.2 });
  });

  it('normalizes without flips when invertY is off', () => {
    const { center, rect } = normalizeDetection(det([10, 10, 30, 30]), 100, 100, {
      invertY: false,
    });
    expect(center).toEqual({ x: 0.2, y: 0.2 });
    expect(rect).toEqual({ x: 0.1, y: 0.1, w: 0.2, h: 0.2 });
  });

  it('is resolution independent (adaptive server frame sizes)', () => {
    const small = normalizeDetection(det([10, 10, 30, 30]), 100, 100);
    const big = normalizeDetection(det([20, 20, 60, 60]), 200, 200);
    expect(big).toEqual(small);
  });

  it('guards against zero-sized frames', () => {
    const { center } = normalizeDetection(det([0, 0, 0, 0]), 0, 0);
    expect(Number.isFinite(center.x)).toBe(true);
    expect(Number.isFinite(center.y)).toBe(true);
  });
});

describe('invertX (mirrored streams)', () => {
  it('mirrors the center and rect horizontally', () => {
    const { center, rect } = normalizeDetection(det([10, 10, 30, 30]), 100, 100, {
      invertY: false,
      invertX: true,
    });
    expect(center).toEqual({ x: 0.8, y: 0.2 });
    // rect left edge after h-flip: rx = 1 - 0.1 - 0.2 = 0.7
    expect(rect).toEqual({ x: 0.7, y: 0.1, w: 0.2, h: 0.2 });
  });

  it('composes with invertY', () => {
    const { center } = normalizeDetection(det([10, 10, 30, 30]), 100, 100, {
      invertY: true,
      invertX: true,
    });
    expect(center).toEqual({ x: 0.8, y: 0.8 });
  });

  it('flows through toRenderBatch options', () => {
    const batch = toRenderBatch(
      {
        type: 'detections',
        frame: 1,
        width: 100,
        height: 100,
        detections: [det([10, 10, 30, 30])],
      },
      { invertY: false, invertX: true },
    );
    expect(batch.detections[0]?.center.x).toBeCloseTo(0.8);
  });
});

describe('DetectionDeduper baselines', () => {
  it('per-class places each label once', () => {
    const deduper = new DetectionDeduper({ policy: 'per-class' });
    expect(deduper.shouldPlace('cup')).toBe(true);
    expect(deduper.shouldPlace('cup')).toBe(false);
    expect(deduper.shouldPlace('plant')).toBe(true);
    deduper.reset();
    expect(deduper.shouldPlace('cup')).toBe(true);
  });

  it('spatial-per-class enforces the minimum distance per label', () => {
    const deduper = new DetectionDeduper({ policy: 'spatial-per-class', minDistanceMeters: 1 });
    expect(deduper.shouldPlace('cup', { x: 0, y: 0, z: 0 })).toBe(true);
    expect(deduper.shouldPlace('cup', { x: 0.5, y: 0, z: 0 })).toBe(false); // too close
    expect(deduper.shouldPlace('cup', { x: 2, y: 0, z: 0 })).toBe(true); // far enough
    expect(deduper.shouldPlace('plant', { x: 0, y: 0, z: 0 })).toBe(true); // other class
  });

  it("'none' always places", () => {
    const deduper = new DetectionDeduper({ policy: 'none' });
    expect(deduper.shouldPlace('cup')).toBe(true);
    expect(deduper.shouldPlace('cup')).toBe(true);
  });
});
