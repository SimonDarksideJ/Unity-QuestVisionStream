import type { IService } from './IService';
import { ServiceLifecycle, type RegistrationResult, type ServiceToken } from './types';

/**
 * The central service registry and lifecycle orchestrator — the TypeScript
 * analogue of `RealityCollective.ServiceFramework.ServiceManager`.
 *
 * Responsibilities:
 *  - register/resolve services by their interface {@link ServiceToken};
 *  - drive the shared lifecycle (initialize → start → update loop → destroy) in
 *    deterministic ascending-priority order;
 *  - forward focus/pause and enable/disable transitions.
 *
 * The manager is host-agnostic: it does not know about IWSDK, Three.js or the
 * DOM. A host (e.g. an IWSDK System) pumps it by calling `update(delta)` each
 * frame. This keeps the streaming/orchestration services fully reusable across
 * any WebXR or browser host.
 */
export class ServiceManager {
  private static _instance: ServiceManager | undefined;

  /** Shared instance. Most apps use one manager; tests may construct their own. */
  static get instance(): ServiceManager {
    return (ServiceManager._instance ??= new ServiceManager());
  }

  /** Replace/clear the shared instance (primarily for tests and hot-reload). */
  static setInstance(instance: ServiceManager | undefined): void {
    ServiceManager._instance = instance;
  }

  private readonly services = new Map<symbol, IService>();
  private ordered: IService[] = [];
  private lifecycle: ServiceLifecycle = ServiceLifecycle.Registered;

  /** Current lifecycle phase of the manager. */
  get phase(): ServiceLifecycle {
    return this.lifecycle;
  }

  // ---------------------------------------------------------------- registration

  /**
   * Register a service under its interface token. If the manager has already
   * initialized/started, the new service is brought up to the current phase
   * immediately (mirrors runtime registration in the C# framework).
   */
  registerService<T extends IService>(token: ServiceToken<T>, instance: T): RegistrationResult {
    if (this.services.has(token.id)) {
      return { success: false, reason: `Service '${token.name}' is already registered.` };
    }
    this.services.set(token.id, instance);
    this.reorder();

    // Catch a late registration up to the manager's current phase.
    if (
      this.lifecycle === ServiceLifecycle.Initialized ||
      this.lifecycle === ServiceLifecycle.Started
    ) {
      void Promise.resolve(instance.initialize()).then(() => {
        if (this.lifecycle === ServiceLifecycle.Started) return instance.start();
      });
    }
    return { success: true };
  }

  /** Resolve a service or return `undefined` if not registered. */
  tryGetService<T extends IService>(token: ServiceToken<T>): T | undefined {
    return this.services.get(token.id) as T | undefined;
  }

  /** Resolve a service, throwing if it is not registered. */
  getService<T extends IService>(token: ServiceToken<T>): T {
    const svc = this.services.get(token.id);
    if (!svc) throw new Error(`Service '${token.name}' is not registered.`);
    return svc as T;
  }

  isServiceRegistered<T extends IService>(token: ServiceToken<T>): boolean {
    return this.services.has(token.id);
  }

  /** Unregister and destroy a service. */
  async unregisterService<T extends IService>(token: ServiceToken<T>): Promise<boolean> {
    const svc = this.services.get(token.id);
    if (!svc) return false;
    await svc.destroy();
    this.services.delete(token.id);
    this.reorder();
    return true;
  }

  /** All registered services in ascending priority order. */
  getAllServices(): readonly IService[] {
    return this.ordered;
  }

  // ------------------------------------------------------------------- lifecycle

  /** Initialize every registered service (ascending priority). Idempotent. */
  async initialize(): Promise<void> {
    if (this.lifecycle === ServiceLifecycle.Initialized || this.lifecycle === ServiceLifecycle.Started) {
      return;
    }
    for (const svc of this.ordered) await svc.initialize();
    this.lifecycle = ServiceLifecycle.Initialized;
  }

  /** Start every registered service. Initializes first if needed. */
  async start(): Promise<void> {
    if (this.lifecycle !== ServiceLifecycle.Initialized) await this.initialize();
    for (const svc of this.ordered) await svc.start();
    this.lifecycle = ServiceLifecycle.Started;
  }

  /** Per-frame tick. Host calls this once per animation frame. */
  update(delta: number): void {
    for (const svc of this.ordered) if (svc.isEnabled) svc.update(delta);
  }

  /** Late tick, after all `update`s for the frame. */
  lateUpdate(delta: number): void {
    for (const svc of this.ordered) if (svc.isEnabled) svc.lateUpdate(delta);
  }

  /** Fixed-timestep tick. */
  fixedUpdate(delta: number): void {
    for (const svc of this.ordered) if (svc.isEnabled) svc.fixedUpdate(delta);
  }

  onApplicationFocus(focused: boolean): void {
    for (const svc of this.ordered) svc.onApplicationFocus(focused);
  }

  onApplicationPause(paused: boolean): void {
    for (const svc of this.ordered) svc.onApplicationPause(paused);
  }

  /** Destroy every service (reverse priority order) and clear the registry. */
  async destroy(): Promise<void> {
    for (const svc of [...this.ordered].reverse()) await svc.destroy();
    this.services.clear();
    this.ordered = [];
    this.lifecycle = ServiceLifecycle.Destroyed;
  }

  private reorder(): void {
    this.ordered = [...this.services.values()].sort((a, b) => a.priority - b.priority);
  }
}
