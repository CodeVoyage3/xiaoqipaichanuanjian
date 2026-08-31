using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace StoreExpiryInspector.UI;

public sealed class WindowsTrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const int CallbackMessage = 0x8001;
    private const int LeftButtonDoubleClick = 0x0203;
    private const int RightButtonUp = 0x0205;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconHandle = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint AddIcon = 0x00000000;
    private const uint DeleteIcon = 0x00000002;
    private const uint SetVersion = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private static readonly IntPtr ApplicationIconId = new(32512);

    private readonly Action _open;
    private readonly Action _exit;
    private readonly HwndSource _source;
    private readonly ContextMenu _menu;
    private NotifyIconData _data;
    private bool _disposed;

    public WindowsTrayIcon(Window window, Action open, Action exit)
    {
        ArgumentNullException.ThrowIfNull(window);
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));

        var handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(handle)
            ?? throw new InvalidOperationException("Unable to attach the tray icon to the main window.");
        _source.AddHook(WindowHook);
        _menu = CreateMenu();
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = handle,
            IconId = IconId,
            Flags = NotifyIconMessage | NotifyIconHandle | NotifyIconTip,
            CallbackMessage = CallbackMessage,
            IconHandle = LoadIcon(IntPtr.Zero, ApplicationIconId),
            Tip = "门店效期排查软件",
            Info = string.Empty,
            InfoTitle = string.Empty
        };

        if (_data.IconHandle == IntPtr.Zero || !ShellNotifyIcon(AddIcon, ref _data))
        {
            _source.RemoveHook(WindowHook);
            throw new Win32Exception("Unable to create the Windows tray icon.");
        }

        _data.Version = NotifyIconVersion4;
        ShellNotifyIcon(SetVersion, ref _data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _menu.IsOpen = false;
        ShellNotifyIcon(DeleteIcon, ref _data);
        _source.RemoveHook(WindowHook);
    }

    private ContextMenu CreateMenu()
    {
        var openItem = new MenuItem { Header = "打开" };
        openItem.Click += (_, _) => _open();
        var exitItem = new MenuItem { Header = "退出应用" };
        exitItem.Click += (_, _) => _exit();
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private IntPtr WindowHook(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        var mouseMessage = unchecked((int)(longParameter.ToInt64() & 0xFFFF));
        if (mouseMessage == LeftButtonDoubleClick)
        {
            _open();
            handled = true;
        }
        else if (mouseMessage == RightButtonUp)
        {
            _menu.IsOpen = true;
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    private static bool ShellNotifyIcon(uint message, ref NotifyIconData data) =>
        Shell_NotifyIcon(message, ref data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint IconId;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint Version;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIconHandle;
    }
}
