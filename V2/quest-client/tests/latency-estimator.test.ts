/**
 * LatencyEstimator (new): turns the server's `pts` media timestamps into a
 * per-frame latency estimate. Absolute latency is unknowable from pts alone
 * (unknown clock offset), but the *variation* is measurable: offset_i =
 * arrival_i − pts_i(ms); latency_i = base + (offset_i − min(offset over a
 * window)). Queuing spikes (slow inference, network jitter) raise the
 * estimate; the windowed minimum re-anchors the baseline.
 */
import { describe, expect, it } from 'vitest';
import { LatencyEstimator } from '../src/rendering/LatencyEstimator';

// pts is in RTP 90 kHz clock units: ptsMs * 90 = pts.
const pts = (ms: number): number => ms * 90;

describe('LatencyEstimator', () => {
  it('returns the base latency before any observations', () => {
    const estimator = new LatencyEstimator(200);
    expect(estimator.latencyMs()).toBe(200);
  });

  it('stays at base when arrival tracks capture with a constant offset', () => {
    const estimator = new LatencyEstimator(200);
    // Constant 5000ms clock offset between server pts-time and client clock.
    estimator.observe(5000, pts(0));
    estimator.observe(5033, pts(33));
    estimator.observe(5066, pts(66));
    expect(estimator.latencyMs()).toBe(200);
  });

  it('adds measured queuing delay on top of base', () => {
    const estimator = new LatencyEstimator(200);
    estimator.observe(5000, pts(0));
    estimator.observe(5033, pts(33));
    // This frame arrived 120ms later than the established offset — it queued
    // behind slow inference.
    estimator.observe(5066 + 120, pts(66));
    expect(estimator.latencyMs()).toBe(320);
  });

  it('recovers once the spike passes', () => {
    const estimator = new LatencyEstimator(200);
    estimator.observe(5000, pts(0));
    estimator.observe(5153, pts(33)); // +120ms spike
    estimator.observe(5066, pts(66)); // back to the baseline offset
    expect(estimator.latencyMs()).toBe(200);
  });

  it('evicts offsets older than the window so the baseline can drift', () => {
    const estimator = new LatencyEstimator(200, 1000);
    estimator.observe(5000, pts(0));
    // 2s later (old sample far outside the 1s window): everything now runs
    // 80ms later relative to the original baseline — the new normal.
    estimator.observe(7080, pts(2000));
    estimator.observe(7113, pts(2033));
    expect(estimator.latencyMs()).toBe(200); // re-anchored, not stuck at +80
  });

  it('ignores frames without pts and keeps the base', () => {
    const estimator = new LatencyEstimator(200);
    estimator.observe(5000, null);
    estimator.observe(5033, undefined);
    expect(estimator.latencyMs()).toBe(200);
  });
});
