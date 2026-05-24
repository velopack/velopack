#include "Velopack.hpp"

#include <iostream>
#include <string>
#include <cstdlib>
#include <optional>
#include <memory>

static std::string escape_json(const std::string& s) {
    std::string result;
    result.reserve(s.size() + 16);
    for (char c : s) {
        switch (c) {
            case '"':  result += "\\\""; break;
            case '\\': result += "\\\\"; break;
            case '\b': result += "\\b";  break;
            case '\f': result += "\\f";  break;
            case '\n': result += "\\n";  break;
            case '\r': result += "\\r";  break;
            case '\t': result += "\\t";  break;
            default:
                if (static_cast<unsigned char>(c) < 0x20) {
                    char buf[8];
                    snprintf(buf, sizeof(buf), "\\u%04x", static_cast<unsigned int>(static_cast<unsigned char>(c)));
                    result += buf;
                } else {
                    result += c;
                }
                break;
        }
    }
    return result;
}

static std::string asset_to_json(const Velopack::VelopackAsset& a) {
    std::string json;
    json += "{\n";
    json += "      \"PackageId\": \"" + escape_json(a.PackageId) + "\",\n";
    json += "      \"Version\": \"" + escape_json(a.Version) + "\",\n";
    json += "      \"FileName\": \"" + escape_json(a.FileName) + "\",\n";
    json += "      \"Type\": \"" + escape_json(a.Type) + "\",\n";
    json += "      \"SHA1\": \"" + escape_json(a.SHA1) + "\",\n";
    json += "      \"SHA256\": \"" + escape_json(a.SHA256) + "\",\n";
    json += "      \"Size\": " + std::to_string(a.Size) + ",\n";
    json += "      \"NotesMarkdown\": \"" + escape_json(a.NotesMarkdown) + "\",\n";
    json += "      \"NotesHtml\": \"" + escape_json(a.NotesHtml) + "\"\n";
    json += "    }";
    return json;
}

static std::string assets_to_json_array(const std::vector<Velopack::VelopackAsset>& assets) {
    std::string json = "[";
    for (size_t i = 0; i < assets.size(); i++) {
        if (i > 0) json += ", ";
        json += asset_to_json(assets[i]);
    }
    json += "]";
    return json;
}

static void print_usage(const char* argv0) {
    std::cerr << "Usage: " << argv0
              << " <source_type> <url_or_path> <token>"
              << " --channel <channel> --manifest <path> --packages-dir <path>"
              << " [--download]"
              << std::endl;
}

int main(int argc, char* argv[]) {
    try {
        if (argc < 4) {
            print_usage(argv[0]);
            return 1;
        }

        std::string sourceType = argv[1];
        std::string url = argv[2];
        std::string token = argv[3];

        std::string channel;
        std::string manifestPath;
        std::string packagesDir;
        bool doDownload = false;

        for (int i = 4; i < argc; i++) {
            std::string arg = argv[i];
            if (arg == "--channel" && i + 1 < argc) {
                channel = argv[++i];
            } else if (arg == "--manifest" && i + 1 < argc) {
                manifestPath = argv[++i];
            } else if (arg == "--packages-dir" && i + 1 < argc) {
                packagesDir = argv[++i];
            } else if (arg == "--download") {
                doDownload = true;
            } else {
                std::cerr << "Unknown argument: " << arg << std::endl;
                print_usage(argv[0]);
                return 1;
            }
        }

        if (manifestPath.empty() || packagesDir.empty()) {
            std::cerr << "Error: --manifest and --packages-dir are required." << std::endl;
            print_usage(argv[0]);
            return 1;
        }

        std::unique_ptr<Velopack::IUpdateSourcePointer> source;
        if (sourceType == "gitea") {
            source = std::make_unique<Velopack::GiteaSource>(url, token, false);
        } else if (sourceType == "gitlab") {
            source = std::make_unique<Velopack::GitlabSource>(url, token, false);
        } else if (sourceType == "http") {
            source = std::make_unique<Velopack::HttpSource>(url);
        } else if (sourceType == "file") {
            source = std::make_unique<Velopack::FileSource>(url);
        } else {
            std::cerr << "Error: Unknown source type '" << sourceType << "'." << std::endl;
            return 1;
        }

        Velopack::VelopackLocatorConfig locator;
        locator.ManifestPath = manifestPath;
        locator.PackagesDir = packagesDir;
        locator.UpdateExePath = argv[0];
        locator.RootAppDir = packagesDir;
        locator.CurrentBinaryDir = packagesDir;
        locator.IsPortable = true;

        Velopack::UpdateOptions opts;
        opts.AllowVersionDowngrade = false;
        opts.ExplicitChannel = channel.empty() ? std::optional<std::string>(std::nullopt) : std::optional<std::string>(channel);
        opts.MaximumDeltasBeforeFallback = 10;

        Velopack::UpdateManager manager(std::move(source), &opts, &locator);
        auto updateInfo = manager.CheckForUpdates();

        if (doDownload && updateInfo.has_value()) {
            manager.DownloadUpdates(updateInfo.value());
        }

        std::string json;
        json += "{\n";
        if (updateInfo.has_value()) {
            json += "  \"target\": " + asset_to_json(updateInfo->TargetFullRelease) + ",\n";
            json += "  \"deltas\": " + assets_to_json_array(updateInfo->DeltasToTarget) + ",\n";
            json += "  \"isDowngrade\": " + std::string(updateInfo->IsDowngrade ? "true" : "false") + ",\n";
        } else {
            json += "  \"target\": null,\n";
            json += "  \"deltas\": null,\n";
            json += "  \"isDowngrade\": false,\n";
        }
        json += "  \"feed\": null\n";
        json += "}\n";

        std::cout << json;
        return 0;

    } catch (const std::exception& ex) {
        std::cerr << "Error: " << ex.what() << std::endl;
        return 1;
    } catch (...) {
        std::cerr << "Error: Unknown exception occurred." << std::endl;
        return 1;
    }
}
