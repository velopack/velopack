package main

import (
	"fmt"
	"os"
	"strings"

	"graphics.gd/classdb"
	"graphics.gd/classdb/Button"
	"graphics.gd/classdb/Engine"
	"graphics.gd/classdb/HBoxContainer"
	"graphics.gd/classdb/Label"
	"graphics.gd/classdb/ProgressBar"
	"graphics.gd/classdb/ScrollContainer"
	"graphics.gd/classdb/VBoxContainer"
	"graphics.gd/startup"
	"graphics.gd/variant/Float"

	"github.com/velopack/velopack/src/lib-go/velopack"
)

var early_logs []string

func init() {
	velopack.HookRestarted = func(appVersion string) {
		fmt.Println("Restarted with version:", appVersion)
	}
	velopack.Logger = func(level, message string) {
		early_logs = append(early_logs, level+": "+message)
	}
	velopack.Run()
}

type GUI struct {
	VBoxContainer.Extension[GUI]

	Actions struct {
		HBoxContainer.Instance

		Check       Button.Instance
		Download    Button.Instance
		ProgressBar ProgressBar.Instance
		Apply       Button.Instance
	}
	ScrollContainer struct {
		ScrollContainer.Instance

		Label Label.Instance
	}
}

func (gui *GUI) Ready() {
	gui.ScrollContainer.Label.SetText(strings.Join(early_logs, "\n"))
	velopack.Logger = func(level, message string) {
		gui.ScrollContainer.Label.SetText(gui.ScrollContainer.Label.Text() + "\n" + level + ": " + message)
	}
	up, err := velopack.NewUpdateManager(os.Getenv("RELEASES_DIR"))
	if err != nil {
		Engine.Raise(err)
		return
	}
	var update *velopack.UpdateInfo
	gui.Actions.Check.AsBaseButton().OnPressed(func() {
		update, _ = up.CheckForUpdates()
	})
	gui.Actions.Download.AsBaseButton().OnPressed(func() {
		if err := up.DownloadUpdates(update, func(progress uint) {
			gui.Actions.ProgressBar.AsRange().SetValue(Float.X(progress))
		}); err != nil {
			Engine.Raise(err)
			return
		}
	})
	gui.Actions.Apply.AsBaseButton().OnPressed(func() {
		if err := up.WaitExitThenApplyUpdates(update); err != nil {
			Engine.Raise(err)
			return
		}
		os.Exit(0)
	})
}

func main() {
	fmt.Println("HELLO")
	classdb.Register[GUI]()
	startup.Scene()
}
