**日本語** | **[简体中文](README-zh-CN.md)** | **[English](README-en.md)**

<div align="center">
    <img width="700" src="./Logo/Logo-en.png"></img>
</div>
<h1 align="center">ContextMenuManager</h1>
<h4 align="center">Windows の右クリックメニューを管理するツール（Windows 7～11 対応）</h4>

---

このリポジトリは [Jack251970/ContextMenuManager](https://github.com/Jack251970/ContextMenuManager) の個人用フォークです。
本家に加えて、主に次の点を改善しています。

## ✨ このフォークでの変更点

### Windows 11 の新しいメニューの項目を自動検出
Windows 11 の新しい右クリックメニューには、パッケージアプリ（MSIX やスパースパッケージ）が追加した項目が表示されます。
「Copilot に質問」やセキュリティソフト（カスペルスキーなど）の項目がその例です。

本家では、これらの項目は**内蔵の辞書（UwpModeItemsDic.xml）に載っているものしか一覧に出ません**。
このフォークでは、インストール済みパッケージの `AppxManifest.xml` に書かれた右クリックメニュー拡張（`FileExplorerContextMenus`）を読み取り、辞書に無い項目も自動で一覧に追加します。

* 表示場所
  * 「ファイル」：すべてのファイル向け（`*`）の項目と、特定の拡張子向け（`.docx` など）の項目。拡張子限定の項目は、名前のあとに `[.docx, .xlsx …]` のように対象の拡張子を表示します
  * 「フォルダー」「ディレクトリ」：フォルダー向けの項目
  * 「フォルダーの背景」「デスクトップの背景」：背景向けの項目
  * 「拡張子を指定」：選んだ拡張子向けの項目
* オン・オフの仕組み
  * 本家の UWP モジュールと同じく、`Shell Extensions\Blocked` に CLSID を登録して非表示にします。アプリを更新しても元に戻りにくい方法です
  * 変更を反映するには、エクスプローラーの再起動が必要です
* 設定
  * 「設定」→「Windows 11 の新しいメニューに出るアプリ（パッケージ）の項目を自動検出する」で、自動検出を無効にできます（既定は有効）

### 日本語表示の改善
* 日本語の言語ファイル（`languages/ja-JP.ini`）を全面的に見直しました。訳の抜けていた項目（バックアップと復元、検索など）も補っています
* 辞書（拡張メニュー、詳細な編集、GUID 情報）に日本語訳を追加し、辞書ファイル内の説明コメントも日本語にしました
* 日本語訳が無い辞書の項目は、中国語ではなく英語訳があれば英語で表示します
* 言語ファイルと辞書をアプリに埋め込み、起動時に設定フォルダー（`Config\Languages`、`Config\Dictionaries\Web`）へ反映します。古いバージョンや本家から取得したファイルが残っていても、最新の訳が使われます
* アプリ内の「言語ファイルのダウンロード」と「辞書の更新」の取得先を、このフォークに変更しました

## 🚀 ダウンロードとビルド
* このフォークのビルドは、GitHub Actions の「build」ワークフローの成果物（Artifacts）からダウンロードできます
* タグを付けてプッシュすると、「publish」ワークフローがリリースを作成します
* .NET 10 で動作します。.NET 10 が入っていない場合は、[.NET デスクトップ ランタイム][.NET]をインストールしてください
* 自分でビルドする場合は、Visual Studio 2026 などで `ContextMenuManager.sln` を開くか、次のコマンドを実行します

```powershell
dotnet build ContextMenuManager.sln --configuration Release
```

## ⭐ 主な機能
* ファイル、フォルダー、新規作成、送る、プログラムから開く、指定した拡張子、IE、Win+X などの右クリックメニュー項目の有効・無効を切り替え、バックアップと復元ができます
* 各項目の名前やアイコンの変更、レジストリやファイルの場所へのジャンプ、完全な削除ができます
* 右クリックメニューに独自の項目やコマンドを追加できます

## 🖥️ 動作環境
* Windows 7、8、8.1、10、11（Windows 7、8、8.1 は本家の 3.6.2.2 以前を使用してください）
* 64 ビット / 32 ビット OS
* 高 DPI に対応（表示スケール 150% で最適）
* 多言語表示に対応

## 🖼️ スクリーンショット
<picture>
  <source media="(prefers-color-scheme: dark)" srcset="./Screenshot/Screenshot-en-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="./Screenshot/Screenshot-en.png">
  <img alt="スクリーンショット" src="./Screenshot/Screenshot-en.png">
</picture>

## 🪧 注意事項
* 大量のレジストリやファイルを読み書きするため、Windows Defender などにウイルスと誤判定されることがあります。その場合は除外リストに追加してください。
* 特殊なメニュー項目（シェル拡張、暗号化ファイルなど）の中には、他の要因で右クリックメニューに表示されないものがあります。その場合でも、このツールでは一般的な判定ルールに従って「有効」と表示されることがありますが、正常な動作です。
* 右クリックメニュー管理ツールごとに無効化の方法は異なります。複数のツールを同時に使わないでください。
* 多くのツールは「バックアップして削除」という単純な方法を使いますが、このツールはできるだけ OS が用意している値を使って非表示にし、あとで元に戻しやすくしています。
* 他のツールで無効にした項目は、先にそのツールで元に戻してください。戻さないと、このツールでは表示されないことがあります。
* このツールはアンインストールの残骸を掃除するためのものではありません。ただし、メニュー項目に関係するレジストリやファイルの場所をすばやく探す手助けにはなります。慣れていない場合は、有効・無効の切り替えだけを使うことをおすすめします。

## 💕 謝辞
* 原作者 [蓝点lilac（BluePointLilac）](https://github.com/BluePointLilac/ContextMenuManager)
* 本家フォークの作者 [Jack251970](https://github.com/Jack251970/ContextMenuManager)
* 新しいアイコンを制作した [澜芸](https://github.com/LanYun2022) と [KamilDev](https://github.com/KamilDev)
* 日本語訳の元になった翻訳者 [夕綺Yuuki](https://github.com/JDYuuki) と [Jing](https://github.com/delgh1)

## 📄 ライセンス
[LICENSE](LICENSE) を参照してください。

  [.NET]: https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0
