
// This header assumes all strings will be in UTF-8. If you are using wstring's on Windows, 
// you will need to convert them to UTF-8 before using any of the functions in this header.
// Additionally, if you are on Windows, to enable unicode you will need to specify the UTF-8 
// codepage in your application's manifest file. 
// https://learn.microsoft.com/en-us/windows/apps/design/globalizing/use-utf8-code-page

#pragma once

#ifndef VELOPACKSDK_H
#define VELOPACKSDK_H

#include <string>
#include <functional>
#include <stdexcept>
#include <filesystem>
#include <iostream>
#include <fstream>
#include <sstream>
#include <vector>

// https://github.com/sheredom/subprocess.h
#include "subprocess.h"

// import windows header only if we are using MSVC
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#define PATH_MAX MAX_PATH
#include <windows.h>
#endif // VELO_MSVC

struct velo_update_info {
	bool is_update_available;
	std::string version;
	std::string sha1;
	std::string file_name;
	uint64_t file_size;

	velo_update_info()
		: is_update_available(false), version(""), sha1(""), file_name(""), file_size(0) {}

	velo_update_info(std::string version, std::string sha1, std::string file_name, uint64_t file_size)
		: is_update_available(true), version(version), sha1(sha1), file_name(file_name), file_size(file_size) {}
};

#if UNICODE
void velo_startup(wchar_t** args, size_t c_args) {
	for (size_t i = 0; i < c_args; ++i) {
		if (std::wstring(args[i]) == L"--veloapp-install") {
			exit(0);
		}
		if (std::wstring(args[i]) == L"--veloapp-updated") {
			exit(0);
		}
		if (std::wstring(args[i]) == L"--veloapp-obsolete") {
			exit(0);
		}
		if (std::wstring(args[i]) == L"--veloapp-uninstall") {
			exit(0);
		}
	}
}
#endif // UNICODE

void velo_startup(char** args, size_t c_args) {
	for (size_t i = 0; i < c_args; ++i) {
		if (std::string(args[i]) == "--veloapp-install") {
			exit(0);
		}
		if (std::string(args[i]) == "--veloapp-updated") {
			exit(0);
		}
		if (std::string(args[i]) == "--veloapp-obsolete") {
			exit(0);
		}
		if (std::string(args[i]) == "--veloapp-uninstall") {
			exit(0);
		}
	}
}

std::string velo_get_own_exe_path() {
	const size_t buf_size = PATH_MAX;
	char path_buf[buf_size];
	size_t bytes_read = buf_size;

#ifdef __APPLE__
	if (_NSGetExecutablePath(path_buf, &bytes_read) != 0) {
		throw std::runtime_error("Buffer size is too small for executable path.");
	}
#elif defined(_WIN32)
	HMODULE hMod = GetModuleHandleA(NULL);
	bytes_read = GetModuleFileNameA(hMod, path_buf, buf_size);
#else
	bytes_read = readlink("/proc/self/exe", path_buf, bufSize);
	if ((int)bytes_read == -1) {
		throw std::runtime_error("Permission denied to /proc/self/exe.");
	}
#endif

	return std::string(path_buf, bytes_read);
}

std::string velo_get_update_exe_path() {
	return "C:\\Users\\Caelan\\AppData\\Local\\VeloCppWinSample\\Update.exe";
	std::string own_path = velo_get_own_exe_path();
	std::filesystem::path p(own_path);

#ifdef __APPLE__
	p = p.parent_path() / "UpdateMac";
#elif defined(_WIN32)
	p = p.parent_path().parent_path() / "Update.exe";
#else
	p = p.parent_path() / "UpdateNix";
#endif

	if (!std::filesystem::exists(p)) {
		throw std::runtime_error("Update executable not found. Is this an installed app?");
	}

	return p.string();
}

subprocess_s velo_start_subprocess(const std::vector<std::string>& command_line, int options) {
	const char** command_line_array = new const char* [command_line.size() + 1];
	for (size_t i = 0; i < command_line.size(); ++i) {
		command_line_array[i] = command_line[i].c_str();
	}
	command_line_array[command_line.size()] = NULL; // last element must be NULL

	struct subprocess_s subprocess;
	int result = subprocess_create(command_line_array, options, &subprocess);
	delete[] command_line_array; // clean up the array

	if (result != 0) {
		throw std::runtime_error("Unable to start Update process.");
	}

	return subprocess;
}

std::string velo_get_version() {
	std::string update_exe = velo_get_update_exe_path();
	std::vector<std::string> command_line;
	command_line.push_back(update_exe);
	command_line.push_back("get-version");
	subprocess_s subprocess = velo_start_subprocess(command_line, subprocess_option_no_window);

	// read all stdout from the process
	FILE* p_stdout = subprocess_stdout(&subprocess);
	std::filebuf buf = std::basic_filebuf<char>(p_stdout);
	std::istream is(&buf);
	std::stringstream buffer;
	buffer << is.rdbuf();
	std::string output = buffer.str();

	const char* ws = " \t\n\r\f\v";
	output.erase(0, output.find_first_not_of(ws));
	output.erase(output.find_last_not_of(ws) + 1);

	return output;
}

velo_update_info velo_check_for_updates(const char* url_or_path, bool allow_downgrade = false, char* explicit_channel = 0) {
	std::string update_exe = velo_get_update_exe_path();
	std::vector<std::string> command_line;
	command_line.push_back(update_exe);
	command_line.push_back("check");
	command_line.push_back("--url");
	command_line.push_back(url_or_path);
	command_line.push_back("--format");
	command_line.push_back("text");
	if (allow_downgrade) {
		command_line.push_back("--downgrade");
	}
	if (explicit_channel != NULL) {
		command_line.push_back("--channel");
		command_line.push_back(explicit_channel);
	}

	subprocess_s subprocess = velo_start_subprocess(command_line, subprocess_option_no_window);

	int process_return;
	if (subprocess_join(&subprocess, &process_return) != 0) {
		throw std::runtime_error("Unable to join Update process.");
	}

	// read all stdout from the process
	FILE* p_stdout = subprocess_stdout(&subprocess);
	std::filebuf buf = std::basic_filebuf<char>(p_stdout);
	std::istream is(&buf);
	std::stringstream buffer;
	buffer << is.rdbuf();
	std::string output = buffer.str();

	if (output.empty() || output.rfind("null", 0) == 0) {
		return velo_update_info();
	}

	// split the output into whitespace-delimited tokens
	std::istringstream iss(output);
	std::vector<std::string> tokens;
	std::string token;
	while (iss >> token) {
		tokens.push_back(token);
	}

	// parse the tokens into an update_info struct
	velo_update_info info(tokens[0], tokens[1], tokens[2], std::stoull(tokens[3]));
	return info;
}

void velo_download_updates(const char* url_or_path, const char* release_name,
	std::function<void(uint8_t)> progress_fn, std::function<void(std::string)> complete_fn) {
	std::string update_exe = velo_get_update_exe_path();
	std::vector<std::string> command_line;
	command_line.push_back(update_exe);
	command_line.push_back("download");
	command_line.push_back("--clean");
	command_line.push_back("--url");
	command_line.push_back(url_or_path);
	command_line.push_back("--format");
	command_line.push_back("text");
	command_line.push_back("--name");
	command_line.push_back(release_name);

	subprocess_s subprocess = velo_start_subprocess(command_line, subprocess_option_no_window | subprocess_option_enable_async);

	const unsigned BUFFER_SIZE = 1024;
	char readBuffer[BUFFER_SIZE];
	std::string accumulatedData;

	const char* ws = " \t\n\r\f\v";
	auto handle_line = [&ws, &progress_fn, &complete_fn](std::string& line)
		{
			line.erase(0, line.find_first_not_of(ws));
			line.erase(line.find_last_not_of(ws) + 1);

			if (line.rfind("complete:", 0) == 0) {
				std::string pkg_path = line.substr(9);
				pkg_path.erase(0, pkg_path.find_first_not_of(ws));
				pkg_path.erase(pkg_path.find_last_not_of(ws) + 1);
				complete_fn(pkg_path);
				return true;
			}

			if (line.rfind("err:", 0) == 0) {
				throw new std::runtime_error("Error downloading update: " + line);
			}

			try {
				uint8_t progress = static_cast<uint8_t>(std::stoi(line));
				progress_fn(progress);
			}
			catch (const std::exception& e) {
			}
			return false;
		};

	// read all stdout from the process one line at a time
	while (true) {
		unsigned bytesRead = subprocess_read_stdout(&subprocess, readBuffer, BUFFER_SIZE - 1);

		if (bytesRead == 0) {
			// bytesRead is 0, indicating the process has completed
			// Process any remaining data in accumulatedData as the last line if needed
			if (!accumulatedData.empty()) {
				handle_line(accumulatedData);
			}
			return;
		}

		readBuffer[bytesRead] = '\0';
		accumulatedData += readBuffer;

		// Process accumulated data for lines
		size_t pos;
		while ((pos = accumulatedData.find('\n')) != std::string::npos) {
			std::string line = accumulatedData.substr(0, pos);
			if (handle_line(line)) {
				return; // complete or err
			}
			accumulatedData.erase(0, pos + 1);
		}
	}
}

void velo_apply_updates(bool restart, const char* package_path = 0) {
	std::string update_exe = velo_get_update_exe_path();
	std::vector<std::string> command_line;
	command_line.push_back(update_exe);
	command_line.push_back("apply");
	if (package_path != NULL) {
		command_line.push_back("--package");
		command_line.push_back(package_path);
	}
	if (restart) {
		command_line.push_back("--restart");
	}

	subprocess_s subprocess = velo_start_subprocess(command_line, subprocess_option_no_window);
	exit(0);
}


#endif // VELOPACKSDK_H