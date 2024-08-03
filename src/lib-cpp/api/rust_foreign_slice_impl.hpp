#pragma once

#include "rust_foreign_slice_iter.hpp"

namespace velopack_cpp {
template <class ForeignClassRef, typename CContainerType>
class RustForeignSlice final : public CContainerType {
public:
    using const_reference = ForeignClassRef;
    using CForeignType = typename ForeignClassRef::CForeignType;
    using value_type = typename ForeignClassRef::value_type;
    using iterator = RustForeignSliceIterator<ForeignClassRef>;
    using const_iterator = RustForeignSliceIterator<ForeignClassRef>;

    RustForeignSlice() noexcept { reset(); }
    explicit RustForeignSlice(const CContainerType &o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->step = o.step;
    }
    RustForeignSlice(const RustForeignSlice &) = delete;
    RustForeignSlice &operator=(const RustForeignSlice &) = delete;
    RustForeignSlice(RustForeignSlice &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->step = o.step;
        o.reset();
    }
    RustForeignSlice &operator=(RustForeignSlice &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->step = o.step;
        o.reset();
        return *this;
    }
    ~RustForeignSlice() noexcept {}
    size_t size() const noexcept { return this->len; }
    bool empty() const noexcept { return this->len == 0; }
    ForeignClassRef operator[](size_t i) const noexcept
    {
        assert(i < this->len);
        auto p = static_cast<const uint8_t *>(this->data);
        p += this->step * i;
        auto elem_ptr = static_cast<const CForeignType *>(static_cast<const void *>(p));
        return ForeignClassRef{ elem_ptr };
    }
    iterator begin() noexcept { return iterator{ this->data, this->step }; }

    const_iterator begin() const noexcept { return const_iterator{ this->data, this->step }; }

    iterator end() noexcept
    {
        auto p = static_cast<const uint8_t *>(this->data);
        p += this->step * this->len;
        return iterator{ p, this->step };
    }

    const_iterator end() const noexcept
    {
        auto p = static_cast<const uint8_t *>(this->data);
        p += this->step * this->len;
        return const_iterator{ p, this->step };
    }

private:
    void reset()
    {
        this->step = 0;
        this->len = 0;
        this->data = nullptr;
    }
};

} // namespace velopack_cpp
