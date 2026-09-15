#ifndef BINARY_READER_HPP
#define BINARY_READER_HPP
#include "../../runtime/native_runtime.hpp"

#include "stream.hpp"
#include <type_traits>



class BinaryReader {
private:
    Stream& stream;
    bool littleEndian;

public:
    explicit BinaryReader(Stream& s, bool isLittleEndian = true);

    void SetEndianness(bool isLittleEndian);

    template<typename T>
    T Read();

    HeCppVector<uint8_t> ReadBytes(size_t count);
    uint8_t ReadByte();
    int32_t ReadInt32();
    uint32_t ReadUInt32();
    int16_t ReadInt16();
    uint16_t ReadUInt16();
    int64_t ReadInt64();
    uint64_t ReadUInt64();
    float ReadSingle();
    double ReadDouble();
    char* ReadString();

    void Close();
};

#endif // BINARY_READER_HPP
