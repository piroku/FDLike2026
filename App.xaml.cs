using System.IO;
using System.Windows;
using System.Windows.Input;

namespace FDLike2026;

public partial class App : Application
{
    private bool _inHandler;

    public static string LogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FDLike2026", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 診断用: FDLike2026.exe --extract <文書> <出力txt>  … Office / PDF からのテキスト抽出結果をファイルに書いて終了
        if (e.Args.Length >= 3 && e.Args[0] == "--extract")
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var text = Services.DocumentText.Extract(e.Args[1]);
                sw.Stop();
                File.WriteAllText(e.Args[2], text is null ? $"(抽出できず) {sw.ElapsedMilliseconds} ms\n" : $"({sw.ElapsedMilliseconds} ms, {text.Length} 文字)\n{text}", System.Text.Encoding.UTF8);
            }
            catch (Exception ex) { try { File.WriteAllText(e.Args[2], "ERROR: " + ex); } catch { } }
            Shutdown();
            return;
        }
        // すべてのウィンドウ（ダイアログ含む）にアプリアイコンを付ける
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is Window w && w.Icon is null)
            {
                try { w.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/app.ico")); } catch { }
            }
        }));
        // 日本語 IME が ON のまま一覧やボタンにフォーカスがあると、E や V などのキーが IME に取られて
        // Key.ImeProcessed になり、ショートカットが効かない。文字入力欄以外では IME を無効にする
        // （入力欄に戻れば IME はそのまま使える。IME の ON/OFF 自体は変えない）。
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewGotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, args) =>
        {
            if (args.NewFocus is not DependencyObject d) return;
            bool text = d is System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.PasswordBox
                     || d is System.Windows.Controls.ComboBox { IsEditable: true }
                     || d is System.Windows.Controls.FlowDocumentScrollViewer;
            if (!text) InputMethod.SetIsInputMethodEnabled(d, false);
        }));
        // 設定ファイルのフォント・配色を反映（MainWindow 生成前）
        var settings = Services.Settings.Load();
        Services.ListModeService.Apply(settings.ListModes[Math.Clamp(settings.StartupListMode, 0, 4)], settings.DirLabel, settings.Theme);
        Services.ThemeService.Apply(settings.Theme);
        Services.ThemeService.ApplyIcons(settings.ShowIcons);

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Log(args.Exception);
            // MessageBox はメッセージポンプを回すので、レイアウト中の例外が再帰すると無限ループになる。再入は抑止。
            if (_inHandler) return;
            _inHandler = true;
            try
            {
                MessageBox.Show((RuntimeUpdatedMessage(args.Exception) ?? args.Exception.Message) + "\n\n詳細: " + LogPath, "FDLike2026 - エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _inHandler = false; }
        };
    }

    /// <summary>
    /// FDLike2026 を起動したまま Windows Update などで .NET ランタイムが更新されると、実行中の版のフォルダが削除され、
    /// まだ読み込んでいなかった DLL（右クリックメニューの Accessibility、ごみ箱の Microsoft.VisualBasic.Core など）が
    /// 「ファイルが見つかりません」になる。その場合に出す案内文を返す（該当しなければ null）。
    /// 判定: 読めなかったのがアセンブリで、実行中のランタイムのフォルダには無く、別の版のフォルダにはある。
    /// </summary>
    public static string? RuntimeUpdatedMessage(Exception? ex)
    {
        try
        {
            for (int depth = 0; ex is not null && depth < 8; ex = ex.InnerException, depth++)
            {
                string? name = ex switch { FileNotFoundException f => f.FileName, FileLoadException l => l.FileName, _ => null };
                if (string.IsNullOrEmpty(name)) continue;
                int comma = name.IndexOf(',');
                if (comma <= 0) continue;   // 「名前, Version=...」の形でなければ普通のファイル
                string dll = name[..comma].Trim() + ".dll";
                // .NET 本体（Microsoft.NETCore.App\<版>）と WPF（Microsoft.WindowsDesktop.App\<版>）のフォルダ
                foreach (var dir in new[] { Path.GetDirectoryName(typeof(object).Assembly.Location), Path.GetDirectoryName(typeof(Window).Assembly.Location) })
                {
                    if (string.IsNullOrEmpty(dir) || File.Exists(Path.Combine(dir, dll))) continue;
                    var parent = Path.GetDirectoryName(dir);
                    if (parent is null || !Directory.Exists(parent)) continue;
                    foreach (var other in Directory.GetDirectories(parent))
                    {
                        if (!string.Equals(other, dir, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(other, dll)))
                            return ".NET が更新されたため、この操作に必要な部品を読み込めません。\nFDLike2026 を終了して、起動し直してください。";
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { }
    }
}
