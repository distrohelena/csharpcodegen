int runtime_streams_smoke();

namespace he_cpp_custom {
[[noreturn]] void Fail(const char*) {
    __builtin_trap();
}
}

int main() {
    return runtime_streams_smoke();
}
