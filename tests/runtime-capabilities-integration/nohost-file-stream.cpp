#include "system/io/file-stream.hpp"

#include <exception>

int main() {
    const uint8_t packagedBytes[] = { 'p', 's', '1' };
    FileStream packagedStream(packagedBytes, sizeof(packagedBytes));
    uint8_t packagedReadback[sizeof(packagedBytes)] = {};
    if (packagedStream.Read(packagedReadback, 0, sizeof(packagedReadback)) != sizeof(packagedBytes) ||
        packagedReadback[0] != 'p' || packagedReadback[1] != 's' || packagedReadback[2] != '1' ||
        packagedStream.CanWrite()) {
        return 1;
    }

    try {
        FileStream unsupportedPath("content.bin", FileMode::Open);
        return 2;
    }
    catch (const std::exception&) {
        return 0;
    }
}
