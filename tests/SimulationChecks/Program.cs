using HaspDemo.Applicationsss;
using HaspDemo.Core.Entities;
using HaspDemo.Core.Interfaces;
using HaspDemo.Persistence;
using HaspDemo.UI;
using HaspDemo.UI.Simulation;
using HaspDemo.Vision;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass a fresh temporary output directory.");
            string root = Path.GetFullPath(args[0]);
            if (Directory.Exists(root)) throw new ArgumentException("Output directory must not exist.");
            var assets = new SimulationAssets(root);
            assets.EnsureCreated();
            RunPipelineChecks(assets).GetAwaiter().GetResult();
            ApplicationConfiguration.Initialize();
            RunUiChecks(new SimulationAssets(Path.Combine(root, "ui")));
            Console.WriteLine("PASS: all simulation checks completed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task RunPipelineChecks(SimulationAssets assets)
    {
        using var inspector = assets.CreateInspector();
        Check(inspector.ModelReady, "real HALCON model created and loaded");
        var camera = new SimulatedCamera(assets.SamplePaths);
        var plc = new SimulatedPlc();
        var log = new TestLog();
        var repository = new JsonRecordRepository(assets.RecordPath);
        var service = new DetectionService(camera, plc, inspector, repository, log, inspector);
        var results = new ConcurrentQueue<(InspectionResult Result, ushort Id, string Path)>();
        service.DetectionCompleted += (r, id, path) => results.Enqueue((r, id, path));
        try
        {
            Check(!plc.TryTrigger(), "disconnected PLC rejects triggers");
            service.StartPolling("simulation", 0, 0);
            Check(!service.IsRunning, "disconnected start rejected");
            await camera.ConnectAsync();
            await plc.ConnectAsync("simulation", 0, 0);
            service.StartPolling("simulation", 0, 0);
            service.StartPolling("simulation", 0, 0);
            Check(service.IsRunning, "polling started");
            short[] expectedCodes = { 0, 3, 1, 0 };
            for (int i = 0; i < expectedCodes.Length; i++)
            {
                Check(plc.TryTrigger(), $"trigger {i + 1} accepted");
                Check(!plc.TryTrigger(), "duplicate pending trigger rejected");
                int expectedCount = i + 1;
                await WaitUntil(() => results.Count == expectedCount && !plc.HasPendingTrigger);
                var result = results.Last();
                Console.WriteLine(string.Join(" | ", result.Result.Items.Select(x => $"{x.Name}: {x.Value} ({x.IsOk})")));
                Check(plc.LastErrorCode == expectedCodes[i], $"real HALCON expected code {expectedCodes[i]}, actual {plc.LastErrorCode}");
                Check(plc.LastResult == (expectedCodes[i] == 0), "PLC result matches algorithm");
                Check(result.Id == expectedCount && File.Exists(result.Path), "product ID and saved image");
            }
            await service.StopPollingAsync();
            Check(!service.IsRunning, "stop awaited");
            int count = results.Count;
            await Task.Delay(250);
            Check(results.Count == count, "no work after stop");
            Check(File.ReadAllLines(assets.RecordPath).Length == 4, "four JSON records persisted");
            foreach (var line in File.ReadAllLines(assets.RecordPath))
                Check(JsonSerializer.Deserialize<InspectRecord>(line)!.ImagePath.Length > 0, "record image path");

            string broken = Path.Combine(assets.Root, "broken.png");
            await File.WriteAllTextAsync(broken, "not an image");
            camera.SetImages(new[] { broken });
            service.StartPolling("simulation", 0, 0);
            Check(plc.TryTrigger(), "restart trigger accepted");
            await WaitUntil(() => !plc.HasPendingTrigger);
            Check(plc.LastErrorCode == 99 && plc.LastResult == false, "bad image returns error 99 and resets trigger");
            Check(results.Count == count, "bad image does not emit a successful inspection");
            await service.StopPollingAsync();
            camera.SetImages(assets.SamplePaths);
            service.StartPolling("simulation", 0, 0);
            Check(plc.TryTrigger(), "recovery trigger accepted");
            await WaitUntil(() => results.Count == count + 1 && !plc.HasPendingTrigger);
            Check(plc.LastErrorCode == 0, "pipeline recovers after bad image");
            await service.StopPollingAsync();

            var blocking = new BlockingAlgorithm();
            var cancellationService = new DetectionService(camera, plc, blocking, repository, log, inspector);
            cancellationService.StartPolling("simulation", 0, 0);
            Check(plc.TryTrigger(), "cancellation trigger accepted");
            await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cancellationService.StopPollingAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(!cancellationService.IsRunning && plc.LastErrorCode == null, "cancellation is not reported as error 99");
            await plc.ResetTriggerAsync();

            using var missing = new HalconWasherInspector(Path.Combine(assets.Root, "absent.shm"), assets.ImageDirectory);
            var missingService = new DetectionService(camera, plc, missing, repository, log, missing);
            missingService.StartPolling("simulation", 0, 0);
            Check(!missingService.IsRunning, "missing template still blocks start");

            string rgbPath = Path.Combine(assets.Root, "odd-width.png");
            using (var rgb = new Bitmap(101, 37))
            {
                using var graphics = Graphics.FromImage(rgb);
                graphics.Clear(Color.Red);
                rgb.Save(rgbPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            var mono = SimulatedCamera.ReadImage(rgbPath);
            Check(mono.Width == 101 && mono.Height == 37 && mono.Pixels.Length == 3737, "RGB image converted to Mono8");
        }
        finally
        {
            await service.StopPollingAsync();
            await camera.DisconnectAsync();
            await plc.DisconnectAsync();
        }
    }

    private static void RunUiChecks(SimulationAssets assets)
    {
        assets.EnsureCreated();
        using var inspector = assets.CreateInspector();
        var camera = new SimulatedCamera(assets.SamplePaths);
        var plc = new SimulatedPlc();
        var log = new TestLog(assets.Root);
        var service = new DetectionService(camera, plc, inspector, new JsonRecordRepository(assets.RecordPath), log, inspector);
        using var form = new MainForm(service, log, assets);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                Button Button(string name) => (Button)form.Controls.Find(name, true).Single();
                await WaitUntil(() => Button("btnStart").Enabled);
                Check(!Button("btnSimulateTrigger").Enabled, "manual trigger disabled before start");
                var picture = (PictureBox)form.Controls.Find("picPreview", true).Single();
                await WaitUntil(() => picture.Image != null);
                Check(picture.Image!.Width == 1800, "initial preview loaded");
                var conversion = typeof(MainForm).GetMethod("ToBitmap", BindingFlags.NonPublic | BindingFlags.Instance)!;
                using var odd = (Bitmap)conversion.Invoke(form, new object[] {
                    new ImageData { Width = 3, Height = 2, Pixels = new byte[] { 0, 50, 100, 150, 200, 250 } }
                })!;
                Check(odd.GetPixel(0, 1).R == 150 && odd.GetPixel(2, 1).R == 250, "odd-width preview respects bitmap stride");

                Button("btnStart").PerformClick();
                Button("btnSimulateTrigger").PerformClick();
                var resultLabel = (Label)form.Controls.Find("lbResult", true).Single();
                await WaitUntil(() => resultLabel.Text == "OK" && !plc.HasPendingTrigger);
                var grid = (DataGridView)form.Controls.Find("dgvRecord", true).Single();
                Check(grid.Rows.Count == 3, "UI displays three actual algorithm checks");
                var topLevel = form.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                for (int i = 0; i < topLevel.Length; i++)
                for (int j = i + 1; j < topLevel.Length; j++)
                    Check(!topLevel[i].Bounds.IntersectsWith(topLevel[j].Bounds),
                        $"UI non-overlap: {topLevel[i].Name} {topLevel[i].Bounds} / {topLevel[j].Name} {topLevel[j].Bounds}");
                CaptureForm(form, assets.Root, "simulation-ok.png");
                for (int productId = 2; productId <= 3; productId++)
                {
                    Button("btnSimulateTrigger").PerformClick();
                    var timeLabel = (Label)form.Controls.Find("lbTime", true).Single();
                    await WaitUntil(() => timeLabel.Text.StartsWith($"ProductID: {productId} ") && !plc.HasPendingTrigger);
                    Check(resultLabel.Text == "NG", "documented NG result is visible");
                    await Task.Delay(100);
                    CaptureForm(form, assets.Root, productId == 2 ? "simulation-scratch.png" : "simulation-missing.png");
                }
                Button("btnStop").PerformClick();
                await WaitUntil(() => Button("btnStart").Enabled);
                Check(!Button("btnSimulateTrigger").Enabled, "trigger disabled after stop");
                Button("btnStart").PerformClick();
                var auto = form.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<CheckBox>().Single();
                auto.Checked = true;
                await WaitUntil(() => File.ReadAllLines(assets.RecordPath).Length >= 5 && !plc.HasPendingTrigger);
                auto.Checked = false;
                Button("btnDisconnectPLC").PerformClick();
                await WaitUntil(() => Button("btnConnectPLC").Enabled);
                Check(!service.IsRunning && !Button("btnStart").Enabled, "disconnect stops detection");
                Button("btnConnectPLC").PerformClick();
                await WaitUntil(() => Button("btnStart").Enabled);
                Button("btnStart").PerformClick();
                Button("btnSimulateTrigger").PerformClick();
                Check(!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m =>
                    m.ModuleName.Contains("MvCamera", StringComparison.OrdinalIgnoreCase)), "simulation has not loaded camera SDK");
            }
            catch (Exception ex) { failure = ex; }
            finally { form.Close(); }
        };
        Application.Run(form);
        Check(!service.IsRunning, "window close waits for detection shutdown");
        if (failure != null) throw new Exception("UI check failed", failure);
    }

    private static void CaptureForm(Form form, string directory, string fileName)
    {
        using var screenshot = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
        screenshot.Save(Path.Combine(directory, fileName), System.Drawing.Imaging.ImageFormat.Png);
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Condition did not become true.");
            await Task.Delay(25);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private sealed class TestLog : ILogService
    {
        private readonly string? _dataRoot;
        public TestLog(string? dataRoot = null) => _dataRoot = dataRoot;
        public event Action<string>? OnLog;
        private void Publish(string msg)
        {
            Console.WriteLine(msg);
            OnLog?.Invoke(_dataRoot == null ? msg : msg.Replace(_dataRoot, "SimulationData"));
        }
        public void Info(string msg) => Publish(msg);
        public void Warn(string msg) => Publish(msg);
        public void Error(string msg, Exception? ex = null) => Publish(msg + " " + ex?.Message);
    }

    private sealed class BlockingAlgorithm : IDetectAlgorithm
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<InspectionResult> DetectAsync(ImageData image, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new InspectionResult();
        }
    }
}
