import type { IService } from './IService';
import type { IServiceModule } from './IServiceModule';

/**
 * Convenience base class providing no-op lifecycle implementations and module
 * management, so concrete services override only what they need. Mirrors the C#
 * `BaseServiceWithConstructor` / `BaseService`.
 *
 * Registered {@link IServiceModule}s are driven automatically: the base forwards
 * every lifecycle call to enabled modules in priority order.
 */
export abstract class BaseService implements IService {
  readonly name: string;
  readonly priority: number;
  isEnabled = true;

  private readonly modules: IServiceModule[] = [];

  protected constructor(name: string, priority = 100) {
    this.name = name;
    this.priority = priority;
  }

  /** Register a module (data provider) owned by this service. */
  protected registerModule(module: IServiceModule): void {
    this.modules.push(module);
    this.modules.sort((a, b) => a.priority - b.priority);
  }

  /** Enabled modules in priority order. */
  protected get activeModules(): readonly IServiceModule[] {
    return this.modules.filter((m) => m.isEnabled);
  }

  async initialize(): Promise<void> {
    for (const m of this.activeModules) await m.initialize();
  }

  async start(): Promise<void> {
    for (const m of this.activeModules) await m.start();
  }

  reset(): void {
    for (const m of this.activeModules) m.reset();
  }

  enable(): void {
    this.isEnabled = true;
    for (const m of this.activeModules) m.enable();
  }

  disable(): void {
    for (const m of this.activeModules) m.disable();
    this.isEnabled = false;
  }

  update(delta: number): void {
    for (const m of this.activeModules) m.update(delta);
  }

  lateUpdate(delta: number): void {
    for (const m of this.activeModules) m.lateUpdate(delta);
  }

  fixedUpdate(delta: number): void {
    for (const m of this.activeModules) m.fixedUpdate(delta);
  }

  async destroy(): Promise<void> {
    // Tear down modules in reverse priority order.
    for (const m of [...this.modules].reverse()) await m.destroy();
    this.modules.length = 0;
  }

  onApplicationFocus(focused: boolean): void {
    for (const m of this.activeModules) m.onApplicationFocus(focused);
  }

  onApplicationPause(paused: boolean): void {
    for (const m of this.activeModules) m.onApplicationPause(paused);
  }
}
