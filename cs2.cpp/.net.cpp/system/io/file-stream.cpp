#include "file-stream.hpp"
#include "helcpp_config.hpp"
#include "../../runtime/native_algorithm.hpp"
#include "../../runtime/native_memory_ops.hpp"
#include "../../runtime/native_exceptions.hpp"

#ifndef HE_CPP_USE_HOSTED_FILE_SYSTEM
#define HE_CPP_USE_HOSTED_FILE_SYSTEM 1
#endif

#ifndef HE_CPP_RUNTIME_HAS_FILE_DESCRIPTOR_APIS
// Existing hosted consumers retain descriptor APIs; limited-libc profiles explicitly opt out.
#define HE_CPP_RUNTIME_HAS_FILE_DESCRIPTOR_APIS 1
#endif

#if HE_CPP_USE_HOSTED_FILE_SYSTEM
#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM
#include HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER
#endif
#include <sys/stat.h> // For file size retrieval
#include <memory>
#include <string>
#include <vector>
#include <cerrno>
#include <cstring>
#include <fcntl.h>
#if defined(_WIN32)
#include <io.h>
#else
#include <unistd.h>
#endif
#endif

#if HE_CPP_USE_HOSTED_FILE_SYSTEM && HE_CPP_PLATFORM_PS2
namespace {
    bool FileStreamSupportStartsWithPs2CdromPrefix(const std::string& path) {
        return path.rfind("cdrom0:", 0) == 0;
    }

    std::string FileStreamSupportResolvePs2DiscReadPath(const std::string& path) {
        if (!FileStreamSupportStartsWithPs2CdromPrefix(path)) {
            return path;
        }

        std::string normalizedPath = path;
        he_cpp_alg::Replace(normalizedPath.begin(), normalizedPath.end(), '\\', '/');
        return normalizedPath;
    }

    std::vector<std::string> BuildPs2DiscReadCandidates(const std::string& resolvedPath) {
        std::vector<std::string> candidates;
        candidates.push_back(resolvedPath);
        if (resolvedPath.rfind("cdrom0:/", 0) == 0) {
            candidates.push_back("cdrom0:" + resolvedPath.substr(8));
        }

        return candidates;
    }

    std::vector<uint8_t> ReadPs2DiscFile(const std::string& path) {
        const std::vector<std::string> candidates = BuildPs2DiscReadCandidates(path);
        for (size_t candidateIndex = 0; candidateIndex < candidates.size(); candidateIndex++) {
            const std::string& candidatePath = candidates[candidateIndex];
            int fileDescriptor = open(candidatePath.c_str(), O_RDONLY);
            if (fileDescriptor < 0) {
                continue;
            }

            const off_t fileLength = lseek(fileDescriptor, 0, SEEK_END);
            if (fileLength < 0) {
                close(fileDescriptor);
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
                he_cpp_raise(InvalidOperationException());
#else
                he_cpp_raise(InvalidOperationException(std::string("Failed to determine file length: ") + candidatePath));
#endif
            }
            if (lseek(fileDescriptor, 0, SEEK_SET) < 0) {
                close(fileDescriptor);
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
                he_cpp_raise(InvalidOperationException());
#else
                he_cpp_raise(InvalidOperationException(std::string("Failed to seek file: ") + candidatePath));
#endif
            }

            std::vector<uint8_t> bytes(static_cast<size_t>(fileLength));
            size_t totalBytesRead = 0;
            while (totalBytesRead < bytes.size()) {
                const ssize_t bytesRead = read(
                    fileDescriptor,
                    bytes.data() + totalBytesRead,
                    bytes.size() - totalBytesRead);
                if (bytesRead <= 0) {
                    close(fileDescriptor);
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
                    he_cpp_raise(InvalidOperationException());
#else
                    he_cpp_raise(InvalidOperationException(std::string("Failed to read file: ") + candidatePath));
#endif
                }

                totalBytesRead += static_cast<size_t>(bytesRead);
            }

            close(fileDescriptor);
            if (totalBytesRead != bytes.size()) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
                he_cpp_raise(InvalidOperationException());
#else
                he_cpp_raise(InvalidOperationException(std::string("Failed to read file: ") + candidatePath));
#endif
            }

            return bytes;
        }

#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(FileNotFoundException());
#else
        he_cpp_raise(FileNotFoundException(std::string("Failed to open file: ") + path));
#endif
    }
}
#endif

#if HE_CPP_USE_HOSTED_FILE_SYSTEM
// Helper function to get file mode as C-style string
const char* GetFileMode(FileMode mode) {
    switch (mode) {
    case FileMode::Append: return "a+b";
    case FileMode::Create: return "w+b";
    case FileMode::CreateNew: return "wbx+";
    case FileMode::Open: return "rb";
    case FileMode::OpenOrCreate: return "r+b";
    case FileMode::Truncate: return "wb";
    default:
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(NotSupportedException());
#else
        he_cpp_raise(NotSupportedException("Invalid FileMode"));
#endif
    }
}

/// <summary>Validates supplied access before any open or truncation and selects the corresponding hosted descriptor mode.</summary>
const char* GetFileMode(FileMode mode, FileAccess access) {
    if (access != FileAccess::Read && access != FileAccess::Write && access != FileAccess::ReadWrite) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(ArgumentOutOfRangeException());
#else
        he_cpp_raise(ArgumentOutOfRangeException("access", "Invalid FileAccess value."));
#endif
    }
    if (((mode == FileMode::Create || mode == FileMode::CreateNew || mode == FileMode::Truncate) && access == FileAccess::Read) ||
        (mode == FileMode::Append && access != FileAccess::Write)) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(ArgumentException());
#else
        he_cpp_raise(ArgumentException("The requested FileMode and FileAccess combination is invalid."));
#endif
    }
    switch (mode) {
    case FileMode::Open:
    case FileMode::OpenOrCreate:
        return access == FileAccess::Read ? "rb" : "r+b";
    case FileMode::Create:
        return access == FileAccess::Write ? "wb" : "w+b";
    case FileMode::CreateNew:
        return access == FileAccess::Write ? "wbx" : "wbx+";
    case FileMode::Truncate:
        return access == FileAccess::Write ? "wb" : "w+b";
    case FileMode::Append:
        return "ab";
    default:
        return GetFileMode(mode);
    }
}
#endif

// Constructor
FileStream::FileStream(const uint8_t* data, size_t dataLength)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(true), readable(true), writable(false) {
    if (data == nullptr && dataLength > 0) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(InvalidOperationException());
#else
        he_cpp_raise(InvalidOperationException("Cannot create a memory-backed file stream from a null buffer."));
#endif
    }

    if (dataLength > 0) {
        memoryBuffer.assign(data, data + dataLength);
    }
    length = memoryBuffer.size();
}

/// <summary>Preserves legacy two-argument open strings and creation behavior through shared initialization.</summary>
FileStream::FileStream(const char* path, FileMode mode)
    : FileStream(path, mode, FileAccess::ReadWrite, FileShare::ReadWrite, true) {
}

/// <summary>Initializes path storage once and honors explicit access before opening a descriptor or read-only provider.</summary>
FileStream::FileStream(const char* path, FileMode mode, FileAccess access, FileShare share, bool legacyMode)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(false), readable(true), writable(true) {
    (void)share; // Existing FileShare policy is intentionally unchanged.
    if (path == nullptr) {
        he_cpp_raise(ArgumentNullException("path"));
    }
#if HE_CPP_USE_HOSTED_FILE_SYSTEM
    const char* openMode = legacyMode ? GetFileMode(mode) : GetFileMode(mode, access);
    const bool updateMode = std::strchr(openMode, '+') != nullptr;
    readable = legacyMode ? openMode[0] == 'r' || updateMode : access != FileAccess::Write;
    writable = legacyMode ? openMode[0] != 'r' || updateMode : access != FileAccess::Read;
#if !HE_CPP_RUNTIME_HAS_FILE_DESCRIPTOR_APIS
    // Limited-libc hosts can stream existing files through stdio, but cannot create or mutate them.
    if (mode != FileMode::Open || writable) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(NotSupportedException());
#else
        he_cpp_raise(NotSupportedException("FileStream supports only opening existing files for read-only access on this target."));
#endif
    }
#endif
#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM
    if (path != nullptr && mode == FileMode::Open && HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::CanHandlePath(path)) {
        if (!legacyMode && access != FileAccess::Read) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
            he_cpp_raise(NotSupportedException());
#else
            he_cpp_raise(NotSupportedException("The custom filesystem provider supports read-only file access."));
#endif
        }
        std::unique_ptr<FileStream> customStream(HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::OpenRead(path));
        file = customStream->file;
        memoryBuffer.swap(customStream->memoryBuffer);
        position = customStream->position;
        length = customStream->length;
        ownsMemoryBuffer = customStream->ownsMemoryBuffer;
        readable = customStream->readable;
        writable = customStream->writable;
        customStream->file = nullptr;
        customStream->ownsMemoryBuffer = false;
        return;
    }
#endif
#if HE_CPP_PLATFORM_PS2
    std::string resolvedPs2ReadPath = FileStreamSupportResolvePs2DiscReadPath(path != nullptr ? path : "");
    bool usesPs2DirectRead = mode == FileMode::Open && FileStreamSupportStartsWithPs2CdromPrefix(resolvedPs2ReadPath);
    if (usesPs2DirectRead) {
        if (!legacyMode && access != FileAccess::Read) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
            he_cpp_raise(NotSupportedException());
#else
            he_cpp_raise(NotSupportedException("PS2 disc paths support read-only file access."));
#endif
        }
        memoryBuffer = ReadPs2DiscFile(resolvedPs2ReadPath);
        ownsMemoryBuffer = true;
        writable = false;
        length = memoryBuffer.size();
        return;
    }
#endif
    file = std::fopen(path, openMode);
    if (!file) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(FileNotFoundException());
#else
        he_cpp_raise(FileNotFoundException(std::string("Failed to open file: ") + path));
#endif
    }

    UpdateLength();
#else
    (void)path;
    (void)mode;
    he_cpp_raise(NotSupportedException("Path-backed FileStream is unavailable when hosted filesystem support is disabled."));
#endif
}

/// <summary>Uses the requested FileAccess while retaining the established FileShare policy.</summary>
FileStream::FileStream(const char* path, FileMode mode, FileAccess access, FileShare share)
    : FileStream(path, mode, access, share, false) {
}

FileStream::FileStream(const HeCppString& path, FileMode mode)
    : FileStream(path.is_null() ? nullptr : path.c_str(), mode) {
}

FileStream::FileStream(const HeCppString& path, FileMode mode, FileAccess access, FileShare share)
    : FileStream(path.is_null() ? nullptr : path.c_str(), mode, access, share) {
}

// Destructor
FileStream::~FileStream() {
    Close();
}

// Reads data from file
size_t FileStream::Read(uint8_t* buffer, size_t offset, size_t count) {
    if (!CanRead()) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(NotSupportedException());
#else
        he_cpp_raise(NotSupportedException("This file stream does not support reading."));
#endif
    }
    if (!buffer) return 0;

    if (file == nullptr) {
        size_t available = position >= memoryBuffer.size() ? 0 : memoryBuffer.size() - position;
        size_t bytesRead = he_cpp_alg::Min(count, available);
        if (bytesRead == 0) {
            return 0;
        }

        he_cpp_memory::Copy(buffer + offset, memoryBuffer.data() + position, bytesRead);
        position += bytesRead;
        return bytesRead;
    }

#if HE_CPP_USE_HOSTED_FILE_SYSTEM
    std::fseek(file, position, SEEK_SET);

    size_t bytesRead = std::fread(buffer + offset, 1, count, file);
    position += bytesRead;
    return bytesRead;
#else
    he_cpp_raise(InvalidOperationException("Path-backed FileStream state is unavailable when hosted filesystem support is disabled."));
#endif
}

// Writes data to file
void FileStream::Write(const uint8_t* buffer, size_t offset, size_t count) {
    if (!CanWrite()) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(NotSupportedException());
#else
        he_cpp_raise(NotSupportedException("This file stream does not support writing."));
#endif
    }
    if (!buffer) return;

    if (file == nullptr) {
        size_t requiredLength = position + count;
        if (requiredLength > memoryBuffer.size()) {
            memoryBuffer.resize(requiredLength);
        }

        he_cpp_memory::Copy(memoryBuffer.data() + position, buffer + offset, count);
        position += count;
        length = memoryBuffer.size();
        return;
    }

#if HE_CPP_USE_HOSTED_FILE_SYSTEM
    std::fseek(file, position, SEEK_SET);

    size_t bytesWritten = std::fwrite(buffer + offset, 1, count, file);
    position += bytesWritten;
    UpdateLength();
#else
    he_cpp_raise(InvalidOperationException("Path-backed FileStream state is unavailable when hosted filesystem support is disabled."));
#endif
}

// Seeks to a position in file
size_t FileStream::Seek(int64_t offset, SeekOrigin origin) {
    if (!CanSeek()) return position;

    if (file == nullptr) {
        int64_t basePosition = 0;
        switch (origin) {
        case SeekOrigin::Begin: basePosition = 0; break;
        case SeekOrigin::Current: basePosition = static_cast<int64_t>(position); break;
        case SeekOrigin::End: basePosition = static_cast<int64_t>(length); break;
        }

        int64_t nextPosition = basePosition + offset;
        if (nextPosition < 0) {
            nextPosition = 0;
        } else if (static_cast<size_t>(nextPosition) > length) {
            nextPosition = static_cast<int64_t>(length);
        }

        position = static_cast<size_t>(nextPosition);
        return position;
    }

#if HE_CPP_USE_HOSTED_FILE_SYSTEM
    int seekMode;
    switch (origin) {
    case SeekOrigin::Begin: seekMode = SEEK_SET; break;
    case SeekOrigin::Current: seekMode = SEEK_CUR; break;
    case SeekOrigin::End: seekMode = SEEK_END; break;
    }

    std::fseek(file, offset, seekMode);
    position = std::ftell(file);
    return position;
#else
    he_cpp_raise(InvalidOperationException("Path-backed FileStream state is unavailable when hosted filesystem support is disabled."));
#endif
}

// Truncates or extends the file
void FileStream::SetLength(size_t newLength) {
    if (!CanWrite()) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(NotSupportedException());
#else
        he_cpp_raise(NotSupportedException("This file stream does not support resizing."));
#endif
    }
    if (file == nullptr) {
        memoryBuffer.resize(newLength);
        length = memoryBuffer.size();
        if (position > length) {
            position = length;
        }
        return;
    }

#if HE_CPP_USE_HOSTED_FILE_SYSTEM
#if !HE_CPP_RUNTIME_HAS_FILE_DESCRIPTOR_APIS
    (void)newLength;
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    he_cpp_raise(NotSupportedException());
#else
    he_cpp_raise(NotSupportedException("FileStream cannot resize a file-backed stream on this target."));
#endif
#else
    std::fflush(file);
#if defined(_WIN32)
    _chsize_s(fileno(file), newLength);
#else
    ftruncate(fileno(file), newLength);
#endif
    UpdateLength();
#endif
#else
    he_cpp_raise(InvalidOperationException("Path-backed FileStream state is unavailable when hosted filesystem support is disabled."));
#endif
}

// Updates the stored file length
void FileStream::UpdateLength() {
#if HE_CPP_USE_HOSTED_FILE_SYSTEM
    if (!file) {
        length = memoryBuffer.size();
        return;
    }

#if !HE_CPP_RUNTIME_HAS_FILE_DESCRIPTOR_APIS
    // Preserve the stdio cursor while determining length, without descriptor APIs or a file-sized buffer.
    const long savedPosition = std::ftell(file);
    long endPosition = -1;
    if (savedPosition >= 0 && std::fseek(file, 0, SEEK_END) == 0) {
        endPosition = std::ftell(file);
    }
    const bool restoredPosition = savedPosition >= 0 && std::fseek(file, savedPosition, SEEK_SET) == 0;
    if (endPosition < 0 || !restoredPosition) {
        // A failing constructor has no destructor, so release its stdio handle before raising.
        std::fclose(file);
        file = nullptr;
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        he_cpp_raise(InvalidOperationException());
#else
        he_cpp_raise(InvalidOperationException("Failed to determine or restore the file position while querying length."));
#endif
    }
    length = static_cast<size_t>(endPosition);
#else
    struct stat fileStat;
    if (fstat(fileno(file), &fileStat) == 0) {
        length = fileStat.st_size;
    }
#endif
#else
    length = memoryBuffer.size();
#endif
}

// Properties
bool FileStream::CanRead() const { return readable && (file != nullptr || ownsMemoryBuffer); }
bool FileStream::CanWrite() const { return writable && (file != nullptr || ownsMemoryBuffer); }
bool FileStream::CanSeek() const { return file != nullptr || ownsMemoryBuffer; }

size_t FileStream::Length() const { return length; }
size_t FileStream::Position() const { return position; }
void FileStream::SetPosition(size_t value) { position = he_cpp_alg::Min(value, length); }

// Internal byte-level operations
void FileStream::InternalReserve(size_t count) { (void)count; /* Not needed for file streams */ }

void FileStream::InternalWriteByte(uint8_t byte) {
    Write(&byte, 0, 1);
}

int FileStream::InternalReadByte() {
    uint8_t byte;
    return (Read(&byte, 0, 1) > 0) ? byte : -1;
}

// Flushes the file buffer
void FileStream::Flush() {
#if HE_CPP_USE_HOSTED_FILE_SYSTEM
    if (file) std::fflush(file);
#endif
}

// Closes the file
void FileStream::Close() {
#if HE_CPP_USE_HOSTED_FILE_SYSTEM
    if (file) {
        std::fclose(file);
        file = nullptr;
    }
#endif

    if (ownsMemoryBuffer) {
        memoryBuffer.clear();
        memoryBuffer.shrink_to_fit();
        ownsMemoryBuffer = false;
    }
}

// Cleanup function
void FileStream::Dispose() {
    Close();
}
