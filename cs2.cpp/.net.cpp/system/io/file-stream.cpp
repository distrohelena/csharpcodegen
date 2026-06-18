#include "file-stream.hpp"
#include "helcpp_config.hpp"
#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM
#include HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_HEADER
#endif
#include <stdexcept>  // For exceptions
#include <cstring>    // For std::memcpy
#include <sys/stat.h> // For file size retrieval
#include <memory>
#include <algorithm>
#include <cerrno>
#include <fcntl.h>
#if defined(_WIN32)
#include <io.h>
#else
#include <unistd.h>
#endif

#if HE_CPP_PLATFORM_PS2
namespace {
    bool FileStreamSupportStartsWithPs2CdromPrefix(const std::string& path) {
        return path.rfind("cdrom0:", 0) == 0;
    }

    std::string FileStreamSupportResolvePs2DiscReadPath(const std::string& path) {
        if (!FileStreamSupportStartsWithPs2CdromPrefix(path)) {
            return path;
        }

        std::string normalizedPath = path;
        std::replace(normalizedPath.begin(), normalizedPath.end(), '\\', '/');
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
                throw std::runtime_error(std::string("Failed to determine file length: ") + candidatePath);
            }
            if (lseek(fileDescriptor, 0, SEEK_SET) < 0) {
                close(fileDescriptor);
                throw std::runtime_error(std::string("Failed to seek file: ") + candidatePath);
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
                    throw std::runtime_error(std::string("Failed to read file: ") + candidatePath);
                }

                totalBytesRead += static_cast<size_t>(bytesRead);
            }

            close(fileDescriptor);
            if (totalBytesRead != bytes.size()) {
                throw std::runtime_error(std::string("Failed to read file: ") + candidatePath);
            }

            return bytes;
        }

        throw std::runtime_error(std::string("Failed to open file: ") + path);
    }
}
#endif

// Helper function to get file mode as C-style string
const char* GetFileMode(FileMode mode) {
    switch (mode) {
    case FileMode::Append: return "a+b";
    case FileMode::Create: return "w+b";
    case FileMode::CreateNew: return "wbx+";
    case FileMode::Open: return "rb";
    case FileMode::OpenOrCreate: return "r+b";
    case FileMode::Truncate: return "wb";
    default: throw std::runtime_error("Invalid FileMode");
    }
}

// Constructor
FileStream::FileStream(const uint8_t* data, size_t dataLength)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(true), writable(false) {
    if (data == nullptr && dataLength > 0) {
        throw std::runtime_error("Cannot create a memory-backed file stream from a null buffer.");
    }

    memoryBuffer.assign(data, data + dataLength);
    length = memoryBuffer.size();
}

FileStream::FileStream(const char* path, FileMode mode)
    : file(nullptr), memoryBuffer(), position(0), length(0), ownsMemoryBuffer(false), writable(true) {
#if HE_CPP_RUNTIME_HAS_CUSTOM_FILE_SYSTEM
    if (path != nullptr && mode == FileMode::Open && HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::CanHandlePath(path)) {
        std::unique_ptr<FileStream> customStream(HE_CPP_RUNTIME_CUSTOM_FILE_SYSTEM_TYPE::OpenRead(path));
        file = customStream->file;
        memoryBuffer.swap(customStream->memoryBuffer);
        position = customStream->position;
        length = customStream->length;
        ownsMemoryBuffer = customStream->ownsMemoryBuffer;
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
        memoryBuffer = ReadPs2DiscFile(resolvedPs2ReadPath);
        ownsMemoryBuffer = true;
        writable = false;
        length = memoryBuffer.size();
        return;
    }
#endif
    file = std::fopen(path, GetFileMode(mode));
    if (!file) {
        throw std::runtime_error(std::string("Failed to open file: ") + path);
    }

    UpdateLength();
}

FileStream::FileStream(const char* path, FileMode mode, FileAccess, FileShare)
    : FileStream(path, mode) {
}

FileStream::FileStream(const std::string& path, FileMode mode)
    : FileStream(path.c_str(), mode) {
}

FileStream::FileStream(const std::string& path, FileMode mode, FileAccess access, FileShare share)
    : FileStream(path.c_str(), mode, access, share) {
}

// Destructor
FileStream::~FileStream() {
    Close();
}

// Reads data from file
size_t FileStream::Read(uint8_t* buffer, size_t offset, size_t count) {
    if (!CanRead() || !buffer) return 0;

    if (file == nullptr) {
        size_t available = position >= memoryBuffer.size() ? 0 : memoryBuffer.size() - position;
        size_t bytesRead = std::min(count, available);
        if (bytesRead == 0) {
            return 0;
        }

        std::memcpy(buffer + offset, memoryBuffer.data() + position, bytesRead);
        position += bytesRead;
        return bytesRead;
    }

    std::fseek(file, position, SEEK_SET);

    size_t bytesRead = std::fread(buffer + offset, 1, count, file);
    position += bytesRead;
    return bytesRead;
}

// Writes data to file
void FileStream::Write(const uint8_t* buffer, size_t offset, size_t count) {
    if (!CanWrite() || !buffer) return;

    if (file == nullptr) {
        size_t requiredLength = position + count;
        if (requiredLength > memoryBuffer.size()) {
            memoryBuffer.resize(requiredLength);
        }

        std::memcpy(memoryBuffer.data() + position, buffer + offset, count);
        position += count;
        length = memoryBuffer.size();
        return;
    }

    std::fseek(file, position, SEEK_SET);

    size_t bytesWritten = std::fwrite(buffer + offset, 1, count, file);
    position += bytesWritten;
    UpdateLength();
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

    int seekMode;
    switch (origin) {
    case SeekOrigin::Begin: seekMode = SEEK_SET; break;
    case SeekOrigin::Current: seekMode = SEEK_CUR; break;
    case SeekOrigin::End: seekMode = SEEK_END; break;
    }

    std::fseek(file, offset, seekMode);
    position = std::ftell(file);
    return position;
}

// Truncates or extends the file
void FileStream::SetLength(size_t newLength) {
    if (file == nullptr) {
        if (!writable) {
            return;
        }

        memoryBuffer.resize(newLength);
        length = memoryBuffer.size();
        if (position > length) {
            position = length;
        }
        return;
    }

    std::fflush(file);
#if defined(_WIN32)
    _chsize_s(fileno(file), newLength);
#else
    ftruncate(fileno(file), newLength);
#endif
    UpdateLength();
}

// Updates the stored file length
void FileStream::UpdateLength() {
    if (!file) {
        length = memoryBuffer.size();
        return;
    }

    struct stat fileStat;
    if (fstat(fileno(file), &fileStat) == 0) {
        length = fileStat.st_size;
    }
}

// Properties
bool FileStream::CanRead() const { return file != nullptr || ownsMemoryBuffer; }
bool FileStream::CanWrite() const { return file != nullptr || (ownsMemoryBuffer && writable); }
bool FileStream::CanSeek() const { return file != nullptr || ownsMemoryBuffer; }

size_t FileStream::Length() const { return length; }
size_t FileStream::Position() const { return position; }
void FileStream::SetPosition(size_t value) { position = std::min(value, length); }

// Internal byte-level operations
void FileStream::InternalReserve(size_t count) { /* Not needed for file streams */ }

void FileStream::InternalWriteByte(uint8_t byte) {
    Write(&byte, 0, 1);
}

int FileStream::InternalReadByte() {
    uint8_t byte;
    return (Read(&byte, 0, 1) > 0) ? byte : -1;
}

// Flushes the file buffer
void FileStream::Flush() {
    if (file) std::fflush(file);
}

// Closes the file
void FileStream::Close() {
    if (file) {
        std::fclose(file);
        file = nullptr;
    }

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
