import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    globals: true,
    reporters: [
      "default",
      "github-actions",
      ...(process.env.CI
        ? [["junit", { outputFile: "../../test/coverage/junit.nodejs.xml" }]]
        : []),
    ],
    coverage: {
      enabled: !!process.env.CI,
      provider: "v8",
      reporter: ["cobertura", "text-summary"],
      reportsDirectory: "coverage",
      include: ["src/**/*.ts"],
      exclude: ["src/types.ts"],
    },
  },
});
