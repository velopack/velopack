import type { Config } from "jest";

const config: Config = {
  verbose: true,
  testEnvironment: "node",
  transform: {
    "^.+.ts$": ["ts-jest", {}],
  },
  reporters: ["default", ["github-actions", { silent: true }], "summary"],
  collectCoverage: !!process.env.CI,
  coverageDirectory: "coverage",
  coverageReporters: ["cobertura", "text-summary"],
  collectCoverageFrom: [
    "src/**/*.ts",
    "!src/types.ts",
  ],
};

export default config;

// /** @type {import('ts-jest').JestConfigWithTsJest} **/
// module.exports = {
//     testEnvironment: "node",
//     transform: {
//       "^.+.ts$": ["ts-jest", {}],
//     },
//   };
