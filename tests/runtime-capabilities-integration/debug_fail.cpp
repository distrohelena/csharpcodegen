#include "system/diagnostics/debug.hpp"

#ifdef HE_CPP_TEST_HOST
#include <cstdio>
#include <cstdlib>

namespace he_cpp_custom {
[[noreturn]] void Fail(const char* message) {
    std::fputs(message == nullptr ? "<null>" : message, stderr);
    std::fputc('\n', stderr);
    std::exit(73);
}
}
#endif

#ifdef HE_CPP_TEST_HOST
int main() {
    System::Diagnostics::Debug::Fail(HeCppString("debug failure"));
}
#else
void CompileDebugFail() {
    System::Diagnostics::Debug::Fail(HeCppString("debug failure"));
}
#endif
