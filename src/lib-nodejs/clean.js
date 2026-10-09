const fs = require("node:fs");
const path = require("node:path");

// removes build outputs: compiled lib, the native modules copied by copy-lib.js, and packed tarballs
fs.rmSync("./lib", { recursive: true, force: true });

if (fs.existsSync("./src/native")) {
  for (const file of fs.readdirSync("./src/native")) {
    if (file.endsWith(".node")) fs.rmSync(path.join("./src/native", file), { force: true });
  }
}

for (const file of fs.readdirSync(".")) {
  if (file.startsWith("velopack-") && file.endsWith(".tgz")) fs.rmSync(file, { force: true });
}
