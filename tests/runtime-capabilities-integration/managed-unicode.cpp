#include "runtime/native_string.hpp"
#include "runtime/native_dictionary.hpp"
#include "system/text/encoding.hpp"
#include "system/text/string-builder.hpp"
#include <cstdio>
#include <cstdlib>
#if HE_CPP_USE_EXCEPTIONS
#include <stdexcept>
#endif
#include <cstring>

#if defined(HE_CPP_TEST_HOST)
namespace he_cpp_custom {
/// <summary>Provides the fixture's non-returning failure hook for exception-free runtime branches.</summary>
[[noreturn]] void Fail(const char* message) { std::fprintf(stderr, "%s\n", message); std::abort(); }
}
#endif

/// <summary>Exercises UTF16 managed operations independently of the preserved UTF8 native byte facade.</summary>
class ManagedUnicodeTests {
    /// <summary>Stops at the first observed contract mismatch with a specific headless diagnostic.</summary>
    static void Require(bool condition, const char* message) {
        if (!condition) {
#if HE_CPP_USE_EXCEPTIONS
            throw std::runtime_error(message);
#else
            std::fprintf(stderr, "%s\n", message); std::abort();
#endif
        }
    }
public:
    /// <summary>Checks accents, BMP keys, surrogate pairs and isolated UTF16 units across real runtime helpers.</summary>
    static void Run() {
        HeCppString value = he_cpp_string_from_utf16(u"A\u00e7\u00e3o \u0100 \U0001F600", 9);
        Require(he_cpp_string_length(value) == 9, "Managed length counts UTF8 bytes instead of UTF16 units.");
        Require(he_cpp_string_char_at(value, 1) == 0x00e7 && he_cpp_string_char_at(value, 2) == 0x00e3, "Accent indexing did not preserve code units.");
        Require(he_cpp_string_char_at(value, 5) == 0x0100, "BMP indexing narrowed a code unit.");
        Require(he_cpp_string_char_at(value, 7) == 0xd83d && he_cpp_string_char_at(value, 8) == 0xde00, "Supplementary indexing must expose UTF16 surrogate units.");
        Require(value.size() == 14 && std::memcmp(value.data(), "A\xc3\xa7\xc3\xa3o \xc4\x80 \xf0\x9f\x98\x80", 14) == 0, "Native byte facade changed UTF8 wire contents.");
        int count = 0; for (char16_t unit : he_cpp_string_chars(value)) { Require(unit == he_cpp_string_char_at(value, count++), "Foreach disagrees with UTF16 indexing."); }
        Require(count == 9, "Foreach counted bytes or codepoints instead of UTF16 units.");
        HeCppString high = String::Substring(value, 7, 1), low = String::Substring(value, 8, 1);
        Require(he_cpp_string_length(high) == 1 && he_cpp_string_char_at(high, 0) == 0xd83d, "Substring lost an isolated high surrogate.");
        Require(he_cpp_string_char_at(low, 0) == 0xde00, "Substring lost an isolated low surrogate.");
        HeCppString joined = String::Concat(high, low);
        Require(joined.size() == 4, "Rejoined surrogate halves must expose canonical UTF8 at existing native bridges.");
        Require(joined == String::Substring(value, 7, 2), "Concatenated surrogate halves did not restore the original value.");
        Array<uint8_t>* encoded = Encoding::GetBytes(Encoding::UTF8, joined);
        Require(encoded->Length == 4 && (*encoded)[0] == 0xf0 && (*encoded)[3] == 0x80, "UTF8 encoding did not recombine surrogate halves."); delete encoded;
        encoded = Encoding::GetBytes(Encoding::UTF8, high);
        Require(encoded->Length == 3 && (*encoded)[0] == 0xef && (*encoded)[1] == 0xbf && (*encoded)[2] == 0xbd, "Default UTF8 encoding must replace an unpaired surrogate."); delete encoded;
        StringBuilder builder; builder.Append(static_cast<char16_t>(0x00e3)); builder.Append(static_cast<char16_t>(0x0100)); builder.Append(static_cast<char16_t>(0xd83d)); builder.Append(static_cast<char16_t>(0xde00));
        Require(builder.get_Length() == 4 && builder.ToString() == he_cpp_string_from_utf16(u"\u00e3\u0100\U0001F600", 4), "StringBuilder did not retain UTF16 characters and surrogate pairs.");
        builder.set_Length(3); Require(he_cpp_string_char_at(builder.ToString(), 2) == 0xd83d, "Builder truncation lost the isolated UTF16 unit.");
        Dictionary<char16_t, int> atlas; atlas.Add(0, 1); atlas.Add(0x0100, 2); Require(atlas.get_Count() == 2, "BMP atlas keys collided after UTF16 typing.");
        Require(HeCppString(nullptr) != HeCppString("") && String::IsNullOrEmpty(nullptr), "Unicode storage changed null versus empty semantics.");
        Require(String::Substring(HeCppString("abcdef"), 2, 2) == "cd", "ASCII substring regressed.");
        Array<char16_t>* chars = String::ToCharArray(value); Require(chars->Length == 9 && (*chars)[5] == 0x0100, "ToCharArray narrowed BMP characters."); delete chars;
        encoded = Encoding::GetBytes(Encoding::UTF8, value); HeCppString decoded = Encoding::GetString(Encoding::UTF8, encoded); Require(decoded == value, "UTF8 roundtrip changed Unicode contents."); delete encoded;
        Require(String::Replace(value, static_cast<char16_t>(0x00e3), static_cast<char16_t>(0x0100)) == he_cpp_string_from_utf16(u"A\u00e7\u0100o \u0100 \U0001F600", 9), "Character replacement matched bytes rather than UTF16 units.");
        Require(HeCppHash<HeCppString>{}(joined) == HeCppHash<HeCppString>{}(String::Substring(value, 7, 2)), "Equivalent UTF16 strings have different hashes.");
        Require(String::CompareOrdinal(he_cpp_string_from_utf16(u"\U00010000", 2), he_cpp_string_from_utf16(u"\ue000", 1)) < 0, "Ordinal comparison sorts UTF8 bytes instead of UTF16 units.");
        const char16_t embedded[] = {u'a', 0, 0x0100};
        HeCppString nul = he_cpp_string_from_utf16(embedded, 3);
        Require(he_cpp_string_length(nul) == 3 && he_cpp_string_char_at(nul, 1) == 0 && nul.size() == 4, "Embedded NUL changed logical length or byte length.");
        Require(String::IndexOf(value, static_cast<char16_t>(0x0100)) == 5 && String::LastIndexOf(value, u' ') == 6, "Search returns byte positions.");
        Require(String::Contains(value, high) && String::IndexOf(value, low) == 8, "Search cannot find UTF16 surrogate halves.");
        Require(String::Insert(String::Remove(value, 5, 1), 5, HeCppString(static_cast<char16_t>(0x0100))) == value, "Remove/Insert disagree about UTF16 ranges.");
        Array<HeCppString>* parts = String::Split(he_cpp_string_from_utf16(u"\u0100\u00e3\u0100", 3), static_cast<char16_t>(0x00e3));
        Require(parts->Length == 2 && (*parts)[0] == HeCppString(static_cast<char16_t>(0x0100)), "Split narrows Unicode separators."); delete parts;
        Require(!String::IsDigit(static_cast<char16_t>(0x0130)) && String::ToUpper(static_cast<char16_t>(0x0100)) == 0x0100, "Existing ASCII-only character helpers must preserve and not misclassify other BMP units.");
        Require(String::LastIndexOf("abc", HeCppString("bc"), 1) == -1, "LastIndexOf matched beyond its inclusive backward-search window.");
        Require(String::LastIndexOf("abc", HeCppString("")) == 3 && String::LastIndexOf("abc", HeCppString(""), 0) == 1, "LastIndexOf empty-search positions disagree with .NET9.");
        Require(String::LastIndexOf("abc", HeCppString("bc"), 3, 2) == -1 && String::LastIndexOf("abc", HeCppString("bc"), 3, 3) == 1, "LastIndexOf count ignores its UTF16 backward window.");
        Require(String::LastIndexOf("abc", HeCppString(""), 3, 0) == 3 && String::LastIndexOf("abc", HeCppString(""), 1, 0) == 2, "Empty search must use the inclusive window's end position.");
        Require(String::LastIndexOf("", HeCppString(""), -1, -1) == 0 && String::LastIndexOf("", HeCppString("a"), 0, 1) == -1, "Empty-source early-return behavior differs from .NET9.");
#if HE_CPP_USE_EXCEPTIONS
        bool invalidRange = false; try { String::LastIndexOf("abc", HeCppString(""), -1); } catch (const std::exception&) { invalidRange = true; }
        Require(invalidRange, "Explicit negative LastIndexOf start was mistaken for the default overload.");
#endif
        Require(he_cpp_string_repeat(static_cast<char16_t>(0x0100), 2) == he_cpp_string_from_utf16(u"\u0100\u0100", 2), "Repeated characters were reversed or narrowed.");
        Require(he_cpp_string_from_utf16(static_cast<Array<char16_t>*>(nullptr)) == "", "The complete null char-array constructor must produce Empty like .NET9.");
        Array<char16_t> sliced({u'a', 0x0100, 0xd83d, 0xde00});
        Require(he_cpp_string_from_utf16(&sliced, 1, 2) == he_cpp_string_from_utf16(u"\u0100\xd83d", 2), "Array slicing must preserve exact UTF16 units including a lone surrogate.");
        Require(he_cpp_string_from_utf16(&sliced, 4, 0) == "", "The empty slice at array.Length is valid.");
        Require(String::LastIndexOf("", u'b', 12, -1) == -1, "Empty char LastIndexOf must return before range validation like .NET9.");
#if HE_CPP_USE_EXCEPTIONS
        bool negativeCount = false; try { he_cpp_string_repeat(u'a', -1); } catch (const ArgumentOutOfRangeException&) { negativeCount = true; } Require(negativeCount, "Negative repeat count was converted to a huge size_t allocation.");
        bool absentSlice = false; try { he_cpp_string_from_utf16(static_cast<Array<char16_t>*>(nullptr), 0, 0); } catch (const ArgumentNullException&) { absentSlice = true; } Require(absentSlice, "Sliced null array must throw even with zero bounds.");
        bool charEnd = false; try { String::LastIndexOf("abc", u'a', 3); } catch (const std::exception&) { charEnd = true; } Require(charEnd, "Char LastIndexOf incorrectly accepted String's virtual end position.");
#endif
        const uint8_t invalid[][4] = {{0xed,0xa0,0x80,0},{0xe2,0x82,0,0},{0xf0,0x80,0x80,0x80},{0xc3,0x28,0,0},{0xf4,0x90,0x80,0x80}};
        const int lengths[] = {3,2,4,2,4}, decodedLengths[] = {3,1,4,2,4};
        for(int sample=0;sample<5;++sample) {
            Array<uint8_t> bytes(lengths[sample]); for(int index=0;index<bytes.Length;++index) { bytes[index]=invalid[sample][index]; }
            HeCppString repaired = Encoding::GetString(Encoding::UTF8, &bytes);
            Require(he_cpp_string_length(repaired)==decodedLengths[sample], "Invalid UTF8 replacement differs from the observed .NET9 default oracle.");
            for(int index=0;index<decodedLengths[sample];++index) { Require(he_cpp_string_char_at(repaired,index)==(sample==3 && index==1 ? u'(' : static_cast<char16_t>(0xfffd)), "Invalid UTF8 replacement produced the wrong UTF16 unit."); }
        }
        std::puts("Managed Unicode passed: accents, BMP, UTF16 indexing/foreach/substrings, isolated surrogates, builder, atlas, UTF8 bytes and null/ASCII.");
    }
};

/// <summary>Runs the real runtime contract without initializing an engine or native UI.</summary>
int main() {
#if HE_CPP_USE_EXCEPTIONS
    try { ManagedUnicodeTests::Run(); return 0; } catch (const std::exception& error) { std::fprintf(stderr, "%s\n", error.what()); return 1; }
#else
    ManagedUnicodeTests::Run(); return 0;
#endif
}