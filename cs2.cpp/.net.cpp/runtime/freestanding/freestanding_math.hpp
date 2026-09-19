#pragma once

// Math surface for HE_CPP_USE_STD_MATH=0. system/math.hpp calls the C names
// (::sin, ::sqrt, ...). On a host with <math.h> the platform libm serves them;
// on a freestanding target the software versions below are exported with C
// linkage. HE_CPP_FREESTANDING_MATH_SOFTWARE forces the software versions and
// exposes them in he_cpp_freestanding_math for accuracy tests.

#if !defined(HE_CPP_FREESTANDING_MATH_SOFTWARE)
#if defined(__has_include)
#if __has_include(<math.h>) && !defined(__mos__)
#define HE_CPP_FREESTANDING_MATH_HOSTED 1
#endif
#endif
#endif

#if defined(HE_CPP_FREESTANDING_MATH_HOSTED)
#include <math.h>
#else
namespace he_cpp_freestanding_math {
double Ceil(double value);
double Floor(double value);
double Fabs(double value);
double Sqrt(double value);
double Sin(double value);
double Cos(double value);
double Tan(double value);
double Asin(double value);
double Acos(double value);
double Atan2(double y, double x);
double Log(double value);
double Log2(double value);
double Fmod(double value, double divisor);
int Finite(double value);
}
#if !defined(HE_CPP_FREESTANDING_MATH_SOFTWARE) || defined(__mos__)
extern "C" {
double ceil(double value);
double floor(double value);
double fabs(double value);
double acos(double value);
double asin(double value);
double sin(double value);
double cos(double value);
double tan(double value);
double sqrt(double value);
double log(double value);
double log2(double value);
double fmod(double value, double divisor);
double atan2(double y, double x);
int finite(double value);
}
#endif
#endif
