#pragma once

#include <cstddef>

namespace velopack_cpp {
template <typename VectorTrait, typename CContainerType>
class RustVecAccess final : private CContainerType {
public:
    using ElemType = typename VectorTrait::ElemType;

    explicit RustVecAccess(const CContainerType &o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;
    }
    RustVecAccess() noexcept { reset(*this); }
    RustVecAccess(const RustVecAccess &) = delete;
    RustVecAccess &operator=(const RustVecAccess &) = delete;
    RustVecAccess(RustVecAccess &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;

        reset(o);
    }
    RustVecAccess &operator=(RustVecAccess &&o) noexcept
    {
        free_mem();
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;

        reset(o);
        return *this;
    }
    ~RustVecAccess() noexcept { free_mem(); }
    size_t size() const noexcept { return this->len; }
    bool empty() const noexcept { return this->len == 0; }
    ElemType operator[](size_t i) const noexcept { return VectorTrait::index(*this, i); }

private:
    void free_mem() noexcept
    {
        if (this->data != nullptr) {
            VectorTrait::free(*this);
            reset(*this);
        }
    }
    static void reset(RustVecAccess &o) noexcept
    {
        o.data = nullptr;
        o.len = 0;
        o.capacity = 0;
    }
};
} // namespace velopack_cpp
