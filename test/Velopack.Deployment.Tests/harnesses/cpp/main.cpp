// C++ source-test harness for Velopack.Deployment.Tests.
//
// Invocation contract (shared by every language harness):
//   harness <config.json>
// The result JSON is written to stdout as the LAST line; all log noise goes to stderr.
// Exit 0 on success, exit 1 on failure (still emitting {"ok":false,"error":...} as the last stdout line).
//
// Config schema:
//   { "source":  { "kind":"file|http|gitea|gitlab|github", "url":"...", "token":null, "prerelease":false },
//     "locator": { PascalCase VelopackLocatorConfig fields },
//     "channel": "stable",
//     "action":  "check" | "download",
//     "downloadDir": "..." }
// Downloads always land in locator.PackagesDir (the binding ignores downloadDir); we report the real path.

#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

#include <nlohmann/json.hpp>

#include "Velopack.hpp"

using json = nlohmann::json;

// ---- Minimal public-domain SHA-256 (so the harness has no crypto dependency) --------------------

namespace {

struct Sha256 {
    uint32_t state[8];
    uint64_t bitlen = 0;
    uint8_t data[64];
    size_t datalen = 0;

    Sha256() {
        state[0] = 0x6a09e667;
        state[1] = 0xbb67ae85;
        state[2] = 0x3c6ef372;
        state[3] = 0xa54ff53a;
        state[4] = 0x510e527f;
        state[5] = 0x9b05688c;
        state[6] = 0x1f83d9ab;
        state[7] = 0x5be0cd19;
    }

    static uint32_t rotr(uint32_t x, uint32_t n) { return (x >> n) | (x << (32 - n)); }

    void transform(const uint8_t* chunk) {
        static const uint32_t k[64] = {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2};

        uint32_t m[64];
        for (uint32_t i = 0, j = 0; i < 16; ++i, j += 4) {
            m[i] = (uint32_t(chunk[j]) << 24) | (uint32_t(chunk[j + 1]) << 16) | (uint32_t(chunk[j + 2]) << 8) |
                   (uint32_t(chunk[j + 3]));
        }
        for (uint32_t i = 16; i < 64; ++i) {
            uint32_t s0 = rotr(m[i - 15], 7) ^ rotr(m[i - 15], 18) ^ (m[i - 15] >> 3);
            uint32_t s1 = rotr(m[i - 2], 17) ^ rotr(m[i - 2], 19) ^ (m[i - 2] >> 10);
            m[i] = m[i - 16] + s0 + m[i - 7] + s1;
        }

        uint32_t a = state[0], b = state[1], c = state[2], d = state[3];
        uint32_t e = state[4], f = state[5], g = state[6], h = state[7];
        for (uint32_t i = 0; i < 64; ++i) {
            uint32_t s1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
            uint32_t ch = (e & f) ^ (~e & g);
            uint32_t t1 = h + s1 + ch + k[i] + m[i];
            uint32_t s0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
            uint32_t maj = (a & b) ^ (a & c) ^ (b & c);
            uint32_t t2 = s0 + maj;
            h = g;
            g = f;
            f = e;
            e = d + t1;
            d = c;
            c = b;
            b = a;
            a = t1 + t2;
        }
        state[0] += a;
        state[1] += b;
        state[2] += c;
        state[3] += d;
        state[4] += e;
        state[5] += f;
        state[6] += g;
        state[7] += h;
    }

    void update(const uint8_t* buf, size_t len) {
        for (size_t i = 0; i < len; ++i) {
            data[datalen++] = buf[i];
            if (datalen == 64) {
                transform(data);
                bitlen += 512;
                datalen = 0;
            }
        }
    }

    std::string final_hex() {
        uint64_t total_bits = bitlen + uint64_t(datalen) * 8;
        data[datalen++] = 0x80;
        if (datalen > 56) {
            while (datalen < 64) data[datalen++] = 0;
            transform(data);
            datalen = 0;
        }
        while (datalen < 56) data[datalen++] = 0;
        for (int i = 7; i >= 0; --i) data[datalen++] = uint8_t((total_bits >> (i * 8)) & 0xff);
        transform(data);

        static const char* hex = "0123456789ABCDEF";
        std::string out;
        out.reserve(64);
        for (int i = 0; i < 8; ++i) {
            for (int j = 3; j >= 0; --j) {
                uint8_t byte = uint8_t((state[i] >> (j * 8)) & 0xff);
                out.push_back(hex[byte >> 4]);
                out.push_back(hex[byte & 0xf]);
            }
        }
        return out;
    }
};

std::string sha256_upper_of_file(const std::string& path) {
    std::ifstream f(path, std::ios::binary);
    if (!f) {
        throw std::runtime_error("could not open file for hashing: " + path);
    }
    Sha256 sha;
    std::vector<char> buf(1 << 16);
    while (f) {
        f.read(buf.data(), static_cast<std::streamsize>(buf.size()));
        std::streamsize got = f.gcount();
        if (got > 0) {
            sha.update(reinterpret_cast<const uint8_t*>(buf.data()), static_cast<size_t>(got));
        }
    }
    return sha.final_hex();
}

std::unique_ptr<Velopack::IUpdateSourcePointer> build_source(const json& s) {
    const std::string kind = s.at("kind").get<std::string>();
    const std::string url = s.at("url").get<std::string>();
    std::string token;
    if (s.contains("token") && !s.at("token").is_null()) {
        token = s.at("token").get<std::string>();
    }
    bool prerelease = s.value("prerelease", false);

    if (kind == "file") {
        return std::make_unique<Velopack::FileSource>(url);
    } else if (kind == "http") {
        return std::make_unique<Velopack::HttpSource>(url);
    } else if (kind == "github") {
        return std::make_unique<Velopack::GithubSource>(url, token, prerelease);
    } else if (kind == "gitlab") {
        return std::make_unique<Velopack::GitlabSource>(url, token, prerelease);
    } else if (kind == "gitea") {
        return std::make_unique<Velopack::GiteaSource>(url, token, prerelease);
    }
    throw std::runtime_error("unknown source kind: " + kind);
}

int run(const std::string& configPath) {
    std::ifstream cfgFile(configPath);
    if (!cfgFile) {
        throw std::runtime_error("could not open config file: " + configPath);
    }
    json config;
    cfgFile >> config;

    const json& loc = config.at("locator");
    Velopack::VelopackLocatorConfig locator;
    locator.RootAppDir = loc.at("RootAppDir").get<std::string>();
    locator.UpdateExePath = loc.at("UpdateExePath").get<std::string>();
    locator.PackagesDir = loc.at("PackagesDir").get<std::string>();
    locator.ManifestPath = loc.at("ManifestPath").get<std::string>();
    locator.CurrentBinaryDir = loc.at("CurrentBinaryDir").get<std::string>();
    locator.IsPortable = loc.at("IsPortable").get<bool>();

    const std::string channel = config.at("channel").get<std::string>();
    Velopack::UpdateOptions options;
    options.AllowVersionDowngrade = false;
    options.ExplicitChannel = channel;
    options.MaximumDeltasBeforeFallback = 10;

    auto source = build_source(config.at("source"));
    Velopack::UpdateManager manager(std::move(source), &options, &locator);

    std::optional<Velopack::UpdateInfo> update = manager.CheckForUpdates();

    json result;
    result["ok"] = true;
    result["updateAvailable"] = update.has_value();
    if (update.has_value()) {
        result["targetVersion"] = update->TargetFullRelease.Version;
    } else {
        result["targetVersion"] = nullptr;
    }

    const std::string action = config.value("action", std::string("check"));
    if (action == "download") {
        if (!update.has_value()) {
            throw std::runtime_error("no update available to download");
        }
        manager.DownloadUpdates(update.value());
        std::filesystem::path downloaded =
            std::filesystem::path(locator.PackagesDir) / update->TargetFullRelease.FileName;
        if (!std::filesystem::exists(downloaded)) {
            throw std::runtime_error("expected downloaded file not found: " + downloaded.string());
        }
        result["downloadedFile"] = downloaded.string();
        result["sha256"] = sha256_upper_of_file(downloaded.string());
    }

    std::cout << result.dump() << std::endl;
    return 0;
}

}  // namespace

int main(int argc, char** argv) {
    if (argc < 2) {
        json err;
        err["ok"] = false;
        err["error"] = "usage: harness <config.json>";
        std::cout << err.dump() << std::endl;
        return 1;
    }

    try {
        return run(argv[1]);
    } catch (const std::exception& ex) {
        std::cerr << ex.what() << std::endl;
        json err;
        err["ok"] = false;
        err["error"] = ex.what();
        std::cout << err.dump() << std::endl;
        return 1;
    } catch (...) {
        json err;
        err["ok"] = false;
        err["error"] = "unknown fatal error";
        std::cout << err.dump() << std::endl;
        return 1;
    }
}
