interface HeartbeatSource {
  instanceId: string;
  startedAtTicks: bigint;
  sequence: number;
}

// This lives with the HTTP routes, not a match session: returning to Lobby must not
// make an older in-flight request valid again. The bridge is the single trusted host.
export class PluginHeartbeatOrder {
  private current?: HeartbeatSource;

  accept(body: any): boolean {
    const { heartbeatInstanceId, heartbeatStartedAtTicks, heartbeatSequence } = body || {};
    if (heartbeatInstanceId === undefined && heartbeatStartedAtTicks === undefined && heartbeatSequence === undefined) {
      // Allow old bridges for existing modes during a rolling update, until the
      // ordered protocol has appeared. Random requires the new bridge protocol.
      return !this.current && body?.gameMode?.modeId !== 'random';
    }
    if (typeof heartbeatInstanceId !== 'string' || !/^[a-zA-Z0-9_-]{1,128}$/.test(heartbeatInstanceId) ||
        typeof heartbeatStartedAtTicks !== 'string' || !/^[1-9][0-9]{0,19}$/.test(heartbeatStartedAtTicks) ||
        !Number.isSafeInteger(heartbeatSequence) || heartbeatSequence < 1) return false;
    const next: HeartbeatSource = { instanceId: heartbeatInstanceId, startedAtTicks: BigInt(heartbeatStartedAtTicks), sequence: heartbeatSequence };
    if (this.current) {
      if (next.instanceId === this.current.instanceId) {
        if (next.startedAtTicks !== this.current.startedAtTicks || next.sequence <= this.current.sequence) return false;
      } else if (next.startedAtTicks <= this.current.startedAtTicks) {
        // Also rejects requests from a retired bridge, including a retired instance
        // whose first request only arrives after the new instance has taken over.
        return false;
      }
    }
    this.current = next;
    return true;
  }
}
