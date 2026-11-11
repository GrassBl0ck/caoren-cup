#include <algorithm>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <dlfcn.h>
#include <functional>
#include <thread>
#include <vector>

#define CRC_EXPORT extern "C" __attribute__((visibility("default")))

namespace {
std::thread::id owner_thread;
struct Vector3 { float x, y, z; };
using VoxelVisitor = std::function<bool(const Vector3&, const std::uint32_t&)>;
using VisitVolume = void (*)(void*, const Vector3*, const Vector3*, const VoxelVisitor*);
using EngineCallback = bool (*)(const void*, const Vector3*, const std::uint32_t*);

// Private engine ABI, validated against the complete libserver image hash before use.
// The bridge uses the engine's own voxel-density callback and does not reconstruct smoke.
struct DensityContext {
    bool* encountered;
    float* density;
    Vector3** closest;
    void* volume;
};
struct CallbackStorage { const DensityContext* context; std::uintptr_t padding; };

float smooth_step(float value) {
    value = std::clamp(value, 0.0f, 1.0f);
    return value * value * (3.0f - 2.0f * value);
}
} // namespace

CRC_EXPORT int crc_engine_abi_version() {
    owner_thread = std::this_thread::get_id();
    return 2;
}
CRC_EXPORT int crc_engine_on_owner_thread() { return std::this_thread::get_id() == owner_thread ? 1 : 0; }

CRC_EXPORT const char* crc_engine_module_path(void* address) {
    Dl_info information{};
    return dladdr(address, &information) != 0 ? information.dli_fname : nullptr;
}

CRC_EXPORT float crc_smoke_density(void* volume, void* visit_address, void* callback_address,
                                  const float* from, const float* to, float now) {
    if (!volume || !visit_address || !callback_address || !from || !to || !std::isfinite(now) ||
        std::this_thread::get_id() != owner_thread) return -1;
    try {
        void* voxel_data = nullptr;
        std::memcpy(&voxel_data, static_cast<std::byte*>(volume) + 0x70, sizeof(voxel_data));
        if (!voxel_data) return 0;
        bool encountered = false;
        float density = 0;
        Vector3* closest = nullptr;
        const DensityContext context{&encountered, &density, &closest, volume};
        const CallbackStorage storage{&context, 0};
        const auto callback = reinterpret_cast<EngineCallback>(callback_address);
        const VoxelVisitor visitor = [&](const Vector3& position, const std::uint32_t& cell) {
            return callback(&storage, &position, &cell);
        };
        const Vector3 begin{from[0], from[1], from[2]};
        const Vector3 end{to[0], to[1], to[2]};
        reinterpret_cast<VisitVolume>(visit_address)(volume, &begin, &end, &visitor);
        float started = 0;
        std::memcpy(&started, static_cast<std::byte*>(volume) + 0x0c, sizeof(started));
        const float age = now - started;
        // Same fade windows as this engine image's aggregate density query.
        const float fade = smooth_step((age - 0.1f) / 1.4f) * smooth_step((age - 22.0f) / -5.0f);
        return std::max(0.0f, density * fade);
    } catch (...) {
        return -1;
    }
}

CRC_EXPORT int crc_weapon_inaccuracy(void* getter_address, void* weapon, std::size_t data_size,
                                     std::size_t move_offset, float multiplier,
                                     float* total, float* movement, float* air) {
    if (!getter_address || !weapon || !total || !movement || !air || !std::isfinite(multiplier) ||
        multiplier < 0 || multiplier > 1 || move_offset > 65528 || data_size < move_offset + 8 ||
        data_size > 65536 || std::this_thread::get_id() != owner_thread) return 0;
    // Only this weapon's cached data pointer is redirected, while its read-only getter runs.
    // Other weapons keep their shared definitions. Every exit restores the original pointer.
    constexpr std::size_t data_pointer_offset = 0x600;
    auto* slot = static_cast<std::byte*>(weapon) + data_pointer_offset;
    void* original = nullptr;
    std::memcpy(&original, slot, sizeof(original));
    if (!original) return 0;
    try {
        std::vector<std::byte> data(data_size);
        std::memcpy(data.data(), original, data_size);
        for (int mode = 0; mode < 2; ++mode) {
            float value = 0;
            auto* field = data.data() + move_offset + sizeof(float) * mode;
            std::memcpy(&value, field, sizeof(value));
            value *= multiplier;
            std::memcpy(field, &value, sizeof(value));
        }
        struct RestorePointer {
            std::byte* slot;
            void* original;
            ~RestorePointer() { std::memcpy(slot, &original, sizeof(original)); }
        } restore{slot, original};
        void* adjusted = data.data();
        std::memcpy(slot, &adjusted, sizeof(adjusted));
        *movement = *air = 0;
        using Getter = float (*)(void*, float*, float*);
        *total = reinterpret_cast<Getter>(getter_address)(weapon, movement, air);
        return std::isfinite(*total) && std::isfinite(*movement) && std::isfinite(*air) ? 1 : 0;
    } catch (...) {
        return 0;
    }
}
