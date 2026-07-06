import { createServiceToken, type IEventService } from '@realitycollective/service-framework';
import type { DetectedMarker } from '../../apriltags/detector';
import type { AprilTagRoutingEventMap, TagExit, TagObservation, TagRulePhase } from './types';

/**
 * A declarative rule — the "See X → do Y" primitive. Fires `run` whenever a tag
 * matching `tag` (an id, a name, or any when omitted) reaches lifecycle phase
 * `on`. Register with {@link IAprilTagRoutingService.addRule}. Rules can register
 * further rules from inside `run` to express sequences ("wait for Z → look for
 * J"), which is the growth path toward a full rules engine.
 */
export interface TagRule {
  /** Unique id; re-adding the same id replaces the prior rule. */
  readonly id: string;
  /** Lifecycle phase to trigger on. */
  readonly on: TagRulePhase;
  /** Match a specific tag id or name; omit to match any tag. */
  readonly tag?: number | string;
  /** Action to run when the rule matches. */
  run(event: TagObservation | TagExit): void;
}

/**
 * **Routing** responsibility: turn the detector's per-tick markers into *meaning*
 * — stable enter/update/exit lifecycle events — and evaluate rules against them.
 * This is the decoupling seam between "what was detected" and "what to do about
 * it": placement is just one consumer, and app rules ("see Alpha → …") are
 * others. Designed to grow into a rules engine; holds no scene/three state.
 */
export interface IAprilTagRoutingService extends IEventService<AprilTagRoutingEventMap> {
  /** Feed one detection tick; emits enter/update and (via TTL) exit events. */
  ingest(markers: readonly DetectedMarker[], nowMs: number): void;
  /** Register (or replace) a rule. */
  addRule(rule: TagRule): void;
  /** Remove a rule by id. */
  removeRule(ruleId: string): void;
  /** Forget all tracked tags (e.g. on reference-space reset). Keeps rules. */
  forgetAll(): void;
}

export const IAprilTagRoutingService =
  createServiceToken<IAprilTagRoutingService>('IAprilTagRoutingService');
