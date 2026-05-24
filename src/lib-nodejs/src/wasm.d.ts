declare module "../wasm/velopack.js" {
  export function instantiate(
    getCoreModule: (path: string) => any,
    imports: Record<string, any>,
  ): any;
}

declare module "*/wasm/velopack.js" {
  export function instantiate(
    getCoreModule: (path: string) => any,
    imports: Record<string, any>,
  ): any;
}
