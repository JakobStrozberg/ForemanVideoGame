using System;
using System.IO;
using System.Text.Json;

namespace Crewboss.Core;

/// <summary>
/// Live-tunable feel values, loaded from Content/tweaks.json. Press F5 in-game
/// to reload (along with regenerated art) — no restart. Missing file or missing
/// keys fall back to these defaults. The loader prefers the SOURCE Content dir
/// when running from bin/, so edits apply without a rebuild-copy.
/// </summary>
public static class Tweaks
{
    public static float CameraZoom = 1.3f;
    public static float TreeScale = 2.0f;          // tree sprite size multiplier
    public static float FootSpeed = 62f;
    public static float ShiftTime = 1.5f;
    public static float TurnRate = 0.6f;           // steering speed multiplier (1 = original)
    public static float DustMinSpeed = 210f;
    public static float[] GearMax = { 95, 95, 150, 205, 280, 380 };
    public static float[] GearAccel = { 300, 320, 250, 240, 190, 165 };
    public static float[] GearTorque = { 1f, 1f, 0.60f, 0.52f, 0.35f, 0.20f };

    /// <summary>
    /// The live Content root: the SOURCE dir when developing (so hot reload sees
    /// every ArtTool regen instantly, no rebuild-copy), else the output copy.
    /// </summary>
    public static string ContentRoot()
    {
        // walk up from the binary looking for the repo: <root>/game/Content
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "game", "Content");
            if (File.Exists(Path.Combine(candidate, "tweaks.json"))) return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "Content");
    }

    /// <summary>Repo root when developing (ContentRoot is &lt;root&gt;/game/Content), else null.</summary>
    public static string RepoRoot()
    {
        string root = Path.GetFullPath(Path.Combine(ContentRoot(), "..", ".."));
        return File.Exists(Path.Combine(root, "Crewboss.sln")) ? root : null;
    }

    public static string FindPath() => Path.Combine(ContentRoot(), "tweaks.json");

    public static void Load()
    {
        try
        {
            string path = FindPath();
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;

            float F(string name, float cur) =>
                r.TryGetProperty(name, out var el) ? (float)el.GetDouble() : cur;
            float[] A(string name, float[] cur)
            {
                if (!r.TryGetProperty(name, out var el)) return cur;
                var list = new System.Collections.Generic.List<float>();
                foreach (var v in el.EnumerateArray()) list.Add((float)v.GetDouble());
                return list.Count == cur.Length ? list.ToArray() : cur;
            }

            CameraZoom = F("cameraZoom", CameraZoom);
            TreeScale = F("treeScale", TreeScale);
            FootSpeed = F("footSpeed", FootSpeed);
            ShiftTime = F("shiftTime", ShiftTime);
            TurnRate = F("turnRate", TurnRate);
            DustMinSpeed = F("dustMinSpeed", DustMinSpeed);
            GearMax = A("gearMax", GearMax);
            GearAccel = A("gearAccel", GearAccel);
            GearTorque = A("gearTorque", GearTorque);
            Console.WriteLine($"Tweaks loaded from {path}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Tweaks load failed (using current values): {e.Message}");
        }
    }
}
