import type { IService } from './IService';

/**
 * Configuration profile for a service, mirroring the C# `IServiceProfile<T>` /
 * ScriptableObject profiles. In Unity these are authored assets; here a profile
 * is a plain, serializable configuration object passed to the service's
 * constructor. Keeping configuration in a typed profile (rather than scattered
 * constructor args) matches the framework's "configure by profile" convention
 * and makes services trivially reconfigurable and testable.
 */
export interface IServiceProfile<TConfig extends object = object> {
  /** The typed configuration payload for the owning service. */
  readonly configuration: Readonly<TConfig>;
}

/**
 * Descriptor used when registering a service together with its configuration —
 * the analogue of a C# `ServiceConfiguration` entry in a profile. Useful for
 * data-driven bootstrapping where the set of services is declared rather than
 * wired by hand.
 */
export interface ServiceConfiguration<T extends IService = IService> {
  /** The concrete service instance (or a factory that builds it). */
  readonly instance: T | (() => T);
  /** Optional runtime platform gate; when false the service is skipped. */
  readonly enabled?: boolean;
}
