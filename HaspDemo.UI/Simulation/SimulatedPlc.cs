using HaspDemo.Core.Enums;
using HaspDemo.Core.Interfaces;

namespace HaspDemo.UI.Simulation;

public sealed class SimulatedPlc : IPLCService
{
    private readonly object _sync = new();
    private bool _trigger;
    private ushort _productId;
    private DeviceStatus _status = DeviceStatus.Disconnected;

    public DeviceStatus Status { get { lock (_sync) return _status; } }
    public bool HasPendingTrigger { get { lock (_sync) return _trigger; } }
    public bool? LastResult { get; private set; }
    public short? LastErrorCode { get; private set; }
    public event Action<DeviceStatus>? StatusChanged;

    public Task ConnectAsync(string ip, ushort rack, ushort slot)
    {
        lock (_sync)
        {
            if (_status == DeviceStatus.Connected) return Task.CompletedTask;
            _status = DeviceStatus.Connected;
            _trigger = false;
        }
        StatusChanged?.Invoke(DeviceStatus.Connected);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        lock (_sync)
        {
            _status = DeviceStatus.Disconnected;
            _trigger = false;
        }
        StatusChanged?.Invoke(DeviceStatus.Disconnected);
        return Task.CompletedTask;
    }

    public bool TryTrigger()
    {
        lock (_sync)
        {
            if (_status != DeviceStatus.Connected || _trigger) return false;
            _productId = _productId == ushort.MaxValue ? (ushort)1 : (ushort)(_productId + 1);
            LastResult = null;
            LastErrorCode = null;
            _trigger = true;
            return true;
        }
    }

    public Task<bool> ReadTriggerAsync()
    {
        lock (_sync) { EnsureConnected(); return Task.FromResult(_trigger); }
    }

    public Task<ushort> ReadProductIdAsync()
    {
        lock (_sync) { EnsureConnected(); return Task.FromResult(_productId); }
    }

    public Task WriteResultAsync(bool isOk)
    {
        lock (_sync) { EnsureConnected(); LastResult = isOk; }
        return Task.CompletedTask;
    }

    public Task WriteErrorCodeAsync(short code)
    {
        lock (_sync) { EnsureConnected(); LastErrorCode = code; }
        return Task.CompletedTask;
    }

    public Task ResetTriggerAsync()
    {
        lock (_sync) { EnsureConnected(); _trigger = false; }
        return Task.CompletedTask;
    }

    public Task<bool> TryReconnectAsync(CancellationToken ct) =>
        Task.FromResult(!ct.IsCancellationRequested && Status == DeviceStatus.Connected);

    private void EnsureConnected()
    {
        if (_status != DeviceStatus.Connected)
            throw new InvalidOperationException("模拟 PLC 未连接。");
    }
}
