#ifndef STRING_READER_HPP
#define STRING_READER_HPP
#include "../../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_STRING
#error "system/io/string-reader.hpp requires HE_CPP_USE_STD_STRING=1; its text storage has not been adapted to custom string storage."
#endif


#include <cstddef>
#include <string>
#include "../../runtime/native_runtime.hpp"


class StringReaderLine {
private:
    bool hasValue;
    std::string value;

public:
    StringReaderLine()
        : hasValue(false), value() {
    }

    StringReaderLine(const std::string& lineValue, bool hasLine)
        : hasValue(hasLine), value(lineValue) {
    }

    bool operator==(std::nullptr_t) const {
        return !hasValue;
    }

    bool operator!=(std::nullptr_t) const {
        return hasValue;
    }

    operator const std::string&() const {
        return value;
    }
};

class StringReader {
private:
    std::string source;
    size_t position;

public:
    explicit StringReader(const std::string& text)
        : source(text), position(0) {
    }

    StringReaderLine ReadLine() {
        if (position >= source.size()) {
            return StringReaderLine(std::string(), false);
        }

        size_t lineEnd = source.find_first_of("\r\n", position);
        if (lineEnd == std::string::npos) {
            std::string line = source.substr(position);
            position = source.size();
            return StringReaderLine(line, true);
        }

        std::string line = source.substr(position, lineEnd - position);
        position = lineEnd + 1;

        if (position < source.size() &&
            source[lineEnd] == '\r' &&
            source[position] == '\n') {
            position++;
        }

        return StringReaderLine(line, true);
    }

    void Dispose() {
    }
};

#endif
