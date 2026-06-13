using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TouchpadVisualizer;

// ========================================================================
// Win32 P/Invoke — Raw Input / HID 触控板
// ========================================================================

internal static class RawInput
{
    // WM_INPUT
    public const int WM_INPUT = 0x00FF;

    // RAWINPUTDEVICE flags
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_EXINPUTSINK = 0x00000200; // Windows 8+ 扩展接收

    // GetRawInputData command
    public const uint RID_INPUT = 0x10000003;

    // HID 触控板标准 Usage Page & Usage
    public const ushort HID_USAGEPAGE_DIGITIZER = 0x0D;
    public const ushort HID_USAGE_TOUCHPAD = 0x05; // Touch Screen = Precision Touchpad

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWHID
    {
        public uint dwSizeHid;
        public uint dwCount;
        // 后面跟着原始字节
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        // 联合体：RAWMOUSE / RAWKEYBOARD / RAWHID
        // 对于 HID 设备，用 RAWHID
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct)]
        RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("kernel32.dll")]
    public static extern uint GetLastError();

    [DllImport("user32.dll")]
    public static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll")]
    public static extern uint GetRawInputDeviceList(
        IntPtr pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("user32.dll")]
    public static extern uint GetRawInputDeviceInfoA(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000b;

    // ===== CfgMgr32 API (设备禁用/启用) =====
    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    public static extern uint CM_Locate_DevNode(out IntPtr pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll")]
    public static extern uint CM_Disable_DevNode(IntPtr dnDevInst, ulong ulFlags);

    [DllImport("CfgMgr32.dll")]
    public static extern uint CM_Enable_DevNode(IntPtr dnDevInst, ulong ulFlags);

    public const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;

    // ===== SetupAPI (枚举HID设备) =====
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, IntPtr deviceInfoData,
        IntPtr deviceInstanceId, uint deviceInstanceIdSize, out uint requiredSize);

    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_ALLCLASSES = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    public struct RID_DEVICE_INFO
    {
        public uint cbSize;
        public uint dwType;
        public uint dwVendorId;
        public uint dwProductId;
        public uint dwVersionNumber;
        public ushort usUsagePage;
        public ushort usUsage;
    }

    /// <summary>
    /// 枚举系统中所有 Raw Input 设备，返回描述列表
    /// </summary>
    public static List<string> EnumerateDevices()
    {
        var result = new List<string>();

        uint deviceCount = 0;
        uint cbSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        uint count = GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, cbSize);

        if (deviceCount == 0)
        {
            result.Add("  无 Raw Input 设备");
            return result;
        }

        // 分配设备列表缓冲区
        int listSize = (int)(deviceCount * cbSize);
        IntPtr pList = Marshal.AllocHGlobal(listSize);
        try
        {
            count = GetRawInputDeviceList(pList, ref deviceCount, cbSize);

            for (uint i = 0; i < deviceCount; i++)
            {
                IntPtr pEntry = IntPtr.Add(pList, (int)(i * cbSize));
                var entry = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(pEntry);

                // 获取设备名
                uint nameLen = 0;
                GetRawInputDeviceInfoA(entry.hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref nameLen);
                string deviceName = "";
                if (nameLen > 0)
                {
                    IntPtr pName = Marshal.AllocHGlobal((int)nameLen * 2);
                    try
                    {
                        GetRawInputDeviceInfoA(entry.hDevice, RIDI_DEVICENAME, pName, ref nameLen);
                        deviceName = Marshal.PtrToStringAnsi(pName) ?? "";
                    }
                    finally { Marshal.FreeHGlobal(pName); }
                }

                // 获取设备信息 (HID UsagePage/Usage)
                var info = new RID_DEVICE_INFO();
                info.cbSize = (uint)Marshal.SizeOf<RID_DEVICE_INFO>();
                uint infoSize = info.cbSize;
                GetRawInputDeviceInfoA(entry.hDevice, RIDI_DEVICEINFO, IntPtr.Zero, ref infoSize);
                IntPtr pInfo = Marshal.AllocHGlobal((int)infoSize);
                try
                {
                    Marshal.StructureToPtr(info, pInfo, false);
                    GetRawInputDeviceInfoA(entry.hDevice, RIDI_DEVICEINFO, pInfo, ref infoSize);
                    info = Marshal.PtrToStructure<RID_DEVICE_INFO>(pInfo);
                }
                finally { Marshal.FreeHGlobal(pInfo); }

                string typeName = entry.dwType switch
                {
                    0 => "鼠标", 1 => "键盘", 2 => "HID",
                    _ => $"类型{entry.dwType}"
                };

                string detail = $"  [{i}] {typeName} UsagePage=0x{info.usUsagePage:X2} Usage=0x{info.usUsage:X2}";
                if (!string.IsNullOrEmpty(deviceName))
                    detail += $"  {deviceName}";

                result.Add(detail);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pList);
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    /// <summary>
    /// 注册接收触控板 HID 原始输入，逐个 Usage 尝试并返回详细结果
    /// </summary>
    public static string RegisterAll(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder();
        uint lastErr = 0;

        // 每个 Usage 单独注册，避免一个失败影响其他
        var usages = new (ushort page, ushort usage, string name)[]
        {
            (0x0D, 0x05, "Digitizer_TouchScreen"),
            (0x0D, 0x02, "Digitizer_Pen"),
            (0x0D, 0x06, "Digitizer_TouchPad"),
        };

        int successCount = 0;
        foreach (var (page, usage, name) in usages)
        {
            var device = new RAWINPUTDEVICE
            {
                usUsagePage = page,
                usUsage = usage,
                dwFlags = RIDEV_INPUTSINK,  // 只用 INPUTSINK，不用 EXINPUTSINK
                hwndTarget = hwnd,
            };

            bool ok = RegisterRawInputDevices(
                new[] { device },
                1,
                (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

            if (ok)
            {
                successCount++;
                sb.AppendLine($"  ✅ {name} (0x{page:X2}/0x{usage:X2})");
            }
            else
            {
                uint err = GetLastError();
                lastErr = err;
                string desc = err switch
                {
                    5 => "拒绝访问",
                    87 => "参数错误",
                    1168 => "设备未找到",
                    _ => $"错误{err}"
                };
                sb.AppendLine($"  ❌ {name}: {desc} (0x{err:X8})");
            }
        }

        string result = $"注册结果: {successCount}/{usages.Length} 成功\n{sb}";
        return result;
    }
}

// ========================================================================
// 触控板 HID 报告解析器（基于 Precision Touchpad HID 标准）
// ========================================================================

internal class TouchpadHidParser
{
    // Apple MacBook Air 2017 (VID_05AC) Precision Touchpad HID 报告
    // 完整报告 50 字节，每指 8 字节，从字节 6 开始
    //
    //  Byte 0:    ReportID (0x05)
    //  Byte 1:    Status (bit0=tip)
    //  Bytes 2-5: 未知/时间戳
    //  Bytes 6+ : 触点数据，每指 8 字节:
    //    +0: X (LE, 16-bit)
    //    +2: Y (LE, 16-bit)
    //    +4: Width
    //    +5: Height
    //    +6: Pressure
    //    +7: Flags

    public const int FINGER_SIZE = 8;       // 每指数据块大小
    public const int DATA_START = 6;        // 数据起始偏移
    public const int MAX_FINGERS = 5;       // 最多 5 指

    public static List<TouchpadContact> Parse(byte[] rawData, int maxX, int maxY,
        out string debugInfo, bool showAll = false)
    {
        var contacts = new List<TouchpadContact>();
        var dbg = new System.Text.StringBuilder();

        if (rawData == null || rawData.Length < DATA_START + FINGER_SIZE)
        {
            debugInfo = $"数据不足: {rawData?.Length ?? 0}B";
            return contacts;
        }

        int reportId = rawData[0];
        byte status = rawData[1];
        bool tip = (status & 0x01) != 0;

        dbg.Append($"RID=0x{reportId:X2} Status=0x{status:X2}");

        if (!tip)
        {
            dbg.Append(" [无触控]");
            if (!showAll)
            {
                debugInfo = dbg.ToString();
                return contacts;
            }
            // showAll 模式：即使无触控也显示槽位数据
        }

        for (int f = 0; f < MAX_FINGERS; f++)
        {
            int off = DATA_START + f * FINGER_SIZE;
            if (off + FINGER_SIZE > rawData.Length) break;

            int x = rawData[off]     | (rawData[off + 1] << 8);
            int y = rawData[off + 2] | (rawData[off + 3] << 8);

            if (!showAll)
            {
                // 正常模式：过滤无效触点
                if (x < 100 || y < 100) continue;
                if (x > maxX * 1.15 || y > maxY * 1.15) continue;
            }

            int w = rawData[off + 4];
            int h = rawData[off + 5];
            int p = rawData[off + 6];
            int flags = rawData[off + 7];

            // 全部添加，showAll 模式下显示所有 5 个槽位
            contacts.Add(new TouchpadContact
            {
                X = x, Y = y,
                Width = w, Height = h,
                Pressure = p,
                Confidence = (flags & 0x01) != 0,
                ContactId = f,
            });

            dbg.Append($" F{f}:({x},{y}) W={w} H={h} P={p}");
        }

        if (contacts.Count == 0)
            dbg.Append(" [无有效触点]");

        debugInfo = dbg.ToString();
        return contacts;
    }
}

public class TouchpadContact
{
    public int X, Y;
    public int Width, Height;
    public int Pressure;
    public bool Confidence;
    public int ContactId;
}

// ========================================================================
// 触控板设备管理器（禁用/启用触控板 HID 设备）
// ========================================================================
internal static class TouchpadDeviceManager
{
    private static IntPtr _deviceNode;
    private static bool _disabled;
    private static string _lastError = "";

    /// <summary>
    /// 查找 Apple 触控板并禁用，返回是否成功
    /// </summary>
    public static bool Disable()
    {
        _deviceNode = IntPtr.Zero;
        _disabled = false;

        // 使用 SetupAPI 枚举 HID 设备
        var hidGuid = new Guid("{4d1e55b2-f16f-11cf-88cb-001111000030}");
        IntPtr deviceInfoSet = RawInput.SetupDiGetClassDevs(
            ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            RawInput.DIGCF_PRESENT | RawInput.DIGCF_ALLCLASSES);

        if (deviceInfoSet == new IntPtr(-1))
        {
            _lastError = "SetupDiGetClassDevs 失败";
            return false;
        }

        try
        {
            uint index = 0;
            while (true)
            {
                int dataSize = 32;
                IntPtr pData = Marshal.AllocHGlobal(dataSize);
                try
                {
                    Marshal.WriteInt32(pData, dataSize);
                    if (!RawInput.SetupDiEnumDeviceInfo(deviceInfoSet, index, pData))
                        break;

                    uint reqSize = 0;
                    RawInput.SetupDiGetDeviceInstanceId(deviceInfoSet, pData, IntPtr.Zero, 0, out reqSize);
                    if (reqSize > 0)
                    {
                        IntPtr pId = Marshal.AllocHGlobal((int)reqSize * 2);
                        try
                        {
                            if (RawInput.SetupDiGetDeviceInstanceId(deviceInfoSet, pData, pId, reqSize, out reqSize))
                            {
                                string instanceId = Marshal.PtrToStringAuto(pId) ?? "";
                                if (instanceId.Contains("VID_05AC") && instanceId.Contains("PID_0291") &&
                                    instanceId.Contains("MI_02"))
                                {
                                    uint result = RawInput.CM_Locate_DevNode(
                                        out _deviceNode, instanceId,
                                        RawInput.CM_LOCATE_DEVNODE_NORMAL);

                                    if (result == 0 && _deviceNode != IntPtr.Zero)
                                    {
                                        result = RawInput.CM_Disable_DevNode(_deviceNode, 0);
                                        if (result == 0)
                                        {
                                            _disabled = true;
                                            _lastError = $"已禁用: {instanceId}";
                                            return true;
                                        }
                                        else
                                        {
                                            _lastError = $"CM_Disable_DevNode 失败: error={result}";
                                            return false;
                                        }
                                    }
                                    else
                                    {
                                        _lastError = $"CM_Locate_DevNode 失败: error={result}";
                                        return false;
                                    }
                                }
                            }
                        }
                        finally { Marshal.FreeHGlobal(pId); }
                    }
                }
                finally { Marshal.FreeHGlobal(pData); }
                index++;
            }

            _lastError = "未找到 Apple 触控板设备";
            return false;
        }
        finally
        {
            RawInput.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    public static bool ReEnable()
    {
        if (!_disabled || _deviceNode == IntPtr.Zero)
        {
            _lastError = "没有需要启用的设备";
            return false;
        }

        uint result = RawInput.CM_Enable_DevNode(_deviceNode, 0);
        if (result == 0)
        {
            _disabled = false;
            _lastError = "已重新启用";
            return true;
        }
        else
        {
            _lastError = $"CM_Enable_DevNode 失败: error={result}";
            return false;
        }
    }

    public static bool IsDisabled => _disabled;
    public static string LastError => _lastError;
}

// ========================================================================
// 触控点数据模型
// ========================================================================

public class TouchPointInfoVM
{
    public int DeviceId { get; set; }
    public string DisplayId => $"#{DeviceId % 100}";
    public string PositionText { get; set; } = "";
    public string RawText { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string PressureText { get; set; } = "";
    public Brush Color { get; set; } = Brushes.Transparent;
    public string SourceText { get; set; } = "";
}

internal class TouchState
{
    public int DeviceId;
    public Point Position;
    public int RawX, RawY; // 原始 HID 坐标
    public double ContactWidth;
    public double ContactHeight;
    public double Pressure;
    public int ColorIndex;
    public string Source = "";
    public int StableCount;   // 稳定性计数器
    public bool Visible;      // 通过稳定性检测后才显示
}

// ========================================================================
// 主窗口
// ========================================================================

public partial class MainWindow : Window
{
    private static readonly Color[] Palette =
    {
        Color.FromRgb(88,  166, 255),
        Color.FromRgb(255, 165, 87),
        Color.FromRgb(130, 211, 130),
        Color.FromRgb(255, 120, 120),
        Color.FromRgb(194, 130, 255),
        Color.FromRgb(255, 210, 80),
        Color.FromRgb(80,  220, 200),
        Color.FromRgb(255, 140, 200),
    };

    private readonly Dictionary<int, TouchState> _active = new();
    private readonly Dictionary<int, FrameworkElement> _visuals = new();
    private readonly ObservableCollection<TouchPointInfoVM> _infoItems = new();

    // ===== 调试日志（仅事件变化时记录） =====
    private readonly ObservableCollection<string> _debugLog = new();
    private const int MAX_LOG = 30;
    private long _msgCount;
    private long _touchpadFrames;
    private DateTime _lastTouchTime = DateTime.MinValue;

    // 渲染帧率控制 — 已移除帧率限制，全速渲染
    private Point _canvasOrigin;

    // 调试冻结
    private bool _frozen;
    private string _frozenLog = "";
    private bool _showAllFingers;

    // 信息面板增量更新
    private int _lastInfoCount = -1;
    private int _infoFrameSkip;

    // 触控板设备坐标 → 窗口坐标的映射
    // 触控板通常在 0..MAX 范围内报告绝对坐标
    private const int TOUCHPAD_MAX_X = 10000;
    private const int TOUCHPAD_MAX_Y = 7000;

    public MainWindow()
    {
        InitializeComponent();
        TouchList.ItemsSource = _infoItems;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(hwnd);
        src?.AddHook(WndProc);

        // 1) 枚举所有 Raw Input 设备
        DebugLog("🔍 正在扫描系统中的 Raw Input 设备...");
        var devices = RawInput.EnumerateDevices();
        foreach (var d in devices)
            DebugLog(d);

        // 2) 逐个注册 Raw Input（详细显示每个 Usage 的结果）
        string regResult = RawInput.RegisterAll(hwnd);
        foreach (var line in regResult.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            DebugLog(line.TrimEnd());

        // 3) 更新设备面板
        UpdateDevicePanel(devices, regResult);

        // 窗口大小变化时重算画布原点
        SizeChanged += (_, _) => UpdateCanvasOrigin();
        UpdateCanvasOrigin();

        // 键盘退出
        // 键盘 — Esc=退出  F12=冻结/解冻
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) Close();
            else if (args.Key == Key.F12) ToggleFreeze();
        };

        // 启动时清空任何可能的残留状态
        ClearTouchState();

        DebugLog("🟢 窗口已加载，等待触控输入...");
        DebugList.ItemsSource = _debugLog;
    }

    // ========================================================================
    // 主消息钩子
    // ========================================================================
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _msgCount++;

        switch (msg)
        {
            case RawInput.WM_INPUT:
                HandleRawInput(lParam);
                break;

            case 0x020A: // WM_MOUSEWHEEL
                HandleMouseWheel(wParam, lParam);
                break;
        }

        return IntPtr.Zero;
    }

    // ========================================================================
    // Raw Input 处理
    // ========================================================================
    private int _lastContactCount = -1; // 用于检测变化
    private int _frameCounter;

    private void HandleRawInput(IntPtr lParam)
    {
        if (_frozen) return; // 冻结时跳过处理

        // 获取原始数据大小
        uint size = 0;
        RawInput.GetRawInputData(lParam, RawInput.RID_INPUT, IntPtr.Zero, ref size,
            (uint)Marshal.SizeOf<RawInput.RAWINPUTHEADER>());

        if (size == 0 || size > 65536) return;

        // 读取原始数据
        IntPtr pBuffer = Marshal.AllocHGlobal((int)size);
        try
        {
            RawInput.GetRawInputData(lParam, RawInput.RID_INPUT, pBuffer, ref size,
                (uint)Marshal.SizeOf<RawInput.RAWINPUTHEADER>());

            // HID 数据在 RAWHID 之后
            int hidOffset = Marshal.SizeOf<RawInput.RAWINPUTHEADER>() + sizeof(uint) * 2;
            if (hidOffset >= (int)size) return;

            int hidDataLen = (int)size - hidOffset;
            if (hidDataLen < 3) return;

            // 复制 HID 报告字节
            byte[] hidReport = new byte[hidDataLen];
            Marshal.Copy(IntPtr.Add(pBuffer, hidOffset), hidReport, 0, hidDataLen);

            _touchpadFrames++;
            _frameCounter++;

            // 解析触点
            var contacts = TouchpadHidParser.Parse(hidReport, TOUCHPAD_MAX_X, TOUCHPAD_MAX_Y, out string debug, _showAllFingers);

            // 每 50 帧记录一次摘要 + 触点数量变化时记录
            if (_frameCounter % 50 == 0)
                DebugLog($"🖐 触控帧 #{_touchpadFrames}: {contacts.Count} 触点 | {debug}");
            else if (contacts.Count != _lastContactCount)
                DebugLog($"🖐 #{_touchpadFrames} 触点变化: {contacts.Count} | {debug}");

            _lastContactCount = contacts.Count;

            // 更新最后触摸时间戳
            if (contacts.Count > 0)
                _lastTouchTime = DateTime.UtcNow;

            // 直接渲染，不做帧率限制（降低延迟优先于省电）
            ProcessContactsAndRender(contacts);

            // 超时清理：如果超过 150ms 没有收到触控数据，强制清除
            if (_lastTouchTime != DateTime.MinValue &&
                (DateTime.UtcNow - _lastTouchTime).TotalMilliseconds > 150)
            {
                if (_active.Values.Any(s => s.Visible))
                    ClearTouchState();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pBuffer);
        }
    }

    // ========================================================================
    // 将触控板触点映射到窗口并渲染（受帧率限制）
    // ========================================================================
    private void ProcessContactsAndRender(List<TouchpadContact> contacts)
    {
        // 获取窗口尺寸
        double winW = Math.Max(TouchCanvas.ActualWidth, 100);  // 画布的实际宽度
        double winH = Math.Max(ActualHeight, 100);

        var activeIds = new HashSet<int>();

        for (int i = 0; i < contacts.Count; i++)
        {
            var c = contacts[i];
            int id = i + 1; // 触点编号从 1 开始，确保唯一

            // 触控板设备坐标 → 窗口坐标
            double ratioX = winW / TOUCHPAD_MAX_X;
            double ratioY = winH / TOUCHPAD_MAX_Y;
            double wx = c.X * ratioX;
            double wy = c.Y * ratioY;

            // 限制在窗口范围内
            wx = Math.Clamp(wx, 0, winW);
            wy = Math.Clamp(wy, 0, winH);

            activeIds.Add(id);

            if (_active.TryGetValue(id, out var state))
            {
                state.Position = new Point(wx, wy);
                state.RawX = c.X;
                state.RawY = c.Y;
                state.ContactWidth = c.Width * 2.0;
                state.ContactHeight = c.Height * 2.0;
                state.Pressure = c.Pressure / 255.0;
                state.Source = $"RawInput[{i + 1}]";
                if (_showAllFingers) state.Visible = true;
            }
            else
            {
                _active[id] = new TouchState
                {
                    DeviceId = id,
                    Position = new Point(wx, wy),
                    RawX = c.X, RawY = c.Y,
                    ContactWidth = c.Width * 2.0,
                    ContactHeight = c.Height * 2.0,
                    Pressure = c.Pressure / 255.0,
                    ColorIndex = _active.Count % Palette.Length,
                    Source = $"RawInput[{i + 1}]",
                    Visible = _showAllFingers, // 槽位模式立即可见
                };
            }
        }

        // 稳定性去抖参数（多指显示模式跳过稳定性检测）
        const int SHOW_THRESHOLD = 3;
        const int HIDE_THRESHOLD = -5;

        if (_showAllFingers)
        {
            // 多指显示模式：不过滤，直接显示所有触点
            var notSeen = _active.Keys.Where(k => !activeIds.Contains(k)).ToList();
            foreach (var id in notSeen)
            {
                _active.Remove(id);
                if (_visuals.TryGetValue(id, out var vis))
                {
                    TouchCanvas.Children.Remove(vis);
                    _visuals.Remove(id);
                }
            }
        }
        else
        {
            // 正常模式：稳定性去抖
            foreach (var id in _active.Keys.ToList())
        {
            if (activeIds.Contains(id))
            {
                // 触点本次出现 → 计数器递增（上限）
                var state = _active[id];
                if (state.StableCount < SHOW_THRESHOLD + 2)
                    state.StableCount++;
                if (state.StableCount >= SHOW_THRESHOLD)
                    state.Visible = true;
            }
            else
            {
                // 触点本次未出现 → 计数器递减
                var state = _active[id];
                state.StableCount--;
                if (state.StableCount <= HIDE_THRESHOLD)
                {
                    // 稳定消失 → 移除
                    _active.Remove(id);
                    if (_visuals.TryGetValue(id, out var vis))
                    {
                        TouchCanvas.Children.Remove(vis);
                        _visuals.Remove(id);
                    }
                }
            }
        }
        }

        RenderTouches();
        UpdateInfoPanel();
    }

    // ========================================================================
    // 鼠标滚轮（2 指滚动）
    // ========================================================================
    private void HandleMouseWheel(IntPtr wParam, IntPtr lParam)
    {
        // 简单的滚轮检测——有 2 指滚动时记录
        int delta = (short)((ulong)wParam >> 16 & 0xFFFF);
        if (delta != 0)
            DebugLog($"🔄 触控板滚轮: delta={delta}");
    }

    // ========================================================================
    // 缓存画布原点（避免每帧计算 TranslatePoint）
    // ========================================================================
    private void UpdateCanvasOrigin()
    {
        if (TouchCanvas.IsVisible)
            _canvasOrigin = TouchCanvas.TranslatePoint(new Point(0, 0), this);
    }

    // ========================================================================
    // 鼠标回退 — 已移除，触控板 Raw Input 已正常工作
    // 保留鼠标滚轮事件仅用于日志
    // ========================================================================

    // ========================================================================
    // 清空所有触控状态
    // ========================================================================
    private void ClearTouchState()
    {
        _active.Clear();
        _visuals.Clear();
        TouchCanvas.Children.Clear();
        _infoItems.Clear();
        _lastContactCount = -1;
        HintText.Visibility = Visibility.Visible;
    }

    // ========================================================================
    // 渲染
    // ========================================================================
    private void RenderTouches()
    {
        int visibleCount = _active.Values.Count(s => s.Visible);
        HintText.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        Point origin = _canvasOrigin;

        foreach (var kvp in _active)
        {
            var s = kvp.Value;
            if (!s.Visible) continue;
            double cx = s.Position.X - origin.X;
            double cy = s.Position.Y - origin.Y;

            double contact = Math.Max(s.ContactWidth, s.ContactHeight);
            double displaySize = Math.Max(contact * 2.0, 40.0);
            double alpha = 0.5 + Math.Clamp(s.Pressure, 0, 1) * 0.35;

            if (_visuals.TryGetValue(kvp.Key, out var existing) && existing is Grid grid)
            {
                grid.Width = displaySize;
                grid.Height = displaySize;
                if (grid.Children[0] is Ellipse el)
                {
                    el.Width = displaySize;
                    el.Height = displaySize;
                    el.Opacity = alpha;
                }
                Canvas.SetLeft(grid, cx - displaySize / 2);
                Canvas.SetTop(grid, cy - displaySize / 2);
            }
            else
            {
                var color = Palette[s.ColorIndex % Palette.Length];
                var fill = new SolidColorBrush(color);
                var stroke = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));

                var ellipse = new Ellipse
                {
                    Width = displaySize,
                    Height = displaySize,
                    Fill = fill,
                    Opacity = alpha,
                    Stroke = stroke,
                    StrokeThickness = 2,
                };

                var label = new TextBlock
                {
                    Text = (kvp.Key % 100).ToString(),
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };

                var container = new Grid
                {
                    Width = displaySize,
                    Height = displaySize,
                    IsHitTestVisible = false,
                };
                container.Children.Add(ellipse);
                container.Children.Add(label);

                TouchCanvas.Children.Add(container);
                _visuals[kvp.Key] = container;

                Canvas.SetLeft(container, cx - displaySize / 2);
                Canvas.SetTop(container, cy - displaySize / 2);
            }
        }
    }

    // ========================================================================
    // 信息面板
    // ========================================================================
    private void UpdateInfoPanel()
    {
        int visibleCount = _active.Values.Count(s => s.Visible);
        TouchCount.Text = $"触点数量：{visibleCount}";
        _debugCounter.Text = $"📨 WM消息: {_msgCount}  |  🖐 触控帧: {_touchpadFrames}";

        int count = visibleCount;
        _infoFrameSkip++;

        // 触点数量变化 或 每 10 帧重建一次列表（更新坐标数值）
        if (count != _lastInfoCount || _infoFrameSkip >= 10)
        {
            _lastInfoCount = count;
            _infoFrameSkip = 0;
            _infoItems.Clear();
            foreach (var kvp in _active.OrderBy(k => k.Key))
            {
                var s = kvp.Value;
                if (!s.Visible) continue;
                var c = Palette[s.ColorIndex % Palette.Length];
                _infoItems.Add(new TouchPointInfoVM
                {
                    DeviceId = s.DeviceId,
                    PositionText = $"X: {s.Position.X:F1}   Y: {s.Position.Y:F1}",
                    RawText = $"原始(X:{s.RawX} Y:{s.RawY})",
                    SizeText = $"接触: {s.ContactWidth:F1} × {s.ContactHeight:F1}",
                    PressureText = $"压力: {s.Pressure * 100:F0}%",
                    Color = new SolidColorBrush(c),
                    SourceText = $"来源: {s.Source}",
                });
            }
        }
    }

    // ========================================================================
    // 调试日志（自动滚底）
    // ========================================================================
    private void DebugLog(string msg)
    {
        var line = $"[{DateTime.Now:mm:ss.fff}] {msg}";
        _debugLog.Add(line);
        while (_debugLog.Count > MAX_LOG)
            _debugLog.RemoveAt(0);

        // 自动滚动到最新消息
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (DebugScrollViewer != null &&
                DebugScrollViewer.VerticalOffset >= DebugScrollViewer.ScrollableHeight - 30)
            {
                DebugScrollViewer.ScrollToBottom();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);

        Debug.WriteLine(line);
    }

    // ========================================================================
    // F12 调试冻结/解冻
    // ========================================================================
    private void ToggleFreeze()
    {
        _frozen = !_frozen;
        if (_frozen)
        {
            // 冻结：捕获当前快照
            _frozenLog = $"❄️ 已冻结 — 触控帧 #{_touchpadFrames} 触点={_active.Count}";
            DebugLog(_frozenLog);

            // 在画布上显示冻结标识
            var freezeLabel = new TextBlock
            {
                Text = "❄️ 已冻结",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 50)),
                FontSize = 36,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            TouchCanvas.Children.Add(freezeLabel);
            Canvas.SetLeft(freezeLabel, TouchCanvas.ActualWidth / 2 - 60);
            Canvas.SetTop(freezeLabel, TouchCanvas.ActualHeight / 2 - 20);
        }
        else
        {
            // 解冻：移除冻结标识，清空所有触点状态
            for (int i = TouchCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (TouchCanvas.Children[i] is TextBlock tb && tb.Text == "❄️ 已冻结")
                {
                    TouchCanvas.Children.RemoveAt(i);
                    break;
                }
            }

            ClearTouchState();
            DebugLog("🔥 已解冻 — 恢复实时更新");
        }
    }

    // 侧边栏状态
    private bool _sidebarExpanded = true;

    // ========================================================================
    // 侧边栏折叠/展开
    // ========================================================================
    private void SidebarToggle_Click(object sender, RoutedEventArgs e)
    {
        _sidebarExpanded = !_sidebarExpanded;
        if (_sidebarExpanded)
        {
            SidebarColumn.Width = new GridLength(280);
            SidebarToggle.Content = "◀";
            SidebarToggle.ToolTip = "折叠侧边栏";
            SidebarToggle.Margin = new Thickness(0, 0, -9, 0);
        }
        else
        {
            SidebarColumn.Width = new GridLength(0);
            SidebarToggle.Content = "▶";
            SidebarToggle.ToolTip = "展开侧边栏";
            SidebarToggle.Margin = new Thickness(0, 0, 0, 0);
        }
    }

    // ========================================================================
    // 检查是否以管理员权限运行
    // ========================================================================
    private static bool IsAdministrator()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ========================================================================
    // 触控板禁用/启用
    // ========================================================================
    private void TouchpadToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!IsAdministrator())
        {
            DebugLog("⚠️ 触控板禁用需要管理员权限");
            DebugLog("   请以管理员身份重新运行程序");
            return;
        }

        if (TouchpadDeviceManager.IsDisabled)
        {
            if (TouchpadDeviceManager.ReEnable())
            {
                TouchpadToggleBtn.Content = "🖐 禁用";
                TouchpadToggleBtn.Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217));
                DebugLog("✅ 触控板已重新启用");
            }
            else
            {
                DebugLog($"❌ 重新启用失败: {TouchpadDeviceManager.LastError}");
            }
        }
        else
        {
            if (TouchpadDeviceManager.Disable())
            {
                TouchpadToggleBtn.Content = "🚫 已禁用";
                TouchpadToggleBtn.Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 120));
                DebugLog("🚫 触控板已禁用，鼠标不再响应触控板");
            }
            else
            {
                DebugLog($"❌ 禁用失败: {TouchpadDeviceManager.LastError}");
                DebugLog("💡 提示: 请以管理员身份运行此程序");
            }
        }
    }

    // ========================================================================
    // 多指模式切换
    // ========================================================================
    private void MultiFingerBtn_Click(object sender, RoutedEventArgs e)
    {
        _showAllFingers = !_showAllFingers;
        MultiFingerBtn.Content = _showAllFingers ? "🔍 槽位" : "🔍 正常";
        MultiFingerBtn.Foreground = _showAllFingers
            ? new SolidColorBrush(Color.FromRgb(255, 200, 80))
            : new SolidColorBrush(Color.FromRgb(139, 148, 158));
        ClearTouchState();
        DebugLog(_showAllFingers
            ? "🔍 槽位模式：显示全部 5 个 HID 原始槽位"
            : "🔍 正常模式：过滤无效触点 + 去抖");
    }

    // ========================================================================
    // 窗口关闭 — 确保恢复触控板
    // ========================================================================
    private void Window_Closed(object sender, EventArgs e)
    {
        if (TouchpadDeviceManager.IsDisabled)
        {
            if (TouchpadDeviceManager.ReEnable())
                Debug.WriteLine("[Touchpad] 已自动重新启用触控板");
            else
                Debug.WriteLine($"[Touchpad] 重新启用失败: {TouchpadDeviceManager.LastError}");
        }
    }

    // ========================================================================
    // 复制设备信息
    // ========================================================================
    private void CopyBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 触控板诊断报告
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 触控板诊断报告 ===");
            sb.AppendLine();

            // 设备信息
            sb.AppendLine("【设备信息】");
            sb.AppendLine(DeviceInfo.Text);
            sb.AppendLine();

            // 当前触点详情
            sb.AppendLine($"【触点详情】({_active.Count} 个触点)");
            if (_active.Count == 0)
                sb.AppendLine("  (无触点)");
            else
            {
                foreach (var kvp in _active.OrderBy(k => k.Key))
                {
                    var s = kvp.Value;
                    sb.AppendLine($"  #{kvp.Key}:  X:{s.Position.X:F1}  Y:{s.Position.Y:F1}" +
                                  $"  接触:{s.ContactWidth:F0}×{s.ContactHeight:F0}" +
                                  $"  压力:{s.Pressure * 100:F0}%  {s.Source}");
                }
            }
            sb.AppendLine();

            // 调试日志
            sb.AppendLine("【调试日志】");
            foreach (var line in _debugLog)
                sb.AppendLine(line);

            // 计数
            sb.AppendLine();
            sb.AppendLine($"WM消息总数: {_msgCount}");
            sb.AppendLine($"触控帧数: {_touchpadFrames}");

            Clipboard.SetText(sb.ToString());
            CopyBtn.Content = "✅ 已复制";
            _ = Task.Delay(1500).ContinueWith(_ =>
                Dispatcher.Invoke(() => CopyBtn.Content = "📋 复制"));
        }
        catch (Exception ex)
        {
            DebugLog($"复制失败: {ex.Message}");
        }
    }

    // ========================================================================
    // 设备信息面板
    // ========================================================================
    private void UpdateDevicePanel(List<string> devices, string regDetail)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📟 Raw Input 设备扫描：");
        if (devices.Count == 0)
            sb.AppendLine("  (无设备)");
        else
            foreach (var d in devices)
                sb.AppendLine(d);

        sb.AppendLine();
        sb.AppendLine(regDetail);

        sb.AppendLine();
        sb.AppendLine("💡 如果碰到触控板时有 📥 WM_INPUT 出现");
        sb.AppendLine("   就说明 Raw Input 收到数据了！");
        sb.AppendLine();
        sb.AppendLine($"⏱ 消息计数: {_msgCount}");

        DeviceInfo.Text = sb.ToString();
    }
}
