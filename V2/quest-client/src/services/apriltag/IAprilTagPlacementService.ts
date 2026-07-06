import { createServiceToken, type IService } from '@realitycollective/service-framework';
import type * as THREE from 'three';
import type { CameraPoseSnapshot } from '../../rendering/PoseHistory';

/** Supplies the capture-time camera pose for unprojection, or null if unknown. */
export type SnapshotProvider = () => CameraPoseSnapshot | null;

/**
 * **Placement** responsibility: turn routing's tag lifecycle events into 3D
 * scene objects — unproject the tag's viewport corners through the capture-time
 * pose and draw/replace/remove a coloured plane. It is a pure *consumer* of
 * routing events (subscribes on start), so it holds no detection logic; the only
 * thing it needs from the host System is the render surface + a pose provider,
 * injected via {@link attach} (the scene isn't known at DI-construction time).
 */
export interface IAprilTagPlacementService extends IService {
  /** Provide the scene + capture-pose source. Called once by the host System. */
  attach(scene: THREE.Scene, snapshotProvider: SnapshotProvider): void;
  /** Remove every placed plane (e.g. on reference-space reset). */
  clear(): void;
}

export const IAprilTagPlacementService =
  createServiceToken<IAprilTagPlacementService>('IAprilTagPlacementService');
