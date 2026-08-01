namespace ArtTool;

/// <summary>
/// Procedural audio: synthesizes every game sound as 16-bit mono 44.1kHz WAV.
/// The engine is a seamless 2s loop — the game sweeps RPM via pitch at runtime.
/// Loops (engine, skid, ambience) get a crossfaded seam so they cycle clean.
/// </summary>
public static class SoundGen
{
    private const int SR = 44100;

    public static void ExportAll(string outDir, int seed)
    {
        Directory.CreateDirectory(outDir);
        Write(outDir, "EngineLoop.wav", Loopify(Engine(), 2000));
        Write(outDir, "GearShift.wav", GearShift());
        Write(outDir, "Skid.wav", Loopify(Skid(), 2000));
        Write(outDir, "Plant.wav", Plant());
        Write(outDir, "BoxThud.wav", BoxThud());
        Write(outDir, "Blip.wav", Blip());
        Write(outDir, "Ambience.wav", Loopify(Ambience(seed), 4000));
        Console.WriteLine($"wrote 7 sounds to {outDir}");
    }

    // ---------- instruments ----------

    /// <summary>Quad engine at idle-ish RPM: saw stack + firing thump, soft-clipped.</summary>
    private static float[] Engine()
    {
        int n = SR * 2;
        var s = new float[n];
        var rng = new Random(7);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float x = 0.50f * Saw(55f, t)
                    + 0.26f * Saw(110f, t)
                    + 0.13f * Saw(220f, t)
                    + 0.07f * ((float)rng.NextDouble() * 2f - 1f);
            // single-cylinder firing thump at half the fundamental
            float th = 0.5f + 0.5f * MathF.Sin(MathF.Tau * 27.5f * t);
            x *= 0.55f + 0.45f * th * th;
            s[i] = MathF.Tanh(x * 1.7f) * 0.62f;
        }
        return s;
    }

    /// <summary>Clutch click, gear thud, second lighter click.</summary>
    private static float[] GearShift()
    {
        int n = (int)(SR * 0.32f);
        var s = new float[n];
        var rng = new Random(11);
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float x = 0;
            if (t < 0.012f) x += ((float)rng.NextDouble() * 2 - 1) * 0.5f;             // click
            if (t is > 0.03f and < 0.22f)
            {
                float tt = t - 0.03f;
                x += MathF.Sin(MathF.Tau * 72f * tt) * MathF.Exp(-tt * 22f) * 0.9f;    // thud
                float nz = ((float)rng.NextDouble() * 2 - 1);
                lp += 0.2f * (nz - lp);
                x += lp * MathF.Exp(-tt * 30f) * 0.5f;
            }
            if (t is > 0.14f and < 0.155f) x += ((float)rng.NextDouble() * 2 - 1) * 0.3f; // second click
            s[i] = x * 0.8f;
        }
        return s;
    }

    /// <summary>Dirt skid: band-ish noise with flutter.</summary>
    private static float[] Skid()
    {
        int n = SR;
        var s = new float[n];
        var rng = new Random(13);
        float lp1 = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float nz = (float)rng.NextDouble() * 2 - 1;
            lp1 += 0.16f * (nz - lp1);
            lp2 += 0.035f * (nz - lp2);
            float band = lp1 - lp2;
            float flutter = 0.62f + 0.38f * MathF.Sin(MathF.Tau * 13f * t);
            s[i] = band * flutter * 1.6f;
        }
        return s;
    }

    private static float[] Plant()
    {
        int n = (int)(SR * 0.11f);
        var s = new float[n];
        var rng = new Random(17);
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float nz = (float)rng.NextDouble() * 2 - 1;
            lp += 0.22f * (nz - lp);
            s[i] = lp * MathF.Exp(-t * 42f) * 0.9f
                 + MathF.Sin(MathF.Tau * 190f * t) * MathF.Exp(-t * 34f) * 0.3f;
        }
        return s;
    }

    private static float[] BoxThud()
    {
        int n = (int)(SR * 0.26f);
        var s = new float[n];
        var rng = new Random(19);
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float nz = (float)rng.NextDouble() * 2 - 1;
            lp += 0.12f * (nz - lp);
            s[i] = MathF.Sin(MathF.Tau * 85f * t) * MathF.Exp(-t * 17f) * 0.85f
                 + lp * MathF.Exp(-t * 50f) * 0.4f;
        }
        return s;
    }

    private static float[] Blip()
    {
        int n = (int)(SR * 0.12f);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float f = t < 0.06f ? 880f : 1174f;
            float env = MathF.Min(1f, t / 0.008f) * MathF.Exp(-t * 16f);
            s[i] = (Frac(f * t) < 0.5f ? 1f : -1f) * env * 0.30f;
        }
        return s;
    }

    /// <summary>Wind bed + sparse bird trills. 8 seconds, loops.</summary>
    private static float[] Ambience(int seed)
    {
        int n = SR * 8;
        var s = new float[n];
        var rng = new Random(seed * 7 + 23);
        float lp = 0;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float nz = (float)rng.NextDouble() * 2 - 1;
            lp += 0.02f * (nz - lp);
            float gust = 0.55f + 0.30f * MathF.Sin(MathF.Tau * 0.25f * t)
                       + 0.15f * MathF.Sin(MathF.Tau * 0.125f * t + 1.3f);
            s[i] = lp * gust * 1.1f;
        }

        // birds: a dozen short trills at seeded times
        for (int b = 0; b < 12; b++)
        {
            float t0 = (float)rng.NextDouble() * 7.4f;
            float dur = 0.14f + (float)rng.NextDouble() * 0.12f;
            float baseF = 2500f + (float)rng.NextDouble() * 1100f;
            int i0 = (int)(t0 * SR), len = (int)(dur * SR);
            for (int i = 0; i < len && i0 + i < n; i++)
            {
                float tt = i / (float)SR;
                float f = baseF + 320f * MathF.Sin(MathF.Tau * 27f * tt);
                float env = MathF.Sin(MathF.PI * tt / dur);
                s[i0 + i] += MathF.Sin(MathF.Tau * f * tt) * env * env * 0.16f;
            }
        }
        return s;
    }

    // ---------- helpers ----------

    private static float Saw(float f, float t) => 2f * Frac(f * t) - 1f;
    private static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>Crossfade the tail into the head so the loop seam is silent.</summary>
    private static float[] Loopify(float[] s, int fadeSamples)
    {
        int n = s.Length;
        var o = new float[n - fadeSamples];
        for (int i = 0; i < o.Length; i++) o[i] = s[i + fadeSamples];
        for (int i = 0; i < fadeSamples; i++)
        {
            float k = i / (float)fadeSamples;
            o[o.Length - fadeSamples + i] = o[o.Length - fadeSamples + i] * (1 - k) + s[i] * k;
        }
        return o;
    }

    private static void Write(string dir, string name, float[] data)
    {
        using var fs = File.Create(Path.Combine(dir, name));
        using var bw = new BinaryWriter(fs);
        int byteLen = data.Length * 2;
        bw.Write("RIFF"u8); bw.Write(36 + byteLen); bw.Write("WAVE"u8);
        bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(SR); bw.Write(SR * 2); bw.Write((short)2); bw.Write((short)16);
        bw.Write("data"u8); bw.Write(byteLen);
        foreach (float v in data)
            bw.Write((short)(Math.Clamp(v, -1f, 1f) * short.MaxValue));
    }
}
