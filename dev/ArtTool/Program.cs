using System.Text.Json;
using ArtTool;
using SixLabors.ImageSharp;

if (args.Length == 0)
{
    Console.WriteLine("""
        ArtTool — Crewboss asset pipeline

        Usage:
          arttool compose <block.json> <brushDir> <outDir>
              Composite a block definition into <outDir>/<Name>.png + tiles/trees/debris/veg json

          arttool sprites <outDir> [seed=7]
              Generate every runtime sprite atlas (trees, debris, veg, quad, figures,
              seedlings, cache, badges, font).
        """);
    return 1;
}

switch (args[0])
{
    case "compose":
    {
        var def = JsonSerializer.Deserialize<BlockDef>(File.ReadAllText(args[1]),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Compositor.Compose(def, args[2], args[3]);
        return 0;
    }
    case "sprites":
    {
        Directory.CreateDirectory(args[1]);
        int seed = args.Length > 2 ? int.Parse(args[2]) : 7;
        using var cache = PropGen.Cache(seed, 96);
        string path = Path.Combine(args[1], "Cache.png");
        cache.SaveAsPng(path);
        Console.WriteLine($"wrote {path}");
        TreeGen.ExportAtlas(args[1], seed, pixelSize: 2);
        FigureGen.ExportAtlas(args[1]);
        FigureGen.ExportPlanterAtlas(args[1]);
        PropGen.ExportSeedlingAtlas(args[1]);
        PropGen.ExportDebrisAtlas(args[1]);
        PropGen.ExportVegAtlas(args[1]);
        QuadGen.ExportAtlas(args[1], seed);
        using (var b = PropGen.Badge('E')) b.SaveAsPng(Path.Combine(args[1], "BadgeE.png"));
        using (var b = PropGen.Badge('Q')) b.SaveAsPng(Path.Combine(args[1], "BadgeQ.png"));
        using (var b = PropGen.Badge('C')) b.SaveAsPng(Path.Combine(args[1], "BadgeC.png"));
        using (var b = PropGen.Badge('T')) b.SaveAsPng(Path.Combine(args[1], "BadgeT.png"));
        PropGen.ExportFont(args[1]);
        using (var b = PropGen.Badge('!', new SixLabors.ImageSharp.PixelFormats.Rgba32(152, 44, 40, 235)))
            b.SaveAsPng(Path.Combine(args[1], "BadgeAlert.png"));
        using (var b = PropGen.Badge('V', new SixLabors.ImageSharp.PixelFormats.Rgba32(42, 110, 52, 235)))
            b.SaveAsPng(Path.Combine(args[1], "BadgeDone.png"));
        Console.WriteLine("wrote BadgeE.png, BadgeQ.png, BadgeAlert.png, BadgeDone.png");
        return 0;
    }
    default:
        Console.WriteLine($"Unknown command: {args[0]}");
        return 1;
}
