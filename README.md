# ProjectLauncher（ProjectDock）

個人開発のプロジェクト（Unity / Three.js などの Web / Windows ツール…）を 1 画面に並べて、
**どこにあるか・未コミットが残ってないか** を一目で見て、1 クリックで開くための Windows 用ランチャー。

- C# / .NET 8 / **WinForms**、win-x64 の self-contained 単一 exe（.NET のインストール不要）
- タスクトレイ常駐、`Ctrl+Alt+P` でどこからでも呼び出し
- アプリ本体は外部 NuGet パッケージなし

## ダウンロードとインストール（PC 側）

1. GitHub のこのリポジトリの **Releases** を開く
2. 最新版の `ProjectLauncher-vX.Y.Z-win-x64.exe` をダウンロード（zip 版も同じ中身）
3. 好きなフォルダ（例: `C:\Tools\ProjectLauncher\`）に置いてダブルクリック
   - 署名していない exe なので、初回は SmartScreen の「Windows によって PC が保護されました」が出る。
     「詳細情報」→「実行」で起動できる
4. 新しい版に更新するときは、トレイの「終了」で止めてから exe を差し替える

## リリースの作り方（スマホからでも可）

1. GitHub の **Actions** タブ → 左の一覧から **Release** を選ぶ
2. **Run workflow** → `version` に `1.0.0` のようなバージョンを入力 → **Run workflow**
3. 数分待つと **Releases** に `v1.0.0` ができて、exe と zip が添付される → PC でダウンロード

ワークフローがやること:

- `version` が `x.y.z` 形式でなければ失敗
- タグ `v{version}` が既にあれば失敗
- windows-latest でビルド → テスト → `dotnet publish`（Release / win-x64 / self-contained / PublishSingleFile、exe のバージョンに入力値を反映）
- タグ `v{version}` を作って push → GitHub Release を作成して exe・zip・SHA256SUMS.txt を添付
- Release 作成に失敗したら、push したタグを削除して同じバージョンで再実行できるようにする

> **注意**: `workflow_dispatch` のワークフローは、**既定ブランチ（main）に `release.yml` が入って初めて** Actions の一覧に出る。
> 最初は開発ブランチの PR を main にマージしてから実行すること。

通常の push / PR では `build.yml` が走り、ビルドとテストだけ行う（ついでに試用版 exe を 7 日間 Artifacts に置く）。

## 使い方

### 登録

- **ドラッグ＆ドロップ**: エクスプローラーからフォルダをウィンドウに落とす（複数可）
- **＋ フォルダ追加**: フォルダ選択ダイアログから 1 件
- **親フォルダをスキャン**: `C:\dev` のような親フォルダを指定すると、直下（深さ 1〜2）から候補を探して一覧表示。
  チェックを付けたものだけ一括登録する。登録済みのものはグレーで表示され、選べない
  - 候補の条件: `.git` がある / Unity（`Assets` と `ProjectSettings` がある）/ `package.json` か `index.html` がある / `.sln`・`.csproj` 等がある
- 行をダブルクリック（または F2、右クリック →「編集…」）で、表示名・フォルダ・種別・公開URL・タグ・メモを編集。
  種別は「自動判定」のチェックを外すと手動で固定できる

### 一覧

| 列 | 中身 |
|---|---|
| 種別 | 🎮 Unity / 🌐 Web / 🪟 Windowsアプリ / 📦 その他 |
| 表示名 | ツールチップにパス・タグ・メモ |
| 最終更新 | Git があれば最終コミット日時、なければファイル更新日時の最大値（Library / Temp / obj / bin / node_modules / .git / Build は除外） |
| Git | `未コミット N件`（オレンジ）/ `クリーン` / `Git未管理`（グレー）/ `git未検出` / `見つかりません`（赤） |
| ブランチ | 現在のブランチ名 |

ボタン: 📁 エクスプローラー / 💻 VS Code / 🐙 GitHub / 🌐 公開URL（登録時のみ）/ ⌨ ターミナル / 🎮 Unity（Unity のみ）/ 🗑 削除（フォルダが見つからないときのみ）

使えないボタンはグレーになり、マウスを乗せると理由が出る。

- 検索ボックス: 名前・タグ・メモで絞り込み（空白区切りで AND）
- 並び替え: 最終更新が新しい順（既定）/ 名前順 / 未コミットありを上に
- 「未コミットありだけ」トグル

### キーボード

| キー | 動作 |
|---|---|
| `Ctrl+Alt+P`（変更可） | どこからでもウィンドウを前面に出して検索欄にフォーカス |
| 検索欄で `Enter` | 先頭の行を開く（VS Code、なければエクスプローラー） |
| 検索欄で `↓` | 一覧に移動 |
| 一覧で `Enter` / `F2` / `Delete` | 開く / 編集 / 一覧から削除 |
| `F5` | 全プロジェクトを再スキャン |
| `Ctrl+F` | 検索欄へ |
| `Esc` | 検索をクリア、空ならウィンドウを隠す |

### 常駐

- × ボタンで閉じても終了しない（トレイに格納）。終了はトレイアイコン右クリック →「終了」
- トレイメニュー: 開く / 全プロジェクトを再スキャン / 設定 / 終了
- 設定: ホットキー、Windows 起動時の自動起動（`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`。トレイに格納した状態で起動）、Git 取得の並列数
- 二重起動すると、既に動いている方のウィンドウが前に出る

### データの場所

| 内容 | 場所 |
|---|---|
| プロジェクト一覧・設定・前回の状態キャッシュ | `%APPDATA%\ProjectLauncher\projects.json` |
| ログ（日付ごと、7 日で削除、2MB 超で退避） | `%APPDATA%\ProjectLauncher\logs\` |

`projects.json` が壊れていたら `projects.json.broken-日時.bak` に退避して空の状態で起動する（バルーンで通知）。

## 軽さのための方針

- 状態の自動更新は「ウィンドウを開いたとき」と「手動の再スキャン」だけ。常時ポーリングはしない
  （ホットキー連打で何度も走らないよう、自動更新は前回から 30 秒以上空いたときだけ）
- 起動時は前回の結果（キャッシュ）を即表示し、裏で更新
- Git の取得は非同期・並列数 4（設定で 1〜8）。UI は固まらない
- `git fetch` などネットワークを使う操作はしない
- WinForms・外部 NuGet なし・バックグラウンド GC オフ

設計の詳細は [docs/DESIGN.md](docs/DESIGN.md)、実機での確認項目は [docs/CHECKLIST.md](docs/CHECKLIST.md)。

## 仮定（仕様に書かれていなかったので決めたこと）

- **UI は WinForms**（理由は [docs/DESIGN.md](docs/DESIGN.md#winforms-を選んだ理由)）
- **種別の自動判定の優先順位**: Unity → Windowsアプリ（.sln / .csproj / .vbproj / .fsproj / .vcxproj）→ Web → その他
- **Windowsアプリ種別の判定条件**: 仕様になかったので、ルートに .sln / .csproj 等があることにした。スキャン候補にも含める
- 種別は「自動判定」のままなら再スキャンのたびに判定し直す。手動で変えたものは固定
- 同じフォルダの二重登録はしない（パスは大文字小文字を区別せずに比較）
- **Unity で開く** は Unity Hub 経由ではなく、Hub がインストールしたエディタ（ProjectVersion.txt と同じバージョン）を直接起動する。
  Hub に「GUI でプロジェクトを開く」公式の CLI がないため。エディタが見つからなければ Unity Hub を起動する
- **VS Code** は PATH の `code` → `%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe` → `%ProgramFiles%\Microsoft VS Code\Code.exe` の順に探す。
  PATH の `code.cmd` が見つかったら、コンソール窓を出さないよう隣の `Code.exe` を直接起動する
- **ターミナル** は Windows Terminal（`wt.exe`）があれば `wt -d <フォルダ>`、なければ `cmd.exe` をそのフォルダで開く
- **GitHub ボタン** は Git 未管理なら非表示、origin が GitHub 以外（またはない）ならグレーで理由を表示
- コミットがまだない Git リポジトリの「最終更新」はファイル日時で代用
- 自動起動時は `--tray` 付きで起動し、ウィンドウは出さない（最初に開いたときに状態を更新）
- 未コミット数は `git status --porcelain` の行数（untracked も 1 行 1 件として数える）

## 将来案（MVP には入れていない）

- `git fetch` による ahead / behind の表示
- プロジェクトごとの作業時間の記録
- GitHub Pages の公開 URL の自動推測
- スマホ（同じリポジトリ一覧）との連携
- （追加で思いついたもの）よく開くプロジェクトを上に出す「最近使った順」の並び替え

## 開発

```sh
dotnet test                 # ロジック層のテスト（Linux / macOS でも可）
dotnet build src/ProjectLauncher.App   # Windows のみ（WinForms）
```

アイコンは `python3 tools/make_icon.py`（要 Pillow）で再生成できる。
