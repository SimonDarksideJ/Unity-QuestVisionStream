import type { IService } from './IService';
import type { IServiceModule } from './IServiceModule';

/**
 * Convenience base class for service modules (data providers). Provides no-op
 * lifecycle hooks and holds a reference to the parent service. Mirrors the C#
 * `BaseServiceModule`.
 */
export abstract class BaseServiceModule implements IServiceModule {
  readonly name: string;
  readonly priority: number;
  readonly parentService: IService;
  isEnabled = true;

  protected constructor(name: string, parentService: IService, priority = 100) {
    this.name = name;
    this.parentService = parentService;
    this.priority = priority;
  }

  initialize(): void | Promise<void> {}
  start(): void | Promise<void> {}
  reset(): void {}
  enable(): void {
    this.isEnabled = true;
  }
  disable(): void {
    this.isEnabled = false;
  }
  update(_delta: number): void {}
  lateUpdate(_delta: number): void {}
  fixedUpdate(_delta: number): void {}
  destroy(): void | Promise<void> {}
  onApplicationFocus(_focused: boolean): void {}
  onApplicationPause(_paused: boolean): void {}
}
