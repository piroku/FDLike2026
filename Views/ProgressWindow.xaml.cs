using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace FDLike2026.Views;

/// <summary>圧縮・解凍などの長い処理用の進捗ダイアログ。Run() にバックグラウンド処理を渡す。</summary>
public partial class ProgressWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private bool _done;
    public Exception? Error { get; private set; }
    public bool Cancelled => _cts.IsCancellationRequested;

    public ProgressWindow(Window owner, string title, string caption)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        Caption.Text = caption;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { RequestCancel(); e.Handled = true; } };
    }

    /// <summary>work を別スレッドで実行し、終わるまでモーダル表示する。戻り値: 正常終了なら true。</summary>
    public bool Run(Action<IProgress<(int done, int total, string name)>, CancellationToken> work)
    {
        var progress = new Progress<(int done, int total, string name)>(p =>
        {
            Bar.Maximum = Math.Max(1, p.total);
            Bar.Value = Math.Min(p.done, p.total);
            Detail.Text = p.name;
            CountText.Text = $"{p.done:#,0} / {p.total:#,0}";
        });
        Loaded += async (_, _) =>
        {
            try { await Task.Run(() => work(progress, _cts.Token), _cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Error = ex; }
            _done = true;
            DialogResult = Error is null && !Cancelled;
        };
        return ShowDialog() == true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => RequestCancel();

    /// <summary>中止要求を出し、押したことが分かるようにボタンの表示を変える（処理側が次の確認点で止まる）。</summary>
    private void RequestCancel()
    {
        if (_done || _cts.IsCancellationRequested) return;
        _cts.Cancel();
        CancelButton.IsEnabled = false;
        CancelButton.Content = "中止しています...";
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_done) { RequestCancel(); e.Cancel = true; }   // 処理が終わるまで閉じない（中止要求だけ出す）
    }
}
