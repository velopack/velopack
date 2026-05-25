import {
  openSync,
  closeSync,
  readSync,
  writeSync,
  mkdirSync,
  readdirSync,
  unlinkSync,
  renameSync,
  statSync,
} from "node:fs";
import { dirname } from "node:path";

interface HandleEntry {
  fd: number;
  position: number;
  path: string;
}

const handles = new Map<number, HandleEntry>();
let nextHandle = 1;

function witError(e: unknown): never {
  throw { payload: e instanceof Error ? e.message : String(e) };
}

export function open(
  path: string,
  writable: boolean,
  create: boolean,
): number {
  try {
    if (writable) {
      mkdirSync(dirname(path), { recursive: true });
    }
    const flags = writable ? (create ? "w" : "r+") : "r";
    const fd = openSync(path, flags);
    const h = nextHandle++;
    handles.set(h, { fd, position: 0, path });
    return h;
  } catch (e) {
    witError(e);
  }
}

let sharedReadBuf: Buffer | null = null;

export function read(handle: number, length: number): Uint8Array {
  try {
    const entry = handles.get(handle);
    if (!entry) throw new Error(`Invalid file handle: ${handle}`);
    if (!sharedReadBuf || sharedReadBuf.length < length) {
      sharedReadBuf = Buffer.alloc(length);
    }
    const bytesRead = readSync(
      entry.fd,
      sharedReadBuf,
      0,
      length,
      entry.position,
    );
    entry.position += bytesRead;
    return new Uint8Array(sharedReadBuf.buffer, sharedReadBuf.byteOffset, bytesRead).slice();
  } catch (e) {
    witError(e);
  }
}

export function write(handle: number, data: Uint8Array): void {
  try {
    const entry = handles.get(handle);
    if (!entry) throw new Error(`Invalid file handle: ${handle}`);
    const bytesWritten = writeSync(
      entry.fd,
      data,
      0,
      data.length,
      entry.position,
    );
    entry.position += bytesWritten;
  } catch (e) {
    witError(e);
  }
}

export function seek(handle: number, pos: bigint): void {
  try {
    const entry = handles.get(handle);
    if (!entry) throw new Error(`Invalid file handle: ${handle}`);
    entry.position = Number(pos);
  } catch (e) {
    witError(e);
  }
}

export function close(handle: number): void {
  try {
    const entry = handles.get(handle);
    if (!entry) throw new Error(`Invalid file handle: ${handle}`);
    handles.delete(handle);
    closeSync(entry.fd);
  } catch (e) {
    witError(e);
  }
}

export function listDir(dirPath: string): string[] {
  try {
    return readdirSync(dirPath);
  } catch {
    return [];
  }
}

export function deleteFile(path: string): void {
  try {
    unlinkSync(path);
  } catch (e) {
    witError(e);
  }
}

export function renameFile(oldPath: string, newPath: string): void {
  try {
    renameSync(oldPath, newPath);
  } catch (e) {
    witError(e);
  }
}

export function getFileSize(path: string): bigint | undefined {
  try {
    return BigInt(statSync(path).size);
  } catch {
    return undefined;
  }
}

