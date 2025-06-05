import atexit
import functools
from http.server import HTTPServer, SimpleHTTPRequestHandler
import platform
import shutil
import subprocess
from pathlib import Path
import threading
import time
import zipfile

httpd = None

def log(msg):
    print(f"[LOG] {msg}")


# Register a cleanup function to ensure the HTTP server is stopped on exit
def cleanup():
    global httpd
    if httpd:
        log("Stopping HTTP server...")
        httpd.shutdown()
        httpd.server_close()
        log("HTTP server stopped.")
    shutil.rmtree("output", ignore_errors=True)
    shutil.rmtree("dist", ignore_errors=True)
    shutil.rmtree("build", ignore_errors=True)
    shutil.rmtree("Releases", ignore_errors=True)

atexit.register(cleanup)

def _run_cmd(args):
    log(f"Running command: {' '.join(args)}")
    result = subprocess.run(args, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"Command failed: {' '.join(args)}\n{result.stdout}\n{result.stderr}")
    return result.stdout.strip()

def write_app_version(version):
    log(f"Writing app version: {version}")
    with open("app_version.py", "w") as f:
        f.write(f'version = "{version}"\n')
    log("App version written successfully")

def read_app_version(path):
    log(f"Reading app version from: {path}")
    with open(path, "r") as f:
        version = f.read()
    log(f"App version read successfully: {version}")
    return version

def extract_full_path(zip_file, target_dir):
    log(f"Extracting {zip_file} to {target_dir}")
    with zipfile.ZipFile(zip_file, 'r') as zip_ref:
        zip_ref.extractall(target_dir)
    log("Extraction completed")

# check if we are running on Windows
if platform.system() != "Windows":
    raise RuntimeError("This script is intended to run on Windows only, for now")

_pyinstaller_command = ["uv", "run", "pyinstaller", "--onedir", "--console", "app.py", "--noconfirm"]

# check if we are running from the test dir
if Path(__file__).parent.name != "test":
    raise RuntimeError("This script must be run from the 'test' directory")

# check for vpk cli
_run_cmd(["vpk", "-h"])

write_app_version("1.0.0")

_run_cmd(_pyinstaller_command)

# make app version
_run_cmd(["vpk", "pack", "--packId", "test-app", "--packVersion", "1.0.0", "--packDir", "dist/app/", "--mainExe", "app.exe"])

extract_full_path("Releases/test-app-win-Portable.zip", "output")

log("Starting HTTP server thread serving ./Releases …")
handler = functools.partial(SimpleHTTPRequestHandler, directory=str(Path("Releases").resolve()))
httpd = HTTPServer(("localhost", 8080), handler)
server_thread = threading.Thread(target=httpd.serve_forever, daemon=True)
server_thread.start()
log("HTTP server is now running at http://localhost:8000")
# Give it a moment to start
time.sleep(1)


# check if the app version is correct
_run_cmd(["output/test-app.exe"])

current_version = read_app_version("output/current/version_result.txt")
if current_version.strip() != "1.0.0":
    raise RuntimeError(f"Version mismatch: expected '1.0.0', got '{current_version.strip()}'")

log("App version is correct: 1.0.0")
log("Trying to create update package...")
write_app_version("1.0.1")
_run_cmd(_pyinstaller_command)
_run_cmd(["vpk", "pack", "--packId", "test-app", "--packVersion", "1.0.1", "--packDir", "dist/app/", "--mainExe", "app.exe"])
# check if the app version is correct
_run_cmd(["output/test-app.exe"])
new_version = read_app_version("output/current/version_result.txt")
if new_version.strip() != "1.0.1":
    raise RuntimeError(f"Version mismatch: expected '1.0.1' after update, got '{new_version.strip()}'")

log("Update package created successfully and version is now 1.0.1")
log("Test completed successfully")