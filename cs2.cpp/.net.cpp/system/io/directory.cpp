#include "directory.hpp"

#include "path.hpp"

#include <cerrno>
#include <string>
#include <sys/stat.h>
#include <vector>

#if defined(_WIN32)
#include <direct.h>
#endif

namespace {
    bool IsDirectorySeparator(char character) {
        return character == Path::DirectorySeparatorChar || character == Path::AltDirectorySeparatorChar;
    }

    bool CreateSingleDirectory(const std::string& path) {
        if (path.empty()) {
            return true;
        }

#if defined(_WIN32)
        const int createResult = _mkdir(path.c_str());
#else
        const int createResult = mkdir(path.c_str(), 0777);
#endif
        return createResult == 0 || errno == EEXIST;
    }
}

bool Directory::Exists(const std::string& path) {
    if (path.empty()) {
        return false;
    }

    struct stat status;
    return stat(path.c_str(), &status) == 0 && (status.st_mode & S_IFDIR) != 0;
}

void Directory::CreateDirectory(const std::string& path) {
    if (path.empty()) {
        return;
    }

    const std::string normalized = Path::GetFullPath(path);
    if (normalized.empty() || Exists(normalized)) {
        return;
    }

    std::string currentPath;
    const std::size_t rootLength = normalized.size() >= 2 && normalized[1] == ':'
        ? (normalized.size() >= 3 && IsDirectorySeparator(normalized[2]) ? 3 : 2)
        : (!normalized.empty() && IsDirectorySeparator(normalized[0]) ? 1 : 0);
    if (rootLength > 0) {
        currentPath = normalized.substr(0, rootLength);
    }

    std::string segment;
    for (std::size_t index = rootLength; index <= normalized.size(); index++) {
        const bool endOfPath = index == normalized.size();
        const char character = endOfPath ? Path::DirectorySeparatorChar : normalized[index];
        if (!endOfPath && !IsDirectorySeparator(character)) {
            segment.push_back(character);
            continue;
        }

        if (!segment.empty()) {
            if (!currentPath.empty() && !IsDirectorySeparator(currentPath.back())) {
                currentPath.push_back(Path::DirectorySeparatorChar);
            }

            currentPath += segment;
            if (!Exists(currentPath) && !CreateSingleDirectory(currentPath)) {
                return;
            }

            segment.clear();
        }
    }
}
