# Token Checker for Windows

タスクバーに Claude Code と Codex の使用率を常時表示する Windows アプリ。

macOS 版 [Token Checker](https://github.com/satonico/Token-Checker) の Windows 移植版。

## 動作要件

- Windows 10 / 11（64bit）
- Claude Code CLI（`claude login` 済み）
- Codex CLI（`npm i -g @openai/codex` 後、`codex login` 済み）

どちらか一方のみでも動作する。

## インストール

### 方法A: exe をダウンロード（推奨・無設定）

[Releases](https://github.com/satonico/Token-Checker-win/releases) から `TokenChecker.exe` をダウンロードしてダブルクリックするだけ。.NET ランタイムを同梱しているため、**.NET のインストールは不要**。

### 方法B: ソースからビルド

開発者向け。先に [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) が必要（`winget install Microsoft.DotNet.SDK.8` でも可。インストール後は PowerShell を開き直す）。

```powershell
git clone https://github.com/satonico/Token-Checker-win.git
cd Token-Checker-win
dotnet build TokenChecker.sln -c Release
```

出力先: `TokenChecker\bin\Release\net8.0-windows\TokenChecker.exe`

`.exe` 単体で他PCに配布したい場合は、ランタイム同梱版を発行する。

```powershell
dotnet publish TokenChecker\TokenChecker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\
```

`publish\TokenChecker.exe` が .NET 不要で動く単一ファイルになる。

## 使い方

1. 事前にターミナルでログインしておく

```powershell
claude login
codex login
```

2. `TokenChecker.exe` を起動するとタスクバー上にウィジェットが表示される
3. ウィジェットをクリックするとポップアップで詳細（使用率・リセット時間・更新間隔設定）が開く

## アンインストール

PowerShell で以下を順に実行する（対象が無くてもエラーにならない）。

```powershell
# 1. アプリを終了（起動中だとファイルがロックされ削除に失敗するため）
Stop-Process -Name TokenChecker -Force -ErrorAction SilentlyContinue

# 2. 自動起動の登録を削除（登録が無い場合はスキップ）
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TokenChecker /f 2>$null

# 3. 設定・キャッシュを削除（フォルダが無い場合はスキップ）
Remove-Item "$env:APPDATA\TokenChecker" -Recurse -Force -ErrorAction SilentlyContinue
```

あとはビルド／ダウンロードした `TokenChecker.exe`（やクローンしたフォルダ）を削除すれば完了。Claude / Codex の認証情報は CLI 側（`claude` / `codex`）の管理なので、本アプリのアンインストールでは消さない。

## 免責事項

本ソフトウェアは現状有姿 (as-is) で提供される。利用に起因するいかなる損害についても作者は責任を負わない。
