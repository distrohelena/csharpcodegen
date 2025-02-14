#ifndef STREAM_HPP
#define STREAM_HPP

#include <cstdint>  // For uint8_t
#include "seek-origin.hpp"  // Assuming this exists like in your TypeScript

class Stream {
public:
    virtual ~Stream() = default;

    // Abstract methods
    virtual size_t Read(uint8_t* buffer, size_t offset, size_t count) = 0;
    virtual void Write(const uint8_t* buffer, size_t offset, size_t count) = 0;
    virtual size_t Seek(int64_t offset, SeekOrigin origin) = 0;
    virtual void SetLength(size_t length) = 0;

    // Properties
    virtual bool CanRead() const = 0;
    virtual bool CanWrite() const = 0;
    virtual bool CanSeek() const = 0;
    virtual size_t Length() const = 0;
    virtual size_t Position() const = 0;
    virtual void SetPosition(size_t value) = 0;

    // Default Implementation for Timeout (Exception-like behavior)
    virtual bool CanTimeout() const { return false; }

    virtual size_t ReadTimeout() const {
        throw std::runtime_error("Timeout not supported");
    }

    virtual void SetReadTimeout(size_t) {
        throw std::runtime_error("Timeout not supported");
    }

    virtual size_t WriteTimeout() const {
        throw std::runtime_error("Timeout not supported");
    }

    virtual void SetWriteTimeout(size_t) {
        throw std::runtime_error("Timeout not supported");
    }

    virtual void InternalReserve(size_t count) = 0;
    virtual void InternalWriteByte(uint8_t byte) = 0;
    virtual int InternalReadByte() = 0;

    virtual void Dispose() {}
    virtual void Close() {}
    virtual void Flush() {}
};

#endif // STREAM_HPP
