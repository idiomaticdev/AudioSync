using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AudioSync;

// ── Windows Core Audio COM Enums ──────────────────────────────────────────────

enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

// ── COM Structures ────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;

    public static readonly PROPERTYKEY DeviceFriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14
    };
}

[StructLayout(LayoutKind.Sequential)]
struct PropVariant
{
    public ushort vt;
    public ushort wReserved1, wReserved2, wReserved3;
    public IntPtr data1;
    public IntPtr data2;
}

// ── COM Interfaces ────────────────────────────────────────────────────────────

[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int key);
}

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IntPtr device);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorCom { }

[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
    int OpenPropertyStore(int stgmAccess, out IntPtr propertyStore);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetState(out int state);
}

[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    int GetCount(out int count);
    int Item(int index, out IntPtr device);
}

[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    int GetCount(out int count);
    int GetAt(int index, out PROPERTYKEY key);
    int GetValue(ref PROPERTYKEY key, out PropVariant value);
    int SetValue(ref PROPERTYKEY key, ref PropVariant value);
    int Commit();
}

[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    int GetMixFormat(string deviceId, IntPtr format);
    int GetDeviceFormat(string deviceId, int p, IntPtr format);
    int ResetDeviceFormat(string deviceId);
    int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    int GetProcessingPeriod(string deviceId, int p, long defaultPeriod, long minPeriod);
    int SetProcessingPeriod(string deviceId, long period);
    int GetShareMode(string deviceId, IntPtr mode);
    int SetShareMode(string deviceId, IntPtr mode);
    int GetPropertyValue(string deviceId, int storeType, IntPtr propKey, IntPtr propVariant);
    int SetPropertyValue(string deviceId, int storeType, IntPtr propKey, IntPtr propVariant);
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    int SetEndpointVisibility(string deviceId, int isVisible);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
class PolicyConfigCom { }

// ── Audio Helper ──────────────────────────────────────────────────────────────

static class AudioHelper
{
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    public static string GetDeviceName(IMMDeviceEnumerator enumerator, string deviceId)
    {
        try
        {
            int hr = enumerator.GetDevice(deviceId, out IntPtr devPtr);
            if (hr != 0 || devPtr == IntPtr.Zero) return deviceId;

            var device = (IMMDevice)Marshal.GetObjectForIUnknown(devPtr);
            Marshal.Release(devPtr);
            return GetDeviceNameFromDevice(device) ?? deviceId;
        }
        catch { return deviceId; }
    }

    public static string? GetDeviceNameFromDevice(IMMDevice device)
    {
        try
        {
            int hr = device.OpenPropertyStore(0, out IntPtr storePtr);
            if (hr != 0 || storePtr == IntPtr.Zero) return null;

            var store = (IPropertyStore)Marshal.GetObjectForIUnknown(storePtr);
            Marshal.Release(storePtr);

            var key = PROPERTYKEY.DeviceFriendlyName;
            hr = store.GetValue(ref key, out PropVariant pv);
            if (hr != 0) return null;

            string? name = pv.vt == 31 ? Marshal.PtrToStringUni(pv.data1) : null;
            PropVariantClear(ref pv);
            return name;
        }
        catch { return null; }
    }

    public static List<(string id, string name)> GetRenderDevices(IMMDeviceEnumerator enumerator)
    {
        var result = new List<(string, string)>();
        try
        {
            enumerator.EnumAudioEndpoints(EDataFlow.eRender, 1, out IntPtr collPtr);
            if (collPtr == IntPtr.Zero) return result;

            var collection = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collPtr);
            Marshal.Release(collPtr);

            collection.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                collection.Item(i, out IntPtr devPtr);
                if (devPtr == IntPtr.Zero) continue;

                var device = (IMMDevice)Marshal.GetObjectForIUnknown(devPtr);
                Marshal.Release(devPtr);

                device.GetId(out string id);
                string name = GetDeviceNameFromDevice(device) ?? id;
                result.Add((id, name));
            }
        }
        catch { }
        return result;
    }

    public static string? GetDefaultDeviceId(IMMDeviceEnumerator enumerator, ERole role)
    {
        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, role, out IntPtr devPtr);
            if (hr != 0 || devPtr == IntPtr.Zero) return null;

            var device = (IMMDevice)Marshal.GetObjectForIUnknown(devPtr);
            Marshal.Release(devPtr);

            device.GetId(out string id);
            return id;
        }
        catch { return null; }
    }
}

// ── NotificationClient ────────────────────────────────────────────────────────

class NotificationClient : IMMNotificationClient
{
    private readonly Action<string> _log;
    private readonly IMMDeviceEnumerator _enumerator;

    public NotificationClient(Action<string> log, IMMDeviceEnumerator enumerator)
    {
        _log = log;
        _enumerator = enumerator;
    }

    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
    {
        if (flow != EDataFlow.eRender || role != ERole.eConsole || defaultDeviceId == null)
            return;

        string deviceName = AudioHelper.GetDeviceName(_enumerator, defaultDeviceId);
        _log($"Default output changed → {deviceName}");

        if (!Program.SyncEnabled)
        {
            _log("Sync disabled — skipping communication device update");
            return;
        }

        try
        {
            var policyConfig = (IPolicyConfig)new PolicyConfigCom();
            int hr = policyConfig.SetDefaultEndpoint(defaultDeviceId, ERole.eCommunications);
            if (hr == 0)
                _log($"Synced communication device → {deviceName}");
            else
                _log($"SetDefaultEndpoint failed (HRESULT 0x{hr:X8})");
        }
        catch (Exception ex)
        {
            _log($"Error syncing: {ex.Message}");
        }
    }

    public void OnDeviceStateChanged(string deviceId, int newState) { }
    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string deviceId, int key) { }
}

// ── Application Entry Point ───────────────────────────────────────────────────

static class Program
{
    // ── Win32 Constants ───────────────────────────────────────────────────────

    const uint WM_DESTROY = 0x0002;
    const uint WM_APP = 0x8000;
    const uint WM_TRAYICON = WM_APP + 1;
    const int WM_RBUTTONUP = 0x0205;
    const int WM_LBUTTONUP = 0x0202;

    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;

    const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    const uint MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_GRAYED = 0x1;
    const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;

    const int CMD_SYNC = 1, CMD_STARTUP = 2, CMD_OPENLOG = 3, CMD_EXIT = 4;
    const int CMD_DEVICE_BASE = 100;

    // ── Win32 Structs ─────────────────────────────────────────────────────────

    delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public IntPtr lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    // ── Win32 P/Invoke ────────────────────────────────────────────────────────

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassW(ref WNDCLASSW wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll")]
    static extern void PostQuitMessage(int code);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenuW(IntPtr hMenu, uint flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessageW(string msg);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // ── App State ─────────────────────────────────────────────────────────────

    static readonly string LogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AudioSync", "log.txt");

    static readonly string ExePath = Environment.ProcessPath!;
    const string StartupKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "AudioSync";
    const long MaxLogSize = 100_000;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    static bool _syncEnabled = true;
    static IMMDeviceEnumerator _enumerator = null!;
    static IntPtr _hWnd;
    static Icon? _currentIcon;
    static WndProcDelegate _wndProcDelegate = null!;
    static NOTIFYICONDATAW _nid;
    static uint _wmTaskbarCreated;
    static DateTime _lastRotateCheck = DateTime.MinValue;
    static List<(string id, string name)> _deviceList = new();
    static bool _menuShowing;

    public static bool SyncEnabled => _syncEnabled;

    // ── Entry Point ───────────────────────────────────────────────────────────

    [STAThread]
    static void Main()
    {
        using var mutex = new System.Threading.Mutex(true, "Global\\AudioSync_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBoxW(IntPtr.Zero, "AudioSync is already running.", AppName, 0x40);
            return;
        }

        var logDir = System.IO.Path.GetDirectoryName(LogPath)!;
        System.IO.Directory.CreateDirectory(logDir);

        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        var client = new NotificationClient(Log, _enumerator);
        _enumerator.RegisterEndpointNotificationCallback(client);

        Log("AudioSync started");

        // Register for taskbar-recreated message (handles Explorer crash/restart)
        _wmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        // Create hidden message-only window
        _wndProcDelegate = WndProc;
        var hInstance = GetModuleHandleW(null);
        var wc = new WNDCLASSW
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = hInstance,
            lpszClassName = "AudioSyncWindow"
        };
        RegisterClassW(ref wc);
        _hWnd = CreateWindowExW(0, "AudioSyncWindow", "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, hInstance, IntPtr.Zero);

        AddTrayIcon();

        // Message loop
        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        _enumerator.UnregisterEndpointNotificationCallback(client);
        Log("AudioSync stopped");
    }

    // ── Window Procedure ──────────────────────────────────────────────────────

    static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            int ev = (int)lParam;
            if (ev == WM_RBUTTONUP)
                ShowContextMenu();
            else if (ev == WM_LBUTTONUP)
            {
                _syncEnabled = !_syncEnabled;
                UpdateTrayIcon();
                Log(_syncEnabled ? "Sync enabled" : "Sync disabled");
            }
            return IntPtr.Zero;
        }

        if (msg == WM_DESTROY)
        {
            RemoveTrayIcon();
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        if (msg == _wmTaskbarCreated && _wmTaskbarCreated != 0)
        {
            AddTrayIcon();
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ── Tray Icon ─────────────────────────────────────────────────────────────

    static void AddTrayIcon()
    {
        _currentIcon?.Dispose();
        _currentIcon = CreateSpeakerIcon(_syncEnabled);

        _nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _currentIcon.Handle,
            szTip = _syncEnabled ? "AudioSync — Syncing" : "AudioSync — Paused"
        };
        Shell_NotifyIconW(NIM_ADD, ref _nid);
    }

    static void RemoveTrayIcon()
    {
        Shell_NotifyIconW(NIM_DELETE, ref _nid);
        _currentIcon?.Dispose();
        _currentIcon = null;
    }

    static void UpdateTrayIcon()
    {
        var oldIcon = _currentIcon;
        _currentIcon = CreateSpeakerIcon(_syncEnabled);

        _nid.uFlags = NIF_ICON | NIF_TIP;
        _nid.hIcon = _currentIcon.Handle;
        _nid.szTip = _syncEnabled ? "AudioSync — Syncing" : "AudioSync — Paused";
        Shell_NotifyIconW(NIM_MODIFY, ref _nid);

        oldIcon?.Dispose();
    }

    // ── Context Menu ──────────────────────────────────────────────────────────

    static void ShowContextMenu()
    {
        if (_menuShowing) return;
        _menuShowing = true;

        try
        {
            var menu = CreatePopupMenu();

            // Sync toggle
            AppendMenuW(menu, _syncEnabled ? MF_CHECKED : MF_STRING, (IntPtr)CMD_SYNC, "Sync Enabled");

            // Device submenu
            _deviceList = AudioHelper.GetRenderDevices(_enumerator);
            string? currentCommId = AudioHelper.GetDefaultDeviceId(_enumerator, ERole.eCommunications);

            var deviceMenu = CreatePopupMenu();
            if (_deviceList.Count == 0)
            {
                AppendMenuW(deviceMenu, MF_GRAYED, IntPtr.Zero, "(no devices found)");
            }
            else
            {
                for (int i = 0; i < _deviceList.Count; i++)
                {
                    var (id, name) = _deviceList[i];
                    uint flags = MF_STRING;
                    if (string.Equals(id, currentCommId, StringComparison.OrdinalIgnoreCase))
                        flags |= MF_CHECKED;
                    AppendMenuW(deviceMenu, flags, (IntPtr)(CMD_DEVICE_BASE + i), name);
                }
            }

            uint deviceMenuFlags = MF_POPUP;
            if (_syncEnabled) deviceMenuFlags |= MF_GRAYED;
            AppendMenuW(menu, deviceMenuFlags, deviceMenu, "Set Communication Device");

            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);

            // Start with Windows
            AppendMenuW(menu, IsRegisteredForStartup() ? MF_CHECKED : MF_STRING,
                (IntPtr)CMD_STARTUP, "Start with Windows");

            // Open Log
            AppendMenuW(menu, MF_STRING, (IntPtr)CMD_OPENLOG, "Open Log");

            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);

            // Exit
            AppendMenuW(menu, MF_STRING, (IntPtr)CMD_EXIT, "Exit");

            // Version
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            AppendMenuW(menu, MF_SEPARATOR, IntPtr.Zero, null);
            AppendMenuW(menu, MF_GRAYED, IntPtr.Zero, $"v{ver?.Major}.{ver?.Minor}.{ver?.Build}");

            // Show menu
            GetCursorPos(out POINT pt);
            SetForegroundWindow(_hWnd);
            int cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.x, pt.y, _hWnd, IntPtr.Zero);
            DestroyMenu(menu);

            // Handle selection
            switch (cmd)
            {
                case CMD_SYNC:
                    _syncEnabled = !_syncEnabled;
                    UpdateTrayIcon();
                    Log(_syncEnabled ? "Sync enabled" : "Sync disabled");
                    break;

                case CMD_STARTUP:
                    if (IsRegisteredForStartup()) UnregisterStartup();
                    else RegisterStartup();
                    break;

                case CMD_OPENLOG:
                    if (System.IO.File.Exists(LogPath))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LogPath) { UseShellExecute = true });
                    break;

                case CMD_EXIT:
                    DestroyWindow(_hWnd);
                    break;

                default:
                    if (cmd >= CMD_DEVICE_BASE)
                    {
                        int idx = cmd - CMD_DEVICE_BASE;
                        if (idx < _deviceList.Count)
                        {
                            var (id, name) = _deviceList[idx];
                            try
                            {
                                var policyConfig = (IPolicyConfig)new PolicyConfigCom();
                                int hr = policyConfig.SetDefaultEndpoint(id, ERole.eCommunications);
                                if (hr == 0)
                                    Log($"Manually set communication device → {name}");
                                else
                                    Log($"Failed to set communication device (HRESULT 0x{hr:X8})");
                            }
                            catch (Exception ex)
                            {
                                Log($"Error setting communication device: {ex.Message}");
                            }
                        }
                    }
                    break;
            }
        }
        finally
        {
            _menuShowing = false;
        }
    }

    // ── Icon Drawing ──────────────────────────────────────────────────────────

    static Icon CreateSpeakerIcon(bool enabled)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var color = enabled ? Color.FromArgb(50, 200, 100) : Color.Gray;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, 1.5f);

            g.FillRectangle(brush, 2, 5, 3, 6);
            g.FillPolygon(brush, new[] {
                new Point(5, 5), new Point(9, 2), new Point(9, 14), new Point(5, 11)
            });

            if (enabled)
            {
                g.DrawArc(pen, 10, 4, 3, 8, -50, 100);
                g.DrawArc(pen, 12, 3, 3, 10, -50, 100);
            }
            else
            {
                using var xPen = new Pen(Color.FromArgb(220, 60, 60), 2f);
                g.DrawLine(xPen, 10, 4, 15, 12);
                g.DrawLine(xPen, 15, 4, 10, 12);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        var tmp = Icon.FromHandle(hIcon);
        var clone = (Icon)tmp.Clone();
        DestroyIcon(hIcon);
        return clone;
    }

    // ── Startup Registration ──────────────────────────────────────────────────

    static bool IsRegisteredForStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey, false);
        return key?.GetValue(AppName) is string val &&
               string.Equals(val, ExePath, StringComparison.OrdinalIgnoreCase);
    }

    static void RegisterStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey, true);
        key?.SetValue(AppName, ExePath);
        Log("Registered for startup");
    }

    static void UnregisterStartup()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey, true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
        Log("Unregistered from startup");
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    static void Log(string message)
    {
        try
        {
            var now = DateTime.Now;
            if ((now - _lastRotateCheck).TotalMinutes >= 60)
            {
                _lastRotateCheck = now;
                RotateLogIfNeeded();
            }
            var line = $"[{now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            System.IO.File.AppendAllText(LogPath, line);
        }
        catch { }
    }

    static void RotateLogIfNeeded()
    {
        try
        {
            var fi = new System.IO.FileInfo(LogPath);
            if (!fi.Exists || fi.Length <= MaxLogSize) return;

            var oldPath = LogPath + ".old";
            if (System.IO.File.Exists(oldPath))
                System.IO.File.Delete(oldPath);
            System.IO.File.Move(LogPath, oldPath);
        }
        catch { }
    }
}
