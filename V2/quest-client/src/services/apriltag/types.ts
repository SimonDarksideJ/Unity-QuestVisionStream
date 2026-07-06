import type { NormalizedPoint } from '@questvisionstream/client';

/**
 * A live observation of a detected tag, carried on the routing service's
 * `tagEnter`/`tagUpdate` events. `corners` are already in normalized viewport
 * space (0..1, origin bottom-left) so a consumer (placement, or a future rule)
 * can unproject them without knowing about image pixels or the detector.
 */
export interface TagObservation {
  readonly id: number;
  readonly name: string;
  readonly color: string;
  readonly corners: readonly NormalizedPoint[];
  readonly firstSeenMs: number;
  readonly lastSeenMs: number;
}

/** A tag that has left view (TTL expired) — carried on `tagExit`. */
export interface TagExit {
  readonly id: number;
  readonly name: string;
}

/**
 * Semantic detection events — the substrate the rules engine grows on. Consumers
 * (placement today; arbitrary "see X → do Y" rules tomorrow) subscribe here
 * rather than polling the detector, so behaviour is decoupled from detection.
 */
export type AprilTagRoutingEventMap = {
  /** A tag id became visible (was not tracked on the previous tick). */
  tagEnter: TagObservation;
  /** A tracked tag was seen again this tick (fresh corners). */
  tagUpdate: TagObservation;
  /** A tracked tag was not seen within its TTL and is now gone. */
  tagExit: TagExit;
};

/** The lifecycle phases a {@link TagRule} can trigger on. */
export type TagRulePhase = keyof AprilTagRoutingEventMap;
