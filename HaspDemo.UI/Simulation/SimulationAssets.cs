using HalconDotNet;
using HaspDemo.Core.Entities;
using HaspDemo.Vision;

namespace HaspDemo.UI.Simulation;

public sealed class SimulationAssets
{
    public string Root { get; }
    public string ModelPath => Path.Combine(Root, "washer_model.shm");
    public string ImageDirectory => Path.Combine(Root, "Images");
    public string RecordPath => Path.Combine(Root, "records.jsonl");
    public string[] SamplePaths => new[] { "01-ok.png", "02-scratch.png", "03-missing.png" }
        .Select(name => Path.Combine(Root, "Samples", name)).ToArray();

    public SimulationAssets(string root) => Root = Path.GetFullPath(root);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.Combine(Root, "Samples"));
        if (!File.Exists(ModelPath))
        {
            using var template = HalconImageHelper.ToHObject(CreateImage(800, 800, 1, false));
            HOperatorSet.CreateShapeModel(template, "auto", -Math.PI, 2 * Math.PI,
                "auto", "auto", "use_polarity", 30, 10, out HTuple model);
            try { HOperatorSet.WriteShapeModel(model, ModelPath); }
            finally { HOperatorSet.ClearShapeModel(model); model.Dispose(); }
        }

        var paths = SamplePaths;
        for (int i = 0; i < paths.Length; i++)
        {
            if (File.Exists(paths[i])) continue;
            using var image = HalconImageHelper.ToHObject(CreateImage(1800, 1800, i == 2 ? 3 : 4, i == 1));
            HOperatorSet.WriteImage(image, "png", 0, paths[i]);
        }
    }

    public HalconWasherInspector CreateInspector() => new(ModelPath, ImageDirectory)
    {
        // Demonstration annulus area: pi * (335^2 - 195^2), not the production preset.
        WasherAreaMin = 200000,
        WasherAreaMax = 260000,
        MinMatchScore = 0.5,
        MeanWindowSize = 31
    };

    private static ImageData CreateImage(int width, int height, int count, bool scratch)
    {
        var pixels = new byte[width * height];
        Array.Fill(pixels, (byte)255);
        for (int i = 0; i < count; i++)
        {
            int cx = width == 800 ? 400 : 450 + (i % 2) * 900;
            int cy = height == 800 ? 400 : 450 + (i / 2) * 900;
            for (int y = cy - 335; y <= cy + 335; y++)
            for (int x = cx - 335; x <= cx + 335; x++)
            {
                int distance = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (distance <= 335 * 335 && distance >= 195 * 195)
                    pixels[y * width + x] = 160;
            }
        }
        if (scratch)
            for (int y = 444; y <= 455; y++)
            for (int x = 665; x <= 755; x++)
                pixels[y * width + x] = 40;

        return new ImageData { Width = width, Height = height, Pixels = pixels, GrabTime = DateTime.Now };
    }
}
