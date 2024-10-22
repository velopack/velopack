#include "autoupdater.h"

#include <QString>
#include <QtDebug>

#include "constants.h"

auto kUpdateUrl = UPDATE_URL;

AutoUpdater::AutoUpdater(QObject *parent) : QObject(parent) {

  try {
    // Init Velopack auto-updater
    manager.setUrlOrPath(kUpdateUrl);

    QString currentVersion =
        QString::fromStdString(manager.getCurrentVersion());

    qInfo() << "Current version: " << currentVersion;
    setCurrentVersion(currentVersion);
  } catch (const std::exception &err) {
    qInfo() << "Error initating auto-updater, msg: " << err.what();
  }
  // Do a check on startup, if desired
  // checkForUpdates();
}

AutoUpdater::~AutoUpdater(){};

void AutoUpdater::checkForUpdates() {

  try {
    // Check for updates
    updInfo = manager.checkForUpdates();

    if (updInfo == nullptr) {
      // no updates available
      qInfo() << "No updates available, running latest version \\o/";
      Q_EMIT noNewUpdatesAvailable();
    } else {
      setNewVersion(
          QString::fromStdString(updInfo->targetFullRelease->version));
      qInfo() << "Update available: " << newVersion();
    }
  } catch (const std::exception &err) {
    qInfo() << "Error checking for new updates, msg: " << err.what();
    Q_EMIT noNewUpdatesAvailable();
  }
}

void AutoUpdater::downloadLatestUpdate() {
  try {
    if (updInfo != nullptr) {
      qInfo() << __FUNCTION__ << "Downloading new version: "
              << QString::fromStdString(updInfo->targetFullRelease->version);
      manager.downloadUpdates(updInfo->targetFullRelease.get());

      qInfo() << __FUNCTION__ << "Downloaded version: "
              << QString::fromStdString(updInfo->targetFullRelease->version);

      setUpdateReadyToInstall(true);
      Q_EMIT updateDownloaded();
    } else {
      qInfo() << __FUNCTION__
              << "Trying to update, even though we don't have a new version! "
                 "This shouldn't happen...";
      setUpdateReadyToInstall(false);
    }
  } catch (const std::exception &err) {
    qWarning() << __FUNCTION__ << "Updating failed with error: " << err.what();
    setUpdateReadyToInstall(false);
    Q_EMIT updateDownloadFailed();
  }
}

QString AutoUpdater::updateUrl() { return QString::fromStdString(kUpdateUrl); }

void AutoUpdater::applyUpdateAndRestart() {
  if (!updateReadyToInstall()) {
    Q_EMIT updatingFailed("Update not ready, try restarting the sample app");
    return;
  }

  try {
    if (updInfo != nullptr) {
      qInfo() << __FUNCTION__ << "Downloading and installing new update: "
              << QString::fromStdString(updInfo->targetFullRelease->version);
      // We should now have the package downloaded, so update and restart the
      // app
      manager.applyUpdatesAndRestart(updInfo->targetFullRelease.get());
    } else {
      qInfo() << __FUNCTION__
              << "Trying to update, even tho we don't have a new version! This "
                 "shouldn't happen...";
    }
  } catch (const std::exception &err) {
    qWarning() << __FUNCTION__ << "Updating failed with error: " << err.what();
    Q_EMIT updatingFailed(QString::fromStdString(err.what()));
  }
}

bool AutoUpdater::updateReadyToInstall() const {
  return m_updateReadyToInstall;
}

void AutoUpdater::setUpdateReadyToInstall(bool newUpdateReady) {
  if (m_updateReadyToInstall == newUpdateReady)
    return;
  m_updateReadyToInstall = newUpdateReady;
  Q_EMIT updateReadyToInstallChanged();
}

QString AutoUpdater::currentUpdateChannel() const {
  return m_currentUpdateChannel;
}

void AutoUpdater::setCurrentUpdateChannel(
    const QString &newCurrentUpdateChannel) {
  if (m_currentUpdateChannel == newCurrentUpdateChannel)
    return;
  m_currentUpdateChannel = newCurrentUpdateChannel;
  Q_EMIT currentUpdateChannelChanged();
}

QString AutoUpdater::currentVersion() const { return m_currentVersion; }

void AutoUpdater::setCurrentVersion(const QString &newCurrentVersion) {
  if (m_currentVersion == newCurrentVersion)
    return;
  m_currentVersion = newCurrentVersion;
  Q_EMIT currentVersionChanged();
}

QString AutoUpdater::newVersion() const { return m_newVersion; }

void AutoUpdater::setNewVersion(const QString &newNewVersion) {
  if (m_newVersion == newNewVersion)
    return;
  m_newVersion = newNewVersion;
  Q_EMIT newVersionChanged();
}
