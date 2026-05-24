let _callback: ((percent: number) => void) | null = null;

export function setProgressCallback(
  cb: ((percent: number) => void) | null,
): void {
  _callback = cb;
}

export function report(percent: number): void {
  if (_callback) {
    _callback(percent);
  }
}
