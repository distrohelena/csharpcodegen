#include "path.hpp"

#include "helcpp_config.hpp"

#include "../../runtime/native_algorithm.hpp"
#include <stdlib.h>



#if HE_CPP_PLATFORM_IS_WINDOWS_HOST && defined(_WIN32)
#include <direct.h>
#elif HE_CPP_PLATFORM_IS_WINDOWS_HOST
#include <unistd.h>
#endif

#if HE_CPP_PLATFORM_PS2
namespace {
    bool IsPs2DevicePath(const HeCppString& path) {
        return path.rfind("cdrom0:", 0) == 0
            || path.rfind("host:", 0) == 0
            || path.rfind("mc0:", 0) == 0
            || path.rfind("mc1:", 0) == 0
            || path.rfind("mass:", 0) == 0;
    }

    HeCppString NormalizePs2Path(const HeCppString& path) {
        if (path.empty()) {
            return path;
        }

        HeCppString normalized = path;
        he_cpp_alg::Replace(normalized.begin(), normalized.end(), '/', '\\');
        const std::size_t deviceSeparatorIndex = normalized.find(':');
        if (deviceSeparatorIndex == HeCppString::npos) {
            return normalized;
        }

        HeCppString prefix = normalized.substr(0, deviceSeparatorIndex + 1);
        HeCppString suffix = normalized.substr(deviceSeparatorIndex + 1);
        while (!suffix.empty() && suffix.front() == '\\') {
            suffix.erase(suffix.begin());
        }

        HeCppString collapsedSuffix;
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

    HeCppString CombinePs2Path(const HeCppString& left, const HeCppString& right) {
        if (left.empty()) {
            return NormalizePs2Path(right);
        }

        if (right.empty()) {
            return NormalizePs2Path(left);
        }

        if (IsPs2DevicePath(right)) {
            return NormalizePs2Path(right);
        }

        HeCppString normalizedLeft = NormalizePs2Path(left);
        HeCppString normalizedRight = NormalizePs2Path(right);
        while (!normalizedRight.empty() && normalizedRight.front() == '\\') {
            normalizedRight.erase(normalizedRight.begin());
        }

        if (!normalizedLeft.empty() && normalizedLeft.back() != '\\') {
            normalizedLeft.push_back('\\');
        }

        return normalizedLeft + normalizedRight;
    }

    HeCppString GetPs2DirectoryName(const HeCppString& path) {
        HeCppString normalized = NormalizePs2Path(path);
        std::size_t separatorIndex = normalized.find_last_of("\\/");
        if (separatorIndex == HeCppString::npos) {
            return HeCppString();
        }

        if (separatorIndex > 0 && normalized[separatorIndex - 1] == ':') {
            return normalized.substr(0, separatorIndex + 1);
        }

        return normalized.substr(0, separatorIndex);
    }

    HeCppString GetPs2FileName(const HeCppString& path) {
        HeCppString normalized = NormalizePs2Path(path);
        std::size_t separatorIndex = normalized.find_last_of("\\/");
        HeCppString fileName = separatorIndex == HeCppString::npos ? normalized : normalized.substr(separatorIndex + 1);
        std::size_t versionSeparatorIndex = fileName.find(';');
        if (versionSeparatorIndex != HeCppString::npos) {
            fileName = fileName.substr(0, versionSeparatorIndex);
        }

        return fileName;
    }
}
#endif

#if HELENGINE_NINTENDO_DS_HAS_GENERATED_CORE
namespace {
    bool IsNintendoDsDevicePath(const HeCppString& path) {
        return path.rfind("nitro:", 0) == 0;
    }
}
#endif

namespace {
    bool IsGenericDirectorySeparator(char character) {
        return character == Path::DirectorySeparatorChar || character == Path::AltDirectorySeparatorChar;
    }

    std::size_t GetRootLength(const HeCppString& path) {
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

    HeCppString NormalizeGenericPath(const HeCppString& path) {
        if (path.empty()) {
            return HeCppString();
        }

        HeCppString normalized = path;
        he_cpp_alg::Replace(normalized.begin(), normalized.end(), Path::AltDirectorySeparatorChar, Path::DirectorySeparatorChar);
        const std::size_t rootLength = GetRootLength(normalized);
        const bool rooted = rootLength > 0;
        HeCppString root = normalized.substr(0, rootLength);
        HeCppVector<HeCppString> segments;
        HeCppString segment;

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

        HeCppString result = root;
        for (std::size_t segmentIndex = 0; segmentIndex < segments.size(); segmentIndex++) {
            if (!result.empty() && result.back() != Path::DirectorySeparatorChar) {
                result.push_back(Path::DirectorySeparatorChar);
            }

            result += segments[segmentIndex];
        }

        if (result.empty()) {
            return rooted ? root : HeCppString(".");
        }

        return result;
    }

#if HE_CPP_PLATFORM_IS_WINDOWS_HOST
    HeCppString GetCurrentDirectoryPath() {
        char buffer[4096];
#if HE_CPP_PLATFORM_IS_WINDOWS_HOST && defined(_WIN32)
        if (_getcwd(buffer, static_cast<int>(sizeof(buffer))) == nullptr) {
            return HeCppString(".");
        }
#else
        if (getcwd(buffer, sizeof(buffer)) == nullptr) {
            return HeCppString(".");
        }
#endif
        return NormalizeGenericPath(buffer);
    }
#endif
}

HeCppString Path::Combine(const HeCppString& left, const HeCppString& right) {
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

    HeCppString combined = left;
    if (!combined.empty() && !IsGenericDirectorySeparator(combined.back())) {
        combined.push_back(DirectorySeparatorChar);
    }

    combined += right;
    return NormalizeGenericPath(combined);
}

HeCppString Path::Combine(const HeCppString& first, const HeCppString& second, const HeCppString& third) {
    return Combine(Combine(first, second), third);
}

HeCppString Path::GetDirectoryName(const HeCppString& path) {
    if (path.empty()) {
        return HeCppString();
    }

#if HE_CPP_PLATFORM_PS2
    if (IsPs2DevicePath(path)) {
        return GetPs2DirectoryName(path);
    }
#endif

    HeCppString normalized = NormalizeGenericPath(path);
    const std::size_t rootLength = GetRootLength(normalized);
    const std::size_t separatorIndex = normalized.find_last_of("\\/");
    if (separatorIndex == HeCppString::npos) {
        return HeCppString();
    }

    if (separatorIndex < rootLength) {
        return normalized.substr(0, rootLength);
    }

    return normalized.substr(0, separatorIndex);
}

HeCppString Path::GetFileName(const HeCppString& path) {
    if (path.empty()) {
        return HeCppString();
    }

#if HE_CPP_PLATFORM_PS2
    if (IsPs2DevicePath(path)) {
        return GetPs2FileName(path);
    }
#endif

    HeCppString normalized = NormalizeGenericPath(path);
    const std::size_t separatorIndex = normalized.find_last_of("\\/");
    if (separatorIndex == HeCppString::npos) {
        return normalized;
    }

    return normalized.substr(separatorIndex + 1);
}

HeCppString Path::GetFullPath(const HeCppString& path) {
#if HELENGINE_NINTENDO_DS_HAS_GENERATED_CORE
    if (IsNintendoDsDevicePath(path)) {
        return path;
    }
#endif
#if !HE_CPP_PLATFORM_IS_WINDOWS_HOST
    if (path.empty()) {
        return HeCppString(".");
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

HeCppString Path::ChangeExtension(const HeCppString& path, const HeCppString& extension) {
    if (path.empty()) {
        return HeCppString();
    }

    HeCppString normalized = NormalizeGenericPath(path);
    const std::size_t separatorIndex = normalized.find_last_of("\\/");
    const std::size_t extensionIndex = normalized.find_last_of('.');
    HeCppString updated = normalized;
    if (extensionIndex != HeCppString::npos && (separatorIndex == HeCppString::npos || extensionIndex > separatorIndex)) {
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

bool Path::IsPathRooted(const HeCppString& path) {
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
