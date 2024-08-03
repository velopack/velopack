#pragma once

#include <cstdint>
#include <cstddef>

#include "rust_slice.h"

namespace velopack_cpp {
template <typename IndexAccess> class RustSliceAccess final : public CRustSliceAccess {
public:
    using ReturnType = typename IndexAccess::ReturnType;

    RustSliceAccess() noexcept { reset(); }
    explicit RustSliceAccess(CRustSliceAccess o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
    }
    RustSliceAccess(const RustSliceAccess &) = delete;
    RustSliceAccess &operator=(const RustSliceAccess &) = delete;
    RustSliceAccess(RustSliceAccess &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        o.reset();
    }
    RustSliceAccess &operator=(RustSliceAccess &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        o.reset();
        return *this;
    }
    ~RustSliceAccess() noexcept {}
    size_t size() const noexcept { return this->len; }
    bool empty() const noexcept { return this->len == 0; }
    ReturnType operator[](size_t i) const noexcept
    {
        assert(i < this->len);
        return IndexAccess::index(*this, i);
    }

private:
    void reset() noexcept
    {
        this->data = nullptr;
        this->len = 0;
    }
};
} // namespace velopack_cpp
