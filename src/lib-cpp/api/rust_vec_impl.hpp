#pragma once

#include <type_traits>

namespace velopack_cpp {

namespace internal {
    template <typename T, typename E> E field_type(E T::*);
}

template <typename CContainerType, void (*FreeFunc)(CContainerType)>
class RustVec final : private CContainerType {
public:
    using value_type =
        typename std::remove_const<typename std::remove_reference<decltype(*internal::field_type(
            &CContainerType::data))>::type>::type;
    using iterator = value_type *;
    using const_iterator = const value_type *;

    explicit RustVec(const CContainerType &o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;
    }
    RustVec() noexcept { reset(*this); }
    RustVec(const RustVec &) = delete;
    RustVec &operator=(const RustVec &) = delete;
    RustVec(RustVec &&o) noexcept
    {
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;

        reset(o);
    }
    RustVec &operator=(RustVec &&o) noexcept
    {
        free_mem();
        this->data = o.data;
        this->len = o.len;
        this->capacity = o.capacity;

        reset(o);
        return *this;
    }
    ~RustVec() noexcept { free_mem(); }
    size_t size() const noexcept { return this->len; }
    bool empty() const noexcept { return this->len == 0; }
    const value_type &operator[](size_t i) const noexcept { return this->data[i]; }
    iterator begin() noexcept { return this->data; }
    const_iterator begin() const noexcept { return this->data; }
    iterator end() noexcept { return this->data + this->len; }
    const_iterator end() const noexcept { return this->data + this->len; }
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
    static void reset(RustVec &o) noexcept
    {
        // Rust Vec::new uses NonNull::danling with similar value
        o.data = reinterpret_cast<value_type *>(std::alignment_of<value_type>::value);
        o.len = 0;
        o.capacity = 0;
    }
};

} // namespace velopack_cpp
