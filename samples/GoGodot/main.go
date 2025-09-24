package main

import (
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

var catch_early_logs []string
var ready_logger func(string, string)

func init() {
	velopack.Run(velopack.App{
		AutoApplyOnStartup: true,
		Logger: func(level, message string) {
			if ready_logger != nil {
				ready_logger(level, message)
			} else {
				catch_early_logs = append(catch_early_logs, level+": "+message)
			}
		},
	})
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
	Scrollable struct {
		ScrollContainer.Instance

		Logs Label.Instance
	}
}

func (gui *GUI) Ready() {
	gui.Scrollable.Logs.SetText(strings.Join(catch_early_logs, "\n"))
	ready_logger = func(level, message string) {
		gui.Scrollable.Logs.SetText(gui.Scrollable.Logs.Text() + "\n" + level + ": " + message)
	}
	manager, err := velopack.NewUpdateManager(os.Getenv("RELEASES_DIR"))
	if err != nil {
		Engine.Raise(err)
		return
	}
	var latest *velopack.UpdateInfo
	gui.Actions.Check.AsBaseButton().OnPressed(func() {
		latest, _ = manager.CheckForUpdates()
	})
	gui.Actions.Download.AsBaseButton().OnPressed(func() {
		if err := manager.DownloadUpdates(latest, func(progress uint) {
			gui.Actions.ProgressBar.AsRange().SetValue(Float.X(progress))
		}); err != nil {
			Engine.Raise(err)
			return
		}
		gui.Actions.ProgressBar.AsRange().SetValue(100)
	})
	gui.Actions.Apply.AsBaseButton().OnPressed(func() {
		if err := manager.WaitForExitThenApplyUpdates(latest, velopack.Restart{}); err != nil {
			Engine.Raise(err)
			return
		}
		os.Exit(0)
	})
}

func main() {
	classdb.Register[GUI]()
	startup.Scene()
}
