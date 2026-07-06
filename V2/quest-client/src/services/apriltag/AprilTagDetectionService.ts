import { BaseService, type ServiceActivationContext } from '@realitycollective/service-framework';
import { createLogger } from '@questvisionstream/client';
import { createAprilTagDetector, type ArucoDetector, type DetectedMarker } from '../../apriltags/detector';
import type { IAprilTagConfigService } from './IAprilTagConfigService';
import type { DetectorFrame, IAprilTagDetectionService } from './IAprilTagDetectionService';

const log = createLogger('AprilTagDetection');

/**
 * Detection service — wraps the vendored js-aruco2 detector. The detector is
 * created lazily on {@link start} from the dictionary the config service names,
 * and may be null if the vendored classic scripts didn't load (offline / a
 * headless test): callers see `available === false` and get `[]`, never a throw.
 */
export class AprilTagDetectionService
  extends BaseService
  implements IAprilTagDetectionService
{
  private readonly config: IAprilTagConfigService;
  private detector: ArucoDetector | null = null;

  constructor(context: ServiceActivationContext, config: IAprilTagConfigService) {
    super(context);
    this.config = config;
  }

  get available(): boolean {
    return this.detector !== null;
  }

  override start(): void {
    this.detector = createAprilTagDetector(this.config.dictionary);
    log.info(this.detector ? `detector ready (${this.config.dictionary})` : 'detector unavailable');
  }

  detect(frame: DetectorFrame): DetectedMarker[] {
    if (!this.detector) return [];
    try {
      return this.detector.detectImage(frame.width, frame.height, frame.data);
    } catch (err) {
      log.warn('detectImage failed', err);
      return [];
    }
  }
}
