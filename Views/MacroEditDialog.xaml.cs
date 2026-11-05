using System.IO;
using System.Windows;
using System.Windows.Input;
using FDLike2026.Services;

namespace FDLike2026.Views;

/// <summary>
/// F2 のマクロ 1 件を編集する「マクロの設定」ダイアログ（WinFM2008 準拠）。
/// タイトル(T) / マクロ(M) / 参照(R)... と、下に $ マクロの一覧をスクロール表示する。
/// </summary>
public partial class MacroEditDialog : Window
{
    /// <summary>決定した設定行（「タイトル,コマンド」）。キャンセルなら null。</summary>
    public string? Result { get; private set; }

    private readonly string _currentDir;

    public MacroEditDialog(Window owner, string slotKey, string line, string currentDir)
    {
        InitializeComponent();
        Owner = owner;
        Title = $"マクロの設定 - {slotKey}";
        _currentDir = currentDir;
        var (title, command) = MacroExpander.SplitTitle(line);
        TitleBox.Text = title;
        MacroBox.Text = command;
        HelpText.Text = HelpBody;
        Loaded += (_, _) => { TitleBox.Focus(); TitleBox.SelectAll(); };
    }

    /// <summary>決定した行を返す（キャンセルなら null）。</summary>
    public static string? Show(Window owner, string slotKey, string line, string currentDir)
    {
        var dlg = new MacroEditDialog(owner, slotKey, line, currentDir);
        dlg.ShowDialog();
        return dlg.Result;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Result = null; Close(); e.Handled = true; return; }
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { Ok(); e.Handled = true; return; }
        // 説明欄のスクロール（入力中でも PageUp/PageDown は効く）
        if (e.Key == Key.PageDown) { HelpScroll.PageDown(); e.Handled = true; return; }
        if (e.Key == Key.PageUp) { HelpScroll.PageUp(); e.Handled = true; return; }
        if (AccessKeys.TryHandle(this, e)) { e.Handled = true; return; }   // (_X) の X キー（Alt なし）
    }

    private void Ok()
    {
        var title = TitleBox.Text.Trim().Replace(",", "，");   // 区切りの , はタイトルに使えないので全角に
        var cmd = MacroBox.Text.Trim();
        if (cmd.Length == 0)
        {
            if (title.Length == 0) { Result = ""; Close(); return; }   // 両方空 = 削除
            MessageBox.Show(this, "マクロ（実行するコマンド）を入力してください。", "マクロの設定", MessageBoxButton.OK, MessageBoxImage.Information);
            MacroBox.Focus();
            return;
        }
        if (title.Length == 0) title = Path.GetFileNameWithoutExtension(cmd.Trim('"').Split(' ')[0]);
        Result = title.Length == 0 ? cmd : title + "," + cmd;
        Close();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Ok();
    private void Cancel_Click(object sender, RoutedEventArgs e) { Result = null; Close(); }

    /// <summary>参照(R)...: 実行ファイルを選び、$Q で囲んでマクロ欄のカーソル位置に挿入する。</summary>
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "実行するプログラムの参照",
            Filter = "プログラム (*.exe;*.com;*.bat;*.cmd)|*.exe;*.com;*.bat;*.cmd|すべてのファイル (*.*)|*.*",
            InitialDirectory = Directory.Exists(_currentDir) ? _currentDir : "",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        var path = dlg.FileName;
        var ins = path.Contains(' ') ? "$Q" + path + "$Q" : path;
        if (MacroBox.Text.Trim().Length == 0)
        {
            MacroBox.Text = ins + " $Q$P\\$C$Q";
            if (TitleBox.Text.Trim().Length == 0) TitleBox.Text = Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            int pos = MacroBox.CaretIndex;
            MacroBox.Text = MacroBox.Text.Insert(pos, ins);
            MacroBox.CaretIndex = pos + ins.Length;
        }
        MacroBox.Focus();
    }

    private const string HelpBody =
@"マクロには次の $ 記号が使えます。実行時に置き換えられます。

$P  カレントパス（例: C:\WORK\DOC）
$p  同上（WinFM では 8.3 形式。FDLike では $P と同じ）
$D  カレントドライブ（例: C:）
$C  カーソル位置のファイル名（例: READ.ME）
$c  同上（WinFM では 8.3 形式。FDLike では $C と同じ）
$X  カーソル位置のファイル名から拡張子を除いたもの（例: READ）
$x  同上（FDLike では $X と同じ）
$T  タグ（マーク）したファイル名を空白で区切って並べたもの
$t  タグしたファイルの拡張子なしの名前を並べたもの
$U  $T と同じ（WinFM では 8.3 形式）
$u  $t と同じ（WinFM では 8.3 形式）
$F  タグしたファイルのフルパスを "" で囲んで並べたもの（FDLike 拡張）
$O  反対側ペインのパス（FDLike 拡張。2 画面ファイラーならではの指定）
$OC 反対側ペインのカーソル位置のファイル名（FDLike 拡張）
$Q  ダブルクォート（""）。空白を含むパスを囲むのに使います
$Z  カンマ（,）。タイトルとの区切りに使うため、コマンド中のカンマはこれで書きます
$R  実行時にパラメータの入力を求め、入力した文字列に置き換えます
$SN タグしたファイルの個数
$Ss タグしたファイルの合計サイズ（バイト）
$$  $ そのもの
;   複数のコマンドを続けて実行します（例: cmd /c dir ; notepad）

日付・時刻（実行した時点）
$YY 西暦 4 桁（例: 2026）    $Yy 西暦下 2 桁（例: 26）
$YG 和暦（頭文字付き、例: R08）  $Yg 和暦（数字のみ、例: 08）
$YM 月 2 桁    $YD 日 2 桁    $Yh 時 2 桁    $Ym 分 2 桁    $Ys 秒 2 桁

タグしたファイルがないときは、カーソル位置のファイルを 1 件タグしたものとして扱います。
タイトルを空にすると、コマンドの先頭のプログラム名がタイトルになります。
タイトル・マクロの両方を空にして OK すると、このスロットの登録を削除します。

例:
  メモ帳で開く       notepad.exe $Q$P\$C$Q
  VS Code で開く     code $F
  左右のフォルダ比較  WinMergeU.exe /r $Q$P$Q $Q$O$Q
    （WinMergeU.exe は PATH に無くても、設定の比較ツールか標準のインストール先から自動で探します）
  反対側の同名と比較  WinMergeU.exe $Q$P\$C$Q $Q$O\$C$Q
  日付付きで複製     cmd /c copy $Q$C$Q $Q$X_$YY$YM$YD.bak$Q
  grep して表示      cmd /c findstr /n $R $T & pause

WinFM2008 の $e $G $M $m $L $l $k（DOSRUN / 環境変数 / ドライブ指定など）は FDLike2026 では使えません。";
}
