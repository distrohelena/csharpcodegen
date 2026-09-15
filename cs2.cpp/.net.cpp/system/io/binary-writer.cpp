#include "binary-writer.hpp"
#include "../../runtime/native_memory_ops.hpp"
#include <algorithm>

BinaryWriter::BinaryWriter(Stream& s, bool isLittleEndian)
    : stream(s), littleEndian(isLittleEndian) {
}

void BinaryWriter::SetEndianness(bool isLittleEndian) {
    littleEndian = isLittleEndian;
}

template<typename T>
void BinaryWriter::Write(T value) {
    static_assert(std::is_arithmetic_v<T>, "Only arithmetic types are supported.");
    uint8_t buffer[sizeof(T)];

    he_cpp_memory::Copy(buffer, &value, sizeof(T));
    if (!littleEndian) {
        std::reverse(buffer, buffer + sizeof(T));
    }

    stream.Write(buffer, 0, sizeof(T));
}

void BinaryWriter::WriteBytes(const HeCppVector<uint8_t>& data) {
    stream.Write(data.data(), 0, data.size());
}

void BinaryWriter::WriteByte(uint8_t value) {
    stream.InternalWriteByte(value);
}

void BinaryWriter::WriteInt32(int32_t value) { Write(value); }
void BinaryWriter::WriteUInt32(uint32_t value) { Write(value); }
void BinaryWriter::WriteInt16(int16_t value) { Write(value); }
void BinaryWriter::WriteUInt16(uint16_t value) { Write(value); }
void BinaryWriter::WriteInt64(int64_t value) { Write(value); }
void BinaryWriter::WriteUInt64(uint64_t value) { Write(value); }
void BinaryWriter::WriteFloat(float value) { Write(value); }
void BinaryWriter::WriteDouble(double value) { Write(value); }

void BinaryWriter::WriteString(const HeCppString& str) {
    WriteUInt32(static_cast<uint32_t>(str.size())); // Prefix with length
    stream.Write(reinterpret_cast<const uint8_t*>(str.data()), 0, str.size());
}

void BinaryWriter::Flush() {
    stream.Flush();
}

void BinaryWriter::Close() {
    stream.Close();
}
