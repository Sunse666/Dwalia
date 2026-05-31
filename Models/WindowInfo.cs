using Dwalia.Win32;

namespace Dwalia.Models;

public class WindowInfo
{
    public nint OriginalStyle { get; init; }
    public nint OriginalExStyle { get; init; }
    public NativeMethods.RECT OriginalRect { get; init; }
    public bool WasVisible { get; init; }
}
