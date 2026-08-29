namespace ArtTool;

/// <summary>Deterministic hash noise + periodic (tileable) value noise.</summary>
public static class Noise
{
    /// <summary>Deterministic integer hash → [0,1).</summary>
    public static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>Bilinear value noise on an integer lattice, wrapping at `period` cells (tileable).</summary>
    public static float Value(float x, float y, int seed, int period)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        // smoothstep
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);

        int X0 = Mod(x0, period), X1 = Mod(x0 + 1, period);
        int Y0 = Mod(y0, period), Y1 = Mod(y0 + 1, period);

        float v00 = Hash(X0, Y0, seed), v10 = Hash(X1, Y0, seed);
        float v01 = Hash(X0, Y1, seed), v11 = Hash(X1, Y1, seed);
        return (v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy;
    }

    /// <summary>Fractal (octaved) tileable value noise → [0,1].</summary>
    public static float Fbm(float px, float py, int seed, int octaves, float cellSize, int periodPx)
    {
        float sum = 0, amp = 1, norm = 0;
        float freq = 1f / cellSize;
        int period = Math.Max(1, (int)(periodPx / cellSize));
        for (int o = 0; o < octaves; o++)
        {
            sum += Value(px * freq, py * freq, seed + o * 101, period) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
            period *= 2;
        }
        return sum / norm;
    }

    /// <summary>1D smooth noise (for wavy edges), period in samples.</summary>
    public static float Value1D(float x, int seed, int period) => Value(x, 0.37f, seed, period);

    public static int Mod(int a, int m) => ((a % m) + m) % m;
}
