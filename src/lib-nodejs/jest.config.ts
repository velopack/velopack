import type { Config } from "jest";

const config: Config = {
  verbose: true,
  testEnvironment: "node",
  transform: {
    "^.+.ts$": "@swc/jest",
  },
  reporters: [
    "default",
    ["github-actions", { silent: true }],
    "summary",
    ...(process.env.CI
      ? [["jest-junit", { outputDirectory: "../../test/coverage", outputName: "junit.nodejs.xml" }] as [string, Record<string, unknown>]]
      : []),
  ],
  collectCoverage: !!process.env.CI,
  coverageDirectory: "coverage",
  coverageReporters: ["cobertura", "text-summary"],
  collectCoverageFrom: [
    "src/**/*.ts",
    "!src/types.ts",
  ],
};

export default config;

