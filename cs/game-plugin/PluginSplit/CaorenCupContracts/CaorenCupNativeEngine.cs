using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;

namespace CaorenCup.Contracts;

/// <summary>经镜像摘要验证的 Linux 引擎桥接。原生库与共享契约一起打包。</summary>
public static class CaorenCupNativeEngine
{
    private const string SupportedImage = "ee0cc32a4b83c4a11bdd6bc32c5347000165ee236b5c6ff0b1901f49827405e3";
    private const string DensitySignature = "55 48 89 E5 41 57 41 56 48 8D 45 ? 41 55 49 89 FD 41 54 49 89 F4 53 48 89 D3 48 8D 55 ? 48 83 EC ? 66 48 0F 6E FA";
    private static nint _library, _visitor, _callback;
    private static SmokeDensity? _density;
    private static InaccuracyQuery? _inaccuracy;
    private static AbiVersion? _onOwnerThread;
    private static nint _inaccuracyAddress;
    [ThreadStatic] private static int _inaccuracyDepth;
    public static bool Available => _library != nint.Zero && _density is not null && _inaccuracy is not null;
    public static bool OnOwnerThread => Available && _onOwnerThread!() == 1;
    public static bool InaccuracyQueryInProgress => _inaccuracyDepth > 0;
    public const string InaccuracySignature = "55 48 89 E5 41 57 41 56 41 55 49 89 D5 41 54 49 89 F4 53 48 89 FB 48 83 EC 48 E8 21 C3 1A 00 48 85 C0";
    public sealed record InaccuracyParts(float Total, float Movement, float Air);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AbiVersion();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ModulePath(nint address);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float SmokeDensity(nint volume,
        nint visitor, nint callback, nint from, nint to, float now);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InaccuracyQuery(nint getter, nint weapon,
        nuint size, nuint moveOffset, float multiplier, out float total, out float movement, out float air);

    public static void Initialize(string directory)
    {
        if (Available) return;
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("当前随机乱斗原生组件需要 Linux x64 服务器。");
        var library = NativeLibrary.Load(Path.Combine(directory, "native", "libcaorencup_engine.so"));
        try
        {
            var version = Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(library, "crc_engine_abi_version"));
            if (version() != 2) throw new InvalidOperationException("随机乱斗原生组件版本不匹配。");
            var address = NativeAPI.FindSignature(Addresses.ServerPath, DensitySignature);
            if (address == nint.Zero) throw new InvalidOperationException("无法定位引擎烟雾查询。");
            var getPath = Marshal.GetDelegateForFunctionPointer<ModulePath>(NativeLibrary.GetExport(library, "crc_engine_module_path"));
            var path = Marshal.PtrToStringUTF8(getPath(address)) ?? throw new InvalidOperationException("无法确认引擎模块路径。");
            using var file = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
            if (hash != SupportedImage) throw new InvalidOperationException("游戏引擎已更新，需要重新验证随机乱斗原生接口。");
            _visitor = address - 0x1040;
            _callback = address - 0xaee0;
            _density = Marshal.GetDelegateForFunctionPointer<SmokeDensity>(NativeLibrary.GetExport(library, "crc_smoke_density"));
            _inaccuracyAddress = address + 0x41fa0;
            if (NativeAPI.FindSignature(Addresses.ServerPath, InaccuracySignature) != _inaccuracyAddress)
                throw new InvalidOperationException("原生精准接口指纹不匹配。");
            _inaccuracy = Marshal.GetDelegateForFunctionPointer<InaccuracyQuery>(NativeLibrary.GetExport(library, "crc_weapon_inaccuracy"));
            _onOwnerThread = Marshal.GetDelegateForFunctionPointer<AbiVersion>(NativeLibrary.GetExport(library, "crc_engine_on_owner_thread"));
            _library = library;
        }
        catch { _density = null; _inaccuracy = null; NativeLibrary.Free(library); throw; }
    }

    public static float SmokeDensityInLine(CSmokeGrenadeProjectile smoke, Vector from, Vector to)
    {
        if (!Available) throw new InvalidOperationException("随机乱斗原生组件尚未加载。");
        if (!smoke.IsValid || !smoke.DidSmokeEffect) return 0;
        return _density!(smoke.Handle + 0xe98, _visitor, _callback, from.Handle, to.Handle, Server.CurrentTime);
    }

    public static bool SmokeContains(CSmokeGrenadeProjectile smoke, Vector point) => SmokeDensityInLine(smoke,
        new(point.X, point.Y, point.Z - 1), new(point.X, point.Y, point.Z + 1)) > 0;

    public static InaccuracyParts WeaponInaccuracy(CCSWeaponBase weapon, float movementMultiplier = 1)
    {
        if (!Available || !weapon.IsValid) throw new InvalidOperationException("原生精准接口或目标武器不可用。");
        _inaccuracyDepth++;
        try
        {
            var success = _inaccuracy!(_inaccuracyAddress, weapon.Handle, (nuint)Schema.GetClassSize("CCSWeaponBaseVData"),
                (nuint)Schema.GetSchemaOffset("CCSWeaponBaseVData", "m_flInaccuracyMove"), movementMultiplier,
                out var total, out var movement, out var air);
            if (success != 1) throw new InvalidOperationException("原生精准查询失败。");
            return new(total, movement, air);
        }
        finally { _inaccuracyDepth--; }
    }
}
