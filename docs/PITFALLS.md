# 已知坑

踩过一次的问题记在这里，动手前先对一遍。每条控制在两三行。

## Core COM 引用不兼容 dotnet build
`HaspDemo.Core/HaspDemo.Core.csproj` 保留了 COMReference，.NET MSBuild 会报 MSB4803。使用 Visual Studio MSBuild 或 `Start-Simulation.ps1`；已有 MSB3290 警告不等同于构建失败。

## 同步模拟设备使无延迟轮询空转
`HaspDemo.Applicationsss/DetectionService.cs` 必须保留后台 Task 与轮询延迟，不能假设所有接口调用都异步让出线程。停止要等待该 Task，取消异常不能映射成算法故障 99。

## Mono8 图片宽度不等于 Bitmap stride
`HaspDemo.UI/MainForm.cs` 的 `ToBitmap` 必须逐行按 `BitmapData.Stride` 写入；任意本地图片宽度可能不是 4 的倍数。3x2 像素测试能复现整块复制导致的错行。

## 动态控件与设计器控件缩放不同
`HaspDemo.UI/MainForm.Simulation.cs` 按已有 `grpDevices`、`grpDetail` 实际 Bounds 定位，不能直接沿用设计时坐标。需在本机字体/DPI 缩放后验证控件不重叠。

## 演示图片不可直接套用生产算法阈值
`HaspDemo.UI/Simulation/SimulationAssets.cs` 使用独立参数：环面积 200000~260000、均值窗口 31。151 窗口会让划痕连入边缘暗区，被细长度过滤；自选图片仍需匹配模板、尺度及参数，不能把演示通过当成现场精度验证。

## 构建目标内 SDK 引用更新范围过宽
`Directory.Build.props` 的目标内修改 Reference 元数据要按 `%(Reference.Identity)` 精确限定 SDK 项；不能把 SDK HintPath 写入 System 框架引用。发布验证应在无 bin/obj 的目录进行。

## WinForms partial 类资源输出同名
`HaspDemo.UI/MainForm.Simulation.resx` 与 `MainForm.resx` 会按同一个 partial 类名生成资源，触发 MSB3577。`HaspDemo.UI.csproj` 排除空的 Simulation resx；不要删掉用户设计器文件。
