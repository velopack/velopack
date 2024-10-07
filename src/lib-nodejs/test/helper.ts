import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { setVelopackLogger } from "../lib";

export function isWindows(): boolean {
  return os.platform() == "win32";
}

export function isLinux(): boolean {
  return os.platform() == "linux";
}

export function isMacos(): boolean {
  return os.platform() == "darwin";
}

export function getTempDir(): string {
  return fs.realpathSync(os.tmpdir());
}

export function fixture(name: string): string {
  return path.join("..", "..", "test", "fixtures", name);
}

export function makeId(length: number): string {
  let result = "";
  const characters =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
  const charactersLength = characters.length;
  let counter = 0;
  while (counter < length) {
    result += characters.charAt(Math.floor(Math.random() * charactersLength));
    counter += 1;
  }
  return result;
}

export async function captureLogs<T>(cb: () => Promise<T>): Promise<T> {
  setVelopackLogger((level, msg) => {
    console.log(level, msg);
  });

  try {
    return await cb();
  } finally {
    await shortDelay();
    setVelopackLogger(() => {
      // unhook logger from jest
    });
  }
}

// export function tempd1<T>(cb: (dir: string) => T): T {
//   const id = makeId(10);
//   const dir = path.join(os.tmpdir(), id);
//   fs.mkdirSync(dir);
//   try {
//     return cb(dir);
//   } finally {
//     fs.rmSync(dir, { recursive: true });
//   }
// }

// export function tempd2<T>(cb: (dir1: string, dir2: string) => T): T {
//   const dir1 = path.join(os.tmpdir(), makeId(10));
//   const dir2 = path.join(os.tmpdir(), makeId(10));
//   fs.mkdirSync(dir1);
//   fs.mkdirSync(dir2);
//   try {
//     return cb(dir1, dir2);
//   } finally {
//     fs.rmSync(dir1, { recursive: true });
//     fs.rmSync(dir2, { recursive: true });
//   }
// }

export async function tempd3<T>(
  cb: (dir1: string, dir2: string, dir3: string) => Promise<T>,
): Promise<T> {
  const dir1 = path.join(os.tmpdir(), makeId(16));
  const dir2 = path.join(os.tmpdir(), makeId(16));
  const dir3 = path.join(os.tmpdir(), makeId(16));
  fs.mkdirSync(dir1);
  fs.mkdirSync(dir2);
  fs.mkdirSync(dir3);
  try {
    return await cb(dir1, dir2, dir3);
  } finally {
    fs.rmSync(dir1, { recursive: true });
    fs.rmSync(dir2, { recursive: true });
    fs.rmSync(dir3, { recursive: true });
  }
}

// export async function tempd4<T>(
//     cb: (dir1: string, dir2: string, dir3: string, dir4: string) => T,
// ): Promise<T> {
//     const dir1 = path.join(os.tmpdir(), makeId(16));
//     const dir2 = path.join(os.tmpdir(), makeId(16));
//     const dir3 = path.join(os.tmpdir(), makeId(16));
//     const dir4 = path.join(os.tmpdir(), makeId(16));
//     fs.mkdirSync(dir1);
//     fs.mkdirSync(dir2);
//     fs.mkdirSync(dir3);
//     fs.mkdirSync(dir4);
//     try {
//         return await cb(dir1, dir2, dir3, dir4);
//     } finally {
//         fs.rmSync(dir1, {recursive: true});
//         fs.rmSync(dir2, {recursive: true});
//         fs.rmSync(dir3, {recursive: true});
//         fs.rmSync(dir4, {recursive: true});
//     }
// }

export function updateExe(): string {
  const paths = [];

  if (isMacos()) {
    paths.push(path.join("..", "..", "target", "release", "UpdateMac"));
  }

  if (isLinux()) {
    if (os.machine() == "x64") {
      paths.push(path.join("..", "..", "target", "release", "UpdateNix_x64"));
    } else if (os.machine() == "aarch64" || os.machine() == "arm64") {
      paths.push(path.join("..", "..", "target", "release", "UpdateNix_arm64"));
    }
  }

  if (isMacos() || isLinux()) {
    paths.push(path.join("..", "..", "target", "debug", "update"));
    paths.push(path.join("..", "..", "target", "release", "update"));
  }

  if (isWindows()) {
    paths.push(path.join("..", "..", "target", "debug", "Update.exe"));
    paths.push(path.join("..", "..", "target", "release", "Update.exe"));
  }

  for (const p of paths) {
    if (fs.existsSync(p)) {
      return p;
    }
  }

  // could not find update.exe
  let message =
    "Could not find update binary. Searched these paths: " +
    paths.join(", ") +
    ". And found these binaries: ";

  for (const p of paths) {
    for (const file of fs.readdirSync(p)) {
      message += file + ", ";
    }
  }

  throw new Error(message);
}

function shortDelay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 150));
}

// export function copyUpdateExeTo(dir: string, filename?: string): string {
//   const exe = updateExe();
//   const dest = path.join(dir, filename ?? path.basename(exe));
//   fs.copyFileSync(exe, dest);
//   return dest;
// }
