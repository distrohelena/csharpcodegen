#include "system/guid.hpp"

// Keep the host test entrypoint linkable when checking -ffreestanding code generation.
extern "C" int main(int argc, char** argv);

/// <summary>Checks generated asset IDs, copies and null handling with hosted or custom string storage.</summary>
int main(int argc, char**) {
    if (argc > 1) {
#if HE_CPP_USE_EXCEPTIONS
        try { Guid invalid(static_cast<const char*>(nullptr)); }
        catch (const ArgumentNullException&) { return 73; }
        return 9;
#else
        Guid invalid(static_cast<const char*>(nullptr));
        return 9;
#endif
    }
    Guid empty;
    if (empty.ToString() != "00000000000000000000000000000000") return 1;
    Guid first = Guid::NewGuid();
    Guid copy = first;
    if (first.ToString("N") != "00000000000000000000000000000001") return 2;
    if (copy.ToString() != first.ToString()) return 3;
    HeCppString previous = first.ToString();
    for (int index = 0; index < 4096; ++index) {
        HeCppString current = Guid::NewGuid().ToString("N");
        if (current.size() != 32 || current == previous) return 4;
        previous = current;
    }
    Guid managed(HeCppString("copied-id"));
    Guid raw(HeCppRawString("raw-id"));
    if (managed.ToString() != "copied-id" || raw.ToString() != "raw-id") return 5;
    return 0;
}

#if defined(HE_CPP_TEST_HOST)
#include <cstdio>
#include <cstdlib>
namespace he_cpp_custom {
/// <summary>Turns the expected fatal null-argument path into a distinct test process exit.</summary>
[[noreturn]] void Fail(const char* message) { std::fputs(message, stderr); std::exit(73); }
}
#endif
