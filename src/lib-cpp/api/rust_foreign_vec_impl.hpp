#pragma once

#include <cassert>

#include "rust_foreign_slice_impl.hpp"
#include "rust_foreign_slice_iter.hpp"
#include "rust_slice.h"
#include "rust_slice_mut.h"

namespace velopack_cpp {

template <class ForeignClassRef, typename CContainerType, void (*FreeFunc)(CContainerType),
          void (*PushFunc)(CContainerType *, void *),
          void *(*RemoveFunc)(CContainerType *, uintptr_t), const uintptr_t &ELEM_SIZE>
class RustForeignVec final : private CContainerType {
public:
    using const_reference = ForeignClassRef;
    using CForeignType = typename ForeignClassRef::CForeignType;
    using value_type = typename ForeignClassRef::value_type;
    using iterator = RustForeignSliceIterator<ForeignClassRef>;
    using const_iterator = RustForeignSliceIterator<ForeignClassRef>;

    RustForeignVec() noexcept { reset(*this); }
    explicit RustForeignVec(const CContainerType &o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;
    }
    RustForeignVec(const RustForeignVec &) = delete;
    RustForeignVec &operator=(const RustForeignVec &) = delete;
    RustForeignVec(RustForeignVec &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;
        reset(o);
    }
    RustForeignVec &operator=(RustForeignVec &&o) noexcept
    {
        free_mem();
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;
        reset(o);
        return *this;
    }
    ~RustForeignVec() noexcept { free_mem(); }

    size_t size() const noexcept { return this->len; }
    bool empty() const noexcept { return this->len == 0; }

    ForeignClassRef operator[](size_t i) const noexcept
    {
        assert(i < this->len);
        auto p = static_cast<const uint8_t *>(this->data);
        p += ELEM_SIZE * i;
        auto elem_ptr = static_cast<const CForeignType *>(static_cast<const void *>(p));
        return ForeignClassRef{ elem_ptr };
    }

    void push(value_type o) noexcept { PushFunc(this, o.release()); }

    value_type remove(size_t idx) noexcept
    {
        auto p = static_cast<CForeignType *>(RemoveFunc(this, idx));
        return value_type{ p };
    }

    iterator begin() noexcept { return iterator{ this->data, ELEM_SIZE }; }

    const_iterator begin() const noexcept { return const_iterator{ this->data, ELEM_SIZE }; }

    iterator end() noexcept
    {
        auto p = static_cast<const uint8_t *>(this->data);
        p += ELEM_SIZE * this->len;
        return iterator{ p, ELEM_SIZE };
    }

    const_iterator end() const noexcept
    {
        auto p = static_cast<const uint8_t *>(this->data);
        p += ELEM_SIZE * this->len;
        return const_iterator{ p, ELEM_SIZE };
    }

    RustForeignSlice<ForeignClassRef, CRustObjectSlice> as_slice() const noexcept
    {
        return RustForeignSlice<ForeignClassRef, CRustObjectSlice>{ CRustObjectSlice{
            this->data, this->len, ELEM_SIZE } };
    }

    RustForeignSlice<ForeignClassRef, CRustObjectMutSlice> as_slice_mut() noexcept
    {
        return RustForeignSlice<ForeignClassRef, CRustObjectMutSlice>{ CRustObjectMutSlice{
            this->data, this->len, ELEM_SIZE } };
    }

    void clear() noexcept { free_mem(); }

    CContainerType release() noexcept
    {
        CContainerType ret{ this->data, this->len, this->capacity };
        reset(*this);
        return ret;
    }

private:
    void free_mem() noexcept
    {
        FreeFunc(*this);
        reset(*this);
    }
    static void reset(RustForeignVec &o) noexcept
    {
        // Rust Vec::new uses NonNull::danling with similar value
        o.data = reinterpret_cast<value_type *>(std::alignment_of<value_type>::value);
        o.len = 0;
        o.capacity = 0;
    }
};
} // namespace velopack_cpp
