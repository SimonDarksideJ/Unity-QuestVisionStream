# @realitycollective/service-framework-ts

A faithful **TypeScript port of the architecture** of the [RealityCollective
Service Framework](https://github.com/realitycollective/com.realitycollective.service-framework).

The upstream framework is a Unity/C# package (used by MRTK, XRTK and the Reality
Toolkit) with **no official JavaScript/TypeScript build**. This package
reproduces its core concepts so browser/WebXR apps can use the same clean,
DI-driven service architecture for orchestration.

## Why

WebXR apps accumulate cross-cutting concerns — signaling, WebRTC, detection
parsing, image qualification, rendering — that don't belong in the render/ECS
loop and shouldn't be wired by hand or reached through globals. The Service
Framework gives each concern a **service** with a well-defined lifecycle,
resolved by **interface**, orchestrated centrally.

## Concepts (mirrors the C# framework)

| Concept | Here | C# original |
|---------|------|-------------|
| Registry + orchestrator | `ServiceManager` | `ServiceManager` |
| Service contract | `IService` | `IService` |
| Base implementation | `BaseService` | `BaseServiceWithConstructor` |
| Sub-service / data provider | `IServiceModule` / `BaseServiceModule` | `IServiceModule` |
| Configuration | `IServiceProfile<TConfig>` | `IServiceProfile<T>` |
| Resolve by interface | `ServiceToken<T>` | request by `Type` |

Interfaces are erased at runtime in TS, so services are addressed by a typed
**`ServiceToken`** (a stable identity created once per interface) instead of a
C# `Type`. `getService(token)` returns the correct type with no casting.

## Lifecycle

```
constructor → initialize() → start() → enable()
   → (per frame) update() → lateUpdate()      [fixedUpdate() on the fixed step]
   → disable() → destroy()
```

Services run in ascending `priority` order. Lifecycle hooks may be sync or async
(`initialize`/`start`/`destroy` are awaited) — the one deliberate deviation from
the synchronous C# original, because browser services (WebSocket,
`RTCPeerConnection`, `getUserMedia`) are inherently asynchronous.

## Usage

```ts
import { ServiceManager, BaseService, createServiceToken, type IService } from '@realitycollective/service-framework-ts';

export interface IClock extends IService { now(): number; }
export const IClock = createServiceToken<IClock>('IClock');

class SystemClock extends BaseService implements IClock {
  constructor() { super('SystemClock', /* priority */ 10); }
  now() { return performance.now(); }
}

const manager = ServiceManager.instance;
manager.registerService(IClock, new SystemClock());
await manager.start();

// Anywhere, resolved by interface — no import of the concrete class:
const clock = manager.getService(IClock);

// Host pumps the loop (e.g. from an IWSDK System's update):
manager.update(deltaSeconds);
```

The manager is host-agnostic — it has no dependency on IWSDK, Three.js or the
DOM. A host pumps `update(delta)` each frame; everything else is portable.
