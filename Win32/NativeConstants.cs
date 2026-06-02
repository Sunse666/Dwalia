namespace Dwalia.Win32;

internal static class NativeConstants
{
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_FOCUS = 0x8005;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;

    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNTHREAD = 0x0001;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint WINEVENT_INCONTEXT = 0x0004;

    public const int OBJID_WINDOW = 0;
    public const int OBJID_CLIENT = unchecked((int)0xFFFFFFFC);

    public const uint VK_RETURN = 0x0D;
    public const uint VK_SPACE = 0x20;
    public const uint VK_LEFT = 0x25;
    public const uint VK_UP = 0x26;
    public const uint VK_RIGHT = 0x27;
    public const uint VK_DOWN = 0x28;
    public const uint VK_TAB = 0x09;
    public const uint VK_ESCAPE = 0x1B;

    public const uint VK_0 = 0x30;
    public const uint VK_1 = 0x31;
    public const uint VK_2 = 0x32;
    public const uint VK_3 = 0x33;
    public const uint VK_4 = 0x34;
    public const uint VK_5 = 0x35;
    public const uint VK_6 = 0x36;
    public const uint VK_7 = 0x37;
    public const uint VK_8 = 0x38;
    public const uint VK_9 = 0x39;

    public const uint VK_A = 0x41;
    public const uint VK_B = 0x42;
    public const uint VK_C = 0x43;
    public const uint VK_D = 0x44;
    public const uint VK_E = 0x45;
    public const uint VK_F = 0x46;
    public const uint VK_G = 0x47;
    public const uint VK_H = 0x48;
    public const uint VK_I = 0x49;
    public const uint VK_J = 0x4A;
    public const uint VK_K = 0x4B;
    public const uint VK_L = 0x4C;
    public const uint VK_M = 0x4D;
    public const uint VK_N = 0x4E;
    public const uint VK_O = 0x4F;
    public const uint VK_P = 0x50;
    public const uint VK_Q = 0x51;
    public const uint VK_R = 0x52;
    public const uint VK_S = 0x53;
    public const uint VK_T = 0x54;
    public const uint VK_U = 0x55;
    public const uint VK_V = 0x56;
    public const uint VK_W = 0x57;
    public const uint VK_X = 0x58;
    public const uint VK_Y = 0x59;
    public const uint VK_Z = 0x5A;

    public const uint VK_LWIN = 0x5B;
    public const uint VK_RWIN = 0x5C;
    public const uint VK_OEM_3 = 0xC0;
    public const uint VK_OEM_COMMA = 0xBC;
    public const uint VK_OEM_PERIOD = 0xBE;
    public const uint VK_LCONTROL = 0xA2;
    public const uint VK_RCONTROL = 0xA3;
    public const uint VK_LMENU = 0xA4;
    public const uint VK_RMENU = 0xA5;
    public const uint VK_LSHIFT = 0xA0;
    public const uint VK_RSHIFT = 0xA1;

    public const int WH_KEYBOARD_LL = 13;

    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;

    public const uint LLKHF_ALTDOWN = 0x20;

    public const int WM_DWALIA_COMMAND = 0x8001;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_WINDOWPOSCHANGING = 0x0046;

    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const int HTTRANSPARENT = -1;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const uint DWMWA_BORDER_COLOR = 34;
    public const int DWM_ACTIVE_BORDER = unchecked((int)0xFF7AA2F7);
    public const int DWM_INACTIVE_BORDER = unchecked((int)0xFF1A1B26);

    public static int ParseDwmColor(string hex)
    {
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return unchecked((int)(0xFF000000 | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
        }
        catch { return DWM_ACTIVE_BORDER; }
    }
}
