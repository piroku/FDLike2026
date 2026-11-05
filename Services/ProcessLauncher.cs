using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FDLike2026.Services;

/// <summary>
/// 「起動中」のマウスカーソル（矢印＋リング）を出さずにプロセスを起動する。
/// ShellExecute / CreateProcess は既定で STARTF_FORCEONFEEDBACK 相当の動きをし、起動したアプリが
/// メッセージループに入るか数秒経つまで、起動元（FDLike）のウィンドウ上でビジーカーソルが続く。
/// code.cmd のようなランチャ経由だとその合図が来ず、しばらく ● のままになるので、
/// STARTF_FORCEOFFFEEDBACK を付けて CreateProcess する。
/// </summary>
public static class ProcessLauncher
{
    private const uint STARTF_FORCEOFFFEEDBACK = 0x00000080;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
        public uint dwFlags; public short wShowWindow; public short cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string? lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    /// <summary>
    /// exe（または PATH 上のコマンド / .cmd / .bat）を引数付きで起動する。見つからなければ ShellExecute にフォールバック。
    /// </summary>
    public static void Start(string command, string args, string? workDir = null)
    {
        var full = Resolve(command);
        if (full is null)
        {
            Process.Start(new ProcessStartInfo(command, args) { UseShellExecute = true, WorkingDirectory = workDir ?? "" });
            return;
        }
        var ext = Path.GetExtension(full).ToLowerInvariant();
        string cmdLine;
        uint flags = CREATE_UNICODE_ENVIRONMENT;
        if (ext is ".cmd" or ".bat")
        {
            // バッチは cmd.exe 経由（窓は出さない）
            cmdLine = $"\"{Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"}\" /c \"\"{full}\" {args}\"";
            flags |= CREATE_NO_WINDOW;
        }
        else if (ext is ".exe" or ".com")
        {
            cmdLine = $"\"{full}\" {args}";
        }
        else
        {
            // 関連付けで開くもの（.ps1 など）は ShellExecute に任せる
            Process.Start(new ProcessStartInfo(full, args) { UseShellExecute = true, WorkingDirectory = workDir ?? "" });
            return;
        }

        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), dwFlags = STARTF_FORCEOFFFEEDBACK };
        if (!CreateProcessW(null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero,
                            string.IsNullOrEmpty(workDir) ? null : workDir, ref si, out var pi))
        {
            // 失敗したら従来どおり
            Process.Start(new ProcessStartInfo(full, args) { UseShellExecute = true, WorkingDirectory = workDir ?? "" });
            return;
        }
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }

    /// <summary>コマンド名をフルパスに。絶対パスならそのまま、そうでなければ PATH と PATHEXT で探す。</summary>
    public static string? Resolve(string command)
    {
        command = command.Trim().Trim('"');
        if (command.Length == 0) return null;
        if (Path.IsPathRooted(command)) return File.Exists(command) ? command : null;
        if (command.Contains('\\') || command.Contains('/')) return File.Exists(command) ? Path.GetFullPath(command) : null;

        var exts = Path.HasExtension(command) ? new[] { "" }
                 : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM").Split(';', StringSplitOptions.RemoveEmptyEntries);
        var dirs = new List<string> { Environment.CurrentDirectory };
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var d in dirs)
        {
            foreach (var e in exts)
            {
                try
                {
                    var p = Path.Combine(d.Trim('"'), command + e.ToLowerInvariant());
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
        }
        return null;
    }
}
