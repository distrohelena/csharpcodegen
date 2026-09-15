#pragma once
#include "../../runtime/native_runtime.hpp"

#include "helcpp_config.hpp"

#include "../../runtime/native_exceptions.hpp"
#include "runtime/native_string.hpp"



namespace System {
namespace Diagnostics {
class Debug {
public:
    static void Assert(bool condition) {
        (void)condition;
    }

    static void Assert(bool condition, const HeCppString& message) {
        (void)condition;
        (void)message;
    }

    static void WriteLine(const HeCppString& text) {
        (void)text;
    }

    static void Fail(const HeCppString& message) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        (void)message;
        he_cpp_raise(InvalidOperationException());
#else
        he_cpp_raise(InvalidOperationException(message));
#endif
    }
};
}
}

