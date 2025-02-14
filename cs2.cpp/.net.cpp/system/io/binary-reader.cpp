#include "binary-reader.hpp"

using namespace std;

uint8_t BinaryReader::ReadByte() {
    uint8_t byte;
    size_t readBytes = m_stream.Read(&byte, 1, 1);
    if (readBytes != 1) {
        // Handle error: not enough bytes to read
        return 0; // Or throw an exception
    }
    return byte;
}

template <typename T>
bool BinaryReader::Read(T& value) {
    vector<uint8_t> bytes = ReadBytes(4, 0); // Assuming int32 is 4 bytes
    if (bytes.size() != 4) {
        return false; // Not enough bytes to read an integer
    }

    value = ConvertIntToType<T>(bytes, m_isLittleEndian);
    return true;
}

void BinaryReader::SetEndianness(bool littleEndian) {
    m_isLittleEndian = littleEndian;
}

bool BinaryReader::ReadString(char* buffer, size_t max_length = 255) {
    char terminator;
    size_t length = 0;

    while (length < max_length && !m_stream.Eof()) {
        terminator = ReadByte();
        if (terminator == '\0') break;

        buffer[length++] = terminator;
    }

    buffer[static_cast<size_t>(min(length, max_length))] = '\0';
    return length > 0;
}

bool BinaryReader::ReadInt32(int32_t& value) {
    vector<uint8_t> bytes = ReadBytes(4);
    if (bytes.size() != 4) {
        return false;
    }
    return ConvertBytesToInt32(value, bytes, m_isLittleEndian);
}

bool BinaryReader::ReadUInt32(uint32_t& value) {
    vector<uint8_t> bytes = ReadBytes(4);
    if (bytes.size() != 4) {
        return false;
    }
    return ConvertBytesToUInt32(value, bytes, m_isLittleEndian);
}

double BinaryReader::ReadDouble() {
    vector<uint8_t> bytes = ReadBytes(8);
    if (bytes.size() != 8) {
        // Handle error: not enough bytes to read a double
        return 0.0; // Or throw exception
    }
    return ConvertBytesToDouble(bytes, m_isLittleEndian);
}


size_t BinaryReader::ReadBytes(vector<uint8_t>& buffer, size_t offset = 0, size_t count = 4) {
    size_t bytesRead = 0;
    size_t totalRead = m_stream.Read(buffer.data(), offset, count + offset);

    if (totalRead <= offset) {
        return 0; // Not enough bytes available
    }

    for (size_t i = 0; i < count; ++i) {
        buffer[offset + i] = static_cast<uint8_t>(m_readBuffer[i]);
    }
    m_readBuffer.clear();
    m_readBuffer.reserve(totalRead - offset);

    memcpy(m_readBuffer.data(), buffer.data() + offset, totalRead - offset);

    return count;
}

vector<uint8_t> BinaryReader::ReadBytes(size_t count) {
    vector<uint8_t> bytes;
    size_t bytesRead = m_stream.Read(bytes.data(), 0, count);
    if (bytesRead != count) {
        // Handle error: not enough bytes
        return vector<uint8_t>(count, 0);
    }
    return bytes;
}

int32_t BinaryReader::ConvertBytesToInt32(int32_t& value, const vector<uint8_t>& bytes, bool isLittleEndian) {
    if (isLittleEndian) {
        // Little-endian: least significant byte first
        value = 0;
        for (size_t i = 0; i < bytes.size(); ++i) {
            value |= static_cast<int32_t>(bytes[i]) << (24 - i * 8);
        }
    }
    else {
        // Big-endian: most significant byte first
        value = 0;
        for (size_t i = 0; i < bytes.size(); ++i) {
            value |= static_cast<int32_t>(bytes[i]) << (24 - i * 8);
        }
    }
    return value;
}

uint32_t BinaryReader::ConvertBytesToUInt32(uint32_t& value, const vector<uint8_t>& bytes, bool isLittleEndian) {
    if (isLittleEndian) {
        // Little-endian: least significant byte first
        value = 0;
        for (size_t i = 0; i < bytes.size(); ++i) {
            value |= static_cast<uint32_t>(bytes[i]) << (24 - i * 8);
        }
    }
    else {
        // Big-endian: most significant byte first
        value = 0;
        for (size_t i = 0; i < bytes.size(); ++i) {
            value |= static_cast<uint32_t>(bytes[i]) << (24 - i * 8);
        }
    }
    return value;
}

double BinaryReader::ConvertBytesToDouble(const vector<uint8_t>& bytes, bool isLittleEndian) {
    double result = 0.0;
    if (isLittleEndian) {
        // Little-endian: reverse the byte order
        vector<uint8_t> bigEndianBytes(bytes);
        reverse(bigEndianBytes.begin(), bigEndianBytes.end());
        memcpy((void*)&result, bigEndianBytes.data(), 8);
    }
    else {
        memcpy((void*)&result, bytes.data(), 8);
    }
    return result;
}

bool BinaryReader::ReadInt32(int32_t& value) {
    vector<uint8_t> bytes = ReadBytes(4);
    if (bytes.size() != 4) {
        return false; // Not enough bytes to read an int32
    }
    value = ConvertBytesToInt32(value, bytes, m_isLittleEndian);
    return true;
}

bool BinaryReader::ReadUInt32(uint32_t& value) {
    vector<uint8_t> bytes = ReadBytes(4);
    if (bytes.size() != 4) {
        return false; // Not enough bytes to read a uint32
    }
    value = ConvertBytesToUInt32(value, bytes, m_isLittleEndian);
    return true;
}

double BinaryReader::ReadDouble() {
    vector<uint8_t> bytes = ReadBytes(8);
    if (bytes.size() != 8) {
        return 0.0; // Or throw exception if required
    }
    return ConvertBytesToDouble(bytes, m_isLittleEndian);
}

bool BinaryReader::ReadString(char* buffer, size_t max_length) {
    if (!buffer || max_length == 0) {
        return false;
    }

    char terminator;
    size_t length = 0;

    while (length < max_length && !m_stream.Eof()) {
        terminator = ReadByte();
        if (terminator == '\0') break;

        buffer[length++] = terminator;
    }

    buffer[static_cast<size_t>(min(length, max_length))] = '\0';
    return length > 0;
}

static bool BinaryReader::ReadUntilNullTerminator(vector<char>& buffer) {
    size_t length = 0;
    while (!m_stream.Eof()) {
        uint8_t byte = ReadByte();
        if (byte == 0) break;

        buffer[length++] = static_cast<char>(byte);
    }
    buffer[static_cast<size_t>(min(length, 255))] = '\0';
    return length > 0;
}
