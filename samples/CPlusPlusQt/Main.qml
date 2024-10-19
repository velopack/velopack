import QtQuick
import QtQuick.Window
import QtQuick.Controls
import QtQuick.Dialogs

import VelopackQt 1.0

Window {
    width: 640
    height: 480
    visible: true
    title: qsTr("Velopack Qt c++ example")

    Connections {
        target: AutoUpdater
        // User checked for updates, but there were none, show text about it
        function onNoNewUpdatesAvailable() {
            noNewUpdatesTxt.visible = true
        }

        // There's a new version, hide the text about no new version available
        function onNewVersionChanged() {
            noNewUpdatesTxt.visible = false
        }

        function onUpdatingFailed(errorMsg) {
            errDialog.text = errorMsg
            errDialog.open()
        }
    }

    Column {
        anchors.fill: parent
        anchors.topMargin: 40
        spacing: 10

        Text {
            anchors.horizontalCenter: parent.horizontalCenter
            font.bold: true
            text: "Welcome to Velopack Qt C++ Sample App."
        }

        Text {
            anchors.horizontalCenter: parent.horizontalCenter
            font.bold: true
            text: "Current version: %1".arg(AutoUpdater.currentVersion).arg(AutoUpdater.updateUrl())
        }

        Text {
            anchors.horizontalCenter: parent.horizontalCenter
            font.bold: true
            font.pixelSize: 18
            color: "red"
            text: "New update available! v%1 ".arg(AutoUpdater.newVersion)
            visible: AutoUpdater.newVersion !== ""
        }

        Text {
            id: noNewUpdatesTxt
            anchors.horizontalCenter: parent.horizontalCenter
            font.bold: true
            font.pixelSize: 18
            color: "crimson"
            text: "No new updates right now..."
            visible: false
        }

        Button {
            width: 400
            height: 100
            anchors.horizontalCenter: parent.horizontalCenter
            text: "Check for updates"
            onClicked: {
                AutoUpdater.checkForUpdates()
            }
        }

        Button {
            width: 400
            height: 100
            anchors.horizontalCenter: parent.horizontalCenter
            text: "Download update"
            enabled: AutoUpdater.newVersion !== ""
                     && !AutoUpdater.updateReadyToInstall
            onClicked: {
                AutoUpdater.downloadLatestUpdate()
            }
        }

        Button {
            width: 400
            height: 100
            anchors.horizontalCenter: parent.horizontalCenter
            text: "Apply update and restart"
            enabled: AutoUpdater.updateReadyToInstall
            onClicked: {
                AutoUpdater.applyUpdateAndRestart()
            }
        }
    }

    Text {
        anchors.bottom: parent.bottom
        anchors.bottomMargin: 8
        anchors.horizontalCenter: parent.horizontalCenter
        font.bold: false
        font.italic: true
        font.pixelSize: 12
        text: "Updates URL: %1".arg(AutoUpdater.updateUrl())
    }

    MessageDialog {
        id: errDialog
        buttons: MessageDialog.Ok
    }

}
