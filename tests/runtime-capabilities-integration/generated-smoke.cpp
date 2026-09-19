#include "StringGate.hpp"

int generated_runtime_smoke() {
    StringGate gate(HeCppString("cube"));
    if (gate.Join(HeCppString("_test")) != "cube_test") return 1;
    if (gate.Format(42) != "cube:42") return 2;
    if (gate.Classify(HeCppString("x")) != 1) return 3;
    if (gate.Classify(HeCppString("other")) != 0) return 4;
    gate.Require(&gate);
    return 0;
}

#ifdef HE_CPP_TEST_HOST
#include <cstdint>
#include <cstdlib>
namespace he_cpp_custom {
[[noreturn]] void Fail(const char*) { std::exit(73); }
std::uint64_t MonotonicMicroseconds() { return 0; }
}
int main(int argc, char** argv) {
    if (argc > 1) {
        StringGate gate(HeCppString("cube"));
        if (argv[1][0] == 'n') gate.Require(nullptr);
        else gate.Fail();
        return 6;
    }
    return generated_runtime_smoke();
}
#endif
