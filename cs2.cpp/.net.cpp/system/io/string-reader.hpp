#ifndef STRING_READER_HPP
#define STRING_READER_HPP
#include "../../runtime/native_runtime.hpp"

#include <cstddef>


class StringReaderLine {
private:
    bool hasValue;
    HeCppString value;

public:
    StringReaderLine()
        : hasValue(false), value() {
    }

    StringReaderLine(const HeCppString& lineValue, bool hasLine)
        : hasValue(hasLine), value(lineValue) {
    }

    bool operator==(std::nullptr_t) const {
        return !hasValue;
    }

    bool operator!=(std::nullptr_t) const {
        return hasValue;
    }

    operator const HeCppString&() const {
        return value;
    }
};

class StringReader {
private:
    HeCppString source;
    size_t position;

public:
    explicit StringReader(const HeCppString& text)
        : source(text), position(0) {
    }

    StringReaderLine ReadLine() {
        if (position >= source.size()) {
            return StringReaderLine(HeCppString(), false);
        }

        size_t lineEnd = source.find_first_of("\r\n", position);
        if (lineEnd == HeCppString::npos) {
            HeCppString line = source.substr(position);
            position = source.size();
            return StringReaderLine(line, true);
        }

        HeCppString line = source.substr(position, lineEnd - position);
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
