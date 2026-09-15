#include "system/number.hpp"

int main() {
    int32_t parsed = 41;
    if (Number::TryParse(HeCppString("12x"), parsed) || parsed != 12) return 1;
    parsed = 41;
    if (Number::TryParse(HeCppString("+1"), parsed) || parsed != 41) return 2;
    parsed = 41;
    if (Number::TryParse(HeCppString("2147483648"), parsed) || parsed != 41) return 3;
    parsed = 41;
    if (!Number::TryParse(HeCppString("-2147483648"), parsed) || parsed != INT32_MIN) return 4;
    return 0;
}
