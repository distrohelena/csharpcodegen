#pragma once

#include "../runtime/native_exceptions.hpp"

#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
#include <stdexcept>
#endif

#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
class NotImplementedException : public std::logic_error {
#else
class NotImplementedException : public Exception {
#endif
public:
    NotImplementedException()
#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
        : std::logic_error("Not implemented.") {
#else
        : Exception("Not implemented.") {
#endif
    }

    explicit NotImplementedException(const HeCppString& message)
#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
        : std::logic_error(message) {
#else
        : Exception(message) {
#endif
    }

    explicit NotImplementedException(const char* message)
#if HE_CPP_USE_EXCEPTIONS && HE_CPP_USE_STD_STRING
        : std::logic_error(message == nullptr ? "Not implemented." : message) {
#else
        : Exception(message == nullptr ? "Not implemented." : message) {
#endif
    }
};
