FDLike2026 - はじめにお読みください
====================================

WinFM2008 / FD / FILMTN の操作体系を受け継いだ、キーボード中心の 2 画面ファイラーです。
Windows 11（Windows 10 でも動作します）用。

■ 動かすのに必要なもの

  ・.NET 8 Desktop Runtime（x64）
      https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0
      「.NET デスクトップ ランタイム 8.0.x」の Windows x64 インストーラーを入れてください。
      入っていないと起動時に「.NET をインストールしてください」という案内が出ます。
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
