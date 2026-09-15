#ifndef APP_CONTEXT_HPP
#define APP_CONTEXT_HPP
#include "../runtime/native_runtime.hpp"

#if !HE_CPP_USE_STD_STRING
#error "system/app_context.hpp requires HE_CPP_USE_STD_STRING=1; executable path services have not been adapted to custom string storage."
#endif


#include "helcpp_config.hpp"

#include <string>
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
    inline static std::string BaseDirectory = []() {
#if !HE_CPP_PLATFORM_IS_WINDOWS_HOST
        return std::string(".");
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

        std::string executablePath(buffer, length);
        std::size_t separatorIndex = executablePath.find_last_of("\\/");
        if (separatorIndex == std::string::npos) {
            return std::string(".");
        }

        return executablePath.substr(0, separatorIndex);
#else
        return std::string(".");
#endif
    }();
};

#endif // APP_CONTEXT_HPP
