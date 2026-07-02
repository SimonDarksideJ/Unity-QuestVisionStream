/**
 * TagFactory: tag construction/label baselines, and the GPU-texture disposal
 * gap — SpriteMaterial.dispose() does NOT dispose its .map, so every torn-down
 * tag leaked one CanvasTexture.
 */
import * as THREE from 'three';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import { createTagObject, setTagLabel } from '../src/rendering/TagFactory';
import { stubCanvas2d } from './helpers';

beforeAll(() => {
  stubCanvas2d();
});

describe('createTagObject (baselines)', () => {
  it('builds a marker mesh plus a billboarded label sprite', () => {
    const tag = createTagObject('cup');
    const mesh = tag.children.find((c) => c instanceof THREE.Mesh);
    const sprite = tag.children.find((c) => c instanceof THREE.Sprite) as THREE.Sprite;
    expect(mesh).toBeDefined();
    expect(sprite).toBeDefined();
    expect(sprite.material.map).toBeInstanceOf(THREE.CanvasTexture);
  });

  it('setTagLabel swaps the texture and disposes the old one', () => {
    const tag = createTagObject('cup');
    const sprite = tag.children.find((c) => c instanceof THREE.Sprite) as THREE.Sprite;
    const oldMap = sprite.material.map!;
    const disposed = vi.fn();
    oldMap.addEventListener('dispose', disposed);

    setTagLabel(tag, 'plant');
    expect(sprite.material.map).not.toBe(oldMap);
    expect(disposed).toHaveBeenCalledTimes(1);
  });
});

describe('disposeTagObject', () => {
  it('disposes geometries, materials, AND their textures', async () => {
    const mod = (await import('../src/rendering/TagFactory')) as unknown as {
      disposeTagObject?: (root: THREE.Object3D) => void;
    };
    expect(typeof mod.disposeTagObject).toBe('function');

    const tag = createTagObject('cup');
    const sprite = tag.children.find((c) => c instanceof THREE.Sprite) as THREE.Sprite;
    const mesh = tag.children.find((c) => c instanceof THREE.Mesh) as THREE.Mesh;

    const textureDisposed = vi.fn();
    const materialDisposed = vi.fn();
    const geometryDisposed = vi.fn();
    sprite.material.map!.addEventListener('dispose', textureDisposed);
    sprite.material.addEventListener('dispose', materialDisposed);
    mesh.geometry.addEventListener('dispose', geometryDisposed);

    mod.disposeTagObject!(tag);

    expect(geometryDisposed).toHaveBeenCalledTimes(1);
    expect(materialDisposed).toHaveBeenCalledTimes(1);
    expect(textureDisposed).toHaveBeenCalledTimes(1); // the previously-leaked GPU texture
  });
});
