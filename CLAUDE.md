# CLAUDE.md

このリポジトリで作業するときの指示です。

## 基本方針
- 会話、質問、進捗の報告、途中経過、最終報告はすべて日本語で行うこと。
- 詳しく調査する前に、確認の質問をすること。
- 会話が長くなってきたら `/compact` を勧めること。あわせて、会話を Markdown のチャットログとして残せるように提案すること。
- コミットメッセージも日本語で書く（1 行目に要約、空行、本文に箇条書き）。

## プロジェクト概要
- Windows の右クリックメニュー管理ツール ContextMenuManager の自分用フォーク。
  - 原作: BluePointLilac/ContextMenuManager
  - フォーク元: Jack251970/ContextMenuManager（本家）
- .NET 10（`net10.0-windows`）、WPF、C#。
- 使用環境は Windows 11（日本語 UI）。主な目的は、Windows 11 の新しい右クリックメニューに出るパッケージアプリの項目（カスペルスキー、「Copilotに質問」など）の有効・無効の切り替えと、完全な日本語化。

## ビルドとリリース
- Claude の作業環境には .NET SDK が無く、NuGet にも接続できない。**ビルド確認は GitHub Actions で行う。**
  - `build.yml`：master へのプッシュとプルリクエストで実行される。ビルドとテストを行い、成果物（`ContextMenuManager/bin/Release`）を Artifacts に上げる。
  - `publish.yml`：タグのプッシュで実行される。リリースを作成し、`ContextMenuManager.exe` を添付する。タグ名はアセンブリのファイルバージョン。
- 作業環境からは **GitHub のタグとリリースを作成できない**（API が拒否される）。リリースが必要なときは、ユーザーにタグの作成を依頼する（GitHub の Releases 画面から作るか、手元で `git tag` と `git push origin <tag>`）。
- 作業環境からは Actions の Artifacts もダウンロードできない。動作確認はユーザーに依頼する。
- GraphQL は使えないため、プルリクエストは REST API で作る。
  - 作成：`gh api repos/taketake5656/ContextMenuManager/pulls -f title=... -f head=... -f base=master -F body=@file`
  - マージ：`gh api -X PUT .../pulls/<番号>/merge`
- バージョンは `ContextMenuManager/Properties/AssemblyInfo.cs` の `AssemblyVersion` と `AssemblyFileVersion`。フォーク版は **4.0.1.x 以降**（本家は 4.0.0.9）。リリースのたびに上げる。
- 作業はブランチを切り、プルリクエストを経由して master にマージする。

## コード構成（フォークで追加・変更した主な部分）
- `ContextMenuManager/Methods/PackagedContextMenuHelper.cs`：パッケージアプリの右クリックメニュー項目を自動検出する。
  - `HKCR\PackagedCom\Package` の各パッケージについて `AppxManifest.xml` を探し、`FileExplorerContextMenus/ItemType/Verb`（Clsid）を読む。
  - 表示名の優先順位：GUID 辞書（`GuidInfo.GetText`）→ マニフェストの DisplayName。DisplayName が「ShellEx Package」のような汎用的な名前なら、`PackagedCom\...\Server\*\ApplicationDisplayName` をリソースから解決した名前を使う。それも汎用的なら、末尾にパッケージ名を付ける。
  - 拡張子限定の項目は「ファイル」の場面に `[.docx, …]` を付けて表示する。
- `Controls/ShellList.cs` の `LoadPackagedContextMenuItems()`：検出した項目を `UwpModeItem` として一覧に追加する。オン・オフは `Shell Extensions\Blocked` への CLSID 登録で切り替える。
- `Methods/AppConfig.cs`
  - `DetectPackagedMenus`：自動検出の設定（既定は有効）。
  - `SyncBundledFiles()`：exe に埋め込んだ言語ファイルと辞書を、起動時に `Config\Languages` と `Config\Dictionaries\Web` へ書き出す。`Program.Main` から呼ぶ。
  - 言語・辞書・新規作成テンプレートの GitHub 取得先 URL はこのフォークに変更済み。リリースの更新確認（`GithubLatest*`）は本家のまま。
- `Methods/XmlDicHelper.cs` の `JudgeCulture`：中国語以外の UI では、その言語の訳が無い `Text`、`Tip`、`REG_SZ` に英語訳（en-US）を使う。
- `Methods/GuidInfo.cs` の `GetText`：`[UI言語]-Text` → `[設定言語]-Text` → （中国語以外なら）`en-US-Text` → `Text` の順で探す。
- `ContextMenuManager.csproj`：`..\languages\*.ini` を Resource として埋め込む（`Properties/Resources/Languages/`）。
  - もとの `CopyFilesAfterBuild` は Debug ビルドでしか動かない。これが Release ビルドで中国語が残る原因だった。

## 言語ファイルと辞書を編集するときの注意
- `languages/ja-JP.ini`
  - 値は必ず 1 行で書く。改行は `\r\n` で表す。行頭が「キー =」でない行は読み飛ばされる。
  - キーを追加したら、`ContextMenuManager/Properties/Resources/Texts/AppLanguageDic.ini`（中国語の既定）と、`languages/` のすべての言語ファイルにも追加する。他言語は英語でよい。
- XML 辞書（`EnhanceMenusDic.xml`、`DetailedEditDic.xml`）の日本語訳
  - `<Culture>ja-JP</Culture>` を持つ要素として追加する。
  - 必ず**既定（Culture なし）の要素より後ろ**に置く。読み込み処理は後に一致した要素で上書きするため。慣例として en-US の要素の直前に置く。
- `GuidInfosDic.ini`：日本語名は `ja-JP-Text = ...` で追加する。
- 既存ファイルの文字コード（BOM の有無）と改行コード（CRLF / LF）を保つこと。混在させない。

## わかっている制約（同じ調査を繰り返さないこと）
- パッケージアプリの右クリック拡張（IExplorerCommand）は、パッケージの外から `CoCreateInstance` すると `0x80040154`（クラス未登録）になる。
- `C:\Program Files\WindowsApps` 内の DLL は、`LoadLibrary` がアクセス拒否（エラー 5）になる。
- スパースパッケージ（カスペルスキーなど）は、`DllPath` がファイル名だけで、外部の場所がレジストリから取れない。
  - 例：カスペルスキーの実体は `C:\Program Files (x86)\Kaspersky Lab\Kaspersky 21.27\x64\shellex.dll`。
- 以上の理由で、「実際の右クリックの表示名を GetTitle で取得する」方式は削除した。表示名は辞書で割り当てる。
  - カスペルスキー（`{AC2512EE-996B-4B15-918E-D801FD7DB91B}`）は `GuidInfosDic.ini` に登録済み。

## 次の作業候補
1. 主なパッケージ項目の表示名を `GuidInfosDic.ini` に登録する（`ja-JP-Text` と `en-US-Text`）。CLSID は次のとおり（ユーザー環境で確認済み）。
   - `{db6bacb1-fc31-4b28-9c53-7240a4f98404}` Microsoft.MicrosoftOfficeHub（AskM365Copilot）→「Copilot に質問」
   - `{9f156763-7844-4dc4-b2b1-901f640f5155}` Microsoft.WindowsTerminal（OpenTerminalHere）→「ターミナルで開く」
   - `{ca6cc9f1-867a-481e-951e-a28c5e4f01ea}` Microsoft.WindowsNotepad（OpenInNotepad）→「メモ帳で編集」
   - `{2430f218-b743-4fd6-97bf-5c76541b4ae9}` Microsoft.Paint（EditInPaint）→「ペイントで編集」
   - `{bfe0e2a4-c70c-4ad7-ac3d-10d1ecebb5b4}` Microsoft.Windows.Photos（AShellEdit）→「フォトで編集」
   - `{8ab635f8-9a67-4698-ab99-784ad929f3b4}` Clipchamp.Clipchamp（EditWith）→「Clipchamp で編集」
   - `{1c6df0c0-192a-4451-be36-6a59a86a692e}` Microsoft.VisualStudioCode（OpenWithCode）→「Code で開く」
   - `{30deedf6-63ea-4042-a7d8-0a9e1b17bb99}` AdobeAcrobatReaderCoreApp（EditCommand）
   - `{3282e233-c5d3-4533-9b25-44b8aaafacfa}` AdobeAcrobatReaderCoreApp（CreateAdobePDF）
   - `{f92f683a-1e53-4fe9-8f61-33f8f1f83822}` Explzh（command1）
   - 実際の表示文言はユーザーに確認してから登録すること。
2. 更新の確認先（`AppConfig.GithubLatest`、`GithubLatestApi`）をこのフォークに切り替えるか検討する。現状では、本家が 4.0.1.0 より新しい版を出すと更新を勧められ、更新するとフォーク版の機能が消える。
3. 「作者への寄付」画面の寄付者一覧（本家の `Donate.md` を読み込む）が中国語のままになっている。
4. 「About」の文言（`ja-JP.ini` の `AboutApp`）にあるリポジトリ URL は本家のまま。フォークの URL を併記するか検討する。
