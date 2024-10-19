#include <QGuiApplication>
#include <QQmlApplicationEngine>

#include "autoupdater.h"

int main(int argc, char *argv[]) {

  try {
    // Init Velopack hooks, MUST be as early as possible in main()
    // Velopack may exit / restart our app at this statement
    Velopack::startup(argv, argc);
  } catch (const std::exception &err) {
    qInfo() << "Error initating auto-updater, msg: " << err.what();
  }

  QGuiApplication app(argc, argv);

  // Init Auto Updater as a singleton
  qmlRegisterSingletonInstance("VelopackQt", 1, 0, "AutoUpdater",
                               &AutoUpdater::instance());

  QQmlApplicationEngine engine;
  const QUrl url(u"qrc:/VelopackQtSample/Main.qml"_qs);
  QObject::connect(
      &engine, &QQmlApplicationEngine::objectCreated, &app,
      [url](QObject *obj, const QUrl &objUrl) {
        if (!obj && url == objUrl)
          QCoreApplication::exit(-1);
      },
      Qt::QueuedConnection);
  engine.load(url);

  return app.exec();
}
