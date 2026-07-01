import {
  createServiceProfile,
  type ServiceActivationContext,
  type ServiceProfile,
  type ServiceRegistration,
} from '@realitycollective/service-framework';
import { setLogLevel, type LogLevel } from '../util/logger';
import type { FrameSource } from '../frame-source';
import { ISignalingService } from '../services/signaling/ISignalingService';
import { SignalingService, type SignalingConfig } from '../services/signaling/SignalingService';
import { IWebRTCService } from '../services/webrtc/IWebRTCService';
import { WebRTCService, type WebRTCConfig, type IceServerConfig } from '../services/webrtc/WebRTCService';
import { IDetectionService } from '../services/detection/IDetectionService';
import { DetectionService, type DetectionConfig } from '../services/detection/DetectionService';
import { IImageQualifierService } from '../services/qualifier/IImageQualifierService';
import {
  ImageQualifierService,
  type ImageQualifierConfig,
} from '../services/qualifier/ImageQualifierService';
import {
  BrightnessQualifierModule,
  IBrightnessQualifierModule,
  type BrightnessQualifierConfig,
} from '../services/qualifier/modules/BrightnessQualifierModule';

export interface QuestVisionStreamOptions {
  /** WebSocket signaling URL, e.g. `ws://192.168.1.20:3000`. */
  readonly signalingUrl: string;
  /** ICE servers. Defaults to Google STUN (LAN direct). Add TURN for remote. */
  readonly iceServers?: readonly IceServerConfig[];
  /** Client-side image qualifier gate. Enabled by default. */
  readonly qualifier?: {
    readonly enabled?: boolean;
    readonly sampleIntervalMs?: number;
    readonly brightness?: BrightnessQualifierConfig;
  };
  /** Log verbosity. Default 'info'. */
  readonly logLevel?: LogLevel;
}

/**
 * Build the RealityCollective service registrations for the streaming graph.
 * Priority-ordered lifecycle:
 *   SignalingService(10) → ImageQualifierService(15) → WebRTCService(20)
 *      → DetectionService(30)
 * Dependencies are declared via `dependencies` and injected as constructor args.
 */
export function createQuestVisionStreamRegistrations(
  options: QuestVisionStreamOptions,
  frameSource: FrameSource,
): ServiceRegistration[] {
  if (options.logLevel) setLogLevel(options.logLevel);
  const qualifierEnabled = options.qualifier?.enabled ?? true;

  return [
    {
      token: ISignalingService,
      priority: 10,
      config: { url: options.signalingUrl, autoConnect: true } satisfies SignalingConfig,
      useFactory: (ctx) =>
        new SignalingService(ctx as ServiceActivationContext<SignalingConfig>),
    },
    {
      token: IImageQualifierService,
      priority: 15,
      config: {
        frameSource,
        sampleIntervalMs: options.qualifier?.sampleIntervalMs,
      } satisfies ImageQualifierConfig,
      useFactory: (ctx) =>
        new ImageQualifierService(ctx as ServiceActivationContext<ImageQualifierConfig>),
      modules: qualifierEnabled
        ? [
            {
              token: IBrightnessQualifierModule,
              config: options.qualifier?.brightness ?? {},
              useFactory: (ctx) =>
                new BrightnessQualifierModule(
                  ctx as ServiceActivationContext<
                    BrightnessQualifierConfig,
                    IImageQualifierService
                  >,
                ),
            },
          ]
        : [],
    },
    {
      token: IWebRTCService,
      priority: 20,
      dependencies: [ISignalingService],
      config: { iceServers: options.iceServers } satisfies WebRTCConfig,
      useFactory: (ctx, signaling) =>
        new WebRTCService(
          ctx as ServiceActivationContext<WebRTCConfig>,
          signaling as ISignalingService,
        ),
    },
    {
      token: IDetectionService,
      priority: 30,
      dependencies: [IWebRTCService],
      config: {} satisfies DetectionConfig,
      useFactory: (ctx, webrtc) =>
        new DetectionService(
          ctx as ServiceActivationContext<DetectionConfig>,
          webrtc as IWebRTCService,
        ),
    },
  ];
}

/** Convenience: wrap {@link createQuestVisionStreamRegistrations} in a profile. */
export function createQuestVisionStreamProfile(
  name: string,
  options: QuestVisionStreamOptions,
  frameSource: FrameSource,
): ServiceProfile {
  return createServiceProfile(name, createQuestVisionStreamRegistrations(options, frameSource));
}
