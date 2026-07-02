/**
 * Minimal structural frame-source contract. The RealityCollective IWSDK bindings'
 * `IWSDKAdapter` satisfies this shape (`onFrame`), so services that need a
 * per-frame tick (e.g. the image qualifier) accept a `FrameSource` via config
 * without the library taking a dependency on `@realitycollective/service-framework-iwsdk`
 * or `@iwsdk/core`. Headless tests can pass a `MockRuntimeAdapter` (same shape).
 */
export interface FrameTick {
  /** Frame timestamp in milliseconds. */
  readonly timestamp: number;
  /** Seconds elapsed since the previous frame. */
  readonly delta: number;
}

export interface FrameSource {
  /** Subscribe to per-frame updates; returns an unsubscribe handle. */
  onFrame(listener: (tick: FrameTick) => void): () => void;
}
