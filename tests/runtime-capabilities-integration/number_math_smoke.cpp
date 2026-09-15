#include "system/math.hpp"
#include "system/number.hpp"

#include <cmath>

int main() {
    int32_t parsed = 41;
    if (!Number::TryParse(HeCppString("-2147483648"), parsed) || parsed != INT32_MIN) return 1;
    parsed = 41;
    if (Number::TryParse(HeCppString("2147483648"), parsed) || parsed != 41) return 2;
    if (Number::TryParse(HeCppString("+1"), parsed) || Number::TryParse(HeCppString(" 1"), parsed)) return 3;
    if (Number::TryParse(HeCppString("12x"), parsed)) return 4;
    if (Math::Round(2.5) != 2.0 || Math::Round(3.5) != 4.0 || Math::Round(-2.5) != -2.0) return 5;
    if (std::fabs(Math::Sqrt(9.0) - 3.0) > 1e-12) return 6;
    if (std::fabs(Math::Log2(8.0) - 3.0) > 1e-12) return 7;
    if (!Math::IsFinite(1.0) || Math::IsFinite(Number::PositiveInfinity<double>())) return 8;
    return 0;
}
