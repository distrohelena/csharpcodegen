#pragma once
#include <cstddef>
#include <cstdint>

/// <summary>Decodes UTF8 and internal WTF8 without depending on hosted Unicode libraries or a particular string provider.</summary>
class HeCppUnicode {
public:
    /// <summary>Reads one maximal valid UTF8 subsequence, preserving surrogate codepoints only for internal WTF8 storage.</summary>
    static uint32_t Decode(const char* bytes, size_t size, size_t& offset, bool allowSurrogates = true) {
        const uint32_t first = static_cast<unsigned char>(bytes[offset++]);
        if (first < 0x80) { return first; }
        int count; uint32_t point; uint32_t secondMinimum = 0x80, secondMaximum = 0xbf;
        if (first >= 0xc2 && first <= 0xdf) { count = 1; point = first & 0x1f; }
        else if (first >= 0xe0 && first <= 0xef) { count = 2; point = first & 0xf; if (first == 0xe0) { secondMinimum = 0xa0; } if (first == 0xed && !allowSurrogates) { secondMaximum = 0x9f; } }
        else if (first >= 0xf0 && first <= 0xf4) { count = 3; point = first & 7; if (first == 0xf0) { secondMinimum = 0x90; } if (first == 0xf4) { secondMaximum = 0x8f; } }
        else { return 0xfffd; }
        for (int index = 0; index < count; ++index) {
            if (offset >= size) { return 0xfffd; }
            const uint32_t next = static_cast<unsigned char>(bytes[offset]);
            if (next < (index == 0 ? secondMinimum : 0x80) || next > (index == 0 ? secondMaximum : 0xbf)) { return 0xfffd; }
            ++offset; point = (point << 6) | (next & 0x3f);
        }
        return point;
    }

    /// <summary>Encodes one Unicode scalar or isolated surrogate into the selected internal WTF8 byte provider.</summary>
    template<typename TBytes> static void Append(TBytes& bytes, uint32_t point) {
        if (point < 0x80) { bytes.push_back(static_cast<char>(point)); }
        else if (point < 0x800) { bytes.push_back(static_cast<char>(0xc0 | (point >> 6))); bytes.push_back(static_cast<char>(0x80 | (point & 0x3f))); }
        else if (point < 0x10000) { bytes.push_back(static_cast<char>(0xe0 | (point >> 12))); bytes.push_back(static_cast<char>(0x80 | ((point >> 6) & 0x3f))); bytes.push_back(static_cast<char>(0x80 | (point & 0x3f))); }
        else { bytes.push_back(static_cast<char>(0xf0 | (point >> 18))); bytes.push_back(static_cast<char>(0x80 | ((point >> 12) & 0x3f))); bytes.push_back(static_cast<char>(0x80 | ((point >> 6) & 0x3f))); bytes.push_back(static_cast<char>(0x80 | (point & 0x3f))); }
    }

    /// <summary>Returns the next UTF16 unit, keeping a supplementary scalar's low surrogate until the next read.</summary>
    static char16_t Next(const char* bytes, size_t size, size_t& offset, char16_t& pending) {
        if (pending != 0) { const char16_t result = pending; pending = 0; return result; }
        uint32_t point = Decode(bytes, size, offset);
        if (point <= 0xffff) { return static_cast<char16_t>(point); }
        point -= 0x10000; pending = static_cast<char16_t>(0xdc00 | (point & 0x3ff)); return static_cast<char16_t>(0xd800 | (point >> 10));
    }

    /// <summary>Builds WTF8 from exact UTF16 units, combining valid pairs while retaining every isolated surrogate without replacement.</summary>
    template<typename TBytes> static TBytes FromUtf16(const char16_t* units, size_t count, bool replaceSurrogates = false) {
        TBytes result;
        for (size_t index = 0; index < count; ++index) {
            uint32_t point = units[index];
            if (point >= 0xd800 && point <= 0xdbff && index + 1 < count && units[index + 1] >= 0xdc00 && units[index + 1] <= 0xdfff) {
                point = 0x10000 + ((point - 0xd800) << 10) + (units[++index] - 0xdc00);
            }
            if (replaceSurrogates && point >= 0xd800 && point <= 0xdfff) { point = 0xfffd; }
            Append(result, point);
        }
        return result;
    }

    /// <summary>Canonicalizes only a newly adjacent high/low surrogate pair so reconstructed valid strings expose ordinary UTF8 to existing native bridges.</summary>
    template<typename TBytes> static void JoinBoundary(TBytes& bytes, size_t boundary) {
        if (boundary < 3 || boundary > bytes.size() || bytes.size() - boundary < 3) { return; }
        size_t leftOffset = boundary - 3, rightOffset = boundary;
        const uint32_t high = Decode(bytes.data(), bytes.size(), leftOffset), low = Decode(bytes.data(), bytes.size(), rightOffset);
        if (leftOffset != boundary || rightOffset != boundary + 3 || high < 0xd800 || high > 0xdbff || low < 0xdc00 || low > 0xdfff) { return; }
        TBytes replacement; Append(replacement, 0x10000 + ((high - 0xd800) << 10) + low - 0xdc00);
        bytes.replace(boundary - 3, 6, replacement);
    }

    /// <summary>Compares ordinal UTF16 units so canonical UTF8 and separately concatenated surrogate halves have identical managed equality/order.</summary>
    template<typename TBytes> static int Compare(const TBytes& left, const TBytes& right) {
        size_t first = 0, second = 0; char16_t firstPending = 0, secondPending = 0;
        while (first < left.size() || firstPending != 0) {
            if (second >= right.size() && secondPending == 0) { return 1; }
            const char16_t a = Next(left.data(), left.size(), first, firstPending), b = Next(right.data(), right.size(), second, secondPending);
            if (a != b) { return a < b ? -1 : 1; }
        }
        return second < right.size() || secondPending != 0 ? -1 : 0;
    }
};
