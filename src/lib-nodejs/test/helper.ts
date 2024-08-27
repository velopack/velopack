import fs from "node:fs";
import os from "node:os";
import path from "node:path";

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
  cb: (dir1: string, dir2: string, dir3: string) => T,
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

export function updateExe(): string {
  const paths = [
    path.join("..", "..", "target", "debug", "Update.exe"),
    path.join("..", "..", "target", "release", "Update.exe"),
    path.join("..", "..", "target", "debug", "update"),
    path.join("..", "..", "target", "release", "update"),
    path.join("..", "..", "target", "debug", "UpdateMac"),
    path.join("..", "..", "target", "release", "UpdateMac"),
    path.join("..", "..", "target", "debug", "UpdateNix"),
    path.join("..", "..", "target", "release", "UpdateNix"),
  ];

  for (const p of paths) {
    if (fs.existsSync(p)) {
      return p;
    }
  }

  throw new Error("Update.exe not found");
}

export function shortDelay(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 100));
}

// export function copyUpdateExeTo(dir: string, filename?: string): string {
//   const exe = updateExe();
//   const dest = path.join(dir, filename ?? path.basename(exe));
//   fs.copyFileSync(exe, dest);
//   return dest;
// }