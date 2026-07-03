import { createSystem } from '@iwsdk/core';
import * as THREE from 'three';
import {
  createStatusDot,
  createTextPanel,
  disposeTagObject,
  setTextPanel,
  STATUS_GREEN,
  STATUS_RED,
} from '../rendering/TagFactory';
import { status as appStatus, type StatusModel } from '../ui/status';

/** Head-relative placement (metres, at ~1 m out). Tune on-device if needed. */
const PANEL_POS = new THREE.Vector3(-0.42, 0.24, -1.0); // top-left, grows down
const DOT_POS = new THREE.Vector3(0.46, 0.3, -1.0); // top-right

/**
 * In-AR rendering of the {@link StatusModel}: the DOM status panel and activity
 * log are invisible inside an immersive session, so this system parents a HUD to
 * the persistent player head entity (`world.playerHeadEntity`, verified in the
 * installed @iwsdk/core 0.4.2 typings). The HUD is two head-locked pieces:
 *
 *  - a **fixed multi-line panel** (half the old label text size) that expands
 *    vertically as status lines populate, and
 *  - a small **green/red connection dot** in the top-right — green once the
 *    WebRTC path is up, red otherwise — for an at-a-glance "am I connected?".
 *
 * Both re-render only when their inputs actually change.
 */
export class StatusSpriteSystem extends createSystem({}) {
  /** Injectable for tests; defaults to the app-wide model. */
  private model: StatusModel = appStatus;
  private hud: THREE.Group | undefined;
  private panel: THREE.Sprite | undefined;
  private dot: THREE.Sprite | undefined;
  private lastLines = '';
  private lastConnected: boolean | undefined;
  private unsub: (() => void) | undefined;

  override init(): void {
    const head = (
      this.world as unknown as { playerHeadEntity?: { object3D?: THREE.Object3D | null } }
    )?.playerHeadEntity?.object3D;
    if (!head) return; // no head rig (e.g. before XR) — nothing to attach to

    const hud = new THREE.Group();

    const panel = createTextPanel(this.model.panelLines());
    panel.position.copy(PANEL_POS);
    hud.add(panel);

    const dot = createStatusDot(STATUS_RED);
    dot.position.copy(DOT_POS);
    hud.add(dot);

    head.add(hud);
    this.hud = hud;
    this.panel = panel;
    this.dot = dot;

    this.apply();
    this.unsub = this.model.onChange(() => this.apply());
  }

  private apply(): void {
    if (this.panel) {
      const lines = this.model.panelLines();
      const joined = lines.join('\n');
      if (joined !== this.lastLines) {
        setTextPanel(this.panel, lines);
        this.lastLines = joined;
      }
    }
    if (this.dot) {
      const connected = this.model.isConnected();
      if (connected !== this.lastConnected) {
        this.dot.material.color.set(connected ? STATUS_GREEN : STATUS_RED);
        this.lastConnected = connected;
      }
    }
  }

  override destroy(): void {
    this.unsub?.();
    if (this.hud) {
      this.hud.parent?.remove(this.hud);
      disposeTagObject(this.hud);
      this.hud = undefined;
      this.panel = undefined;
      this.dot = undefined;
    }
  }
}
