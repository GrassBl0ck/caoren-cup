#include <algorithm>
#include <array>
#include <cassert>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <stdexcept>

extern "C" int crc_engine_abi_version();
extern "C" int crc_weapon_inaccuracy(void*, void*, std::size_t, std::size_t, float, float*, float*, float*);

namespace {
float original_getter(void* weapon, float* movement, float* air) {
    float* data = nullptr;
    std::memcpy(&data, static_cast<std::byte*>(weapon) + 0x600, sizeof(data));
    *movement = data[0];
    *air = data[3];
    return std::clamp(data[2] + *movement + *air, 0.0f, 1.0f);
}
float throwing_getter(void*, float*, float*) { throw std::runtime_error("getter failure"); }
}

int main() {
    assert(crc_engine_abi_version() == 2);
    std::array<std::byte, 0x600 + sizeof(void*)> weapon{};
    std::array<float, 4> data{0.4f, 0.5f, 0.6f, 0.1f};
    float* original = data.data();
    std::memcpy(weapon.data() + 0x600, &original, sizeof(original));
    float total = 0, movement = 0, air = 0;
    auto query = [&](float scale, void* getter) {
        return crc_weapon_inaccuracy(getter, weapon.data(), sizeof(data), 0, scale, &total, &movement, &air);
    };
    assert(query(0.75f, reinterpret_cast<void*>(original_getter)) == 1);
    assert(std::abs(total - 1.0f) < 0.00001f);
    assert(std::abs(movement - 0.3f) < 0.00001f);
    assert(std::abs(air - 0.1f) < 0.00001f);
    assert(query(0.5f, reinterpret_cast<void*>(original_getter)) == 1);
    assert(std::abs(total - 0.9f) < 0.00001f);
    assert(query(0.5f, reinterpret_cast<void*>(throwing_getter)) == 0);
    float* restored = nullptr;
    std::memcpy(&restored, weapon.data() + 0x600, sizeof(restored));
    assert(restored == original);
    assert(data[0] == 0.4f && data[1] == 0.5f && data[2] == 0.6f && data[3] == 0.1f);
    assert(query(2.0f, reinterpret_cast<void*>(original_getter)) == 0);
}
