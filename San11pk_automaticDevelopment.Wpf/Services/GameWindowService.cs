using System.Diagnostics;
using San11pk_automaticDevelopment.Wpf.Interop;

namespace San11pk_automaticDevelopment.Wpf.Services;

internal static class GameWindowService
{
    public static string NormalizeProcessName(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public static bool IsGameForeground(uint gameProcessId)
    {
        nint foreground = Win32.GetForegroundWindow();
        return foreground != 0 &&
               Win32.GetWindowThreadProcessId(foreground, out uint currentProcessId) != 0 &&
               currentProcessId == gameProcessId;
    }

    public static bool TryGetForegroundGame(string processName, out nint hWnd, out uint processId)
    {
        hWnd = Win32.GetForegroundWindow();
        processId = 0;
        if (hWnd == 0 || Win32.GetWindowThreadProcessId(hWnd, out processId) == 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return string.Equals(process.ProcessName, NormalizeProcessName(processName),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool TryFindGame(string processName, out nint hWnd, out uint processId)
    {
        hWnd = 0;
        processId = 0;
        var processes = Process.GetProcessesByName(NormalizeProcessName(processName));
        try
        {
            foreach (var process in processes)
            {
                if (process.MainWindowHandle == 0)
                    continue;
                hWnd = process.MainWindowHandle;
                processId = checked((uint)process.Id);
                return true;
            }
            return false;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
