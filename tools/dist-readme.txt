FDLike2026 - はじめにお読みください
====================================

WinFM2008 / FD / FILMTN の操作体系を受け継いだ、キーボード中心の 2 画面ファイラーです。
Windows 11（Windows 10 でも動作します）用。

■ 動かすのに必要なもの

  ・.NET 10 デスクトップ ランタイム（x64）。Microsoft が無料で配布しています。
      本体の zip には含めていないので、入っていない PC では先に入れてください。
        1. https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0 を開きます。
        2. 「アプリの実行 - ランタイム」の中にある「.NET デスクトップ ランタイム 10.0.x」の表で、
           Windows の「インストーラー」の x64 を押します。
           （「SDK」「ASP.NET Core ランタイム」「.NET Runtime」では起動しません）
        3. ダウンロードした windowsdesktop-runtime-10.0.x-win-x64.exe を実行し、
           「インストール」を押します。
      入れずに起動すると「You must install .NET Desktop Runtime to run this application.」という
      英語のメッセージが出ます。「Download it now」を押すとダウンロードページが開きます。
      ※ v0.2.4 までは .NET 8 用でした。v0.2.5 からは .NET 10 が必要です。
  ・Microsoft Edge WebView2 ランタイム（PDF / HTML の表示に使用）
      Windows 11 には最初から入っています。無い場合は
      https://developer.microsoft.com/ja-jp/microsoft-edge/webview2/ から「Evergreen ブートストラップ」を。

■ インストール

  1. この zip を好きな場所に展開します（例: C:\Tools\FDLike2026）。
     ※ Program Files 配下は書き込み権限の都合で避けてください。
  2. FDLike2026.exe を起動します。ショートカットをデスクトップやタスクバーに作ると便利です。
  3. 初回起動時に SmartScreen の「WindowsによってPCが保護されました」が出た場合は、
     「詳細情報」→「実行」を押してください（コード署名をしていないため表示されます）。

■ アンインストール

  展開したフォルダを削除するだけです。設定ファイルは
    %APPDATA%\FDLike2026\settings.json
  にあるので、完全に消したいときはこのフォルダも削除してください。
  （レジストリには何も書きません）

■ 使い方

  起動して F1 または ? を押すとキー操作の一覧（ヘルプ）が出ます。
  詳しくは同梱の README.md を参照してください。

■ ライセンス

  MIT License（同梱の LICENSE を参照）。
  利用しているライブラリのライセンスは THIRD-PARTY-NOTICES.md にまとめてあります。

■ 不具合・要望

  WinFM2008 の作者とは無関係の独立したソフトです。
  不具合や要望は GitHub の Issues へお願いします。
    https://github.com/piroku/FDLike2026/issues
