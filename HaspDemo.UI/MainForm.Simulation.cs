using HaspDemo.Core.Enums;
using HaspDemo.UI.Simulation;

namespace HaspDemo.UI;

public partial class MainForm
{
    private readonly SimulationAssets? _simulation;
    private readonly Button _triggerButton = new() { Text = "模拟来料", Name = "btnSimulateTrigger" };
    private readonly Button _imagesButton = new() { Text = "选择图片", Name = "btnSelectImages" };
    private readonly Button _demoButton = new() { Text = "恢复演示图片", Name = "btnRestoreDemo" };
    private readonly CheckBox _autoTrigger = new() { Text = "自动来料（每 2 秒）", AutoSize = true };
    private readonly System.Windows.Forms.Timer _simulationTimer = new() { Interval = 2000 };
    private bool _closing;
    private bool _allowClose;
    private bool _deviceBusy;
    private bool _stopping;
    private Task _deviceTask = Task.CompletedTask;
    private GroupBox? _simulationGroup;

    private void InitializeSimulationControls()
    {
        if (_simulation == null) return;
        Text = "垫圈视觉检测 | 模拟设备 / HALCON 真实检测";
        var group = new GroupBox { Text = "模拟控制", Name = "grpSimulation" };
        _simulationGroup = group;
        group.Controls.AddRange(new Control[] { _triggerButton, _imagesButton, _demoButton, _autoTrigger });
        Controls.Add(group);
        Layout += (_, _) => LayoutSimulationControls();
        Load += (_, _) => LayoutSimulationControls();
        LayoutSimulationControls();
        _triggerButton.Click += (_, _) => TriggerSimulation(true);
        _imagesButton.Click += SelectSimulationImages;
        _demoButton.Click += (_, _) =>
        {
            ((SimulatedCamera)_detection.Camera).SetImages(_simulation.SamplePaths);
            _log.Info("已恢复演示图片：合格、划痕、缺件。");
        };
        _autoTrigger.CheckedChanged += (_, _) => RefreshUI();
        _simulationTimer.Tick += (_, _) => TriggerSimulation(false);
    }

    private void LayoutSimulationControls()
    {
        if (_simulationGroup == null) return;
        int gap = Math.Max(8, grpDevices.Left);
        int top = grpDevices.Bottom + gap;
        _simulationGroup.Bounds = new Rectangle(grpDevices.Left, top, grpDevices.Width,
            Math.Max(160, grpDetail.Top - top - gap));
        int padding = Math.Max(12, gap);
        int rowHeight = Math.Max(30, (_simulationGroup.Height - Font.Height - 4 * gap) / 4);
        var buttons = new[] { _triggerButton, _imagesButton, _demoButton };
        for (int i = 0; i < buttons.Length; i++)
            buttons[i].Bounds = new Rectangle(padding, Font.Height + gap + i * (rowHeight + gap),
                _simulationGroup.Width - 2 * padding, rowHeight);
        _autoTrigger.Location = new Point(padding, _demoButton.Bottom + gap);
    }

    private void RefreshSimulationControls()
    {
        if (_simulation == null) return;
        bool running = _detection.IsRunning && !_closing && !_stopping && !_deviceBusy;
        _triggerButton.Enabled = running && !_autoTrigger.Checked;
        _imagesButton.Enabled = !_detection.IsRunning && !_deviceBusy && !_closing;
        _demoButton.Enabled = _imagesButton.Enabled;
        _autoTrigger.Enabled = !_closing && !_stopping;
        _simulationTimer.Enabled = running && _autoTrigger.Checked;
    }

    private void TriggerSimulation(bool reportBusy)
    {
        if (!_detection.IsRunning || _closing || _stopping || _deviceBusy) return;
        if (!((SimulatedPlc)_detection.PLC).TryTrigger() && reportBusy)
            _log.Warn("上一件尚未完成，请等待检测及信号复位。");
    }

    private void SelectSimulationImages(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "图片|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff",
            Multiselect = true,
            InitialDirectory = Path.Combine(_simulation!.Root, "Samples")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            ((SimulatedCamera)_detection.Camera).SetImages(dialog.FileNames);
            _log.Info($"已选择 {dialog.FileNames.Length} 张图片。下次来料开始依次检测，沿用当前模板及参数。");
        }
        catch (Exception ex) { _log.Error("图片选择失败", ex); }
    }

    private void OnDeviceStatusChanged(DeviceStatus _) => PostUI(RefreshUI);
    private void OnRunningChanged() => PostUI(RefreshUI);

    private void PostUI(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (!_closing && !IsDisposed) action();
            }));
        }
        catch (InvalidOperationException) when (_closing || IsDisposed || !IsHandleCreated) { }
    }

    private async Task RunDeviceActionAsync(Func<Task> action, string description)
    {
        if (_deviceBusy || _closing) return;
        _deviceBusy = true;
        RefreshUI();
        try
        {
            _deviceTask = action();
            await _deviceTask;
            _log.Info(description);
        }
        catch (Exception ex) { _log.Error(description + "失败", ex); }
        finally { _deviceBusy = false; if (!_closing) RefreshUI(); }
    }

    private async Task StopDetectionAsync()
    {
        _stopping = true;
        _simulationTimer.Stop();
        RefreshUI();
        try
        {
            await _detection.StopPollingAsync();
            if (_detection.PLC is SimulatedPlc plc && plc.Status == DeviceStatus.Connected)
                await plc.ResetTriggerAsync();
        }
        finally { _stopping = false; if (!_closing) RefreshUI(); }
    }
}
