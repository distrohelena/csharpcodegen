#include "runtime/freestanding/freestanding_string.hpp"
#include "runtime/freestanding/freestanding_vector.hpp"
#include "runtime/freestanding/freestanding_hash.hpp"
#include "runtime/freestanding/freestanding_hash_map.hpp"
#include "runtime/freestanding/freestanding_hash_set.hpp"
#include <string.h>

using he_cpp_freestanding::FreestandingString;
using he_cpp_freestanding::FreestandingVector;
using he_cpp_freestanding::FreestandingHash;

// Tracked gained a default constructor for FreestandingHashMap::operator[]'s value-initialising
// insert (map_smoke's `map[500];`); every other test here still exercises the explicit-id constructor.
struct Tracked {
    inline static int Alive = 0;
    int Id;
    Tracked() : Id(0) { ++Alive; }
    explicit Tracked(int id) : Id(id) { ++Alive; }
    Tracked(const Tracked& other) : Id(other.Id) { ++Alive; }
    Tracked(Tracked&& other) noexcept : Id(other.Id) { other.Id = -1; ++Alive; }
    Tracked& operator=(const Tracked&) = default;
    ~Tracked() { --Alive; }
};

struct IntEqual {
    bool operator()(int a, int b) const { return a == b; }
};
struct StringEqual {
    bool operator()(const FreestandingString& a, const FreestandingString& b) const { return a == b; }
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
    FreestandingString aliased("abcdefgh");
    aliased += aliased;
    aliased.insert(0, aliased.c_str());
    if (aliased != "abcdefghabcdefghabcdefghabcdefgh") return 26;
    if (FreestandingString("hello world").find("world", FreestandingString::npos) != FreestandingString::npos) return 27;
    if (FreestandingString("hello world").find("world", 7) != FreestandingString::npos ||
        FreestandingString("hello world").find("world", 6) != 6) return 28;
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
        auto zeroErase = items.erase(items.begin() + 1, items.begin() + 1);
        if (zeroErase != items.begin() + 1 || items[1].Id != 99 || Tracked::Alive != 18) return 49;
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
    FreestandingVector<int> aliasedInts{10, 20, 30, 40};
    aliasedInts.push_back(aliasedInts[0]);
    aliasedInts.push_back(aliasedInts.back());
    if (aliasedInts.size() != 6 || aliasedInts[4] != 10 || aliasedInts[5] != 10) return 47;
    aliasedInts.resize(9, aliasedInts[1]);
    if (aliasedInts[8] != 20) return 48;
    FreestandingVector<int> w{1, 2, 3, 4};
    w.emplace_back(w[0]);
    w.emplace_back(w.back());
    if (w.size() != 6 || w[4] != 1 || w[5] != 1) return 50;
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

int map_smoke() {
    using Map = he_cpp_freestanding::FreestandingHashMap<int, Tracked, FreestandingHash<int>, IntEqual>;
    {
        Map map;
        for (int index = 0; index < 100; ++index) {
            auto result = map.emplace(index, Tracked(index * 10));
            if (!result.second || result.first->first != index) return 60;
        }
        if (map.size() != 100 || Tracked::Alive != 100) return 61;
        if (map.emplace(5, Tracked(0)).second) return 62;
        auto found = map.find(42);
        if (found == map.end() || found->second.Id != 420) return 63;
        if (map.find(1000) != map.end() || map.count(1000) != 0 || !map.contains(42)) return 64;
        if (map.erase(42) != 1 || map.erase(42) != 0 || map.size() != 99 || map.find(42) != map.end()) return 65;
        map.insert_or_assign(7, Tracked(777));
        if (map[7].Id != 777) return 66;
        map[500];
        if (map.size() != 100 || map.find(500) == map.end()) return 67;
        int visited = 0, sum = 0;
        for (const auto& entry : map) { ++visited; sum += entry.first; }
        if (visited != 100 || sum != (99 * 100 / 2) - 42 + 500) return 68;
        for (auto iterator = map.begin(); iterator != map.end();) {
            if (iterator->first % 2 == 0) iterator = map.erase(iterator); else ++iterator;
        }
        if (map.size() != 50) return 69;
        for (const auto& entry : map) if (entry.first % 2 == 0) return 70;
        map.reserve(4096);
        if (map.size() != 50 || map.find(1) == map.end()) return 71;
        {
            // Emplace(key, build) takes key by const reference; if a rehash inside EnsureRoom ran
            // between reading the key and building the entry, a key that referenced the table's own
            // storage would dangle. Force a rehash on insertion (reserve() just barely covers the
            // current entries, so EnsureRoom's three-quarters-load check trips on the next insert) and
            // have the key expression read straight out of the table to exercise that path.
            Map dense;
            for (int index = 0; index < 6; ++index) dense.emplace(index, Tracked(index * 10));
            dense.reserve(dense.size());
            auto emplaced = dense.emplace(dense.find(3)->first + 1000, Tracked(1));
            if (!emplaced.second || emplaced.first->first != 1003 || emplaced.first->second.Id != 1 || dense.size() != 7) return 76;
        }
        Map copy = map;
        if (copy.size() != 50 || copy.find(3) == copy.end()) return 72;
        map.clear();
        if (!map.empty() || copy.size() != 50) return 73;
    }
    if (Tracked::Alive != 0) return 74;
    he_cpp_freestanding::FreestandingHashMap<FreestandingString, int, FreestandingHash<FreestandingString>, StringEqual> names;
    names.emplace(FreestandingString("alpha"), 1);
    names[FreestandingString("beta")] = 2;
    if (names.find(FreestandingString("alpha"))->second != 1 || names[FreestandingString("beta")] != 2) return 75;
    return 0;
}

int set_smoke() {
    he_cpp_freestanding::FreestandingHashSet<int, FreestandingHash<int>, IntEqual> set;
    for (int index = 0; index < 50; ++index) if (!set.insert(index).second) return 80;
    if (set.insert(10).second || set.size() != 50) return 81;
    if (!set.contains(10) || set.count(10) != 1 || set.find(99) != set.end()) return 82;
    if (set.erase(10) != 1 || set.contains(10)) return 83;
    int sum = 0;
    for (int value : set) sum += value;
    if (sum != (49 * 50 / 2) - 10) return 84;
    set.clear();
    if (!set.empty()) return 85;
    return 0;
}

int freestanding_provider_smoke() {
    int result = string_smoke();
    if (result != 0) return result;
    result = vector_smoke();
    if (result != 0) return result;
    result = hash_smoke();
    if (result != 0) return result;
    result = map_smoke();
    if (result != 0) return result;
    return set_smoke();
}

#if defined(HE_CPP_TEST_HOST)
#include <stdlib.h>
#include <stdio.h>
namespace he_cpp_custom {
[[noreturn]] void Fail(const char* message) { fputs(message, stderr); exit(73); }
uint64_t MonotonicMicroseconds() { return 0; }
}
int main(int argc, char** argv) {
    if (argc > 1 && strcmp(argv[1], "fail") == 0) {
        FreestandingVector<int> overflow;
        overflow.reserve(static_cast<size_t>(-1) / sizeof(int) + 1);
        return 0;
    }
    return freestanding_provider_smoke();
}
#endif
