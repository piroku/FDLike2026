using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>X: コマンドの実行（WinFM2008 のダイアログ構成に準拠）。</summary>
public partial class ExecDialog : Window
{
    private readonly Settings _settings;

    public string App => (AppBox.Text ?? "").Trim();
    public string Args => (ArgBox.Text ?? "").Trim();
    public bool Dos => DosCheck.IsChecked == true;
    public bool NoPause => NoPauseCheck.IsChecked == true;
    public bool RunAs => RunAsCheck.IsChecked == true;

    public ExecDialog(Window owner, Settings settings, string? currentFileName)
    {
        InitializeComponent();
        Owner = owner;
        _settings = settings;
        AppBox.ItemsSource = settings.ExecAppHistory;
        ArgBox.ItemsSource = settings.ExecArgHistory;
        AppBox.Text = currentFileName ?? (settings.ExecAppHistory.FirstOrDefault() ?? "");
        DosCheck.IsChecked = settings.ExecDos;
        NoPauseCheck.IsChecked = settings.ExecNoPause;
        Loaded += (_, _) => { AppBox.Focus(); };
    }

    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Ok_Click(sender, e); e.Handled = true; }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (App.Length == 0) return;
        _settings.ExecDos = Dos;
        _settings.ExecNoPause = NoPause;
        Push(_settings.ExecAppHistory, App);
        if (Args.Length > 0) Push(_settings.ExecArgHistory, Args);
        _settings.Save();
        DialogResult = true;
    }

    private static void Push(List<string> list, string v)
    {
        list.RemoveAll(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, v);
        if (list.Count > 20) list.RemoveRange(20, list.Count - 20);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // (_X) の X キー（Alt なし）
        if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    /// <summary>cmd に渡すとき "..." で囲む必要があるか。全角スペース（U+3000）など半角以外の空白も区切りになるので含める。</summary>
    private static bool NeedsQuote(string s)
        => s.Length > 0 && !s.StartsWith('"')
           && (s.Any(char.IsWhiteSpace) || s.IndexOfAny(new[] { '(', ')', '&', '^', ';', ',', '=', '%', '!' }) >= 0);

    /// <summary>ダイアログの内容で実行する。app / args は展開済みのもの。</summary>
    public static void Execute(string app, string args, string dir, bool dos, bool noPause, bool runAs)
    {
        // カレントフォルダのファイル名ならフルパスにして、関連付けで確実に開けるようにする
        try
        {
            var full = System.IO.Path.Combine(dir, app.Trim('"'));
            if (!System.IO.Path.IsPathRooted(app.Trim('"')) && (System.IO.File.Exists(full) || System.IO.Directory.Exists(full))) app = full;
        }
        catch { }

        if (dos)
        {
            // DOS コマンド: cmd 経由。終了後に一時停止（DosRun 相当）
            // 空白や ( ) & などを含む名前は "..." で囲まないと cmd が途中で切ってしまう
            var quotedApp = NeedsQuote(app) ? $"\"{app}\"" : app;
            var line = args.Length > 0 ? $"{quotedApp} {args}" : quotedApp;
            var tail = noPause ? "" : " & echo. & pause";
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{line}{tail}\"")
            {
                UseShellExecute = true,
                WorkingDirectory = dir,
                Verb = runAs ? "runas" : "",
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo(app, args)
            {
                UseShellExecute = true,
                WorkingDirectory = dir,
                Verb = runAs ? "runas" : "",
            });
        }
    }
}
