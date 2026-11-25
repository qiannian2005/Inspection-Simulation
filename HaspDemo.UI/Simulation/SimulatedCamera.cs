using HalconDotNet;
using HaspDemo.Core.Entities;
using HaspDemo.Core.Enums;
using HaspDemo.Core.Interfaces;
using System.Runtime.InteropServices;

namespace HaspDemo.UI.Simulation;

public sealed class SimulatedCamera : ICameraService
{
    private readonly object _sync = new();
    private string[] _paths;
    private int _next;
    private DeviceStatus _status = DeviceStatus.Disconnected;

    public SimulatedCamera(IEnumerable<string> paths) => _paths = ValidatePaths(paths);
    public DeviceStatus Status { get { lock (_sync) return _status; } }
    public event Action<DeviceStatus>? StatusChanged;
    public event Action<ImageData>? FrameArrived;

    public void SetImages(IEnumerable<string> paths)
    {
        var validated = ValidatePaths(paths);
        lock (_sync) { _paths = validated; _next = 0; }
    }

    public async Task ConnectAsync()
    {
        string path;
        lock (_sync)
        {
            if (_status == DeviceStatus.Connected) return;
            path = _paths[0];
        }
        var image = await Task.Run(() => ReadImage(path));
        lock (_sync) { _next = 0; _status = DeviceStatus.Connected; }
        StatusChanged?.Invoke(DeviceStatus.Connected);
        FrameArrived?.Invoke(image);
    }

    public Task DisconnectAsync()
    {
        lock (_sync) _status = DeviceStatus.Disconnected;
        StatusChanged?.Invoke(DeviceStatus.Disconnected);
        return Task.CompletedTask;
    }

    public async Task<ImageData> GrabLatestAsync()
    {
        string path;
        lock (_sync)
        {
            if (_status != DeviceStatus.Connected)
                throw new InvalidOperationException("模拟相机未连接。");
            path = _paths[_next];
            _next = (_next + 1) % _paths.Length;
        }
        var image = await Task.Run(() => ReadImage(path));
        FrameArrived?.Invoke(image);
        return image;
    }

    public static ImageData ReadImage(string path)
    {
        HOperatorSet.ReadImage(out HObject source, path);
        using (source)
        {
            HOperatorSet.CountChannels(source, out HTuple channels);
            using (channels)
            {
                HObject gray;
                if (channels.I == 3)
                    HOperatorSet.Rgb1ToGray(source, out gray);
                else if (channels.I == 1)
                    HOperatorSet.CopyImage(source, out gray);
                else
                    throw new InvalidDataException("仅支持单通道或 RGB 图片。");

                using (gray)
                {
                    HOperatorSet.GetImagePointer1(gray, out HTuple pointer, out HTuple type,
                        out HTuple width, out HTuple height);
                    using (pointer)
                    using (type)
                    using (width)
                    using (height)
                    {
                        if (type.S != "byte")
                            throw new InvalidDataException("请使用 8 位灰度或 24 位 RGB 图片。");
                        var pixels = new byte[checked(width.I * height.I)];
                        Marshal.Copy(pointer.IP, pixels, 0, pixels.Length);
                        return new ImageData
                        {
                            Width = width.I, Height = height.I,
                            Pixels = pixels, GrabTime = DateTime.Now
                        };
                    }
                }
            }
        }
    }

    private static string[] ValidatePaths(IEnumerable<string> paths)
    {
        var result = paths.Select(Path.GetFullPath).ToArray();
        if (result.Length == 0) throw new ArgumentException("至少需要一张模拟图片。", nameof(paths));
        foreach (string path in result)
            if (!File.Exists(path)) throw new FileNotFoundException("模拟图片不存在。", path);
        return result;
    }
}
