#ifndef FILE_STREAM_HPP
#define FILE_STREAM_HPP
#include "../../runtime/native_runtime.hpp"

#ifndef HE_CPP_USE_HOSTED_FILE_SYSTEM
#define HE_CPP_USE_HOSTED_FILE_SYSTEM 1
#endif


#include "stream.hpp"
#include "seek-origin.hpp"
#include "file-mode.hpp"
#include "file-access.hpp"
#include "file-share.hpp"
#if HE_CPP_USE_HOSTED_FILE_SYSTEM
#include <cstdio>  // For std::FILE*
#endif

class FileStream : public Stream {
private:
#if HE_CPP_USE_HOSTED_FILE_SYSTEM
    std::FILE* file;
#else
    void* file;
#endif
    HeCppVector<uint8_t> memoryBuffer;
    size_t position;
    size_t length;
    bool ownsMemoryBuffer;
    bool writable;

    void UpdateLength();  // Helper to update file size

public:
    FileStream(const uint8_t* data, size_t length);
    FileStream(const char* path, FileMode mode);
    FileStream(const char* path, FileMode mode, FileAccess access, FileShare share);
    FileStream(const HeCppString& path, FileMode mode);
    FileStream(const HeCppString& path, FileMode mode, FileAccess access, FileShare share);
    ~FileStream() override;

    size_t Read(uint8_t* buffer, size_t offset, size_t count) override;
    void Write(const uint8_t* buffer, size_t offset, size_t count) override;
    size_t Seek(int64_t offset, SeekOrigin origin) override;
    void SetLength(size_t length) override;

    bool CanRead() const override;
    bool CanWrite() const override;
    bool CanSeek() const override;

    size_t Length() const override;
    size_t Position() const override;
    void SetPosition(size_t value) override;

    void InternalReserve(size_t count) override;
    void InternalWriteByte(uint8_t byte) override;
    int InternalReadByte() override;

    void Dispose() override;
    void Close() override;
    void Flush() override;
};

#endif // FILE_STREAM_HPP
