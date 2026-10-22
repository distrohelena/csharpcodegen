#pragma once

// Probe the generated configuration first so this header reports its own diagnostic
// before the provider chain is pulled in.
#if defined(__has_include)
#if __has_include("helcpp_config.hpp")
#include "helcpp_config.hpp"
#endif
#endif

#ifndef HE_CPP_RUNTIME_HAS_HOSTED_SERVICES
#define HE_CPP_RUNTIME_HAS_HOSTED_SERVICES 1
#endif

#include "../runtime/native_runtime.hpp"
#include "../runtime/native_exceptions.hpp"

#if HE_CPP_RUNTIME_HAS_HOSTED_SERVICES && (!defined(__STDC_HOSTED__) || __STDC_HOSTED__)
#include <atomic>
#endif
#include <cstdint>
#if HE_CPP_USE_STD_STRING
#include <string_view>
#endif


/// <summary>
/// Provides a lightweight Guid abstraction for transpiled managed code.
/// </summary>
class Guid {
    /// <summary>Uses the selected managed string storage without requiring hosted string facilities.</summary>
    HeCppString value;

    /// <summary>Formats the existing process-local token counter without a hosted formatter.</summary>
    static HeCppString FormatCounterGuid(std::uint64_t counter) {
        constexpr char HexDigits[] = "0123456789abcdef";
        char buffer[33];
        for (int32_t index = 0; index < 32; index++) {
            buffer[index] = '0';
        }

        buffer[32] = '\0';
        int32_t writeIndex = 31;
        while (counter != 0 && writeIndex >= 0) {
            buffer[writeIndex] = HexDigits[counter & 0xf];
            counter >>= 4;
            writeIndex--;
        }

        return HeCppString(buffer, 32);
    }

public:
    /// <summary>
    /// Initializes an all-zero guid value.
    /// </summary>
    Guid() : value("00000000000000000000000000000000") {}

    /// <summary>
    /// Initializes a guid from an existing text representation.
    /// </summary>
    /// <param name="text">Source text used for the guid value.</param>
#if HE_CPP_USE_STD_STRING
    explicit Guid(std::string_view text) : value(text.data(), text.size()) {}
#endif

    /// <summary>Accepts native hosted storage without ambiguity between managed and view conversions.</summary>
    explicit Guid(const HeCppRawString& text) : value(text) {}

    /// <summary>Copies managed guid text into native storage without erasing its null validation.</summary>
    explicit Guid(const HeCppString& text) : value(text) {
        if (text.is_null()) {
            he_cpp_raise(ArgumentNullException("text"));
        }
    }

    /// <summary>Accepts a literal guid without ambiguity between managed and view constructors.</summary>
    explicit Guid(const char* text) : Guid(HeCppString(text)) {}

    /// <summary>
    /// Generates a unique guid-like token for generated content and asset ids.
    /// </summary>
    /// <returns>A unique Guid value.</returns>
    static Guid NewGuid() {
#if HE_CPP_RUNTIME_HAS_HOSTED_SERVICES && (!defined(__STDC_HOSTED__) || __STDC_HOSTED__)
        static std::atomic<std::uint64_t> nextValue{1};
        std::uint64_t value = nextValue.fetch_add(1, std::memory_order_relaxed);
#else
        // A freestanding runtime or compiler has no hosted threading contract. Keep the
        // same process-local token sequence without hosted atomic facilities.
        static std::uint64_t nextValue = 1;
        std::uint64_t value = nextValue++;
#endif
        return Guid(HeCppString(FormatCounterGuid(value)));
    }

    /// <summary>
    /// Returns the guid text representation.
    /// </summary>
    /// <returns>Guid text.</returns>
    HeCppString ToString() const {
        return value;
    }

    /// <summary>
    /// Returns the guid text representation for the requested format.
    /// </summary>
    /// <param name="format">Requested formatting token.</param>
    /// <returns>Guid text.</returns>
    HeCppString ToString(const HeCppString& format) const {
        (void)format;
        return value;
    }
};
