import registryData from './tag-registry.json';

/**
 * One printable/detectable AprilTag: its family index (`id`), a human name, and
 * the accent colour used for its in-AR plane + label. The same JSON file is read
 * by the tag generator in `V2/tools/` so the printed tag's label and the headset
 * colour always agree — one source of truth for the test set.
 */
export interface AprilTagInfo {
  readonly id: number;
  readonly name: string;
  readonly color: string;
}

interface RegistryFile {
  readonly dictionary: string;
  readonly tags: readonly AprilTagInfo[];
}

const FILE = registryData as RegistryFile;

/** js-aruco2 dictionary name the tags belong to (must match the vendored dict). */
export const APRILTAG_DICTIONARY = FILE.dictionary;

const BY_ID: ReadonlyMap<number, AprilTagInfo> = new Map(FILE.tags.map((t) => [t.id, t]));

/** All registered test tags (id order as authored). */
export function knownTags(): readonly AprilTagInfo[] {
  return FILE.tags;
}

/**
 * Info for a detected tag id. Unregistered ids (a stray tag in view) still get a
 * sensible fallback — white plane, `Tag N` label — rather than being dropped, so
 * unexpected detections are visible instead of silently ignored.
 */
export function tagInfo(id: number): AprilTagInfo {
  return BY_ID.get(id) ?? { id, name: `Tag ${id}`, color: '#ffffff' };
}

/** Whether a tag id is in the registry (used to filter decoder false-positives). */
export function isKnownTag(id: number): boolean {
  return BY_ID.has(id);
}
