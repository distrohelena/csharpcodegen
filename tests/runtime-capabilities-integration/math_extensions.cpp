#include "system/math.hpp"
#include "runtime/native_exceptions.hpp"
#include <limits>
#ifdef HE_CPP_TEST_HOST
#include <cstdlib>
namespace he_cpp_custom {
[[noreturn]] void Fail(const char*) { std::exit(73); }
}
#endif
int main(int argc, char**) {
    if (argc > 1) {
#if HE_CPP_USE_EXCEPTIONS
        try { Math::Sign(std::numeric_limits<double>::quiet_NaN()); }
        catch (const ArithmeticException&) { return 73; }
        return 20;
#else
        Math::Sign(std::numeric_limits<double>::quiet_NaN());
        return 21;
#endif
    }
    if (Math::Asin(0.0) != 0.0 || MathF::Asin(0.0f) != 0.0f) return 1;
    if (Math::Abs(Math::Asin(1.0) - 1.5707963267948966) > 1e-12) return 2;
    if (Math::Abs(Math::Asin(-1.0) + 1.5707963267948966) > 1e-12) return 3;
    double invalid = Math::Asin(2.0);
    if (invalid == invalid) return 4;
    if (Math::Sign(std::numeric_limits<long long>::lowest()) != -1) return 5;
    if (Math::Sign(std::numeric_limits<unsigned long long>::max()) != 1) return 6;
    if (Math::Sign(-0.0) != 0 || MathF::Sign(-0.0f) != 0) return 7;
    if (Math::Sign(std::numeric_limits<double>::infinity()) != 1) return 8;
    if (Math::Sign(-std::numeric_limits<double>::infinity()) != -1) return 9;
#if !HE_CPP_COMPACT_NATIVE_EXCEPTION_MESSAGES
    ArithmeticException custom("custom arithmetic");
    if (HeCppString(custom.what()) != "custom arithmetic") return 10;
#endif
    return 0;
}
