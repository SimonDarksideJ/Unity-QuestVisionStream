import { createServiceToken, type IService } from '@realitycollective/service-framework';
import type { AprilTagInfo } from '../../apriltags/registry';

/** Raw configuration values for the AprilTag pipeline (see AppConfig.apriltag). */
export interface AprilTagConfig {
  readonly enabled: boolean;
  /** js-aruco2 dictionary the printed/detected tags belong to. */
  readonly dictionary: string;
  /** Throttle between decode passes (ms). */
  readonly detectIntervalMs: number;
  /** Resolution the passthrough frame is sampled to before decoding. */
  readonly sampleWidth: number;
  readonly sampleHeight: number;
  /** Correct a mirrored / rotated passthrough feed (calibrate on-device). */
  readonly flipX: boolean;
  readonly flipY: boolean;
  /** Keep a placed plane this long after the tag was last seen (ms). */
  readonly tagTtlMs: number;
  /** Ignore detected ids not in the registry (drops decoder false-positives). */
  readonly knownTagsOnly: boolean;
  /** Fixed ray depth for placing a tag's plane (m). */
  readonly placementDistanceMeters: number;
}

/**
 * **Configuration** responsibility: the single source of truth for the AprilTag
 * feature's tunables AND the tag registry (id → name → colour). Every other
 * AprilTag service depends on this rather than importing AppConfig/registry
 * directly, so there is exactly one place these live.
 */
export interface IAprilTagConfigService extends IService, AprilTagConfig {
  /** Registry info for a tag id (safe fallback for unregistered ids). */
  tagInfo(id: number): AprilTagInfo;
  /** Whether a tag id is in the registry. */
  isKnownTag(id: number): boolean;
  /** All registered test tags. */
  knownTags(): readonly AprilTagInfo[];
}

export const IAprilTagConfigService =
  createServiceToken<IAprilTagConfigService>('IAprilTagConfigService');
