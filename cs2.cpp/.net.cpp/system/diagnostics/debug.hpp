#pragma once

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

    static void Assert(bool condition, const std::string& message) {
        (void)condition;
        (void)message;
    }

    static void WriteLine(const std::string& text) {
        (void)text;
    }

    static void Fail(const std::string& message) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
        (void)message;
        throw InvalidOperationException();
#else
        throw InvalidOperationException(message);
#endif
    }
};
}
}
