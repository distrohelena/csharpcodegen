#ifndef FILE_MODE_HPP
#define FILE_MODE_HPP

#include <cstdint>

enum class FileMode : uint8_t {
    Append,
    Create,
    CreateNew,
    Open,
    OpenOrCreate,
    Truncate
};

#endif // FILE_MODE_HPP
