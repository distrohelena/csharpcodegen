#include "runtime/native_string.hpp"
#if defined(HE_CPP_NUMERIC_FORMAT_PARITY)
#include <stdio.h>
#endif
#if defined(HE_CPP_TEST_HOST)
#include <stdlib.h>
namespace he_cpp_custom {
/// Supplies the configured provider's non-returning failure hook to the host fixture.
[[noreturn]] void Fail(const char*) { ::exit(73); }
/// Supplies the unused clock hook required by the custom runtime provider contract.
std::uint64_t MonotonicMicroseconds() { return 0; }
}
#endif

/// Checks portable numeric contracts without introducing hosted formatting headers.
int numeric_format_smoke() {
    if (String::FormatNumeric(-12, "D6") != "-000012") return 1;
    if (String::FormatNumeric(INT64_MIN, "D") != "-9223372036854775808") return 2;
    if (String::FormatNumeric(UINT64_MAX, "D") != "18446744073709551615") return 3;
    if (String::FormatNumeric(static_cast<int8_t>(-1), "X4") != "00FF") return 4;
    if (String::FormatNumeric(-1, "x") != "ffffffff") return 5;
    if (String::FormatNumeric(2.25, "F1") != "2.2") return 6;
    if (String::FormatNumeric(2.75, "f1") != "2.8") return 7;
    if (String::FormatNumeric(-0.0, "F2") != "-0.00") return 8;
    if (String::FormatNumeric(9.999, "E2") != "1.00E+01") return 9;
    if (String::FormatNumeric(1.25, "e") != "1.250000e+00") return 10;
    if (String::FormatNumeric(0.00001, "G3") != "1E-05") return 11;
    if (String::FormatNumeric(0.0001, "g3") != "0.0001") return 12;
    if (String::FormatNumeric(999.9, "G3") != "1E+03") return 13;
    if (String::FormatNumeric(1.25, "F") != "1.25") return 14;
    if (String::FormatNumeric(1.25, "G0") != "1.25") return 15;
    if (String::FormatNumeric(0, "D99").size() != 99) return 16;
    if (String::FormatNumeric(1.0, "F99").size() != 101) return 17;
    if (String::FormatNumeric(__builtin_inf(), "E") != "INF") return 18;
    if (String::FormatNumeric(-__builtin_inf(), "f") != "-inf") return 19;
    if (String::FormatNumeric(__builtin_nan(""), "G") != "NAN") return 20;
    if (String::FormatNumeric(0.0, "g99") != "0") return 21;
    if (String::FormatNumeric(1.15f, "F2") != "1.15") return 29;
    if (String::FormatNumeric(1.15, "F1") != "1.1") return 31;
    if (String::FormatNumeric(3.5, "F0") != "4") return 32;
    if (String::FormatNumeric(0.0, "E0") != "0E+00") return 33;
    HeCppString nullValue;
    HeCppString emptyValue("");
    if (!nullValue.is_null() || emptyValue.is_null()) return 22;
    if (String::FormatNumeric(0, "F0").is_null()) return 23;
#if HE_CPP_USE_EXCEPTIONS
    bool rejected = false;
    try { String::FormatNumeric(1, "F100"); } catch (ArgumentException* error) { delete error; rejected = true; }
    if (!rejected) return 24;
    rejected = false;
    try { String::FormatNumeric(1.0, "D6"); } catch (ArgumentException* error) { delete error; rejected = true; }
    if (!rejected) return 25;
    rejected = false;
    try { String::FormatNumeric(1, "Q2"); } catch (ArgumentException* error) { delete error; rejected = true; }
    if (!rejected) return 26;
#endif
    // Portable formatting preserves the NaN sign, even where hosted UCRT omits it.
    if (String::FormatNumeric(-__builtin_nan(""), "f") != "-nan") return 27;
    if (String::FormatNumeric(-__builtin_nan(""), "G") != "-NAN") return 28;
    return 0;
}

#if defined(HE_CPP_NUMERIC_FORMAT_PARITY)
/// Compares against an independent hosted libc oracle over the binary64 range.
int numeric_format_parity() {
    const int precisions[] = {0, 1, 2, 6, 9, 17, 50, 99};
    const char specifiers[] = {'F', 'f', 'E', 'e', 'G', 'g'};
    uint64_t state = 0x123456789abcdef0ULL;
    for (int sample = 0; sample < 512; ++sample) {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        const uint64_t edgeBits[] = {0, 0x8000000000000000ULL, 1, 0x0010000000000000ULL,
            0x7fefffffffffffffULL, 0x7ff0000000000000ULL, 0xfff0000000000000ULL,
            0x7ff8000000000000ULL, 0xfff8000000000000ULL};
        uint64_t bits = sample < 9 ? edgeBits[sample] : state;
        // Signed NaN spelling differs between UCRT and GNU libc; finite signs remain strict.
        if ((bits & 0x7ff0000000000000ULL) == 0x7ff0000000000000ULL && (bits & 0x000fffffffffffffULL) != 0) bits &= 0x7fffffffffffffffULL;
        double value;
        __builtin_memcpy(&value, &bits, sizeof(value));
        for (char specifier : specifiers) {
            for (int precision : precisions) {
                char format[8];
                snprintf(format, sizeof(format), "%c%d", specifier, precision);
                char expected[768];
                char nativeFormat[] = "%.*f";
                nativeFormat[3] = specifier == 'F' ? 'f' : specifier;
                int effectivePrecision = (specifier == 'G' || specifier == 'g') && precision == 0 ? 9 : precision;
                snprintf(expected, sizeof(expected), nativeFormat, effectivePrecision, value);
                HeCppString actual = String::FormatNumeric(value, format);
                if (actual != expected) {
                    printf("format %s bits %llx expected %s actual %s\n", format,
                        static_cast<unsigned long long>(bits), expected, actual.c_str());
                    return 30;
                }
            }
        }
    }
    return 0;
}
#endif

int main() {
    int result = numeric_format_smoke();
    if (result != 0) return result;
#if defined(HE_CPP_NUMERIC_FORMAT_PARITY)
    return numeric_format_parity();
#else
    return 0;
#endif
}
