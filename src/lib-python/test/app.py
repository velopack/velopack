import velopack

import app_version

version = app_version.version

if __name__ == "__main__":
    velopack.App().run()
    um = velopack.UpdateManager("http://localhost:8080")
    update_info = um.check_for_updates()
    print(f"Update info: {update_info}")
    if update_info:
        um.download_updates(update_info)
        um.apply_updates_and_restart(update_info)
    with open("version_result.txt", "w") as f:
        f.write(f"{version}")