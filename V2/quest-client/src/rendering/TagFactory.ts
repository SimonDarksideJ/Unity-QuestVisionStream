import * as THREE from 'three';

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

function createTextSprite(text: string): THREE.Sprite {
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
  ctx.fillStyle = '#00e5ff';
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
