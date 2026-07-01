/**
 * @realitycollective/service-framework-ts
 *
 * A faithful TypeScript port of the architecture of the RealityCollective
 * Service Framework (https://github.com/realitycollective/com.realitycollective.service-framework),
 * which is a Unity/C# package with no official JS/TS build. This port reproduces
 * its core concepts — a {@link ServiceManager} orchestrating interface-addressed
 * {@link IService}s with a shared lifecycle, composed of {@link IServiceModule}
 * data providers and configured by {@link IServiceProfile}s — so WebXR/browser
 * apps can use the same clean, DI-driven service architecture.
 */
export type { IService } from './IService';
export type { IServiceModule } from './IServiceModule';
export type { IServiceProfile, ServiceConfiguration } from './IServiceProfile';
export { BaseService } from './BaseService';
export { BaseServiceModule } from './BaseServiceModule';
export { ServiceManager } from './ServiceManager';
export {
  ServiceLifecycle,
  createServiceToken,
  type ServiceToken,
  type RegistrationResult,
} from './types';
