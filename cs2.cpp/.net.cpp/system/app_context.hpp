#ifndef APP_CONTEXT_HPP
#define APP_CONTEXT_HPP
#include "../runtime/native_runtime.hpp"




#include "helcpp_config.hpp"


#include "../runtime/native_runtime.hpp"
#include "../runtime/native_exceptions.hpp"


#if defined(_WIN32) && !HE_CPP_USE_EXCEPTIONS
#error "system/app_context.hpp requires HE_CPP_USE_EXCEPTIONS=1 on Windows; its executable path failure still throws a hosted exception."
#endif

#ifdef _WIN32
#include <Windows.h>
#endif

/// Resolves executable-relative application context values used by generated runtime initialization.
class AppContext {
public:
    inline static HeCppString BaseDirectory = []() {
#if !HE_CPP_PLATFORM_IS_WINDOWS_HOST
        return HeCppString(".");
#elif defined(_WIN32)
        char buffer[MAX_PATH];
        DWORD length = GetModuleFileNameA(nullptr, buffer, MAX_PATH);
        if (length == 0) {
#if HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
            throw InvalidOperationException();
#else
            throw InvalidOperationException("Failed to resolve the current executable path.");
#endif
        }

        HeCppString executablePath(buffer, length);
        std::size_t separatorIndex = executablePath.find_last_of("\\/");
        if (separatorIndex == HeCppString::npos) {
            return HeCppString(".");
        }

        return executablePath.substr(0, separatorIndex);
#else
        return HeCppString(".");
#endif
    }();
};

#endif // APP_CONTEXT_HPP
