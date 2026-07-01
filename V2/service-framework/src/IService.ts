/**
 * The base contract every service implements, mirroring
 * `RealityCollective.ServiceFramework.Interfaces.IService`.
 *
 * Lifecycle order driven by the {@link ServiceManager}:
 *
 *   constructor → initialize() → start() → enable()
 *      → (per frame) update() → lateUpdate()        [fixedUpdate() on the fixed step]
 *      → disable() → destroy()
 *
 * Lifecycle hooks may be synchronous or return a `Promise` (the manager awaits
 * `initialize`/`start`/`destroy`). This is the one intentional deviation from the
 * synchronous C# original: browser services (WebSocket, RTCPeerConnection,
 * getUserMedia) are inherently asynchronous.
 */
export interface IService {
  /** Human-readable service name (used in logs and diagnostics). */
  readonly name: string;

  /**
   * Execution priority. Services are initialized, started and ticked in
   * ascending priority order (lower = earlier). Mirrors the C# `Priority`.
   */
  readonly priority: number;

  /** Whether the service participates in the update loop. */
  isEnabled: boolean;

  /** Called once when the service is registered (or when the manager initializes). */
  initialize(): void | Promise<void>;

  /** Called once after all services have initialized, before the first update. */
  start(): void | Promise<void>;

  /** Restore the service to a clean state without tearing it down. */
  reset(): void;

  /** Called when the service transitions from disabled to enabled. */
  enable(): void;

  /** Called when the service transitions from enabled to disabled. */
  disable(): void;

  /** Per-frame tick. `delta` is seconds since the previous update. */
  update(delta: number): void;

  /** Runs after every service's `update` for the frame. */
  lateUpdate(delta: number): void;

  /** Fixed-timestep tick (physics-style), independent of frame rate. */
  fixedUpdate(delta: number): void;

  /** Tear down the service and release all resources. */
  destroy(): void | Promise<void>;

  /** Application gained/lost focus (XR session visibility, tab focus). */
  onApplicationFocus(focused: boolean): void;

  /** Application paused/resumed (XR session blur, backgrounded). */
  onApplicationPause(paused: boolean): void;
}
