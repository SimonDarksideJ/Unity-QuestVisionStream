import type { ServiceManager } from '@realitycollective/service-framework';

/**
 * Holds the app's `ServiceManager` so IWSDK systems (which are instantiated by
 * the world, not by us) can resolve services by interface token. Set once in
 * `index.ts` after `startServiceRuntime`.
 */
let manager: ServiceManager | undefined;

export function setServiceManager(instance: ServiceManager): void {
  manager = instance;
}

export function getServiceManager(): ServiceManager {
  if (!manager) throw new Error('ServiceManager not initialized yet');
  return manager;
}
