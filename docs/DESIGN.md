# 設計メモ

## フォルダ構成

```
ProjectDock/
├─ ProjectLauncher.sln
├─ Directory.Build.props            共通設定（Version は release.yml が -p:Version で上書き）
├─ src/
│  ├─ ProjectLauncher.Core/         ロジック層（net8.0、UI非依存 → Linuxでもテスト可）
│  │  ├─ Models/                    ProjectEntry, ProjectStatus, AppSettings, LauncherData
│  │  ├─ Detection/                 ProjectDetector（種別判定）, FolderScanner（親フォルダのスキャン）
│  │  ├─ FileSystem/                LastModifiedCalculator, ExcludedFolders, PathUtil
│  │  ├─ Git/                       GitClient（git.exe 呼び出し）, GitStatusParser, GitRemoteUrl
│  │  ├─ Storage/                   ProjectStore（projects.json 読み書き・破損時バックアップ）
│  │  ├─ Services/                  ProjectStatusService, StatusRefresher（並列制御）,
│  │  │                             ProjectFilter（検索・並び替え）, HotkeyParser, ToolLocator,
│  │  │                             UnityProjectInfo, DisplayFormat
│  │  └─ Logging/                   FileLogger
│  └─ ProjectLauncher.App/          WinForms（net8.0-windows）
│     ├─ Program.cs                 二重起動防止・例外ハンドラ
│     ├─ TrayAppContext.cs          トレイ常駐の本体（NotifyIcon / ホットキー / 終了処理）
│     ├─ ProjectCatalog.cs          データの持ち主（UIスレッド専用）
│     ├─ Forms/                     MainForm, ProjectEditDialog, ScanDialog, SettingsDialog
│     └─ Infrastructure/            GlobalHotkey, StartupRegistration, ExternalLauncher, AppIcon
├─ tests/ProjectLauncher.Core.Tests/  xUnit
├─ tools/make_icon.py               アイコン生成（Pillow）
└─ .github/workflows/               build.yml（push/PR）, release.yml（手動）
```

## 依存の向き

```
App (WinForms) ──→ Core ←── Tests
```

Core は WinForms も Win32 API も参照しない。外部依存（git.exe、ファイル存在確認、環境変数）は
`IGitClient` / `ToolLocator(fileExists, env)` で差し替え可能にしてテストしている。

## 状態取得の流れ

1. 起動 → `projects.json` を読み、`CachedStatus`（前回の結果）で即座に一覧を描画
2. ウィンドウ表示時（前回から30秒以上経っていれば）/ 手動の再スキャン時だけ `StatusRefresher` が動く
3. `StatusRefresher` は `SemaphoreSlim(4)` で並列数を制限し、各プロジェクトを `Task.Run` 上で処理
4. 1件終わるごとに `BeginInvoke` で該当行だけ更新。全件終わったら並べ直して `projects.json` に保存
5. 前の全件更新が走っている最中に新しい全件更新が来たら、前のものはキャンセルする

1プロジェクトあたりの git 呼び出しは最大3回（すべてローカルのみ、ネットワークなし）:

| コマンド | 用途 |
|---|---|
| `git status --porcelain --branch` | 1行目からブランチ名、残りの行数が未コミット数 |
| `git log -1 --format=%cI` | 最終コミット日時（コミットがなければファイル日時にフォールバック） |
| `git remote get-url origin` | GitHub URL 変換用 |

`GIT_OPTIONAL_LOCKS=0` を付けて、`git status` が index.lock を取りに行かない（VS Code などと競合しない）ようにしている。
各コマンドは15秒でタイムアウトしてプロセスを kill する。

## 一覧の状態表示

| 状態 | Git列 | 色 |
|---|---|---|
| フォルダがない | 見つかりません（名前にも表示、🗑ボタンが出る） | 赤 |
| .git なし | Git未管理 | グレー |
| git.exe がない | git未検出 | グレー |
| git コマンド失敗 | gitエラー（ツールチップに理由） | 赤 |
| 変更あり | 未コミット N件（太字） | オレンジ |
| 変更なし | クリーン | 緑 |

## WinForms を選んだ理由

- **軽さ**: 同じ一覧画面なら WinForms の方が起動が速く、常駐時のメモリも少ない（WPF は描画系の DirectX / milcore と XAML のランタイムを抱える）。メモリ 8GB の ThinkPad で常駐させる前提なので、ここを最優先にした。
- **トレイ・ホットキーが標準で書ける**: `NotifyIcon` は WinForms 標準。WPF でトレイを出すには結局 WinForms を混ぜるか、外部 NuGet（Hardcodet.NotifyIcon.Wpf 等）が要る。
- **NuGet ゼロ**: アプリ本体は外部パッケージを一切使っていない（テストプロジェクトの xUnit だけ）。
- 一覧は `DataGridView` で、操作ボタンはセルを自前描画（`ButtonRenderer`）して、無効・非表示・ツールチップを細かく制御している。

## 自前で判断した細部

- **種別判定の優先順位**: Unity → Windowsアプリ（.sln / .csproj / .vcxproj 等）→ Web（package.json / index.html）→ その他。
  Unity は .sln / .csproj を自動生成するので最優先。
- **スキャン候補**: 仕様の3条件に加えて、.sln / .csproj があるフォルダも候補に入れる（Git 未管理の Windows ツールを拾うため）。
  候補が見つかったフォルダの内側には潜らない。除外フォルダと `.` で始まるフォルダは見ない。
- **更新日時の計算**: 除外フォルダ（Library / Temp / obj / bin / node_modules / .git / Build）はどの深さでも除外。
  シンボリックリンク・ジャンクションは辿らない。安全弁として 20万ファイルで打ち切り。
- **Unity で開く**: Unity Hub には「GUI でこのプロジェクトを開く」公式 CLI がないため、
  `ProjectSettings/ProjectVersion.txt` のバージョンと一致する、Hub がインストールしたエディタ
  (`%ProgramFiles%\Unity\Hub\Editor\<ver>\Editor\Unity.exe` または Hub の追加インストール先) を `-projectPath` で直接起動する。
  見つからなければ Unity Hub 自体を起動する。
- **GitHub URL**: `https://user:token@github.com/...` のような資格情報入り URL は、資格情報を捨てて変換し、キャッシュにも残さない。
  `~/.ssh/config` の別名で多い `github.com-xxx` 形式のホストも GitHub とみなす。
- **公開URL**: http / https 以外は開かない（`file:` などでローカル実行ファイルを起動させないため）。
- **projects.json の書き込み**: `.tmp` に書いてから置き換える（書き込み中に落ちても壊れにくい）。
  壊れていたら `projects.json.broken-yyyyMMdd-HHmmss.bak` に退避し、空で起動してバルーン通知する。
