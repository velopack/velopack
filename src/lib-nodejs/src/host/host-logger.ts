let _callback: ((level: string, msg: string) => void) | null = null;

export function setLoggerCallback(
  cb: ((level: string, msg: string) => void) | null,
): void {
  _callback = cb;
}

const LEVELS = ["error", "warn", "info", "debug", "trace"];

export function log(level: number, msg: string): void {
  if (_callback) {
    const levelStr = LEVELS[level] || "info";
    _callback(levelStr, msg);
  }
}
