#pragma once
#include "stream.hpp"
#include <cstdint>
#include <vector>

using namespace std;

class BinaryReader {
private:
    Stream& m_stream;
    vector<uint8_t> m_readBuffer; // Buffer for reading data
    bool m_isLittleEndian; // Endianness setting

public:
    explicit BinaryReader(Stream& stream) : m_stream(stream), m_isLittleEndian(false) {}
    ~BinaryReader() = default;

    uint8_t ReadByte();

    template <typename T>
    bool Read(T& value);

    void SetEndianness(bool littleEndian);

    size_t ReadBytes(vector<uint8_t>& buffer, size_t offset = 0, size_t count = 4);
    vector<uint8_t> ReadBytes(size_t count);

    bool ReadString(char* buffer, size_t max_length = 255);
    double ReadDouble();
    bool ReadInt32(int32_t& value);
    bool ReadUInt32(uint32_t& value);

private:
    static int32_t ConvertBytesToInt32(int32_t& value, const vector<uint8_t>& bytes, bool isLittleEndian);

    static uint32_t ConvertBytesToUInt32(uint32_t& value, const vector<uint8_t>& bytes, bool isLittleEndian);

    static double ConvertBytesToDouble(const vector<uint8_t>& bytes, bool isLittleEndian);

    static bool ReadUntilNullTerminator(vector<char>& buffer);
};
