/** Minimal typed event emitter — zero-dependency pub/sub for services. */
export type Listener<T> = (value: T) => void;

export class Emitter<T> {
  private readonly listeners = new Set<Listener<T>>();

  /** Subscribe; returns an unsubscribe function. */
  on(listener: Listener<T>): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /** Subscribe for a single emission. */
  once(listener: Listener<T>): () => void {
    const off = this.on((v) => {
      off();
      listener(v);
    });
    return off;
  }

  emit(value: T): void {
    // Copy to tolerate unsubscription during iteration.
    for (const l of [...this.listeners]) l(value);
  }

  clear(): void {
    this.listeners.clear();
  }

  get size(): number {
    return this.listeners.size;
  }
}
