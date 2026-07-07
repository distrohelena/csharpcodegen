#include "path.hpp"

#include "helcpp_config.hpp"

#include <algorithm>
#include <cstdlib>
#include <string>
#include <vector>

#if defined(_WIN32)
#include <direct.h>
#else
#include <unistd.h>
#endif

#if HE_CPP_PLATFORM_PS2
namespace {
    bool IsPs2DevicePath(const std::string& path) {
        return path.rfind("cdrom0:", 0) == 0
            || path.rfind("host:", 0) == 0
            || path.rfind("mc0:", 0) == 0
            || path.rfind("mc1:", 0) == 0
            || path.rfind("mass:", 0) == 0;
    }

    std::string NormalizePs2Path(const std::string& path) {
        if (path.empty()) {
            return path;
        }

        std::string normalized = path;
        std::replace(normalized.begin(), normalized.end(), '/', '\\');
        const std::size_t deviceSeparatorIndex = normalized.find(':');
        if (deviceSeparatorIndex == std::string::npos) {
            return normalized;
        }

        std::string prefix = normalized.substr(0, deviceSeparatorIndex + 1);
        std::string suffix = normalized.substr(deviceSeparatorIndex + 1);
        while (!suffix.empty() && suffix.front() == '\\') {
            suffix.erase(suffix.begin());
        }

        std::string collapsedSuffix;
        bool previousWasSeparator = false;
        for (char character : suffix) {
            if (character == '\\') {
                if (!previousWasSeparator) {
                    collapsedSuffix.push_back(character);
                }

                previousWasSeparator = true;
                continue;
            }

            collapsedSuffix.push_back(character);
            previousWasSeparator = false;
        }

        if (collapsedSuffix.empty()) {
            return prefix + "\\";
        }

        return prefix + "\\" + collapsedSuffix;
    }

    std::string CombinePs2Path(const std::string& left, const std::string& right) {
        if (left.empty()) {
            return NormalizePs2Path(right);
        }

        if (right.empty()) {
            return NormalizePs2Path(left);
        }

        if (IsPs2DevicePath(right)) {
            return NormalizePs2Path(right);
        }

        std::string normalizedLeft = NormalizePs2Path(left);
        std::string normalizedRight = NormalizePs2Path(right);
        while (!normalizedRight.empty() && normalizedRight.front() == '\\') {
            normalizedRight.erase(normalizedRight.begin());
        }

        if (!normalizedLeft.empty() && normalizedLeft.back() != '\\') {
            normalizedLeft.push_back('\\');
        }

        return normalizedLeft + normalizedRight;
    }

    std::string GetPs2DirectoryName(const std::string& path) {
        std::string normalized = NormalizePs2Path(path);
        std::size_t separatorIndex = normalized.find_last_of("\\/");
        if (separatorIndex == std::string::npos) {
            return std::string();
        }

        if (separatorIndex > 0 && normalized[separatorIndex - 1] == ':') {
            return normalized.substr(0, separatorIndex + 1);
        }

        return normalized.substr(0, separatorIndex);
    }

    std::string GetPs2FileName(const std::string& path) {
        std::string normalized = NormalizePs2Path(path);
        std::size_t separatorIndex = normalized.find_last_of("\\/");
        std::string fileName = separatorIndex == std::string::npos ? normalized : normalized.substr(separatorIndex + 1);
        std::size_t versionSeparatorIndex = fileName.find(';');
        if (versionSeparatorIndex != std::string::npos) {
            fileName = fileName.substr(0, versionSeparatorIndex);
        }

        return fileName;
    }
}
#endif

#if HELENGINE_NINTENDO_DS_HAS_GENERATED_CORE
namespace {
    bool IsNintendoDsDevicePath(const std::string& path) {
        return path.rfind("nitro:", 0) == 0;
    }
}
#endif

namespace {
    bool IsGenericDirectorySeparator(char character) {
        return character == Path::DirectorySeparatorChar || character == Path::AltDirectorySeparatorChar;
    }

    std::size_t GetRootLength(const std::string& path) {
        if (path.empty()) {
            return 0;
        }

        if (path.size() >= 2 && path[1] == ':') {
            if (path.size() >= 3 && IsGenericDirectorySeparator(path[2])) {
                return 3;
            }

            return 2;
        }

        if (IsGenericDirectorySeparator(path[0])) {
            return 1;
        }

        return 0;
    }

    std::string NormalizeGenericPath(const std::string& path) {
        if (path.empty()) {
            return std::string();
        }

        std::string normalized = path;
        std::replace(normalized.begin(), normalized.end(), Path::AltDirectorySeparatorChar, Path::DirectorySeparatorChar);
        const std::size_t rootLength = GetRootLength(normalized);
        const bool rooted = rootLength > 0;
        std::string root = normalized.substr(0, rootLength);
        std::vector<std::string> segments;
        std::string segment;

        for (std::size_t index = rootLength; index <= normalized.size(); index++) {
            const bool endOfPath = index == normalized.size();
            const char character = endOfPath ? Path::DirectorySeparatorChar : normalized[index];
            if (!endOfPath && character != Path::DirectorySeparatorChar) {
                segment.push_back(character);
                continue;
            }

            if (segment == "..") {
                if (!segments.empty() && segments.back() != "..") {
                    segments.pop_back();
                } else if (!rooted) {
                    segments.push_back(segment);
                }
            } else if (!segment.empty() && segment != ".") {
                segments.push_back(segment);
            }

            segment.clear();
        }

        std::string result = root;
        for (std::size_t segmentIndex = 0; segmentIndex < segments.size(); segmentIndex++) {
            if (!result.empty() && result.back() != Path::DirectorySeparatorChar) {
                result.push_back(Path::DirectorySeparatorChar);
            }

            result += segments[segmentIndex];
        }

        if (result.empty()) {
            return rooted ? root : std::string(".");
        }

        return result;
    }

    std::string GetCurrentDirectoryPath() {
        char buffer[4096];
#if defined(_WIN32)
        if (_getcwd(buffer, static_cast<int>(sizeof(buffer))) == nullptr) {
            return std::string(".");
        }
#else
        if (getcwd(buffer, sizeof(buffer)) == nullptr) {
            return std::string(".");
        }
#endif
        return NormalizeGenericPath(buffer);
    }
}

std::string Path::Combine(const std::string& left, const std::string& right) {
#if HE_CPP_PLATFORM_PS2
    if (IsPs2DevicePath(left) || IsPs2DevicePath(right)) {
        return CombinePs2Path(left, right);
    }
#endif
#if HELENGINE_NINTENDO_DS_HAS_GENERATED_CORE
    if (IsNintendoDsDevicePath(left)) {
        if (right.empty()) {
            return left;
        }

        if (right[0] == '/') {
            return left + right;
        }

        return left + "/" + right;
    }
#endif
    if (left.empty()) {
        return NormalizeGenericPath(right);
    }

    if (right.empty()) {
        return NormalizeGenericPath(left);
    }

    if (IsPathRooted(right)) {
        return GetFullPath(right);
    }

    std::string combined = left;
    if (!combined.empty() && !IsGenericDirectorySeparator(combined.back())) {
        combined.push_back(DirectorySeparatorChar);
    }

    combined += right;
    return NormalizeGenericPath(combined);
}

std::string Path::Combine(const std::string& first, const std::string& second, const std::string& third) {
    return Combine(Combine(first, second), third);
}

std::string Path::GetDirectoryName(const std::string& path) {
    if (path.empty()) {
        return std::string();
    }

#if HE_CPP_PLATFORM_PS2
    if (IsPs2DevicePath(path)) {
        return GetPs2DirectoryName(path);
    }
#endif

    std::string normalized = NormalizeGenericPath(path);
    const std::size_t rootLength = GetRootLength(normalized);
    const std::size_t separatorIndex = normalized.find_last_of("\\/");
    if (separatorIndex == std::string::npos) {
        return std::string();
    }

    if (separatorIndex < rootLength) {
        return normalized.substr(0, rootLength);
    }

    return normalized.substr(0, separatorIndex);
}

std::string Path::GetFileName(const std::string& path) {
    if (path.empty()) {
        return std::string();
    }

#if HE_CPP_PLATFORM_PS2
    if (IsPs2DevicePath(path)) {
        return GetPs2FileName(path);
    }
#endif

    std::string normalized = NormalizeGenericPath(path);
    const std::size_t separatorIndex = normalized.find_last_of("\\/");
    if (separatorIndex == std::string::npos) {
        return normalized;
    }

    return normalized.substr(separatorIndex + 1);
}

std::string Path::GetFullPath(const std::string& path) {
#if HELENGINE_NINTENDO_DS_HAS_GENERATED_CORE
    if (IsNintendoDsDevicePath(path)) {
        return path;
    }
#endif
#if !HE_CPP_PLATFORM_IS_WINDOWS_HOST
    if (path.empty()) {
        return std::string(".");
    }

#if HE_CPP_PLATFORM_PS2
    if (IsPs2DevicePath(path)) {
        return NormalizePs2Path(path);
    }
#endif
    return NormalizeGenericPath(path);
#else
    if (path.empty()) {
        return GetCurrentDirectoryPath();
    }

    if (IsPathRooted(path)) {
        return NormalizeGenericPath(path);
    }

    return Combine(GetCurrentDirectoryPath(), path);
#endif
}

std::string Path::ChangeExtension(const std::string& path, const std::string& extension) {
    if (path.empty()) {
        return std::string();
    }

    std::string normalized = NormalizeGenericPath(path);
    const std::size_t separatorIndex = normalized.find_last_of("\\/");
    const std::size_t extensionIndex = normalized.find_last_of('.');
    std::string updated = normalized;
    if (extensionIndex != std::string::npos && (separatorIndex == std::string::npos || extensionIndex > separatorIndex)) {
        updated.erase(extensionIndex);
    }

    if (!extension.empty()) {
        if (extension[0] != '.') {
            updated.push_back('.');
        }

        updated += extension;
    }

    return updated;
}

bool Path::IsPathRooted(const std::string& path) {
    if (path.empty()) {
        return false;
    }

#if HE_CPP_PLATFORM_PS2
    if (IsPs2DevicePath(path)) {
        return true;
    }
#endif
#if HELENGINE_NINTENDO_DS_HAS_GENERATED_CORE
    if (IsNintendoDsDevicePath(path)) {
        return true;
    }
#endif
    return GetRootLength(path) > 0;
}
