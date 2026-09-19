#include "runtime/freestanding/freestanding_string.hpp"
#include "runtime/freestanding/freestanding_vector.hpp"
#include "runtime/freestanding/freestanding_hash.hpp"
#include <string.h>

using he_cpp_freestanding::FreestandingString;
using he_cpp_freestanding::FreestandingVector;
using he_cpp_freestanding::FreestandingHash;

struct Tracked {
    inline static int Alive = 0;
    int Id;
    explicit Tracked(int id) : Id(id) { ++Alive; }
    Tracked(const Tracked& other) : Id(other.Id) { ++Alive; }
    Tracked(Tracked&& other) noexcept : Id(other.Id) { other.Id = -1; ++Alive; }
    Tracked& operator=(const Tracked&) = default;
    ~Tracked() { --Alive; }
};

int string_smoke() {
    FreestandingString empty;
    if (!empty.empty() || empty.size() != 0 || empty.c_str()[0] != '\0') return 1;
    FreestandingString text("cube");
    for (int index = 0; index < 40; ++index) text += "_scene";
    if (text.size() != 244 || text.length() != 244) return 2;
    FreestandingString copy = text;
    text[0] = 'C';
    if (copy[0] != 'c' || text[0] != 'C' || copy.size() != 244) return 3;
    FreestandingString moved(static_cast<FreestandingString&&>(copy));
    if (moved.size() != 244 || copy.size() != 0) return 4;
    if (FreestandingString("abc").compare(FreestandingString("abd")) >= 0) return 5;
    if (!(FreestandingString("abc") == "abc") || FreestandingString("abc") != FreestandingString("abc")) return 6;
    if (!(FreestandingString("abc") < FreestandingString("abd"))) return 7;
    FreestandingString hello("hello world");
    if (hello.find("world") != 6 || hello.find('z') != FreestandingString::npos) return 8;
    if (hello.find_first_of("ow") != 4) return 9;
    if (hello.substr(6) != "world" || hello.substr(0, 5) != "hello" || hello.substr(11).size() != 0) return 10;
    hello.erase(5, 6);
    if (hello != "hello") return 11;
    hello.insert(5, " there");
    if (hello != "hello there") return 12;
    hello.replace(0, 5, "HELLO");
    if (hello != "HELLO there") return 13;
    hello.push_back('!');
    if (hello.back() != '!' || hello.front() != 'H') return 14;
    hello.pop_back();
    hello.resize(5);
    if (hello != "HELLO") return 15;
    hello.resize(7, 'x');
    if (hello != "HELLOxx") return 16;
    hello.reserve(100);
    if (hello.capacity() < 100 || hello != "HELLOxx") return 17;
    hello.clear();
    if (!hello.empty()) return 18;
    FreestandingString a("a"), b("b");
    a.swap(b);
    if (a != "b" || b != "a") return 19;
    FreestandingString joined = FreestandingString("x") + "y" + FreestandingString("z");
    if (joined != "xyz") return 20;
    FreestandingString counted(3, 'q');
    if (counted != "qqq") return 21;
    FreestandingString ranged("abcdef", 3);
    if (ranged != "abc") return 22;
    int chars = 0;
    for (char c : ranged) { (void)c; ++chars; }
    if (chars != 3) return 23;
    ranged.assign("zz");
    ranged.append("yy");
    if (ranged != "zzyy" || ranged.at(1) != 'z') return 24;
    if (strcmp(ranged.data(), "zzyy") != 0) return 25;
    return 0;
}

int vector_smoke() {
    {
        FreestandingVector<Tracked> items;
        for (int index = 0; index < 20; ++index) items.push_back(Tracked(index));
        if (items.size() != 20 || Tracked::Alive != 20) return 30;
        items.emplace_back(20);
        if (items.back().Id != 20 || items.front().Id != 0 || items[5].Id != 5) return 31;
        items.erase(items.begin() + 5);
        if (items.size() != 20 || items[5].Id != 6 || Tracked::Alive != 20) return 32;
        items.erase(items.begin(), items.begin() + 3);
        if (items.size() != 17 || items[0].Id != 3) return 33;
        items.insert(items.begin() + 1, Tracked(99));
        if (items[1].Id != 99 || items[2].Id != 4) return 34;
        items.pop_back();
        if (items.size() != 17) return 35;
        items.resize(5);
        if (items.size() != 5 || Tracked::Alive != 5) return 36;
        items.resize(8, Tracked(7));
        if (items.size() != 8 || items[7].Id != 7) return 37;
        int sum = 0;
        for (const Tracked& item : items) sum += item.Id;
        if (sum != 3 + 99 + 4 + 6 + 7 * 4) return 38;
        FreestandingVector<Tracked> copy = items;
        if (copy.size() != 8 || Tracked::Alive != 16) return 39;
        FreestandingVector<Tracked> moved(static_cast<FreestandingVector<Tracked>&&>(copy));
        if (moved.size() != 8 || copy.size() != 0 || Tracked::Alive != 16) return 40;
        moved.swap(copy);
        if (copy.size() != 8 || moved.size() != 0) return 41;
        copy.clear();
        if (!copy.empty() || Tracked::Alive != 8) return 42;
        items.reserve(1000);
        if (items.capacity() < 1000 || items.size() != 8) return 43;
        if (items.data() != &items[0]) return 44;
    }
    if (Tracked::Alive != 0) return 45;
    FreestandingVector<int> numbers{1, 2, 3};
    if (numbers.size() != 3 || numbers[2] != 3) return 46;
    return 0;
}

int hash_smoke() {
    FreestandingHash<int> hashInt;
    FreestandingHash<FreestandingString> hashString;
    FreestandingHash<const char*> hashPointer;
    if (hashInt(1) == hashInt(2)) return 50;
    if (hashString(FreestandingString("cube")) != hashString(FreestandingString("cube"))) return 51;
    if (hashString(FreestandingString("cube")) == hashString(FreestandingString("cubf"))) return 52;
    const char* p = "x";
    if (hashPointer(p) != hashPointer(p)) return 53;
    return 0;
}

int freestanding_provider_smoke() {
    int result = string_smoke();
    if (result != 0) return result;
    result = vector_smoke();
    if (result != 0) return result;
    return hash_smoke();
}

#if defined(HE_CPP_TEST_HOST)
#include <stdlib.h>
#include <stdio.h>
namespace he_cpp_custom {
[[noreturn]] void Fail(const char* message) { fputs(message, stderr); exit(73); }
uint64_t MonotonicMicroseconds() { return 0; }
}
int main() { return freestanding_provider_smoke(); }
#endif
