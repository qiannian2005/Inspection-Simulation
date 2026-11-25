using HaspDemo.Applicationsss;
using HaspDemo.Core.Interfaces;
using HaspDemo.Devices;
using HaspDemo.Persistence;
using HaspDemo.UI.Simulation;
using HaspDemo.Vision;
using Microsoft.Extensions.DependencyInjection;
using MvCameraControl;
using System.Runtime.CompilerServices;

namespace HaspDemo.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool simulation = args.Contains("--simulate", StringComparer.OrdinalIgnoreCase);
        bool cameraSdkInitialized = false;
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILogService, FileLogService>();
            SimulationAssets? assets = null;
            if (simulation)
            {
                assets = new SimulationAssets(Path.Combine(AppContext.BaseDirectory, "SimulationData"));
                assets.EnsureCreated();
                services.AddSingleton<ICameraService>(new SimulatedCamera(assets.SamplePaths));
                services.AddSingleton<IPLCService, SimulatedPlc>();
                services.AddSingleton<HalconWasherInspector>(_ => assets.CreateInspector());
                services.AddSingleton<IDetectRecordRepository>(new JsonRecordRepository(assets.RecordPath));
            }
            else
            {
                InitializeCameraSdk();
                cameraSdkInitialized = true;
                RegisterHardware(services);
            }
            services.AddSingleton<IDetectAlgorithm>(sp => sp.GetRequiredService<HalconWasherInspector>());
            services.AddSingleton<DetectionService>();
            using var provider = services.BuildServiceProvider();
            Application.Run(new MainForm(provider.GetRequiredService<DetectionService>(),
                provider.GetRequiredService<ILogService>(), assets));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), simulation ? "模拟启动失败" : "启动失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (cameraSdkInitialized) FinalizeCameraSdk();
        }
    }

    // Keep vendor SDK loading out of the simulation startup path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InitializeCameraSdk() => SDKSystem.Initialize();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FinalizeCameraSdk() => SDKSystem.Finalize();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterHardware(IServiceCollection services)
    {
        services.AddSingleton<ICameraService, HikCamera>();
        services.AddSingleton<IPLCService, S7Plc>();
        services.AddSingleton<HalconWasherInspector>();
        services.AddSingleton<IDetectRecordRepository>(new InspectRecordRepository(
            "Data Source=WIN-3LH90C0IBON;Initial Catalog=HalconProject;Integrated Security=True;TrustServerCertificate=True;"));
    }
}
