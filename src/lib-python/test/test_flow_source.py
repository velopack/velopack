"""Tests for the Velopack Flow update source and the Flow-by-default UpdateManager.

Mirrors the Rust `manager_flow.rs` test: builds a locator pointing at the
`Test.Squirrel-App.nuspec` fixture and verifies the manager can be constructed
both with no source (defaulting to Velopack Flow) and with an explicit
`VelopackFlowSource`, reading identity from the manifest without any network.
"""

import tempfile
from pathlib import Path

import velopack


def _fixture_manifest() -> Path:
    # test/ -> lib-python -> src -> repo root; fixtures live under repo-root test/fixtures.
    repo_root = Path(__file__).resolve().parents[3]
    return repo_root / "test" / "fixtures" / "Test.Squirrel-App.nuspec"


def _make_locator(root: Path) -> velopack.VelopackLocatorConfig:
    packages_dir = root / "packages"
    current_binary_dir = root / "current"
    update_exe_path = root / "Update.exe"
    packages_dir.mkdir(parents=True, exist_ok=True)
    current_binary_dir.mkdir(parents=True, exist_ok=True)
    update_exe_path.write_bytes(b"")
    return velopack.VelopackLocatorConfig(
        RootAppDir=root,
        UpdateExePath=update_exe_path,
        PackagesDir=packages_dir,
        ManifestPath=_fixture_manifest(),
        CurrentBinaryDir=current_binary_dir,
        IsPortable=True,
    )


def test_update_manager_defaults_to_flow():
    with tempfile.TemporaryDirectory() as tmp:
        locator = _make_locator(Path(tmp) / "root")
        # No source argument -> the hosted Velopack Flow service is assumed.
        um = velopack.UpdateManager(locator=locator)
        assert um.get_app_id() == "Test.Squirrel-App"
        assert um.get_current_version() == "1.0.0"


def test_update_manager_with_explicit_flow_source():
    with tempfile.TemporaryDirectory() as tmp:
        locator = _make_locator(Path(tmp) / "root")
        um = velopack.UpdateManager(velopack.VelopackFlowSource(), locator=locator)
        assert um.get_app_id() == "Test.Squirrel-App"
        assert um.get_current_version() == "1.0.0"


def test_flow_source_accepts_custom_base_uri():
    with tempfile.TemporaryDirectory() as tmp:
        locator = _make_locator(Path(tmp) / "root")
        source = velopack.VelopackFlowSource("http://localhost:65531/")
        um = velopack.UpdateManager(source, locator=locator)
        assert um.get_app_id() == "Test.Squirrel-App"


if __name__ == "__main__":
    test_update_manager_defaults_to_flow()
    test_update_manager_with_explicit_flow_source()
    test_flow_source_accepts_custom_base_uri()
    print("All Flow source tests passed.")
