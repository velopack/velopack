package velopack_test

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"testing"

	"github.com/velopack/velopack/src/lib-go/velopack"
)

var (
	AfterInstall    bool
	BeforeUninstall bool
	BeforeUpdate    bool
	AfterUpdate     bool
	Restarted       bool
	FirstRun        bool
	Version         string
)

func init() {
	velopack.WindowsHookAfterInstall = func(version string) {
		AfterInstall = true
		Version = version
	}
	velopack.WindowsHookBeforeUninstall = func(version string) {
		BeforeUninstall = true
		Version = version
	}
	velopack.WindowsHookBeforeUpdate = func(version string) {
		BeforeUpdate = true
		Version = version
	}
	velopack.WindowsHookAfterUpdate = func(version string) {
		AfterUpdate = true
		Version = version
	}
	velopack.HookRestarted = func(version string) {
		Restarted = true
		Version = version
	}
	velopack.Logger = func(psz_level, psz_message string) {
		fmt.Println(psz_level, psz_message)
	}
}

func expect[T comparable](t *testing.T, a, b T) {
	if a != b {
		t.Errorf("expected %v, got %v", b, a)
	}
}

func TestRestartedEvent(t *testing.T) {
	velopack.Locator = &velopack.LocatorConfig{
		ManifestPath:  "/home/quentin/git/velopack/test/fixtures/Test.Squirrel-App.nuspec",
		UpdateExePath: "/home/quentin/git/velopack/target/debug/update",
		IsPortable:    true,
	}
	velopack.Run()
	expect(t, AfterInstall, false)
	expect(t, BeforeUninstall, false)
	expect(t, BeforeUpdate, false)
	expect(t, AfterUpdate, false)
	expect(t, Restarted, true)
	expect(t, FirstRun, false)
	expect(t, Version, "1.0.0")
}

func tempd3() (string, string, string, error) {
	a, a_err := os.MkdirTemp(os.TempDir(), "velopack-go-test-")
	b, b_err := os.MkdirTemp(os.TempDir(), "velopack-go-test-")
	c, c_err := os.MkdirTemp(os.TempDir(), "velopack-go-test-")
	return a, b, c, errors.Join(a_err, b_err, c_err)
}

func TestUpdateManagerDetectsLocalUpdate(t *testing.T) {
	wd, err := os.Getwd()
	if err != nil {
		t.Fatalf("failed to get current working directory: %v", err)
	}
	tmpDir, packagesDir, rootDir, err := tempd3()
	if err != nil {
		t.Fatalf("failed to create temp directories: %v", err)
	}
	velopack.Locator = &velopack.LocatorConfig{
		ManifestPath:     filepath.Join(wd, "/.../../test/fixtures/Test.Squirrel-App.nuspec"),
		UpdateExePath:    filepath.Join(wd, "/../../target/debug/update"),
		RootAppDir:       rootDir,
		PackagesDir:      packagesDir,
		CurrentBinaryDir: filepath.Join(rootDir, "current"),
		IsPortable:       true,
	}
	up, err := velopack.NewUpdateManager(tmpDir, velopack.UpdateOptions{
		ExplicitChannel:             "beta",
		AllowVersionDowngrade:       false,
		MaximumDeltasBeforeFallback: 10,
	})
	if err != nil {
		t.Fatalf("failed to create update manager: %v", err)
	}
	update, _ := up.CheckForUpdates()
	if update == nil {
		t.Fatal("expected an update, got nil")
	}
	if update.TargetFullRelease == nil {
		t.Fatal("expected a target full release, got nil")
	}
	expect(t, update.TargetFullRelease.Version, "1.0.11")
	expect(t, update.TargetFullRelease.Filename, "AvaloniaCrossPlat-1.0.11-full.nupkg")
}
