#ifndef AUTOUPDATER_H
#define AUTOUPDATER_H

#include "Velopack.hpp"
#include <QDebug>
#include <QObject>
#include <QThread>

class AutoUpdater : public QObject {
  Q_OBJECT

  Q_PROPERTY(
      QString currentUpdateChannel READ currentUpdateChannel WRITE
          setCurrentUpdateChannel NOTIFY currentUpdateChannelChanged FINAL)
  Q_PROPERTY(QString currentVersion READ currentVersion WRITE setCurrentVersion
                 NOTIFY currentVersionChanged FINAL)
  Q_PROPERTY(
      bool updateReadyToInstall READ updateReadyToInstall WRITE
          setUpdateReadyToInstall NOTIFY updateReadyToInstallChanged FINAL)
  Q_PROPERTY(QString newVersion READ newVersion WRITE setNewVersion NOTIFY
                 newVersionChanged FINAL)

public:
  explicit AutoUpdater(QObject *parent = nullptr);
  ~AutoUpdater() override;

public:
  static AutoUpdater &instance() {
    static AutoUpdater _instance;
    return _instance;
  }
  bool updateReadyToInstall() const;
  void setUpdateReadyToInstall(bool newUpdateReady);

  QString currentUpdateChannel() const;
  void setCurrentUpdateChannel(const QString &newCurrentUpdateChannel);

  QString currentVersion() const;
  void setCurrentVersion(const QString &newCurrentVersion);

  QString newVersion() const;
  void setNewVersion(const QString &newNewVersion);

  Q_INVOKABLE void applyUpdateAndRestart();
  Q_INVOKABLE void checkForUpdates();
  Q_INVOKABLE void downloadLatestUpdate();
  Q_INVOKABLE QString updateUrl();

signals:
  void noNewUpdatesAvailable();
  void updateReadyToInstallChanged();
  void currentUpdateChannelChanged();
  void currentVersionChanged();
  void newVersionChanged();
  void updatingFailed(QString errorMg);
  void updateDownloadFailed();
  void updateDownloaded();

private:
  QString m_currentUpdateChannel;
  QString m_currentVersion;
  QString m_newVersion;
  bool m_updateReadyToInstall = false;
  bool m_updateDownloaded = false;
  Velopack::UpdateManagerSync manager{};
  std::shared_ptr<Velopack::UpdateInfo> updInfo{};
};

#endif // AUTOUPDATER_H
