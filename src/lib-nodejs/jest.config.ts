import type { Config } from "jest";

const config: Config = {
  verbose: true,
  testEnvironment: "node",
  transform: {
    "^.+.ts$": ["ts-jest", {}],
  },
  reporters: ["default", ["github-actions", { silent: true }], "summary"],
};

export default config;

// /** @type {import('ts-jest').JestConfigWithTsJest} **/
// module.exports = {
//     testEnvironment: "node",
//     transform: {
//       "^.+.ts$": ["ts-jest", {}],
//     },
//   };
