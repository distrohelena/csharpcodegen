#pragma once
#include "../../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_STRING
#error "system/diagnostics/debug.hpp requires HE_CPP_USE_STD_STRING=1; its diagnostic text surface still uses std::string."
#endif

#if !HE_CPP_USE_EXCEPTIONS
#error "system/diagnostics/debug.hpp requires HE_CPP_USE_EXCEPTIONS=1; Debug::Fail still throws a hosted exception."
#endif


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
