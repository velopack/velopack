#pragma once

#include <cstdint>
#include <type_traits>

namespace velopack_cpp {

template <typename value_type> class RustSlice final {
public:
    using iterator = value_type *;
    using const_iterator = const value_type *;

    explicit RustSlice(value_type *p, size_t n) noexcept
    {
        this->data = p;
        this->len = n;
    }
    RustSlice(const RustSlice &) = delete;
    RustSlice &operator=(const RustSlice &) = delete;
    RustSlice(RustSlice &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;

        reset(o);
    }
    RustSlice &operator=(RustSlice &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;

        reset(o);
        return *this;
    }
    size_t size() const noexcept { return this->len; }
    bool empty() const noexcept { return this->len == 0; }
    const value_type &operator[](size_t i) const noexcept { return this->data[i]; }
    value_type &operator[](size_t i) noexcept { return this->data[i]; }
    iterator begin() noexcept { return this->data; }
    const_iterator begin() const noexcept { return this->data; }
    iterator end() noexcept { return this->data + this->len; }
    const_iterator end() const noexcept { return this->data + this->len; }
    template <typename CContainerType> CContainerType as_c() const noexcept
    {
        return CContainerType{ this->data, this->len };
    }

private:
    value_type *data;
    uintptr_t len;

    static void reset(RustSlice &o) noexcept
    {
        o.data = nullptr;
        o.len = 0;
    }
};

} // namespace velopack_cpp
