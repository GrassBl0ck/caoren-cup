using ValvePak;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: VpkPack <addoninfo.txt> <rush_001.vjs_c> <output.vpk>");
    return 2;
}

if (File.Exists(args[2]))
{
    throw new IOException($"Output already exists: {args[2]}");
}

using (var package = new Package())
{
    package.AddFile("addoninfo.txt", File.ReadAllBytes(args[0]));
    package.AddFile("maps/scripts/rush_001.vjs_c", File.ReadAllBytes(args[1]));
    package.Write(args[2]);
}

using (var check = new Package())
{
    check.Read(args[2]);
    check.VerifyFileChecksums();
    var script = check.FindEntry("maps/scripts/rush_001.vjs_c")
        ?? throw new InvalidOperationException("Packed script missing");
    check.ReadEntry(script, out byte[] extracted);
    if (!extracted.AsSpan().SequenceEqual(File.ReadAllBytes(args[1])))
    {
        throw new InvalidOperationException("Packed script differs from source");
    }
    Console.WriteLine($"Packed and verified {extracted.Length} script bytes");
}

return 0;
