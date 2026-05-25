import { spawn } from "node:child_process";

export function launchDetached(
  path: string,
  args: string[],
  workDir: string | undefined,
): void {
  const child = spawn(path, args, {
    detached: true,
    stdio: "ignore",
    cwd: workDir || undefined,
  });
  child.unref();
}

export function getProcessId(): number {
  return process.pid;
}

export function removeEnv(name: string): void {
  delete process.env[name];
}
