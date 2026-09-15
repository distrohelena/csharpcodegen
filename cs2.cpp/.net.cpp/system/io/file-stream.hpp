#ifndef FILE_STREAM_HPP
#define FILE_STREAM_HPP
#include "../../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_VECTOR
#error "system/io/file-stream.hpp requires HE_CPP_USE_STD_VECTOR=1; memory-backed file buffers still use std::vector."
#endif

#if !HE_CPP_USE_STD_STRING
#error "system/io/file-stream.hpp requires HE_CPP_USE_STD_STRING=1; file paths have not been adapted to custom string storage."
#endif

#if !HE_CPP_USE_EXCEPTIONS
#error "system/io/file-stream.hpp requires HE_CPP_USE_EXCEPTIONS=1; file operations still throw hosted exceptions."
#endif


#include "stream.hpp"
#include "seek-origin.hpp"
#include "file-mode.hpp"
#include "file-access.hpp"
#include "file-share.hpp"
#include <cstdio>  // For std::FILE*
#include <vector>
#include <string>
#include "file-mode.hpp"
#include "../../runtime/native_runtime.hpp"




class FileStream : public Stream {
private:
    std::FILE* file;
    std::vector<uint8_t> memoryBuffer;
    size_t position;
    size_t length;
    bool ownsMemoryBuffer;
    bool writable;

    void UpdateLength();  // Helper to update file size

public:
    FileStream(const uint8_t* data, size_t length);
    FileStream(const char* path, FileMode mode);
    FileStream(const char* path, FileMode mode, FileAccess access, FileShare share);
    FileStream(const std::string& path, FileMode mode);
    FileStream(const std::string& path, FileMode mode, FileAccess access, FileShare share);
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
