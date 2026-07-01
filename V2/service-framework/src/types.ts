/**
 * Core type definitions for the Service Framework (TypeScript port of the
 * RealityCollective Service Framework's architecture).
 *
 * TypeScript erases interfaces at runtime, so — unlike the C# original which can
 * request a service by its interface `Type` — we retrieve services through a
 * strongly-typed {@link ServiceToken}. A token is a stable identity object,
 * created once per service interface as a module-level constant, that also
 * carries the service's type through the generic parameter so `getService(token)`
 * returns the correct type with no casting at the call site.
 */

import type { IService } from './IService';

/**
 * A typed identity handle used to register and resolve a service by its
 * interface. The `_type` field is a phantom (never assigned at runtime) that
 * threads the concrete service type through the generics.
 */
export interface ServiceToken<T extends IService = IService> {
  readonly id: symbol;
  readonly name: string;
  /** Phantom type marker — never read at runtime. */
  readonly _type?: T;
}

/**
 * Create a service token for an interface. Call once per interface and export
 * the result as a constant so all consumers share the same identity, e.g.:
 *
 * ```ts
 * export interface ISignalingService extends IService { ... }
 * export const ISignalingService = createServiceToken<ISignalingService>('ISignalingService');
 * ```
 */
export function createServiceToken<T extends IService>(name: string): ServiceToken<T> {
  return { id: Symbol(name), name };
}

/** Lifecycle phase a service (or the manager) is currently in. */
export enum ServiceLifecycle {
  Unregistered = 'unregistered',
  Registered = 'registered',
  Initialized = 'initialized',
  Started = 'started',
  Destroyed = 'destroyed',
}

/** Result of a registration attempt, mirroring the C# `TryRegister` pattern. */
export interface RegistrationResult {
  readonly success: boolean;
  readonly reason?: string;
}
