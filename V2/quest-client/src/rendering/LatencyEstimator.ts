/**
 * Per-frame latency estimation from the server's `pts` media timestamps.
 *
 * Absolute capture→arrival latency cannot be derived from pts alone (the RTP
 * clock has an arbitrary offset from the client clock), but its *variation*
 * can: `offset_i = arrival_i − pts_i(ms)` is constant when frames flow
 * smoothly and grows when a frame queued behind slow inference or network
 * jitter. The estimate is therefore
 *
 *     latency_i = baseLatencyMs + (offset_i − min(offset over recent window))
 *
 * — the configured baseline plus the measured queuing delay. The windowed
 * minimum re-anchors the baseline as conditions drift.
 */

const RTP_CLOCK_HZ = 90_000;

export class LatencyEstimator {
  /** `[arrivalMs, offsetMs]` pairs within the window, arrival-ordered. */
  private readonly offsets: Array<[number, number]> = [];
  private lastOffsetMs: number | undefined;

  constructor(
    private readonly baseLatencyMs = 200,
    private readonly windowMs = 5000,
  ) {}

  observe(arrivalMs: number, pts: number | null | undefined): void {
    if (typeof pts !== 'number' || !Number.isFinite(pts)) return;
    const offsetMs = arrivalMs - (pts / RTP_CLOCK_HZ) * 1000;
    this.lastOffsetMs = offsetMs;
    this.offsets.push([arrivalMs, offsetMs]);
    const cutoff = arrivalMs - this.windowMs;
    while (this.offsets.length > 1 && this.offsets[0]![0] < cutoff) {
      this.offsets.shift();
    }
  }

  latencyMs(): number {
    if (this.lastOffsetMs === undefined || this.offsets.length === 0) {
      return this.baseLatencyMs;
    }
    let minOffset = Number.POSITIVE_INFINITY;
    for (const [, offset] of this.offsets) {
      if (offset < minOffset) minOffset = offset;
    }
    return this.baseLatencyMs + Math.max(0, this.lastOffsetMs - minOffset);
  }
}
