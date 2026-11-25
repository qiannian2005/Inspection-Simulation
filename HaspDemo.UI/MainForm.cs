            // ★ 原来是 Applicationsss，拼写错了
using HaspDemo.Applicationsss;
using HaspDemo.Core.Entities;
using HaspDemo.Core.Enums;
using HaspDemo.Core.Interfaces;
using HaspDemo.UI.Simulation;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HaspDemo.UI
{
    public partial class MainForm : Form
    {
        private readonly DetectionService _detection;
        private readonly ILogService _log;

        // ===== PLC 连接参数：按你的实际PLC改 =====
        private const string PlcIp = "192.168.231.129";
        private const ushort PlcRack = 0;
        private const ushort PlcSlot = 1;

        public MainForm() { InitializeComponent(); }

        public MainForm(DetectionService detection, ILogService log, SimulationAssets? simulation = null) : this()
        {
            _detection = detection;
            _log = log;
            _simulation = simulation;
            InitializeSimulationControls();
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            _log.OnLog += AppendLog;
            _detection.Camera.StatusChanged += OnDeviceStatusChanged;
            _detection.PLC.StatusChanged += OnDeviceStatusChanged;
            _detection.RunningChanged += OnRunningChanged;
            _detection.Camera.FrameArrived += OnFrameArrived;
            _detection.DetectionCompleted += OnDetectionCompleted;

            RefreshUI();
            if (!_detection.ModelReady)
            {
                Text += " | 缺少模板，检测已禁用";
                _log.Warn("缺少 Models/washer_model.shm，请放入有效模板后重新启动。");
            }
            _log.Info("系统启动完成");
            if (_simulation != null)
            {
                _log.Info($"模拟模式，真实 HALCON 检测；数据目录：{_simulation.Root}");
                await RunDeviceActionAsync(() => _detection.Camera.ConnectAsync(), "模拟相机已连接");
                await RunDeviceActionAsync(() => _detection.PLC.ConnectAsync("simulation", 0, 0), "模拟 PLC 已连接");
            }
        }

        private void AppendLog(string msg)
        {
            if (_closing || IsDisposed) return;
            if (txtLog.InvokeRequired)//表示控件不在主线程
            {
                PostUI(() => AppendLog(msg));
                return;
            }
            txtLog.AppendText(msg + Environment.NewLine);
        }

        /// <summary>所有按钮灰/亮的唯一真相源</summary>
        private void RefreshUI()
        {
            bool cam = _detection.CameraStatus == DeviceStatus.Connected;
            bool plc = _detection.PLCStatus == DeviceStatus.Connected;

            lblCameraStatus.Text = "相机：" + (cam ? "已连接" : "未连接");
            lblCameraStatus.ForeColor = cam ? Color.Green : Color.Red;
            lblPLCStatus.Text = "PLC：" + (plc ? "已连接" : "未连接");
            lblPLCStatus.ForeColor = plc ? Color.Green : Color.Red;

            bool available = !_deviceBusy && !_stopping && !_closing;
            btnConnectCamera.Enabled = !cam && available;
            btnDisconnectCamera.Enabled = cam && available;
            btnConnectPLC.Enabled = !plc && available;
            btnDisconnectPLC.Enabled = plc && available;
            btnStart.Enabled = _detection.ModelReady && cam && plc && !_detection.IsRunning && available;
            btnStop.Enabled = _detection.IsRunning && available;
            RefreshSimulationControls();
        }

        /// <summary>相机实时预览（后台线程来，要切UI线程）</summary>
        private void OnFrameArrived(ImageData img)
        {
            PostUI(() =>
            {
                picPreview.Image?.Dispose();
                picPreview.Image = ToBitmap(img);
            });
        }

        /// <summary>Mono8 byte[] → Bitmap（UI显示用）</summary>
        private Bitmap ToBitmap(ImageData d)
        {
            var bmp = new Bitmap(d.Width, d.Height, PixelFormat.Format8bppIndexed);
            var palette = bmp.Palette;
            for (int i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = palette;

            var rect = new Rectangle(0, 0, d.Width, d.Height);
            var bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try
            {
                for (int row = 0; row < d.Height; row++)
                    Marshal.Copy(d.Pixels, row * d.Width, IntPtr.Add(bd.Scan0, row * bd.Stride), d.Width);
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        /// <summary>每帧检测结果刷新（来自后台线程）</summary>
        private void OnDetectionCompleted(InspectionResult result, ushort productId, string imagePath)
        {
            PostUI(() =>
            {
                bool ok = result.IsAllOk;
                lbResult.Text = ok ? "OK" : "NG";
                lbResult.ForeColor = ok ? Color.LimeGreen : Color.Red;
                lbTime.Text = $"ProductID: {productId}   耗时: {result.ElapseMs} ms";   // ★ ElapsedMs

                dgvRecord.Rows.Clear();
                foreach (var item in result.Items)
                {
                    int i = dgvRecord.Rows.Add();
                    dgvRecord.Rows[i].Cells[0].Value = item.Name;
                    dgvRecord.Rows[i].Cells[1].Value = item.Value;
                    dgvRecord.Rows[i].Cells[2].Value = item.IsOk ? "√ OK" : "× NG";
                    if (!item.IsOk)
                        dgvRecord.Rows[i].DefaultCellStyle.BackColor = Color.MistyRose;
                }
            });
        }

        // ==================== 相机 ====================

        private async void btnConnectCamera_Click(object sender, EventArgs e)
        {
            await RunDeviceActionAsync(() => _detection.Camera.ConnectAsync(), "相机已连接");
        }

        private async void btnDisconnectCamera_Click(object sender, EventArgs e)
        {
            await RunDeviceActionAsync(async () =>
            {
                await StopDetectionAsync();
                await _detection.Camera.DisconnectAsync();
            }, "相机已断开");
        }

        private async void btnConnectPLC_Click(object sender, EventArgs e)
        {
            await RunDeviceActionAsync(() => _detection.PLC.ConnectAsync(
                _simulation == null ? PlcIp : "simulation", PlcRack, PlcSlot), "PLC 已连接");
        }

        private async void btnDisconnectPLC_Click(object sender, EventArgs e)
        {
            await RunDeviceActionAsync(async () =>
            {
                await StopDetectionAsync();
                await _detection.PLC.DisconnectAsync();
            }, "PLC 已断开");
        }

        // ==================== 检测流程 ====================

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (_detection.CameraStatus != DeviceStatus.Connected)
            { MessageBox.Show("请先连接相机"); return; }
            if (_detection.PLCStatus != DeviceStatus.Connected)
            { MessageBox.Show("请先连接PLC"); return; }

            _detection.StartPolling(_simulation == null ? PlcIp : "simulation", PlcRack, PlcSlot);
            RefreshUI();
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            await StopDetectionAsync();
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            Enabled = false;
            _simulationTimer.Stop();
            try
            {
                try { await _deviceTask; } catch { /* Already reported by the device handler. */ }
                await _detection.StopPollingAsync();
                await _detection.Camera.DisconnectAsync();
                await _detection.PLC.DisconnectAsync();
            }
            catch (Exception ex) { _log.Error("关闭设备失败", ex); }
            finally
            {
                _log.OnLog -= AppendLog;
                _detection.Camera.StatusChanged -= OnDeviceStatusChanged;
                _detection.PLC.StatusChanged -= OnDeviceStatusChanged;
                _detection.RunningChanged -= OnRunningChanged;
                _detection.Camera.FrameArrived -= OnFrameArrived;
                _detection.DetectionCompleted -= OnDetectionCompleted;
                _simulationTimer.Dispose();
                picPreview.Image?.Dispose();
                _allowClose = true;
                Close();
            }
        }
    }
}
