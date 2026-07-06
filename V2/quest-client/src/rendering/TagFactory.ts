import * as THREE from 'three';

/** Detection accent colour — red, matching the Unity reference outline boxes. */
export const DETECTION_RED = '#ff3b30';

/**
 * A hollow **red bounding box** with a label — the WebXR analogue of the Unity
 * `SentisInferenceUiManager` 2D outline box (`fillCenter=false`), placed in 3D
 * at the detection's capture-time pose.
 *
 * `corners` are the four box corners already unprojected to WORLD space
 * (TL, TR, BR, BL order). The `LineLoop` closes automatically, so the four
 * points draw a closed rectangle. The label sits just above the top edge.
 */
export function createDetectionBox(
  corners: readonly THREE.Vector3[],
  label: string,
): THREE.Object3D {
  const group = new THREE.Group();

  const geometry = new THREE.BufferGeometry().setFromPoints(corners as THREE.Vector3[]);
  const outline = new THREE.LineLoop(
    geometry,
    new THREE.LineBasicMaterial({ color: DETECTION_RED }),
  );
  group.add(outline);

  // Label anchored to the visually-topmost corner, nudged up so it clears the line.
  const top = corners.reduce((a, b) => (b.y > a.y ? b : a), corners[0]!);
  const sprite = createTextSprite(label, DETECTION_RED);
  sprite.position.copy(top);
  sprite.position.y += 0.035;
  group.add(sprite);

  return group;
}

/**
 * A **filled, colour-tinted plane** covering an AprilTag, plus a solid outline
 * and a name label — the visual anchor an interactive 3D model can later be
 * parented to. `corners` are the tag's four corners already unprojected to WORLD
 * space (the detector's corner order); the quad is two triangles over them.
 *
 * Rendered double-sided and depth-write-off (it's a translucent overlay), so it
 * reads clearly against passthrough regardless of which way the tag faces.
 */
export function createTagPlane(
  corners: readonly THREE.Vector3[],
  label: string,
  colorHex: string,
): THREE.Object3D {
  const group = new THREE.Group();
  const color = new THREE.Color(colorHex);

  // Filled quad: corners 0-1-2 and 0-2-3 (fan) over the four unprojected points.
  const geometry = new THREE.BufferGeometry().setFromPoints(corners as THREE.Vector3[]);
  geometry.setIndex([0, 1, 2, 0, 2, 3]);
  geometry.computeVertexNormals();
  const fill = new THREE.Mesh(
    geometry,
    new THREE.MeshBasicMaterial({
      color,
      transparent: true,
      opacity: 0.35,
      side: THREE.DoubleSide,
      depthWrite: false,
    }),
  );
  group.add(fill);

  // Solid outline for a crisp edge on top of the translucent fill.
  const outline = new THREE.LineLoop(
    new THREE.BufferGeometry().setFromPoints(corners as THREE.Vector3[]),
    new THREE.LineBasicMaterial({ color }),
  );
  group.add(outline);

  // Label at the visually-topmost corner, nudged clear of the edge.
  const top = corners.reduce((a, b) => (b.y > a.y ? b : a), corners[0]!);
  const sprite = createTextSprite(label, colorHex);
  sprite.position.copy(top);
  sprite.position.y += 0.04;
  group.add(sprite);

  return group;
}

/**
 * Builds the visual for a world-anchored detection tag: a small emissive marker
 * plus a billboarded text label rendered to a canvas texture. This is the WebXR
 * analogue of the Unity `DetectionTag` prefab (`DetectionTagController`), minus
 * the spin — kept deliberately lightweight so many tags stay cheap.
 */
export function createTagObject(label: string): THREE.Object3D {
  const group = new THREE.Group();

  // Marker cube.
  const marker = new THREE.Mesh(
    new THREE.BoxGeometry(0.05, 0.05, 0.05),
    new THREE.MeshStandardMaterial({
      color: 0x00e5ff,
      emissive: 0x00e5ff,
      emissiveIntensity: 0.6,
    }),
  );
  group.add(marker);

  // Text label as a billboarded sprite.
  const sprite = createTextSprite(label);
  sprite.position.set(0, 0.08, 0);
  group.add(sprite);

  return group;
}

/**
 * Dispose everything a tag owns on the GPU: geometries, materials, and —
 * crucially — material texture maps. `Material.dispose()` does NOT dispose
 * its `.map`, so without this every torn-down tag leaked one `CanvasTexture`.
 */
export function disposeTagObject(root: THREE.Object3D): void {
  root.traverse((obj) => {
    const mesh = obj as THREE.Mesh & { material?: THREE.Material | THREE.Material[] };
    mesh.geometry?.dispose?.();
    const materials = Array.isArray(mesh.material)
      ? mesh.material
      : mesh.material
        ? [mesh.material]
        : [];
    for (const material of materials) {
      (material as THREE.Material & { map?: THREE.Texture | null }).map?.dispose();
      material.dispose();
    }
  });
}

/** Update the text of a tag created by {@link createTagObject}. */
export function setTagLabel(tag: THREE.Object3D, label: string): void {
  const sprite = tag.children.find((c) => c instanceof THREE.Sprite) as THREE.Sprite | undefined;
  if (!sprite) return;
  const next = createTextSprite(label);
  sprite.material.map?.dispose();
  sprite.material.map = next.material.map;
  sprite.material.needsUpdate = true;
  sprite.scale.copy(next.scale);
  next.material.dispose();
}

/** Billboarded canvas-text sprite (also used by the in-AR status HUD). */
export function createTextSprite(text: string, color = '#00e5ff'): THREE.Sprite {
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d')!;
  const fontSize = 48;
  ctx.font = `${fontSize}px sans-serif`;
  const padding = 24;
  const textWidth = ctx.measureText(text).width;
  canvas.width = Math.ceil(textWidth + padding * 2);
  canvas.height = Math.ceil(fontSize + padding * 2);

  // Re-set after resize (resizing clears the context).
  ctx.font = `${fontSize}px sans-serif`;
  ctx.textBaseline = 'middle';
  ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
  roundRect(ctx, 0, 0, canvas.width, canvas.height, 16);
  ctx.fill();
  ctx.fillStyle = color;
  ctx.fillText(text, padding, canvas.height / 2);

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  const material = new THREE.SpriteMaterial({ map: texture, transparent: true });
  const sprite = new THREE.Sprite(material);

  // Scale so ~1px maps to a comfortable world size; keep aspect ratio.
  const worldHeight = 0.06;
  sprite.scale.set((canvas.width / canvas.height) * worldHeight, worldHeight, 1);
  return sprite;
}

// --- Head-locked status HUD: a multi-line panel + a connection dot ----------

const PANEL_FONT = 40; // px on the canvas; the world scale below is what sets on-headset size
const PANEL_PAD = 20;
const PANEL_LINE_HEIGHT = 1.4;
/** World height per text line — half the single-tag label height (0.06). */
const PANEL_LINE_WORLD = 0.03;

export const STATUS_GREEN = '#39d353';
export const STATUS_RED = '#ff5b52';

function renderPanelCanvas(lines: readonly string[], color: string): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d')!;
  const rows = lines.length ? lines : [' '];
  ctx.font = `${PANEL_FONT}px sans-serif`;
  const widest = Math.max(1, ...rows.map((l) => ctx.measureText(l).width));
  const lineH = PANEL_FONT * PANEL_LINE_HEIGHT;
  canvas.width = Math.ceil(widest + PANEL_PAD * 2);
  canvas.height = Math.ceil(rows.length * lineH + PANEL_PAD * 2);

  ctx.font = `${PANEL_FONT}px sans-serif`;
  ctx.textBaseline = 'top';
  ctx.fillStyle = 'rgba(8, 12, 20, 0.6)';
  roundRect(ctx, 0, 0, canvas.width, canvas.height, 18);
  ctx.fill();
  ctx.fillStyle = color;
  rows.forEach((line, i) => ctx.fillText(line, PANEL_PAD, PANEL_PAD + i * lineH));
  return canvas;
}

function applyPanelScale(sprite: THREE.Sprite, canvas: HTMLCanvasElement, rows: number): void {
  const worldH = Math.max(1, rows) * PANEL_LINE_WORLD + PANEL_LINE_WORLD * 0.6;
  const aspect = canvas.height > 0 ? canvas.width / canvas.height : 1;
  sprite.scale.set(aspect * worldH, worldH, 1);
}

/**
 * A translucent multi-line status panel. Anchored **top-left** (`center = (0,1)`)
 * so it grows rightward and **downward** as lines are added — a fixed corner
 * that expands vertically. Update it in place with {@link setTextPanel}.
 */
export function createTextPanel(lines: readonly string[], color = '#e8f0f8'): THREE.Sprite {
  const canvas = renderPanelCanvas(lines, color);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, transparent: true }));
  sprite.center.set(0, 1);
  applyPanelScale(sprite, canvas, lines.length);
  return sprite;
}

/** Re-render a panel's text in place (disposes the old texture — no leak). */
export function setTextPanel(sprite: THREE.Sprite, lines: readonly string[], color = '#e8f0f8'): void {
  const canvas = renderPanelCanvas(lines, color);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  sprite.material.map?.dispose();
  sprite.material.map = texture;
  sprite.material.needsUpdate = true;
  applyPanelScale(sprite, canvas, lines.length);
}

/**
 * A small solid-colour status dot (a mapless {@link THREE.Sprite}, so it's a
 * flat billboarded quad tinted by its material colour). Recolour with
 * `dot.material.color.set(hex)` — cheap, no texture churn.
 */
export function createStatusDot(colorHex = STATUS_RED): THREE.Sprite {
  const sprite = new THREE.Sprite(
    new THREE.SpriteMaterial({ color: new THREE.Color(colorHex), transparent: true }),
  );
  sprite.scale.set(0.035, 0.035, 1);
  return sprite;
}

function roundRect(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  w: number,
  h: number,
  r: number,
): void {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}
