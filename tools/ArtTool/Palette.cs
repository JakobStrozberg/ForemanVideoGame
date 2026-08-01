using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ArtTool;

/// <summary>
/// Median-cut palette extraction across a directory of images.
/// Output: JSON { "colors": ["#RRGGBB", ...] } — the master palette every
/// ingested asset gets quantized against so mixed sources read as one game.
/// </summary>
public static class Palette
{
    public static void Extract(string inputDir, string outPath, int maxColors)
    {
        var samples = new List<Rgba32>();
        var files = Directory.EnumerateFiles(inputDir)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in files)
        {
            using var img = Image.Load<Rgba32>(file);
            // Sample a grid of pixels — full scan is unnecessary for palette work
            int step = Math.Max(1, Math.Max(img.Width, img.Height) / 256);
            for (int y = 0; y < img.Height; y += step)
                for (int x = 0; x < img.Width; x += step)
                {
                    var p = img[x, y];
                    if (p.A > 200) samples.Add(p);
                }
            Console.WriteLine($"sampled {file}");
        }

        var palette = MedianCut(samples, maxColors);
        var json = JsonSerializer.Serialize(new
        {
            colors = palette.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}").ToArray()
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outPath, json);
        Console.WriteLine($"wrote {palette.Count} colors to {outPath} (from {files.Count} images, {samples.Count} samples)");
    }

    private static List<Rgba32> MedianCut(List<Rgba32> pixels, int target)
    {
        var boxes = new List<List<Rgba32>> { pixels };
        while (boxes.Count < target)
        {
            // Split the box with the largest channel range
            var (box, channel) = boxes
                .Select(b => (b, ch: WidestChannel(b)))
                .OrderByDescending(t => t.ch.range)
                .First() is var pick ? (pick.b, pick.ch.channel) : default;

            if (box.Count < 2) break;

            var sorted = channel switch
            {
                0 => box.OrderBy(p => p.R).ToList(),
                1 => box.OrderBy(p => p.G).ToList(),
                _ => box.OrderBy(p => p.B).ToList()
            };
            int mid = sorted.Count / 2;
            boxes.Remove(box);
            boxes.Add(sorted.Take(mid).ToList());
            boxes.Add(sorted.Skip(mid).ToList());
        }

        return boxes
            .Where(b => b.Count > 0)
            .Select(b => new Rgba32(
                (byte)b.Average(p => (double)p.R),
                (byte)b.Average(p => (double)p.G),
                (byte)b.Average(p => (double)p.B),
                255))
            .ToList();
    }

    private static (int channel, int range) WidestChannel(List<Rgba32> box)
    {
        if (box.Count == 0) return (0, -1);
        int rr = box.Max(p => p.R) - box.Min(p => p.R);
        int gr = box.Max(p => p.G) - box.Min(p => p.G);
        int br = box.Max(p => p.B) - box.Min(p => p.B);
        if (rr >= gr && rr >= br) return (0, rr);
        if (gr >= br) return (1, gr);
        return (2, br);
    }
}
