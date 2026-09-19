#include "runtime/native_algorithm.hpp"
#include <stddef.h>

struct Counter {
    int Value;
    int Double(int factor) { return Value * factor; }
};

struct MoveOnly {
    int* Target;
    explicit MoveOnly(int* target) : Target(target) {}
    MoveOnly(MoveOnly&& other) noexcept : Target(other.Target) { other.Target = nullptr; }
    MoveOnly(const MoveOnly&) = delete;
};

template <size_t... Indexes>
int SumIndexes(he_cpp_alg::IndexSequence<Indexes...>) {
    return (0 + ... + static_cast<int>(Indexes));
}

int algorithm_smoke() {
    int a = 1, b = 2;
    he_cpp_alg::Swap(a, b);
    if (a != 2 || b != 1) return 1;
    if (he_cpp_alg::Min(3, 4) != 3 || he_cpp_alg::Max(3, 4) != 4) return 2;
    int values[6] = {5, 1, 4, 1, 3, 1};
    int* found = he_cpp_alg::FindIf(values, values + 6, [](int v) { return v == 4; });
    if (found != values + 2) return 3;
    if (he_cpp_alg::Distance(values, found) != 2) return 4;
    int* newEnd = he_cpp_alg::RemoveIf(values, values + 6, [](int v) { return v == 1; });
    if (newEnd != values + 3 || values[0] != 5 || values[1] != 4 || values[2] != 3) return 5;
    he_cpp_alg::Replace(values, values + 3, 4, 9);
    if (values[1] != 9) return 6;
    int copy[3] = {0, 0, 0};
    he_cpp_alg::CopyN(values, 3, copy);
    if (copy[0] != 5 || copy[2] != 3) return 7;
    he_cpp_alg::FillN(copy, 3, 7);
    if (copy[0] != 7 || copy[2] != 7) return 8;
    he_cpp_alg::Transform(values, values + 3, copy, [](int v) { return v + 1; });
    if (copy[0] != 6 || copy[1] != 10) return 9;
    int target = 0;
    MoveOnly source(&target);
    MoveOnly moved(he_cpp_alg::Move(source));
    if (moved.Target != &target || source.Target != nullptr) return 10;
    if (he_cpp_alg::AddressOf(target) != &target) return 11;
    if (SumIndexes(he_cpp_alg::MakeIndexSequence<4>{}) != 6) return 12;
    if (SumIndexes(he_cpp_alg::IndexSequenceFor<int, int, int>{}) != 3) return 13;
    Counter counter{21};
    if (he_cpp_alg::Invoke(&Counter::Double, &counter, 2) != 42) return 14;
    if (he_cpp_alg::Invoke(&Counter::Double, counter, 3) != 63) return 15;
    if (he_cpp_alg::Invoke([](int v) { return v + 1; }, 1) != 2) return 16;
    double infinity = he_cpp_alg::Infinity<double>();
    double nan = he_cpp_alg::QuietNaN<double>();
    if (!(infinity > 1.0e308) || nan == nan) return 17;
    return 0;
}

#if defined(HE_CPP_TEST_HOST)
int main() { return algorithm_smoke(); }
#endif
