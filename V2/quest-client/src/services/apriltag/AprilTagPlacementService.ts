import { BaseService, type ServiceActivationContext } from '@realitycollective/service-framework';
import * as THREE from 'three';
import { unprojectThroughSnapshot } from '../../rendering/PoseHistory';
import { createTagPlane, disposeTagObject } from '../../rendering/TagFactory';
import type { IAprilTagConfigService } from './IAprilTagConfigService';
import type { IAprilTagRoutingService } from './IAprilTagRoutingService';
import type { IAprilTagPlacementService, SnapshotProvider } from './IAprilTagPlacementService';
import type { TagExit, TagObservation } from './types';

/**
 * Placement service. Subscribes to the routing service's tag lifecycle events
 * and maintains one coloured plane per visible tag: `tagEnter`/`tagUpdate`
 * unproject the tag's viewport corners through the capture-time pose and
 * (re)draw the plane; `tagExit` removes it. The scene + pose source arrive via
 * {@link attach} from the host System — until then events are safely ignored.
 */
export class AprilTagPlacementService
  extends BaseService
  implements IAprilTagPlacementService
{
  private readonly config: IAprilTagConfigService;
  private readonly routing: IAprilTagRoutingService;
  private readonly planes = new Map<number, THREE.Object3D>();
  private readonly unsubs: Array<() => void> = [];
  private scene: THREE.Scene | undefined;
  private snapshotProvider: SnapshotProvider | undefined;

  constructor(
    context: ServiceActivationContext,
    config: IAprilTagConfigService,
    routing: IAprilTagRoutingService,
  ) {
    super(context);
    this.config = config;
    this.routing = routing;
  }

  override start(): void {
    this.unsubs.push(
      this.routing.on('tagEnter', (obs) => this.upsert(obs)),
      this.routing.on('tagUpdate', (obs) => this.upsert(obs)),
      this.routing.on('tagExit', (exit) => this.remove(exit)),
    );
  }

  attach(scene: THREE.Scene, snapshotProvider: SnapshotProvider): void {
    this.scene = scene;
    this.snapshotProvider = snapshotProvider;
  }

  private upsert(obs: TagObservation): void {
    if (!this.scene || !this.snapshotProvider || obs.corners.length !== 4) return;
    const snapshot = this.snapshotProvider();
    if (!snapshot) return;

    const corners = obs.corners.map((vp) =>
      unprojectThroughSnapshot(snapshot, vp, this.config.placementDistanceMeters),
    );
    const object = createTagPlane(corners, `${obs.name} · #${obs.id}`, obs.color);

    const prev = this.planes.get(obs.id);
    if (prev) {
      this.scene.remove(prev);
      disposeTagObject(prev);
    }
    this.scene.add(object);
    this.planes.set(obs.id, object);
  }

  private remove(exit: TagExit): void {
    const object = this.planes.get(exit.id);
    if (!object) return;
    this.scene?.remove(object);
    disposeTagObject(object);
    this.planes.delete(exit.id);
  }

  clear(): void {
    for (const object of this.planes.values()) {
      this.scene?.remove(object);
      disposeTagObject(object);
    }
    this.planes.clear();
  }

  override destroy(): void {
    for (const unsub of this.unsubs) unsub();
    this.unsubs.length = 0;
    this.clear();
    super.destroy();
  }
}
