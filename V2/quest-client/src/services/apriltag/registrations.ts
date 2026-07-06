import {
  type ServiceActivationContext,
  type ServiceRegistration,
} from '@realitycollective/service-framework';
import { AppConfig } from '../../config';
import { APRILTAG_DICTIONARY } from '../../apriltags/registry';
import { AprilTagConfigService } from './AprilTagConfigService';
import { AprilTagDetectionService } from './AprilTagDetectionService';
import { AprilTagRoutingService } from './AprilTagRoutingService';
import { AprilTagPlacementService } from './AprilTagPlacementService';
import { IAprilTagConfigService, type AprilTagConfig } from './IAprilTagConfigService';
import { IAprilTagDetectionService } from './IAprilTagDetectionService';
import { IAprilTagRoutingService } from './IAprilTagRoutingService';
import { IAprilTagPlacementService } from './IAprilTagPlacementService';

/**
 * The four AprilTag services as RealityCollective registrations, composed into
 * the app profile alongside the library's streaming services (see index.ts).
 * Priority order mirrors the data flow: config → detection → routing → placement
 * (placement depends on routing so it can subscribe to its events on start).
 */
export function createAprilTagRegistrations(): ServiceRegistration[] {
  const config: AprilTagConfig = {
    enabled: AppConfig.apriltag.enabled,
    dictionary: APRILTAG_DICTIONARY,
    detectIntervalMs: AppConfig.apriltag.detectIntervalMs,
    sampleWidth: AppConfig.apriltag.width,
    sampleHeight: AppConfig.apriltag.height,
    flipX: AppConfig.apriltag.flipX,
    flipY: AppConfig.apriltag.flipY,
    tagTtlMs: AppConfig.apriltag.tagTtlMs,
    knownTagsOnly: AppConfig.apriltag.knownTagsOnly,
    placementDistanceMeters: AppConfig.placementDistanceMeters,
  };

  return [
    {
      token: IAprilTagConfigService,
      priority: 40,
      config,
      useFactory: (ctx) =>
        new AprilTagConfigService(ctx as ServiceActivationContext<AprilTagConfig>),
    },
    {
      token: IAprilTagDetectionService,
      priority: 41,
      dependencies: [IAprilTagConfigService],
      useFactory: (ctx, cfg) =>
        new AprilTagDetectionService(
          ctx as ServiceActivationContext,
          cfg as IAprilTagConfigService,
        ),
    },
    {
      token: IAprilTagRoutingService,
      priority: 42,
      dependencies: [IAprilTagConfigService],
      useFactory: (ctx, cfg) =>
        new AprilTagRoutingService(
          ctx as ServiceActivationContext,
          cfg as IAprilTagConfigService,
        ),
    },
    {
      token: IAprilTagPlacementService,
      priority: 43,
      dependencies: [IAprilTagConfigService, IAprilTagRoutingService],
      useFactory: (ctx, cfg, routing) =>
        new AprilTagPlacementService(
          ctx as ServiceActivationContext,
          cfg as IAprilTagConfigService,
          routing as IAprilTagRoutingService,
        ),
    },
  ];
}
