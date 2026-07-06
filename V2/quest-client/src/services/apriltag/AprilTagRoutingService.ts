import {
  BaseEventService,
  type ServiceActivationContext,
} from '@realitycollective/service-framework';
import { createLogger } from '@questvisionstream/client';
import { imagePointToViewport, type DetectedMarker } from '../../apriltags/detector';
import type { IAprilTagConfigService } from './IAprilTagConfigService';
import type { IAprilTagRoutingService, TagRule } from './IAprilTagRoutingService';
import type { AprilTagRoutingEventMap, TagExit, TagObservation, TagRulePhase } from './types';

const log = createLogger('AprilTagRouting');

interface Tracked {
  firstSeenMs: number;
  lastSeenMs: number;
  name: string;
  color: string;
}

/**
 * Routing service. Diffs each detection tick against tracked state to emit
 * `tagEnter` (newly visible), `tagUpdate` (still visible), and — once a tag goes
 * unseen past its TTL — `tagExit`. Rules registered via {@link addRule} are
 * evaluated alongside the events, giving the "See X → do Y" primitive. Marker
 * image-pixels are converted to normalized viewport corners here (using the
 * config's flips) so downstream consumers never touch pixel space.
 */
export class AprilTagRoutingService
  extends BaseEventService<AprilTagRoutingEventMap>
  implements IAprilTagRoutingService
{
  private readonly config: IAprilTagConfigService;
  private readonly tracked = new Map<number, Tracked>();
  private readonly rules = new Map<string, TagRule>();

  constructor(context: ServiceActivationContext, config: IAprilTagConfigService) {
    super(context);
    this.config = config;
  }

  ingest(markers: readonly DetectedMarker[], nowMs: number): void {
    const seen = new Set<number>();
    for (const marker of markers) {
      if (marker.corners.length !== 4) continue;
      // Drop decoder false-positives (unregistered ids) unless configured to
      // surface everything — see IAprilTagConfigService.knownTagsOnly.
      if (this.config.knownTagsOnly && !this.config.isKnownTag(marker.id)) continue;
      seen.add(marker.id);
      const info = this.config.tagInfo(marker.id);
      const corners = marker.corners.map((c) =>
        imagePointToViewport(
          c.x,
          c.y,
          this.config.sampleWidth,
          this.config.sampleHeight,
          this.config.flipX,
          this.config.flipY,
        ),
      );

      const existing = this.tracked.get(marker.id);
      const firstSeenMs = existing?.firstSeenMs ?? nowMs;
      if (existing) existing.lastSeenMs = nowMs;
      else this.tracked.set(marker.id, { firstSeenMs, lastSeenMs: nowMs, name: info.name, color: info.color });

      const obs: TagObservation = {
        id: marker.id,
        name: info.name,
        color: info.color,
        corners,
        firstSeenMs,
        lastSeenMs: nowMs,
      };
      this.dispatch(existing ? 'tagUpdate' : 'tagEnter', obs);
    }

    // Expire tags unseen past the TTL.
    for (const [id, t] of this.tracked) {
      if (seen.has(id) || nowMs - t.lastSeenMs <= this.config.tagTtlMs) continue;
      this.tracked.delete(id);
      this.dispatch('tagExit', { id, name: t.name });
    }
  }

  addRule(rule: TagRule): void {
    this.rules.set(rule.id, rule);
  }

  removeRule(ruleId: string): void {
    this.rules.delete(ruleId);
  }

  forgetAll(): void {
    this.tracked.clear();
  }

  override destroy(): void {
    this.tracked.clear();
    this.rules.clear();
    super.destroy();
  }

  /** Emit to raw subscribers, then evaluate matching rules. */
  private dispatch(phase: TagRulePhase, payload: TagObservation | TagExit): void {
    // Localized cast at the event seam: `phase` is a union, so the generic emit
    // can't narrow the payload — but by construction they always agree.
    this.emit(phase, payload as never);
    for (const rule of this.rules.values()) {
      if (rule.on !== phase) continue;
      if (rule.tag !== undefined && rule.tag !== payload.id && rule.tag !== payload.name) continue;
      try {
        rule.run(payload);
      } catch (err) {
        log.warn(`rule '${rule.id}' threw`, err);
      }
    }
  }
}
