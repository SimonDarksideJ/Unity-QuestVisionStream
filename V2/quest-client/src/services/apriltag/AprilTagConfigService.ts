import { BaseService, type ServiceActivationContext } from '@realitycollective/service-framework';
import { isKnownTag, knownTags, tagInfo, type AprilTagInfo } from '../../apriltags/registry';
import type { AprilTagConfig, IAprilTagConfigService } from './IAprilTagConfigService';

/**
 * Configuration service — exposes the {@link AprilTagConfig} it was registered
 * with plus the tag registry. Pure data + lookups; holds no detection or render
 * state, so it can be depended on by every other AprilTag service without cycles.
 */
export class AprilTagConfigService
  extends BaseService<AprilTagConfig>
  implements IAprilTagConfigService
{
  constructor(context: ServiceActivationContext<AprilTagConfig>) {
    super(context);
  }

  get enabled(): boolean {
    return this.serviceConfig.enabled;
  }
  get dictionary(): string {
    return this.serviceConfig.dictionary;
  }
  get detectIntervalMs(): number {
    return this.serviceConfig.detectIntervalMs;
  }
  get sampleWidth(): number {
    return this.serviceConfig.sampleWidth;
  }
  get sampleHeight(): number {
    return this.serviceConfig.sampleHeight;
  }
  get flipX(): boolean {
    return this.serviceConfig.flipX;
  }
  get flipY(): boolean {
    return this.serviceConfig.flipY;
  }
  get tagTtlMs(): number {
    return this.serviceConfig.tagTtlMs;
  }
  get knownTagsOnly(): boolean {
    return this.serviceConfig.knownTagsOnly;
  }
  get placementDistanceMeters(): number {
    return this.serviceConfig.placementDistanceMeters;
  }

  tagInfo(id: number): AprilTagInfo {
    return tagInfo(id);
  }
  isKnownTag(id: number): boolean {
    return isKnownTag(id);
  }
  knownTags(): readonly AprilTagInfo[] {
    return knownTags();
  }
}
