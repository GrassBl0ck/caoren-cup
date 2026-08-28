type ScheduleCallback = (callback: () => void, delayMs: number) => unknown;
type CancelCallback = (handle: unknown) => void;

export class PluginStateBroadcastScheduler {
    private pendingHandle: unknown = null;
    private pendingToken: object | null = null;

    constructor(
        private readonly broadcast: () => void,
        private readonly intervalMs = 500,
        private readonly schedule: ScheduleCallback = (callback, delayMs) => setTimeout(callback, delayMs),
        private readonly cancel: CancelCallback = (handle) => clearTimeout(handle as NodeJS.Timeout),
    ) {}

    request(): void {
        if (this.pendingToken) return;
        const token = {};
        this.pendingToken = token;
        this.pendingHandle = this.schedule(() => {
            if (this.pendingToken !== token) return;
            this.pendingToken = null;
            this.pendingHandle = null;
            this.broadcast();
        }, this.intervalMs);
    }

    flush(): void {
        if (this.pendingHandle !== null) this.cancel(this.pendingHandle);
        this.pendingToken = null;
        this.pendingHandle = null;
        this.broadcast();
    }
}
