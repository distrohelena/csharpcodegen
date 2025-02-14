#ifndef FILE_MODE_HPP
#define FILE_MODE_HPP

enum FileMode : uint8_t {
    Append,
    Create,
    CreateNew,
    Open,
    OpenOrCreate,
    Truncate
};

#endif // FILE_MODE_HPP
