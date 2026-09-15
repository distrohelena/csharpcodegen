#ifndef BINARY_WRITER_HPP
#define BINARY_WRITER_HPP
#include "../../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_VECTOR
#error "system/io/binary-writer.hpp requires HE_CPP_USE_STD_VECTOR=1; binary writer buffers still use std::vector."
#endif

#if !HE_CPP_USE_STD_STRING
#error "system/io/binary-writer.hpp requires HE_CPP_USE_STD_STRING=1; binary writer text still uses std::string."
#endif


#include "stream.hpp"
#include <vector>
#include <string>
#include <cstring>  // For memcpy
#include <type_traits>
#include "../../runtime/native_runtime.hpp"



class BinaryWriter {
private:
    Stream& stream;
    bool littleEndian;

public:
    explicit BinaryWriter(Stream& s, bool isLittleEndian = true);

    void SetEndianness(bool isLittleEndian);

    template<typename T>
    void Write(T value);

    void WriteBytes(const std::vector<uint8_t>& data);
    void WriteByte(uint8_t value);
    void WriteInt32(int32_t value);
    void WriteUInt32(uint32_t value);
    void WriteInt16(int16_t value);
    void WriteUInt16(uint16_t value);
    void WriteInt64(int64_t value);
    void WriteUInt64(uint64_t value);
    void WriteFloat(float value);
    void WriteDouble(double value);
    void WriteString(const std::string& str);

    void Flush();
    void Close();
};

#endif // BINARY_WRITER_HPP
