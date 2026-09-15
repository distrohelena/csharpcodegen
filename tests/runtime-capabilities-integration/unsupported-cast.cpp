#include "runtime/native_cast.hpp"
struct Base { virtual ~Base() = default; };
struct Derived : Base {};
Derived* unsupported_cast(Base* source) {
    return he_cpp_try_cast<Derived>(source);
}
