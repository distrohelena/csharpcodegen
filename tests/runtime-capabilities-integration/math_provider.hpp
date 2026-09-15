#pragma once

#include <cmath>

inline int finite(double value) {
    return std::isfinite(value) ? 1 : 0;
}
