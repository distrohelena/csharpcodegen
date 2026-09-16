#include "runtime/native_endian.hpp"
#include "system/io/binary-reader.hpp"
#include "system/io/binary-writer.hpp"
#include "system/io/memory-stream.hpp"
#include "system/io/file-stream.hpp"
#include "system/io/stream-reader.hpp"
#include "system/io/string-reader.hpp"

#if defined(HE_CPP_TEST_HOST)
#include <cstdlib>
#endif

/// <summary>
/// Exercises the provider-backed stream, binary, and text-reader surfaces without hosted storage or unwinding.
/// </summary>
int runtime_streams_smoke() {
    MemoryStream binaryStream;
    BinaryWriter writer(binaryStream, true);
    writer.WriteInt32(42);
    writer.WriteString(HeCppString("hello"));

    binaryStream.SetPosition(0);
    BinaryReader reader(binaryStream, true);
    if (reader.ReadInt32() != 42) {
        return 1;
    }

    char* text = reader.ReadString();
    if (text == nullptr || HeCppString(text) != HeCppString("hello")) {
#if defined(HE_CPP_TEST_HOST)
        std::free(text);
#endif
        return 2;
    }
#if defined(HE_CPP_TEST_HOST)
    std::free(text);
#endif

    StringReader stringReader(HeCppString("first\r\nsecond"));
    StringReaderLine firstLine = stringReader.ReadLine();
    StringReaderLine secondLine = stringReader.ReadLine();
    if (firstLine == nullptr || secondLine == nullptr ||
        static_cast<const HeCppString&>(firstLine) != HeCppString("first") ||
        static_cast<const HeCppString&>(secondLine) != HeCppString("second")) {
        return 3;
    }
    if (stringReader.ReadLine() != nullptr) {
        return 4;
    }

    MemoryStream textStream;
    const uint8_t textBytes[] = { 't', 'e', 'x', 't' };
    textStream.Write(textBytes, 0, sizeof(textBytes));
    textStream.SetPosition(0);
    StreamReader streamReader(&textStream, Encoding::UTF8, false, 1024, true);
    if (streamReader.ReadToEnd() != HeCppString("text")) {
        return 5;
    }

    // Byte order. The truth table is asserted for both hosts, not just the one
    // running the test, because the swap rule is what a big-endian target gets
    // wrong; the round trips then check the bytes that actually reach the wire.
    static_assert(!he_cpp_endian::NeedsSwapBetween(true, true),
                  "A little-endian value on a little-endian host needs no swap.");
    static_assert(he_cpp_endian::NeedsSwapBetween(true, false),
                  "A little-endian value on a big-endian host needs a swap.");
    static_assert(he_cpp_endian::NeedsSwapBetween(false, true),
                  "A big-endian value on a little-endian host needs a swap.");
    static_assert(!he_cpp_endian::NeedsSwapBetween(false, false),
                  "A big-endian value on a big-endian host needs no swap.");

    const int32_t orderSample = 0x01020304;
    const uint8_t bigEndianWire[] = { 0x01, 0x02, 0x03, 0x04 };
    const uint8_t littleEndianWire[] = { 0x04, 0x03, 0x02, 0x01 };
    for (int pass = 0; pass < 2; ++pass) {
        const bool wireIsLittleEndian = pass == 1;
        MemoryStream orderStream;
        BinaryWriter orderWriter(orderStream, wireIsLittleEndian);
        orderWriter.WriteInt32(orderSample);

        orderStream.SetPosition(0);
        uint8_t wire[sizeof(orderSample)] = {};
        if (orderStream.Read(wire, 0, sizeof(wire)) != sizeof(wire)) {
            return 7;
        }
        const uint8_t* expected = wireIsLittleEndian ? littleEndianWire : bigEndianWire;
        for (size_t index = 0; index < sizeof(wire); ++index) {
            if (wire[index] != expected[index]) {
                return 8;
            }
        }

        orderStream.SetPosition(0);
        BinaryReader orderReader(orderStream, wireIsLittleEndian);
        if (orderReader.ReadInt32() != orderSample) {
            return 9;
        }
    }

    const uint8_t packagedBytes[] = { 'p', 's', '1' };
    FileStream packagedStream(packagedBytes, sizeof(packagedBytes));
    uint8_t packagedReadback[sizeof(packagedBytes)] = {};
    if (packagedStream.Read(packagedReadback, 0, sizeof(packagedReadback)) != sizeof(packagedBytes) ||
        packagedReadback[0] != 'p' || packagedReadback[1] != 's' || packagedReadback[2] != '1' ||
        packagedStream.CanWrite()) {
        return 10;
    }

    return 0;
}
