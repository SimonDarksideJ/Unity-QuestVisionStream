import type { IService } from './IService';

/**
 * A service module (the framework's "data provider" concept): a sub-service
 * owned by a parent service. Modules share the parent's lifecycle — the parent
 * forwards `initialize`/`start`/`update`/… to its registered modules — and are
 * how a service composes swappable back-ends.
 *
 * Example: an `ImageQualifierService` owns one or more `IImageQualifierModule`s
 * (brightness, blur, exposure), each contributing a score.
 *
 * Mirrors `RealityCollective.ServiceFramework.Interfaces.IServiceModule`.
 */
export interface IServiceModule extends IService {
  /** The service that owns and drives this module. */
  readonly parentService: IService;
}
