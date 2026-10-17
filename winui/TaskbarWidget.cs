using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace CodexWatch;

// Native child owned by the taskbar; the hidden Avalonia host survives Explorer restarts.
internal sealed class TaskbarWidget : IDisposable
{
    public Action<string, bool, int, int>? Hover { get; set; }
    public Action? RefreshRequested, ExitRequested, UsageRequested; public Action? MenuOpening { get; set; }
    private bool _hoverTracking, _hoverShown, _suppressHover;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _actions = new();
    private readonly Thread _thread;
    private uint _threadId;
    private readonly WndProc _proc;
    private readonly Action<bool> _open;
    private IntPtr _hwnd, _parent, _font, _trayHost, _trayIconHandle;
    private NOTIFYICONDATA _tray;
    private string[] _menuInfo = Array.Empty<string>();
    private bool _refreshing, _refreshSucceeded;
    private int _warningPercent=20, _criticalPercent=10;
    private long _feedbackStarted;
    private readonly string _class = "CodexQuotaTaskbar_" + Environment.ProcessId;
    private string _line1 = "周剩余   —", _line2 = "今日 Token   —", _details = "等待同步";
    private bool _disposed;
    private int _dpi;
    private bool _light;
    private int? _remaining;

    public TaskbarWidget(Action<bool> open)
    {
        var controls = new INITCOMMONCONTROLSEX { size = 8, classes = 0xFF }; InitCommonControlsEx(ref controls);
        _open = open; _proc = Procedure;
        var wc = new WNDCLASS { lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc), hInstance = GetModuleHandle(null), lpszClassName = _class, hCursor = LoadCursor(IntPtr.Zero, new IntPtr(32649)) };
        if (RegisterClass(ref wc) == 0) throw new System.ComponentModel.Win32Exception();
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => { _threadId = GetCurrentThreadId(); PeekMessage(out var init, IntPtr.Zero, 0, 0, 0); ready.Set(); while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0) { while (_actions.TryDequeue(out var action)) action(); TranslateMessage(ref message); DispatchMessage(ref message); } }) { IsBackground = true, Name = "Codex taskbar UI" };
        _thread.Start(); ready.Wait();
    }
    public void Notify(string title, string message)
    {
        if (Queue(() => Notify(title, message))) return;
        EnsureTray();
        _tray.uFlags = 0x10;
        _tray.szInfoTitle = title.Length > 63 ? title[..63] : title;
        _tray.szInfo = message.Length > 255 ? message[..255] : message;
        _tray.dwInfoFlags = 1;
        ShellNotifyIcon(1, ref _tray);
        _tray.uFlags = 7;
    }
    public void BeginRefresh()
    {
        if (Queue(BeginRefresh)) return;
        EnsureTray();
        _refreshing = true; _feedbackStarted = Environment.TickCount64;

        Present();
    }
    public void EndRefresh(bool success)
    {
        if (Queue(() => EndRefresh(success))) return;
        _refreshing = false; _refreshSucceeded = success; _feedbackStarted = Environment.TickCount64;
        SetTimer(_trayHost, new UIntPtr(7), 1100, IntPtr.Zero);
        Present();
    }
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, UIntPtr id);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
    public void Update(int? remaining, string tokens, string details)
    {
        if (Queue(() => Update(remaining, tokens, details))) return;
        _remaining = remaining;
        _line1 = "周剩余   " + (remaining.HasValue ? remaining + "%" : "—");
        var match = System.Text.RegularExpressions.Regex.Match(tokens, @"[\d.]+");
        _line2 = match.Success && double.TryParse(match.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tenThousands)
            ? tenThousands >= 100_000
                ? $"今日 Token {tenThousands / 100_000:0.##}B"
                : $"今日 Token {tenThousands / 100:0.#}M"
            : "今日 Token —";
        _details = details;
        if (_hwnd != IntPtr.Zero) Present();
    }
    public void Sync()
    {
        if (Queue(Sync)) return;
        if (_disposed) return;
        EnsureTray();
        var parent = FindWindow("Shell_TrayWnd", null);
        if (parent == IntPtr.Zero) return;
        if (!IsWindow(_hwnd) || parent != _parent)
        {
            if (IsWindow(_hwnd)) DestroyWindow(_hwnd);
            _parent = parent;
            _hwnd = CreateWindowEx(0x08080080, _class, "Codex 任务栏额度", 0x80000000, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) return;
            SetWindowLong(_hwnd, -16, 0x54000000);
            SetParent(_hwnd, parent);

            _dpi = 0;
        }
        GetClientRect(parent, out var rect);
        var dpi = (int)GetDpiForWindow(parent); if (dpi <= 0) dpi = 96;
        var scale = dpi / 96d;
        var width = (int)Math.Round(204 * scale);
        var left = (int)Math.Round(8 * scale);
        // Widgets occupy the left edge on Windows 11. Left-aligned task buttons
        // cannot safely share that edge, so keep the widget hidden in that mode.
        using var advanced = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        var centered = Convert.ToInt32(advanced?.GetValue("TaskbarAl", 1)) != 0;
        var widgets = Convert.ToInt32(advanced?.GetValue("TaskbarDa", 0)) != 0;
        if (widgets) left += (int)(180 * scale);
        if (!centered || rect.Right < width + left || rect.Bottom > rect.Right) { ShowWindow(_hwnd, 0); return; }
        using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = Convert.ToInt32(personalize?.GetValue("SystemUsesLightTheme", 0)) != 0;
        if (dpi != _dpi)
        {
            if (_font != IntPtr.Zero) DeleteObject(_font);
            _font = CreateFont(-(int)Math.Round(14 * scale), 0, 0, 0, 500, 0, 0, 0, 1, 0, 0, 4, 0, "Microsoft YaHei UI");
            _dpi = dpi;
        }
        _light = light;
        SetWindowPos(_hwnd, IntPtr.Zero, left, 0, width, rect.Bottom, 0x0050);
        Present();
    }
    private IntPtr Procedure(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp)
    {
        if (hwnd == _trayHost && msg == 0x0113 && wp.ToInt64() == 7) { if (!_refreshing && Environment.TickCount64 - _feedbackStarted >= 1100) KillTimer(_trayHost, new UIntPtr(7)); Present(); return IntPtr.Zero; }
        if (hwnd == _trayHost && msg == 0x8002)
        {
            var mouse = (uint)(lp.ToInt64() & 0xffff);
            if (mouse == 0x0205 || mouse == 0x007B) ShowMenu(_trayHost);
            else if (mouse == 0x0202) UiDispatch.Post(() => _open(false));
            return IntPtr.Zero;
        }
        if ((msg == 0x0318 || msg == 0x0317) && hwnd == _hwnd) { GetClientRect(hwnd,out var printRect); DrawContent(wp,printRect); return IntPtr.Zero; }
        if (msg == 0x000F)
        {
            var dc = BeginPaint(hwnd, out var ps);
            GetClientRect(hwnd, out var rect);
            DrawContent(dc, rect);
            EndPaint(hwnd, ref ps); return IntPtr.Zero;
        }
        if (msg == 0x0200 && !_hoverTracking && !_suppressHover)
        {
            _hoverTracking = true;
            var track = new TRACK { size = (uint)Marshal.SizeOf<TRACK>(), flags = 3, hwnd = hwnd, hover = 500 };
            TrackMouseEvent(ref track);
        }
        if (msg == 0x02A1 && !_suppressHover && !_hoverShown)
        {
            _hoverShown = true;
            GetWindowRect(hwnd, out var anchor);
            UiDispatch.Post(() => Hover?.Invoke(_details, true, anchor.Left, anchor.Top));
        }
        if (msg == 0x02A3)
        {
            _hoverTracking = _hoverShown = _suppressHover = false;
            UiDispatch.Post(() => Hover?.Invoke(_details, false, 0, 0));
        }
        if (msg == 0x0201 || msg == 0x0204)
        {
            SuppressHover(hwnd);
            return IntPtr.Zero;
        }        if (msg == 0x0014) return new IntPtr(1);
        if (msg == 0x0021) return new IntPtr(3);
        if (msg == 0x0205) { SuppressHover(hwnd); ShowMenu(hwnd); return IntPtr.Zero; }
        if (msg == 0x0202) { SuppressHover(hwnd); UiDispatch.Post(() => _open(msg == 0x0205)); return IntPtr.Zero; }
        if (msg == 0x0002 && hwnd == _hwnd) { _hwnd = IntPtr.Zero; _hoverTracking = _hoverShown = _suppressHover = false; }
        return DefWindowProc(hwnd, msg, wp, lp);
    }
    private void SuppressHover(IntPtr hwnd)
    {
        _suppressHover = true; _hoverShown = false;
        var track = new TRACK { size = (uint)Marshal.SizeOf<TRACK>(), flags = 0x80000001, hwnd = hwnd };
        TrackMouseEvent(ref track);
        track.flags = 2; TrackMouseEvent(ref track);
        UiDispatch.Post(() => Hover?.Invoke(_details, false, 0, 0));
    }
    public bool IsReady => IsWindow(_hwnd);
    public bool ContainsPointer()
    {
        if (!IsWindow(_hwnd)) return false;
        GetWindowRect(_hwnd, out var rect); GetCursorPos(out var point);
        return point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    public void SetMenuInfo(string[] info)
    {
        if (Queue(() => SetMenuInfo(info))) return;
        _menuInfo = info;
    }
    public void RequestMenu()
    {
        if (Queue(RequestMenu)) return;
        ShowMenu(_trayHost);
    }
    private void ShowMenu(IntPtr hwnd)
    {
        if (_hwnd != IntPtr.Zero) SuppressHover(_hwnd);
        UiDispatch.Post(() => MenuOpening?.Invoke());
        var menu = CreatePopupMenu();
        foreach (var text in _menuInfo) AppendMenu(menu, 2, 0, text);
        AppendMenu(menu, 0x800, 0, "");

        AppendMenu(menu, 0, 5, "展开详情 / 固定面板");
        AppendMenu(menu, 0, 2, "刷新额度");
        AppendMenu(menu, 0, 4, "查看官方用量");
        AppendMenu(menu, 0x800, 0, "");
        AppendMenu(menu, 0, 3, "退出 Codex Watch");
        GetCursorPos(out var p);
        SetForegroundWindow(_trayHost);
        var selected = TrackPopupMenuEx(menu, 0x102, p.X, p.Y, _trayHost, IntPtr.Zero);
        DestroyMenu(menu);
        UiDispatch.Post(() =>
        {
            Hover?.Invoke(_details, false, 0, 0);

            if (selected == 5) _open(false);
            if (selected == 2) RefreshRequested?.Invoke();
            if (selected == 3) ExitRequested?.Invoke();
            if (selected == 4) UsageRequested?.Invoke();
        });
    }
    private void EnsureTray()
    {
        if (_trayHost == IntPtr.Zero)
        {
            _trayHost = CreateWindowEx(0x80, _class, "Codex Watch menu host", 0x80000000, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            var mask = Enumerable.Repeat((byte)255, 32).ToArray();
            var pixels = new byte[16 * 16 * 4];
            for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
            {
                var distance = Math.Sqrt((x - 7.5) * (x - 7.5) + (y - 7.5) * (y - 7.5));
                if (distance < 6.5 && distance > 3.8 && !(x > 8 && y > 5 && y < 10))
                {
                    var offset = (y * 16 + x) * 4;
                    pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 125; pixels[offset + 3] = 255;
                    mask[y * 2 + x / 8] &= (byte)~(1 << (7 - x % 8));
                }
            }
            _trayIconHandle = CreateIcon(GetModuleHandle(null), 16, 16, 1, 32, mask, pixels);
            _tray = new NOTIFYICONDATA { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _trayHost, uID = 1, uFlags = 7, uCallbackMessage = 0x8002, hIcon = _trayIconHandle, szTip = "Codex Watch", szInfo = "", szInfoTitle = "" };
        }
        _tray.szTip = "Codex Watch · " + _line1 + " · " + _line2;
        if (!ShellNotifyIcon(1, ref _tray)) ShellNotifyIcon(0, ref _tray);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize; public IntPtr hWnd; public uint uID,uFlags,uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string szTip;
        public uint dwState,dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW")] private static extern bool ShellNotifyIcon(uint message,ref NOTIFYICONDATA data);
    [DllImport("user32.dll")] private static extern IntPtr CreateIcon(IntPtr instance,int width,int height,byte planes,byte bits,byte[] andMask,byte[] xorMask);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu,uint flags,uint id,string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu,uint flags,int x,int y,IntPtr hwnd,IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    public void SetThresholds(int warning,int critical)
    {
        if(Queue(()=>SetThresholds(warning,critical))) return;
        _warningPercent=warning; _criticalPercent=critical;
        if(_hwnd!=IntPtr.Zero) Present();
    }
    private static uint Rgb(byte red,byte green,byte blue) => (uint)(red | green<<8 | blue<<16);
    private void DrawContent(IntPtr dc, RECT rect)
    {
        var background=CreateSolidBrush(_light ? 0xF3F3F3u : 0x202020u);
        FillRect(dc,ref rect,background);DeleteObject(background);
        var scale=Math.Max(96,_dpi)/96d;
        int Px(double value)=>(int)Math.Round(value*scale);
        var bounds=new RECT { Left=Px(2),Right=rect.Right-Px(2),Top=Math.Max(Px(2),(rect.Bottom-Px(32))/2) };
        bounds.Bottom=Math.Min(rect.Bottom-Px(2),bounds.Top+Px(32));
        var oldFont=SelectObject(dc,_font);
        var quotaText="额度"+(_remaining.HasValue?_remaining+"%":"—");
        GetTextExtentPoint32(dc,quotaText,quotaText.Length,out var quotaSize);
        var split=bounds.Left+quotaSize.X+Px(14);
        var low=_remaining.HasValue && _remaining<=_warningPercent;
        var critical=_remaining.HasValue && _remaining<=_criticalPercent;
        var baseColor=_light ? Rgb(248,251,255) : Rgb(45,53,64);
        var quotaBackground=critical ? (_light?Rgb(253,231,231):Rgb(81,48,53)) : low ? (_light?Rgb(255,241,217):Rgb(89,69,44)) : (_light?Rgb(228,239,254):Rgb(45,69,99));
        var quotaInk=critical ? (_light?Rgb(180,75,81):Rgb(240,141,147)) : low ? (_light?Rgb(166,107,30):Rgb(233,189,122)) : (_light?Rgb(40,103,174):Rgb(175,208,251));
        var outline=_light ? Rgb(208,220,232) : Rgb(70,85,104);
        var tokenInk=_light ? Rgb(54,70,90) : Rgb(225,232,241);
        var radius=Px(8);
        var region=CreateRoundRectRgn(bounds.Left,bounds.Top,bounds.Right+1,bounds.Bottom+1,radius*2,radius*2);
        SelectClipRgn(dc,region);
        var brush=CreateSolidBrush(baseColor);FillRect(dc,ref bounds,brush);DeleteObject(brush);
        var left=bounds;left.Right=split;
        brush=CreateSolidBrush(quotaBackground);FillRect(dc,ref left,brush);DeleteObject(brush);
        var divider=bounds;divider.Left=split;divider.Right=split+Math.Max(1,Px(1));
        brush=CreateSolidBrush(outline);FillRect(dc,ref divider,brush);DeleteObject(brush);
        SelectClipRgn(dc,IntPtr.Zero);DeleteObject(region);
        var pen=CreatePen(0,Math.Max(1,Px(1)),outline);
        var oldPen=SelectObject(dc,pen);var oldBrush=SelectObject(dc,GetStockObject(5));
        RoundRect(dc,bounds.Left,bounds.Top,bounds.Right,bounds.Bottom,radius*2,radius*2);
        SelectObject(dc,oldBrush);SelectObject(dc,oldPen);DeleteObject(pen);
        SetBkMode(dc,1);
        var first=bounds;first.Left+=Px(7);first.Right=split-Px(7);
        SetTextColor(dc,quotaInk);
        DrawText(dc,quotaText,-1,ref first,0x8824);
        var second=bounds;second.Left=split+Px(11);second.Right-=Px(8);
        var feedback=_feedbackStarted>0 && !_refreshing && Environment.TickCount64-_feedbackStarted<1100;
        var text=_refreshing ? "正在同步…" : feedback ? (_refreshSucceeded?"已更新":"同步失败") : _line2.Replace("今日 Token ","Token ");
        SetTextColor(dc,!_refreshing && feedback && !_refreshSucceeded ? (_light?Rgb(166,107,30):Rgb(233,189,122)) : tokenInk);
        DrawText(dc,text,-1,ref second,0x8824);
        if(oldFont!=IntPtr.Zero) SelectObject(dc,oldFont);
    }
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern bool GetTextExtentPoint32(IntPtr dc,string text,int length,out POINT size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("gdi32.dll")] private static extern int SelectClipRgn(IntPtr dc,IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style,int width,uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int id);
    [DllImport("gdi32.dll")] private static extern bool RoundRect(IntPtr dc,int left,int top,int right,int bottom,int width,int height);
    private void Present()
    {
        if (_hwnd == IntPtr.Zero) return;
        GetClientRect(_hwnd, out var rect);
        if (rect.Right <= 0 || rect.Bottom <= 0) return;
        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        var info = new BITMAPINFO { size = 40, width = rect.Right, height = -rect.Bottom, planes = 1, bits = 32 };
        var bitmap = CreateDIBSection(screen, ref info, 0, out var pixels, IntPtr.Zero, 0);
        var old = SelectObject(dc, bitmap);
        DrawContent(dc, rect);
        var size = new POINT { X = rect.Right, Y = rect.Bottom };
        var origin = new POINT();
        GdiFlush();
        var data = new byte[rect.Right * rect.Bottom * 4]; Marshal.Copy(pixels, data, 0, data.Length);
        var bg = _light ? (byte)243 : (byte)32;
        // The capsule is opaque; preserve text antialiasing against each segment's
        // surface. Only pixels outside the rounded capsule become transparent.
        for (var i=0;i<data.Length;i+=4)
        {
            if(data[i]==bg && data[i+1]==bg && data[i+2]==bg)
            { data[i]=data[i+1]=data[i+2]=0; data[i+3]=1; }
            else data[i+3]=255;
        }
        Marshal.Copy(data, 0, pixels, data.Length);
        var blend = new BLEND { alpha = 255, format = 1 };
        UpdateLayeredWindowAlpha(_hwnd, screen, IntPtr.Zero, ref size, dc, ref origin, 0, ref blend, 2);
        SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen);
    }
    [StructLayout(LayoutKind.Sequential)] private struct INITCOMMONCONTROLSEX { public uint size,classes; }
    [DllImport("comctl32.dll")] private static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX c);
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public uint size; public int width,height; public ushort planes,bits; public uint compression,imageSize; public int x,y; public uint used,important,color; }
    [StructLayout(LayoutKind.Sequential, Pack=1)] private struct BLEND { public byte op,flags,alpha,format; }
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc,ref BITMAPINFO info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("user32.dll",EntryPoint="UpdateLayeredWindow")] private static extern bool UpdateLayeredWindowAlpha(IntPtr h,IntPtr dst,IntPtr pos,ref POINT size,IntPtr src,ref POINT origin,uint key,ref BLEND blend,uint flags);
    [StructLayout(LayoutKind.Sequential)] private struct TRACK { public uint size,flags; public IntPtr hwnd; public uint hover; }
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TRACK t);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc,int w,int h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dst,IntPtr pos,ref POINT size,IntPtr src,ref POINT origin,uint key,IntPtr blend,uint flags);
    public void Dispose() { if (Queue(Dispose)) return; _disposed = true; ShellNotifyIcon(2, ref _tray); if (IsWindow(_trayHost)) DestroyWindow(_trayHost); if (_trayIconHandle != IntPtr.Zero) DestroyIcon(_trayIconHandle); if (IsWindow(_hwnd)) DestroyWindow(_hwnd); if (_font != IntPtr.Zero) DeleteObject(_font); UnregisterClass(_class, GetModuleHandle(null)); PostQuitMessage(0); }
    private bool Queue(Action action) { if (Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId) return false; _actions.Enqueue(action); PostThreadMessage(_threadId, 0x8001, IntPtr.Zero, IntPtr.Zero); return true; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam,lParam; public uint time; public POINT point; public uint extra; }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg,IntPtr h,uint min,uint max,uint remove);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg,IntPtr h,uint min,uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll",EntryPoint="DispatchMessageW")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id,uint msg,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASS { public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance,hIcon,hCursor,hbrBackground; public string? lpszMenuName; public string lpszClassName; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PAINTSTRUCT { public IntPtr hdc; public int erase; public RECT rect; public int restore,update; [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] public byte[] reserved; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct TOOLINFO { public uint cbSize,uFlags; public IntPtr hwnd,uId; public RECT rect; public IntPtr hinst; [MarshalAs(UnmanagedType.LPWStr)] public string lpszText; public IntPtr lParam,reserved; }
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h,uint key,byte alpha,uint flags);
    [DllImport("user32.dll", EntryPoint="SetWindowLongW")] private static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr h,IntPtr parent);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern ushort RegisterClass(ref WNDCLASS c);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool UnregisterClass(string c,IntPtr i);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex,string cls,string? name,uint style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string cls,string? name);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h,int cmd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h,out RECT r);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr h,IntPtr r,bool erase);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr h,out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr h,ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc,ref RECT r,IntPtr brush);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int DrawText(IntPtr dc,string text,int length,ref RECT r,uint flags);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance,IntPtr name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageW")] private static extern IntPtr SendMessageTool(IntPtr h,uint m,IntPtr w,ref TOOLINFO t);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr o);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc,uint c);
    [DllImport("gdi32.dll")] private static extern uint GetTextColor(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc,int m);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr CreateFont(int h,int w,int e,int o,int weight,uint italic,uint underline,uint strike,uint charset,uint op,uint clip,uint quality,uint pitch,string face);
}
