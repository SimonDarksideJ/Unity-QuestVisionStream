import { createSystem } from '@iwsdk/core';
import { ServiceManager } from '@realitycollective/service-framework-ts';

/**
 * Bridges the IWSDK/ECS frame loop into the Service Framework: pumps the shared
 * {@link ServiceManager} once per frame so every registered streaming service
 * ticks. Registered with a low priority so services update before rendering
 * systems consume their state.
 *
 * `delta` from IWSDK is in seconds; the services expect seconds.
 */
export class ServicePumpSystem extends createSystem({}) {
  override update(delta: number): void {
    ServiceManager.instance.update(delta);
  }
}
