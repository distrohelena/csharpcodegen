#pragma once
// Select the same fixed clock contract as the generated Xbox configuration.
#define HE_CPP_USE_STD_CHRONO 1
#define HE_CPP_PLATFORM_XBOX 1
/// Models the runtime's throwing host mode and non-returning freestanding failure hook.
template<class TException> [[noreturn]] inline void he_cpp_raise(const TException& exception) {
#if defined(__cpp_exceptions)
    throw exception;
#else
    (void)exception;
    __builtin_trap();
#endif
}
