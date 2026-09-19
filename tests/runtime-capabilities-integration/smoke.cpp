#include "runtime/native_string.hpp"
#include "runtime/native_list.hpp"
#include "runtime/native_dictionary.hpp"
#include "runtime/native_hash_set.hpp"
#include "runtime/native_stack.hpp"
#include "runtime/native_cast.hpp"
#include "runtime/native_hash.hpp"
#include "runtime/native_type.hpp"
#include "system/not_implemented_exception.hpp"
#include "system/text/string-builder.hpp"
#include "system/text/encoding.hpp"

struct Tracked {
    inline static int Alive = 0;
    Tracked() { ++Alive; }
    ~Tracked() { --Alive; }
};

using SmokeHashSetBase = HeCppUnorderedSet<HeCppString, NativeHashSetHash<HeCppString>, NativeHashSetEqual<HeCppString>>;
static_assert(std::is_base_of_v<SmokeHashSetBase, HashSet<HeCppString>>, "HashSet must use the selected unordered-set provider.");

#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
static_assert(sizeof(Exception) <= 2 * sizeof(void*), "Compact failures must retain pointer-sized message storage.");
#endif
#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING && !HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
static_assert(std::is_base_of_v<std::runtime_error, Exception>, "Hosted exception catch compatibility must be preserved.");
#endif

// Exposed independently of main so a cross compiler can compile the same checks.
int runtime_capability_smoke() {
    Exception baseFailure("base failure");
    if (baseFailure.what() == nullptr) return 15;
    HeCppString text("cube");
    for (int index = 0; index < 40; ++index) {
        text += "_scene";
    }
    HeCppString copy = text;
    text[0] = 'C';
    if (copy[0] != 'c' || text.size() != 244) return 1;
    if (!String::Equals(String::Trim(HeCppString("  CUBE\t")), HeCppString("CUBE"))) return 2;
    if (String::ToLowerInvariant(HeCppString("CUBE")) != "cube") return 3;
    if (String::Concat(HeCppString("cube"), 42) != "cube42") return 4;
    {
        // Split(char, None) must keep empty leading, consecutive, and trailing segments.
        Array<HeCppString>* keptSegments = String::Split(HeCppString("\na\n\nb\n"), '\n', StringSplitOptions::None);
        bool keptSegmentsValid = keptSegments->Length == 5 &&
            (*keptSegments)[0].empty() && (*keptSegments)[1] == "a" &&
            (*keptSegments)[2].empty() && (*keptSegments)[3] == "b" && (*keptSegments)[4].empty();
        delete keptSegments;
        if (!keptSegmentsValid) return 24;
        // Empty input with None yields a single empty segment, matching System.String.
        Array<HeCppString>* emptySegments = String::Split(HeCppString(""), '\n', StringSplitOptions::None);
        bool emptySegmentsValid = emptySegments->Length == 1 && (*emptySegments)[0].empty();
        delete emptySegments;
        if (!emptySegmentsValid) return 25;
        // RemoveEmptyEntries drops every empty segment.
        Array<HeCppString>* denseSegments = String::Split(HeCppString("\na\n\nb\n"), '\n', StringSplitOptions::RemoveEmptyEntries);
        bool denseSegmentsValid = denseSegments->Length == 2 &&
            (*denseSegments)[0] == "a" && (*denseSegments)[1] == "b";
        delete denseSegments;
        if (!denseSegmentsValid) return 26;
        // A string without the separator is returned whole.
        Array<HeCppString>* wholeSegments = String::Split(HeCppString("cube"), '\n', StringSplitOptions::None);
        bool wholeSegmentsValid = wholeSegments->Length == 1 && (*wholeSegments)[0] == "cube";
        delete wholeSegments;
        if (!wholeSegmentsValid) return 27;
    }

    List<HeCppString> names;
    for (int index = 0; index < 128; ++index) names.Add(copy);
    if (names.get_Count() != 128 || names.get_Item(127) != copy) return 5;

    Dictionary<HeCppString, int> values;
    values.Add(HeCppString("cube"), 7);
    values.set_Item(HeCppString("cube"), 9);
    int found = 0;
    if (!values.TryGetValue(HeCppString("cube"), found) || found != 9) return 6;
    if (values.Keys().size() != 1 || !values.Remove(HeCppString("cube"))) return 7;
    HashSet<HeCppString> namesSet;
    if (!namesSet.Add(HeCppString("cube")) || namesSet.Add(HeCppString("cube"))) return 21;
    if (!namesSet.Contains(HeCppString("cube")) || namesSet.Count() != 1) return 22;
    namesSet.Add(HeCppString("scene"));
    int setLength = 0;
    for (const HeCppString& name : namesSet) {
        setLength += static_cast<int>(name.size());
    }
    if (setLength != 9 || !namesSet.Remove(HeCppString("cube")) || namesSet.Contains(HeCppString("cube"))) return 23;
    Stack<int> stack;
    stack.Push(42);
    if (stack.Pop() != 42) return 8;
    {
        List<Tracked*> owned;
        owned.AddOwned(new Tracked());
        owned.AddOwned(new Tracked());
        if (Tracked::Alive != 2) return 9;
    }
    if (Tracked::Alive != 0) return 10;
    {
        Dictionary<int, Tracked*> owned;
        owned.AddOwned(1, new Tracked());
        owned.AddOwned(1, new Tracked());
        if (Tracked::Alive != 1) return 11;
    }
    if (Tracked::Alive != 0) return 12;
    if (he_cpp_get_hash_code(HeCppString("cube")) !=
        static_cast<int32_t>(HeCppHash<HeCppString>{}(HeCppString("cube")))) return 13;
    if (he_cpp_get_hash_code(0.0f) != he_cpp_get_hash_code(-0.0f)) return 14;
    StringBuilder builder(HeCppString("cube"));
    builder.Append(HeCppString("_test")).Append(42);
    if (builder.ToString() != "cube_test42") return 16;
#if HE_CPP_USE_STD_STRING
    StringBuilder viewBuilder(std::string_view("cube"));
    viewBuilder.Append(std::string_view("_test")).AppendLine(std::string_view("!"));
    if (viewBuilder.ToString() != "cube_test!\n") return 20;
#endif
    Array<uint8_t>* bytes = Encoding::GetBytes(Encoding::UTF8, HeCppString("cube"));
    HeCppString decoded = Encoding::GetString(Encoding::UTF8, bytes);
    delete bytes;
    if (decoded != "cube") return 17;
    if (he_cpp_type_of<Tracked>("Tracked")->get_Name() != "Tracked") return 18;
    NotImplementedException unsupported("not implemented");
    if (unsupported.what() == nullptr) return 19;
    return 0;
}

#ifdef HE_CPP_TEST_HOST
#include <cstdlib>
#include <cstdio>
namespace he_cpp_custom {
[[noreturn]] void Fail(const char* message) {
    std::fputs(message, stderr);
    std::exit(73);
}
std::uint64_t MonotonicMicroseconds() { return 0; }
}

int main(int argc, char**) {
    if (argc > 1) {
#if HE_CPP_USE_EXCEPTIONS
        try {
            he_cpp_raise(ArgumentException("expected runtime failure"));
        } catch (const ArgumentException& failure) {
            return HeCppString(failure.what()) == "expected runtime failure" ? 73 : 13;
        }
#else
        if (argc > 2) {
            ArgumentException failure("expected runtime failure");
            he_cpp_raise(&failure);
        }
        he_cpp_raise(ArgumentException("expected runtime failure"));
#endif
    }
    return runtime_capability_smoke();
}
#endif

