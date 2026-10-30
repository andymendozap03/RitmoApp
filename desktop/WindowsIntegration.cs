using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ritmo;
internal static class WindowsIntegration
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    internal static void Register() => SetCurrentProcessExplicitAppUserModelID("Ritmo.Desktop");
    internal static void DarkTitlebar(Window window)
    {
        int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
    }
}
