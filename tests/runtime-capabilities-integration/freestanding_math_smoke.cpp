#define HE_CPP_FREESTANDING_MATH_SOFTWARE 1
#include "runtime/freestanding/freestanding_math.hpp"

#include <math.h>   // host reference values only
#include <stdio.h>

namespace soft = he_cpp_freestanding_math;

static int Check(const char* name, double actual, double expected, double tolerance) {
    double error = actual - expected;
    if (error < 0) error = -error;
    double scale = expected < 0 ? -expected : expected;
    if (scale < 1.0) scale = 1.0;
    if (error / scale > tolerance) {
        printf("%s: got %.17g expected %.17g\n", name, actual, expected);
        return 1;
    }
    return 0;
}

int main() {
    int failures = 0;
    for (int index = -200; index <= 200; ++index) {
        double x = index * 0.05;
        failures += Check("sin", soft::Sin(x), ::sin(x), 1e-9);
        failures += Check("cos", soft::Cos(x), ::cos(x), 1e-9);
        failures += Check("atan2", soft::Atan2(x, 1.5), ::atan2(x, 1.5), 1e-9);
        failures += Check("atan2b", soft::Atan2(1.5, x), ::atan2(1.5, x), 1e-9);
        failures += Check("fmod", soft::Fmod(x, 0.7), ::fmod(x, 0.7), 1e-12);
        failures += Check("floor", soft::Floor(x), ::floor(x), 0.0);
        failures += Check("ceil", soft::Ceil(x), ::ceil(x), 0.0);
        failures += Check("fabs", soft::Fabs(x), ::fabs(x), 0.0);
        if (x > -1.0 && x < 1.0) {
            failures += Check("asin", soft::Asin(x), ::asin(x), 1e-9);
            failures += Check("acos", soft::Acos(x), ::acos(x), 1e-9);
        }
        if (x > 0.0) {
            failures += Check("sqrt", soft::Sqrt(x), ::sqrt(x), 1e-12);
            failures += Check("log", soft::Log(x), ::log(x), 1e-9);
            failures += Check("log2", soft::Log2(x), ::log2(x), 1e-9);
        }
        if (x > -1.5 && x < 1.5) failures += Check("tan", soft::Tan(x), ::tan(x), 1e-8);
    }
    failures += Check("sqrt-large", soft::Sqrt(1.0e12), 1.0e6, 1e-12);
    failures += Check("sin-large", soft::Sin(1000.0), ::sin(1000.0), 1e-7);
    // Large angles: the software reduction subtracts an exact multiple of pi / 2 up to the gate
    // freestanding_math.cpp documents, 2^26 radians, and answers NaN above it.
    const double gate = 67108864.0;
    const double justUnderGate = ::nextafter(gate, 0.0);
    failures += Check("sin-1e6", soft::Sin(1.0e6), ::sin(1.0e6), 1e-9);
    failures += Check("cos-1e6", soft::Cos(1.0e6), ::cos(1.0e6), 1e-9);
    failures += Check("tan-1e6", soft::Tan(1.0e6), ::tan(1.0e6), 1e-8);
    failures += Check("sin-gate", soft::Sin(justUnderGate), ::sin(justUnderGate), 1e-9);
    failures += Check("cos-gate", soft::Cos(justUnderGate), ::cos(justUnderGate), 1e-9);
    failures += Check("tan-gate", soft::Tan(justUnderGate), ::tan(justUnderGate), 1e-8);
    if (soft::Sin(gate * 2.0) == soft::Sin(gate * 2.0)) { puts("sin past the reduction gate must be NaN"); ++failures; }
    if (soft::Sqrt(-1.0) == soft::Sqrt(-1.0)) { puts("sqrt(-1) must be NaN"); ++failures; }
    if (soft::Finite(soft::Sqrt(-1.0)) != 0 || soft::Finite(1.0) != 1) { puts("finite"); ++failures; }
    if (soft::Log(0.0) > -1.0e300) { puts("log(0) must be -inf"); ++failures; }
    if (::fmod(5.0, 0.0) == ::fmod(5.0, 0.0) || soft::Fmod(5.0, 0.0) == soft::Fmod(5.0, 0.0)) { puts("fmod by zero must be NaN"); ++failures; }
    return failures == 0 ? 0 : 1;
}
