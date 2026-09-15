#include "eastl_provider.hpp"
#include "runtime/native_span.hpp"

#include <utility>

struct CountingDelete {
    int* Calls;

    void operator()(int* value) const {
        if (value != nullptr && *value == 42) {
            ++*Calls;
        }
        delete value;
    }
};

int main() {
    int deletes = 0;
    {
        he_cpp_custom::SharedPtr<int> owner(new int(42), CountingDelete{&deletes});
        if (owner.get() == nullptr || *owner.get() != 42 || owner.use_count() != 1) return 1;

        he_cpp_custom::SharedPtr<int> copy(owner);
        he_cpp_custom::SharedPtr<void> erased(copy);
        he_cpp_custom::SharedPtr<int> moved(std::move(copy));
        if (copy.get() != nullptr || copy.use_count() != 0) return 2;
        if (owner.use_count() != 3 || erased.get() != static_cast<void*>(owner.get())) return 3;

        moved.reset();
        if (owner.use_count() != 2 || deletes != 0) return 4;
        erased.reset();
        if (owner.use_count() != 1 || deletes != 0) return 5;

        owner.reset();
        if (deletes != 1) return 6;
    }

    {
        he_cpp_custom::SharedPtr<void> direct(new int(42), CountingDelete{&deletes});
        if (!direct || direct.use_count() != 1) return 7;
        he_cpp_custom::SharedPtr<void> copy(direct);
        direct.reset();
        if (copy.use_count() != 1 || deletes != 1) return 8;
        copy.reset();
    }

    {
        Span<int> owned{1, 2, 3};
        if (owned.Data == nullptr || owned.get_Length() != 3 || owned.Owner.use_count() != 1) return 10;
        Span<int> copy(owned);
        if (copy.Data != owned.Data || copy.Owner.use_count() != 2) return 11;
        copy.Owner.reset();
        if (owned.Owner.use_count() != 1) return 12;
        owned.Owner.reset();
    }

    return deletes == 2 ? 0 : 13;
}
