using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FDLike2026.Views;

public sealed record HelpRow(string Key, string Desc);
public sealed record HelpSection(string Title, string Note, IReadOnlyList<HelpRow> Rows);

/// <summary>F1 / ?: キー操作一覧。左のカテゴリを選ぶと右に「キー｜動作」の表。下の欄で全カテゴリを横断して絞り込める。</summary>
public partial class HelpWindow : Window
{
    private static readonly IReadOnlyList<HelpSection> Sections = new[]
    {
        new HelpSection("カーソル / 移動", "WinFM2008 互換のキー配置です。", new[]
        {
            new HelpRow("↑  ↓", "カーソルを上下に移動"),
            new HelpRow("PgUp  PgDn", "1 ページ上下"),
            new HelpRow("End  /  Shift+.", "最後のファイルへ"),
            new HelpRow("Shift+,  /  Ctrl+Home", "先頭のファイルへ"),
            new HelpRow("←  →  /  Tab", "左右のウィンドウを切り替え"),
            new HelpRow("Enter", "フォルダへ移動 / ファイルを開く（F4 の拡張子の対応 → システムの関連付け）"),
            new HelpRow("BackSpace", "親フォルダへ"),
            new HelpRow("￥ (\\)", "ドライブのルートへ"),
            new HelpRow("1 ～ 9,  0", "A ～ I, J ドライブへ"),
            new HelpRow("L", "装置（ドライブ）の選択"),
            new HelpRow("H", "パスを入力してフォルダ変更（UNC・環境変数・file:// 形式・\"…\" 付き・ファイルのパス（そのフォルダへ移動してファイルにカーソル）可）"),
            new HelpRow("J", "JumpFolder（登録フォルダへ 1 キーで移動。クリックは選択、ダブルクリック / Enter でジャンプ。上のパス欄に Teams 等から送られたパスを貼り付けて Enter すると、登録せずに 1 回だけ移動。Ctrl+V はクリップボードのパスへ即移動、開いたときにクリップボードがパスなら黄色で入っています）"),
            new HelpRow("Alt+←  /  Alt+→", "フォルダ履歴を戻る / 進む"),
            new HelpRow("Ctrl+H", "最近開いたフォルダの一覧"),
            new HelpRow("O", "反対側のウィンドウを同じフォルダにする"),
            new HelpRow("Shift+I", "イニシャルサーチ（頭文字でジャンプ）"),
            new HelpRow("F5", "再読込（一覧とツリーの両方。他アプリの変更は自動で反映されます）"),
            new HelpRow("T", "ツリーへ移る（WinFMTree 相当のキー操作モード。Esc で一覧へ戻る）"),
            new HelpRow("F6", "ツリーの表示 / 非表示"),
            new HelpRow("Shift+F6", "右ウィンドウの表示 / 非表示（現在の表示モードに保存。右クリックメニューからも）"),
            new HelpRow("Shift+S", "最小化"),
            new HelpRow("Q", "終了"),
        }),
        new HelpSection("タグ（複数選択）", "タグが付いているときは、複写・移動・削除などがタグ全部を対象にします。", new[]
        {
            new HelpRow("Space  /  Ins", "カーソル行のタグ ON/OFF して次へ"),
            new HelpRow("Home", "全ファイルにタグ / 全解除（トグル）"),
            new HelpRow("Shift+Home", "フォルダも含めて全部にタグ"),
            new HelpRow("Ctrl+A", "全て選択"),
            new HelpRow("Ctrl+W", "ファイルだけ全て選択"),
            new HelpRow("Ctrl+Z", "タグを反転"),
            new HelpRow("Ctrl+K", "ワイルドカードに一致するファイルにタグ（例: *.log *.tmp）"),
            new HelpRow("S", "オブジェクトのソート（名前 / 拡張子 / 日付 / サイズ / なし → 昇順(U) / 降順(R)）"),
            new HelpRow("Y", "マスク（一覧に表示するファイルの絞り込み。例: *.txt）"),
        }),
        new HelpSection("ファイル操作", "削除は既定でごみ箱へ。完全削除は Ctrl+Del。", new[]
        {
            new HelpRow("C", "複写（宛先・同名時の方法・更新のみ・フィルタ・名前を変えて複写。宛先が同じフォルダなら 1 件ずつ「名前の変更」を聞く: OK(Y) / スキップ(S) / キャンセル(C)）"),
            new HelpRow("M", "移動（複写と同じダイアログ）"),
            new HelpRow("D  /  Del", "削除（ごみ箱へ）"),
            new HelpRow("Ctrl+Del", "完全に削除"),
            new HelpRow("N", "名前の変更"),
            new HelpRow("Shift+N", "名前の一括変換（固定文字列＋連番、拡張子の変更、プレビュー付き）"),
            new HelpRow("A", "属性の変更（R / H / S / A）"),
            new HelpRow("K", "フォルダの新規作成"),
            new HelpRow("Ctrl+N", "空のファイルを新規作成"),
            new HelpRow("Ctrl+T", "タイムスタンプ（更新日時）の変更"),
            new HelpRow("Ctrl+U", "名前の大文字・小文字変換"),
            new HelpRow("Ctrl+D", "サブフォルダのサイズを計算"),
            new HelpRow("Ctrl+M", "ミラーコピー（robocopy /MIR。実行前の一覧確認、手順の保存）"),
            new HelpRow("P", "zip に圧縮（オブジェクト単位の書庫化、圧縮レベル）"),
            new HelpRow("U", "zip を解凍（書庫名のフォルダに展開、上書き可否）"),
            new HelpRow("Shift+P", "プロパティ（エクスプローラーと同じダイアログ）"),
        }),
        new HelpSection("クリップボード", "エクスプローラーと相互にコピー＆ペーストできます。", new[]
        {
            new HelpRow("Ctrl+C  /  Ctrl+Ins", "ファイルをクリップボードへコピー（エクスプローラーには実体、秀丸やメモ帳などのエディタに貼るとファイル名の一覧になる。WinFM2008 と同じ）"),
            new HelpRow("Ctrl+X", "ファイルを切り取り（貼り付けで移動）"),
            new HelpRow("Ctrl+V", "クリップボードのファイルをこのフォルダへ貼り付け（同じフォルダへの貼り付けは 1 件ずつ「名前の変更」を聞く）"),
            new HelpRow("Ctrl+B", "フルパスを文字列としてコピー"),
            new HelpRow("Ctrl+Shift+B", "ファイル名（パスなし）を文字列としてコピー。タグがあれば 1 行 1 件"),
        }),
        new HelpSection("実行 / 編集 / 表示", "", new[]
        {
            new HelpRow("R", "実行（「コマンドの実行」画面を開いてから実行。フルパス入り。.. や何もない所でも開く）"),
            new HelpRow("X", "コマンドの実行（アプリケーション名を自由入力。$ マクロ可）"),
            new HelpRow("E", "テキストエディタで開く（F10 で設定。既定はメモ帳）。.. やフォルダの上で押すとファイル名を聞き、新規ファイルを作ってエディタで開く（WinFM2008 と同じ）"),
            new HelpRow("Shift+E", "（バイナリエディタ：未設定時はエディタで開く）"),
            new HelpRow("V", "ビューア（docx/xlsx/pptx/doc/xls/ppt は中身のテキストを抽出して表示、/ または Ctrl+F でファイル内検索、n / Shift+N で次 / 前。テキスト / HEX 自動判定、.md は Markdown 整形、.csv/.tsv は表表示（T 先頭行を見出し、M でテキスト）、.pdf/.html は Edge のビューア（WebView2）、ソースは VS Code 風の色分け＋行番号（M で切替）、jpg/png/gif/bmp/webp は画像表示（←→ で前後、+/- で拡大縮小、0 全体、1 等倍）、zip は中身の一覧）"),
            new HelpRow("ビューア内のキー", "↑↓ 1 行スクロール、←→ 横、PgUp/PgDn・Space/BackSpace 1 ページ、Shift+Home/End 先頭/末尾、/ または Ctrl+F 検索、n / Shift+N 次 / 前の該当（末尾まで行くと先頭へ巡回。色分け表示では該当行に ► 印と帯）、M 整形/色分け切替、W 折り返し、H HEX、E エディタ（grep から開いたときは該当行へ）、Esc/Q 閉じる"),
            new HelpRow("Z", "エクスプローラーで表示"),
            new HelpRow("B", "カレントフォルダで PowerShell を開く"),
            new HelpRow("F3", "DOS シェル（Windows Terminal → cmd）"),
            new HelpRow("F", "ファイルの検索（名前のワイルドカード、サブフォルダ、結果から移動。Ctrl+C / コピー(P) で見つかったパスの一覧をクリップボードへ。一覧の右クリック / アプリケーションキーで Windows のメニュー）"),
            new HelpRow("Shift+W", "比較（WinMerge など。タグ 2 件ならその 2 つ、カーソルがファイルなら反対ペインの同名ファイル、フォルダなら左右のフォルダを比較。ツールは 設定 → 操作）"),
            new HelpRow("Shift+F", "文字列の検索 (grep)（ファイルの中身を検索。正規表現 / 大文字小文字 / 単語単位、対象ファイルのマスク。結果から Enter 移動・V ビューアで該当行へ（検索語をハイライト、n / Shift+N で次 / 前）・E エディタで該当行へ（F10 › 操作 の「行指定の引数」）・T ヒットした全ファイルにタグ・Ctrl+C / コピー(P) で結果一覧をテキスト（パス｜行｜内容）としてクリップボードへ。「Office / PDF の中身も」を ON にすると Word / Excel / PowerPoint / PDF の文字も検索（docx/xlsx/pptx と PDF は内蔵、doc/xls/ppt などの旧形式は Office が入っていれば IFilter 経由）。ヒットの行番号は取り出したテキストの行で、V で同じテキストを表示、E は Word 等の既定アプリで開く）"),
            new HelpRow("Shift+D", "隠し / システム属性の表示切替"),
            new HelpRow("Ctrl+I", "アイコンの表示 / 非表示"),
            new HelpRow("Shift+1 ～ Shift+5", "表示モードの切り替え（F10 › リスト で各モードの列・日付書式・右ウィンドウの有無・git 列を設定）"),
            new HelpRow("git 列", "git 管理下のフォルダでは属性の右に状態を表示: M 変更あり / U 未追跡 / A 追加済み / D 削除 / ! 無視、フォルダは配下に変更があれば ●（色は F10 › デザイン）"),
            new HelpRow("Shift+G  /  Ctrl+G", "ごみ箱を空にする / ごみ箱を開く"),
            new HelpRow("Ctrl+Shift+C", "ドライブ選択（マイコンピュータ相当）"),
        }),
        new HelpSection("マクロ / 関連付け / 設定", "", new[]
        {
            new HelpRow("F2", "コマンドマクロ（A ～ Z, 1 ～ 9 に登録して 1 キー実行。登録済みのキーは実行、空のキー / Ins / Space / 右クリック で「マクロの設定」（タイトル(T)・マクロ(M)・参照(R)... と $ マクロ一覧）、Del で削除）"),
            new HelpRow("既定のマクロ", "V = VS Code でカレントフォルダ、F = VS Code でカーソルのファイル、W = WinMerge で左右のフォルダ比較、M = WinMerge で反対側の同名ファイルと比較。不要なら Del で消せます（消したままになります）"),
            new HelpRow("F4", "拡張子の対応（Enter で開くプログラムを拡張子ごとに指定。システムの関連付けより優先）"),
            new HelpRow("F10", "カスタマイズ（デザイン: 配色・フォント・一覧 / ツリー / ビューアの行間・タグマーク・Markdown の色 / リスト: 表示モード 1～5（列・名前列の幅「自動」・右ウィンドウ・git 列、変更は即プレビュー） / 操作: エディタと行指定の引数・比較ツール・ごみ箱・隠しファイル・grep の既定）"),
            new HelpRow("配色", "Classic / DOS 16色 / ダーク / ライト / VS Code 各種 / ユニバーサル（色覚多様性に配慮: 赤緑を対にせず青・オレンジ・黄で区別）。色に頼らず見分けたいときはリストの「ファイル属性を表示」を ON にすると H/S/R の文字で分かります。各配色には Markdown ビューア用の色（見出し・リンク・コード・引用・表の見出しなど）も含まれ、デザインの「Markdown」グループで個別に変えられます。"),
            new HelpRow("F1  /  ?", "このヘルプ"),
            new HelpRow("$ マクロ", "$P パス  $D ドライブ  $C ファイル  $X 拡張子なし  $T タグ列  $t タグ列（拡張子なし）  $F フルパス列  $O 反対側ペインのパス  $OC 反対側ペインのカーソル位置のファイル  $Q \"  $Z ,  $R 入力  $$ = $  $YY $Yy 西暦  $YG $Yg 和暦  $YM $YD $Yh $Ym $Ys 日時  $SN 件数  $Ss サイズ  ; で複数。全文は F2 › マクロの設定 の下欄に"),
            new HelpRow("ダイアログ共通", "ボタン / チェックの「(X)」は Alt を押さなくても X キーだけで押せます（文字入力中は Alt+X）。Enter で OK、Esc で取消"),
            new HelpRow("設定ファイル", "%APPDATA%\\FDLike2026\\settings.json"),
        }),
        new HelpSection("ツリー（T で入る）", "WinFM2008 の WinFMTree と同じキー。ツリーモード中は枠が白くなり、Enter で確定するまで一覧は変わりません。", new[]
        {
            new HelpRow("↑  ↓", "上下のフォルダへ"),
            new HelpRow("→", "フォルダを開く（既に開いていれば最初の子へ）"),
            new HelpRow("←", "フォルダを閉じる（閉じていれば親へ）"),
            new HelpRow("Space  /  Shift+Enter", "開閉の切り替え"),
            new HelpRow("Enter", "カーソル位置のフォルダを一覧に表示して、一覧へ戻る"),
            new HelpRow("Esc  /  Tab", "何もせずに一覧へ戻る"),
            new HelpRow("PgUp  PgDn  Home  End", "ページ移動 / 先頭 / 最後"),
            new HelpRow("F5", "ツリーを読み直す（開閉状態は保持）"),
            new HelpRow("Shift+英字", "同じ階層で、頭文字が一致する次のフォルダへ"),
            new HelpRow("1 ～ 9,  0  /  L", "A ～ J ドライブへ / 装置の選択"),
            new HelpRow("K", "カーソル位置のフォルダの下にサブフォルダを作成"),
            new HelpRow("クリック", "そのフォルダを一覧に表示（ツリーモードは解除）"),
        }),
        new HelpSection("マウス", "", new[]
        {
            new HelpRow("クリック", "そのウィンドウをアクティブにして、その行へカーソル移動"),
            new HelpRow("Shift+クリック", "カーソル行からその行までをまとめてタグ（WinFM2008 と同じ範囲選択）"),
            new HelpRow("Ctrl+クリック", "その行のタグを付ける / 外す"),
            new HelpRow("ダブルクリック", "フォルダへ移動 / ファイルを開く"),
            new HelpRow("右クリック", "メニュー（開く・編集・複写・移動・名前・削除・属性・圧縮・解凍・新規フォルダ・エクスプローラー・PowerShell・検索…。末尾の「Windows のメニュー」でエクスプローラーと同じシェルメニュー）"),
            new HelpRow("行をドラッグ", "他のアプリ（Teams・メール・エクスプローラー等）へファイルを渡す。タグ付きの行を掴むとタグ全部"),
            new HelpRow("ドロップ", "エクスプローラー等からのドロップで複写（Shift を押しながらで移動）。フォルダ行やツリーの上に落とすとそこへ"),
            new HelpRow("ツリーのクリック", "そのフォルダへ移動"),
            new HelpRow("境界をドラッグ", "ツリー｜左一覧、左一覧｜右一覧 の境界を掴んで幅を変更（次回起動時も保持）"),
        }),
        new HelpSection("このソフトについて", "FD や FILMTN、そして WinFM2008。キーボードだけで気持ちよくファイルを扱えるこの操作体系を、Windows 11 でも使い続けたくて作りました。", new[]
        {
            new HelpRow("感謝", "MS-DOS の時代に FD を生み出された出射 厚 氏、同じ時代にこの操作体系を磨き上げた FILMTN の石田 健仁 氏、そして長く Windows で私たちの手元を支えてくれた WinFM2008 の根来 研 氏へ。何十年も変わらずに使えた操作の心地よさは、それぞれの作者の方々の工夫と丁寧な仕事のおかげです。心からありがとうございます。"),
            new HelpRow("WinFM2008 互換", "キー操作・画面構成・配色（Classic）は、WinFM2008 を長く使ってきた指がそのまま動くように、できるだけ忠実になぞっています。「あのキーで、あの動き」が再現されていたら、それは先人の設計の良さです。"),
            new HelpRow("独立した実装", "FDLike2026 は WinFM2008 のコードを含まない、ゼロから書き起こした独立したソフトです。WinFM2008 の作者とは無関係で、「WinFM」の名称も使っていません。先人への敬意として、名前を借りずに系譜だけを受け継ぐことにしました。"),
            new HelpRow("問い合わせ先", "不具合や互換性の問い合わせは、WinFM2008 の作者ではなく FDLike2026 側へお願いします。ご迷惑をおかけしないことも、感謝の表し方のひとつだと考えています。"),
        }),
    };

    public HelpWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        CatList.ItemsSource = Sections;
        CatList.SelectedIndex = 0;
        Loaded += (_, _) => CatList.Focus();
    }

    private void Cat_Changed(object sender, SelectionChangedEventArgs e) => Render();

    private void Filter_Changed(object sender, TextChangedEventArgs e) => Render();

    private void Render()
    {
        var q = FilterBox.Text.Trim();
        if (q.Length > 0)
        {
            // 絞り込み中は全カテゴリ横断
            var rows = Sections.SelectMany(s => s.Rows.Select(r => new HelpRow(r.Key, $"[{s.Title}] {r.Desc}")))
                               .Where(r => r.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Desc.Contains(q, StringComparison.OrdinalIgnoreCase))
                               .ToList();
            SectionTitle.Text = $"「{q}」の検索結果  {rows.Count} 件";
            SectionNote.Text = "";
            Rows.ItemsSource = rows;
            return;
        }
        if (CatList.SelectedItem is not HelpSection sec) return;
        SectionTitle.Text = sec.Title;
        SectionNote.Text = sec.Note;
        Rows.ItemsSource = sec.Rows;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); e.Handled = true; break;
            case Key.Up when !FilterBox.IsKeyboardFocused: if (CatList.SelectedIndex > 0) CatList.SelectedIndex--; e.Handled = true; break;
            case Key.Down when !FilterBox.IsKeyboardFocused: if (CatList.SelectedIndex < Sections.Count - 1) CatList.SelectedIndex++; e.Handled = true; break;
            case Key.PageUp: CatList.SelectedIndex = 0; e.Handled = true; break;
            case Key.PageDown: CatList.SelectedIndex = Sections.Count - 1; e.Handled = true; break;
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control: FilterBox.Focus(); e.Handled = true; break;
        }
    }
}
