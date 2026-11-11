using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;

namespace CaorenCup.Contracts;

/// <summary>调用服务器烟雾密度查询，使用引擎当前体积而非固定距离球。</summary>
public static class CaorenCupSmokeCoverage
{
    // GetSmokeDensityInLine(const float* from, const float* to, float* closest).
    // 引擎 ABI 与指纹参考 https://github.com/XBribo/CS2-Bot-Vision 。
    private static readonly Lazy<MemoryFunctionWithReturn<nint, nint, nint, float>> Query = new(() => new(
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? "55 48 89 E5 41 57 41 56 48 8D 45 ? 41 55 49 89 FD 41 54 49 89 F4 53 48 89 D3 48 8D 55 ? 48 83 EC ? 66 48 0F 6E FA"
            : "40 55 53 56 41 55 41 57 48 8D 6C 24 ? 48 81 EC ? ? ? ? 0F 29 BC 24"));

    public static float Density(Vector from, Vector to) => Query.Value.Invoke(from.Handle, to.Handle, nint.Zero);
    public static void EnsureAvailable() => _ = Query.Value;

    public static bool Contains(Vector point) => Density(new(point.X, point.Y, point.Z - 1),
        new(point.X, point.Y, point.Z + 1)) > 0;

    public static bool Obscures(Vector from, Vector to) => Density(from, to) > 0;
}
