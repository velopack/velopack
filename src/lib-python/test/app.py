
import velopack

import app_version

version = app_version.version

if __name__ == "__main__":
    velopack.App().run()
    um = velopack.UpdateManager("http://localhost:8080")
    if um.check_for_updates():
        um.download_updates()
        um.apply_updates_and_restart()
    with open("version_result.txt", "w") as f:
        f.write(f"{version}")