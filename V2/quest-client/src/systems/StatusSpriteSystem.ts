import { createSystem } from '@iwsdk/core';
import * as THREE from 'three';
import { createTextSprite, disposeTagObject, setTagLabel } from '../rendering/TagFactory';
import { status as appStatus, type StatusModel } from '../ui/status';

/** Head-relative HUD placement: slightly below the gaze line, ~1m out. */
const HUD_OFFSET = new THREE.Vector3(0, -0.18, -1.0);

/**
 * In-AR rendering of the {@link StatusModel}: the DOM status panel is not
 * visible inside an immersive session, so this system parents a text sprite
 * to the persistent player head entity (`world.playerHeadEntity`, verified in
 * the installed @iwsdk/core 0.4.2 typings) showing the current headline. It
 * hides itself when the model reports healthy, and re-renders only when the
 * headline actually changes.
 */
export class StatusSpriteSystem extends createSystem({}) {
  /** Injectable for tests; defaults to the app-wide model. */
  private model: StatusModel = appStatus;
  private hud: THREE.Group | undefined;
  private lastHeadline: string | null = null;
  private unsub: (() => void) | undefined;

  override init(): void {
    const head = (
      this.world as unknown as { playerHeadEntity?: { object3D?: THREE.Object3D | null } }
    )?.playerHeadEntity?.object3D;
    if (!head) return; // no head rig (e.g. before XR) — nothing to attach to

    const hud = new THREE.Group();
    hud.add(createTextSprite(this.model.headline() ?? ''));
    hud.position.copy(HUD_OFFSET);
    head.add(hud);
    this.hud = hud;

    this.apply();
    this.unsub = this.model.onChange(() => this.apply());
  }

  private apply(): void {
    if (!this.hud) return;
    const headline = this.model.headline();
    this.hud.visible = headline !== null;
    if (headline !== null && headline !== this.lastHeadline) {
      setTagLabel(this.hud, headline);
    }
    this.lastHeadline = headline;
  }

  override destroy(): void {
    this.unsub?.();
    if (this.hud) {
      this.hud.parent?.remove(this.hud);
      disposeTagObject(this.hud);
      this.hud = undefined;
    }
  }
}
