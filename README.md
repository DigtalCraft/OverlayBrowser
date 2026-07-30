# Overlay Browser

[日本語](#日本語) / [English](#english)

## 日本語

ゲーム、仕事、配信などの画面のそばで、攻略サイト、チャット、地図、資料などを見たい時のための Windows 用オーバーレイブラウザです。普段使っている Chrome / Edge のブックマークを取り込み、必要なサイトだけを軽く開けます。

### できること

- URLを入力してその場で表示（`https://` の省略可）
- ［＋］またはメニューから複数タブを追加し、サイトごとに切替
- 指定したホームURLを起動時またはホームアイコンから表示
- 表示中のページをブックマークへ追加し、折りたたみ式のツリーで表示
- Chrome または Edge の既定プロファイルからブックマークをインポート
- Chrome / Edge が読み込める標準HTML形式でブックマークをエクスポート
- ［設定］メニューから常に前面に表示するかを切替
- 上部スライダーで透明度を35〜100%に調整
- 右下の銅色マーカーをドラッグしてウィンドウサイズを変更
- ［設定］→［Windowsの起動時にタスクトレイへ常駐］で、サインイン時に画面を開かず常駐
- タスクトレイのアイコンから表示、ヘルプ、終了を操作
- ウィンドウ右上の［×］は画面を隠してタスクトレイへ常駐。終了はメニューバーまたはタスクトレイから確認画面を経由して実行
- 最後に開いたURL、透明度、前面表示設定、Windows起動設定、ブックマークをローカルに保存
- アプリ内ヘルプを日本語・英語で表示
- 右クリックの［ページを翻訳］で、現在のタブをGoogle翻訳のページ翻訳表示へ切替
- Gemini APIキーはWindows Credential Managerに保存し、［Geminiでページを翻訳］を選んだ時だけ表示文章と設定済み指示をGemini APIへ送信
- ［設定］→［翻訳のカスタマイズ］で、翻訳の文体・補足方針・ゲーム用語の扱いを保存

### 使い方

1. URL欄にサイトのアドレスを入力し、［開く］または Enter を押します。［ブックマーク］→［現在のページをホームページに設定］で、そのURLを起動時とホームアイコンで開くホーム画面にできます。
   ［＋］または［ブラウザ］→［新しいタブ］で別サイトを開くタブを追加できます。各タブの［×］で閉じられ、固定のタブ数上限はありません。
2. ［ブックマーク］→［このページをブックマークに追加］で登録します。フォルダを開いて登録済みサイトを選べます。
3. ［ブックマーク整理］では Chrome / Edge / HTMLからのインポート、HTMLへのエクスポート、全削除を行えます。
4. ［設定］→［常に前面に表示］をオンにすると、ゲームの上へ表示できます。
5. 透明度はURL欄右側のスライダーで調整します。
6. 右下の銅色マーカーをドラッグすると、画面サイズを変更できます。
7. ［設定］→［Windowsの起動時にタスクトレイへ常駐］をオンにすると、次回のWindowsサインイン時はタスクトレイから起動します。トレイアイコンを右クリックして［表示］［ヘルプ / Help］［終了］を選べます。
8. ページ上で右クリックし、［ページを翻訳］を選ぶと現在のタブをGoogle翻訳のページ翻訳表示へ切り替えられます。ログインが必要なサイトやCloudflareなどのアクセス保護があるサイトは、Google翻訳側で表示できない場合があります。その場合は［Geminiで本文を翻訳（別画面）］を使います。
   翻訳結果の話し方や説明の詳しさは［設定］→［翻訳のカスタマイズ］から設定できます。これは［Geminiでページを翻訳］でだけ使われ、入力した文章とページ内の表示文章がGemini APIへ送信されます。画像・CSS・スクリプトは送信しません。Gemini翻訳後に元へ戻す時は再読み込みします。APIキーやパスワードは入力しないでください。

### Chrome / Edge ブックマークとの連携

- **Chromeからインポート / Edgeからインポート**: ［ブックマーク整理］から各ブラウザの既定プロファイル（`Default`）を読み込みます。
- **HTMLファイルからインポート**: 別プロファイルやバックアップのブックマークHTMLを取り込む時に使います。
- **Chrome / Edge 用HTMLへエクスポート**: 出力したHTMLファイルをChromeまたはEdgeのブックマーク管理画面からインポートできます。

### 動作環境

- Windows 10 / 11
- 64bit Windows
- Microsoft Visual C++ 2015-2022 再頒布可能パッケージ（x64）
- .NET 10 SDK（ソースからビルドする場合）

ゲーム側は、ウィンドウ表示またはボーダーレスウィンドウ表示での利用をおすすめします。排他的フルスクリーンでは、Windowsの表示仕様により最前面表示が効かない場合があります。ブラウザをクリックした間は、ゲームではなくブラウザが入力を受け取ります。

### ビルド

```powershell
dotnet build OverlayBrowser.slnx
```

### 実行するEXEと出力先

このプロジェクトは Chromium / CefSharp の64bit部品を使用するため、`win-x64` を指定してビルドします。そのため、通常のWPFアプリでよく使う `bin/Release/net10.0-windows/` 直下ではなく、次の場所に出力されます。

| 目的 | 実行ファイル | 更新される操作 |
| --- | --- | --- |
| 開発中の簡易確認 | `OverlayBrowser/bin/Release/net10.0-windows/win-x64/OverlayBrowser.exe` | Visual Studio の「リビルド」または `dotnet build` |
| 普段使う完成版・配布用 | `OverlayBrowser/bin/Release/net10.0-windows/win-x64/publish/OverlayBrowser.exe` | 「発行」または `dotnet publish` |

普段の起動、ショートカットの作成、他のPCへ渡す用途には、**`publish` フォルダ内の `OverlayBrowser.exe`** を使ってください。`publish` はインターネットへ公開する操作ではなく、実行に必要なDLLやChromium部品を1つのフォルダへ揃える「完成版の作成」です。

`OverlayBrowser.exe` だけを別の場所へコピーせず、`publish` フォルダの中身をまとめて扱ってください。CefSharpのDLL、`locales`、`runtimes` なども起動に必要です。

#### 完成版を作り直す

Visual Studioで発行プロファイルを使うか、プロジェクトのルートで次を実行します。

```powershell
dotnet publish OverlayBrowser/OverlayBrowser.csproj -c Release -r win-x64 --self-contained false
```

### インストーラー

`Setup/OverlayBrowser.iss` は Inno Setup 6 用のインストーラー定義です。先に次のコマンドでRelease用ファイルを発行してから、Inno Setup Compilerで `.iss` をビルドしてください。

```powershell
dotnet publish OverlayBrowser/OverlayBrowser.csproj -c Release -r win-x64 --self-contained false
```

インストーラーはアプリ本体のアイコンを利用し、Windowsの「インストールされているアプリ」およびアンインストール画面にも同じアイコンを表示します。

### 証明書なしのインストーラー警告について

現在のインストーラーには、発行元をWindowsへ証明する**コード署名証明書**を付けていません。そのため、初回起動時やダウンロード直後に Microsoft Defender SmartScreen などが「保護されました」「発行元を確認できません」といった警告を表示することがあります。これは署名がないために表示される警告であり、警告だけではマルウェアと判定されたことを意味しません。

ただし、安全が保証されるわけではありません。インストーラーは、このリポジトリのリリースまたは信頼できる作成者から入手したものだけを使用してください。不明なサイト、チャット、メールなどから受け取った同名ファイルでは［詳細情報］→［実行］を選ばないでください。

ソースから自分で作成する場合は、以下の手順で内容を確認できます。

1. リポジトリのソースを確認する。
2. `dotnet publish` を実行する。
3. `Setup/OverlayBrowser.iss` を Inno Setup Compiler でビルドする。

将来、コード署名証明書を導入して署名したリリースでは、この警告が減る場合があります。ただし、SmartScreenの表示は署名の有無だけでなく、配布実績などにも影響されます。

## English

Overlay Browser is a lightweight Windows browser for keeping guides, chat, maps, stream pages, work documents, or other sites beside any app. Import the sites you already use in Chrome or Edge, then keep only the page you need on screen.

### Features

- Open a URL directly; the `https://` prefix is optional
- Add and switch between multiple tabs with no fixed tab limit
- Open a chosen home URL at startup or from the Home button
- Save the current page as a bookmark
- Import bookmarks from Chrome or Edge's default profile
- Export bookmarks as standard HTML that Chrome and Edge can import
- Toggle Always on top from Settings
- Adjust window opacity from 35% to 100%
- Resize the window by dragging the copper marker at the bottom-right corner
- Start in the Windows notification area after sign-in from Settings
- Show the window, open Help, or exit from the notification area icon
- The window close button hides the window in the notification area; explicit Exit asks for confirmation
- Save the last URL, opacity, overlay setting, Windows startup setting, and bookmarks locally
- Built-in help in Japanese and English
- Use the right-click Translate page command to replace the current tab with Google's translated page
- Store the Gemini API key in Windows Credential Manager; visible page text and the saved instruction are sent to Gemini only for Translate page with Gemini
- Save translation tone, detail level, and game-term preferences from Settings → Translation customization

### Build

```powershell
dotnet build OverlayBrowser.slnx
```

### Which EXE should I run?

This project targets `win-x64` because Chromium / CefSharp includes 64-bit native components. Build output is therefore placed below `bin/Release/net10.0-windows/win-x64/`, rather than directly below `bin/Release/net10.0-windows/`.

| Purpose | Executable | Updated by |
| --- | --- | --- |
| Quick development check | `OverlayBrowser/bin/Release/net10.0-windows/win-x64/OverlayBrowser.exe` | Visual Studio **Rebuild** or `dotnet build` |
| Normal use and distribution | `OverlayBrowser/bin/Release/net10.0-windows/win-x64/publish/OverlayBrowser.exe` | Visual Studio **Publish** or `dotnet publish` |

For normal use, shortcuts, and sharing the application, use **`OverlayBrowser.exe` inside the `publish` folder**. `publish` does not upload or release the application to the internet. It creates a complete runnable folder containing the EXE and all required files.

Do not copy only `OverlayBrowser.exe`. Keep the whole `publish` folder together because CefSharp requires its DLLs, `locales`, `runtimes`, and other Chromium files.

#### Create a fresh runnable build

Use a Visual Studio publish profile, or run this from the repository root:

```powershell
dotnet publish OverlayBrowser/OverlayBrowser.csproj -c Release -r win-x64 --self-contained false
```

### Unsigned installer warning

The current installer is **not code-signed** with a certificate that identifies its publisher to Windows. Microsoft Defender SmartScreen or another Windows security feature may therefore show a warning such as *Windows protected your PC* or *Publisher could not be verified* when the installer is downloaded or first run. This warning is expected for an unsigned installer; by itself, it does not mean that the file has been identified as malware.

It is not a guarantee of safety, however. Only use an installer obtained from this repository's release or directly from a trusted maintainer. Do not select **More info → Run anyway** for a file with the same name received from an unknown web site, chat, or email.

If you build the installer yourself, you can inspect the source and create it with the following steps:

1. Review the repository source.
2. Run `dotnet publish`.
3. Compile `Setup/OverlayBrowser.iss` with Inno Setup Compiler.

A future release that uses a code-signing certificate may show fewer warnings. SmartScreen decisions can also depend on distribution reputation, not only on whether a file is signed.

For the most reliable overlay behavior, use games in windowed or borderless-window mode. Exclusive fullscreen mode can prevent a normal Windows topmost window from being displayed above the game.

### Home page

Open the site you want, then select **Bookmarks → Set current page as home**. That URL opens when the app starts and whenever you select the Home button. Until a home URL is set, the app continues to open the last page you used at startup.

### Start with Windows

Select **Settings → Start with Windows and stay in the notification area**. At your next Windows sign-in, the app starts without opening its main window and remains available from the notification area icon. Right-click that icon to choose **Show**, **Help**, or **Exit**.

### Translation

Right-click a web page and select **Translate page** to replace the current tab with Google's translated page. Some pages, including pages that require a sign-in, may not be available through Google Translate. Select **Translate page with Gemini** to replace visible text while keeping the original layout. Reload to restore the original text. Open **Help → Translation Help** for setup and privacy details.

Use **Settings → Translation customization** to save your preferred tone, level of explanation, and treatment of game terms. Your saved instruction and visible page text are sent to Gemini only when you select **Translate page with Gemini**. Images, CSS, and scripts are not sent. Do not enter API keys, passwords, or other secrets in the customization field.
