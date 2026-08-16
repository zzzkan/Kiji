# Kiji 設計ドキュメント

Kiji は .NET 製の静的サイトジェネレーター（SSG）フレームワークである。ページは Razor
コンポーネントとして書き、Blazor の `HtmlRenderer` で静的 HTML にレンダリングする。
本ドキュメントは、Kiji の設計思想・アーキテクチャ・主要コンポーネント・
インクリメンタルビルドとパフォーマンス設計・意図的に採用しなかった選択肢をまとめる。

## 設計思想

- **単一パッケージ**: Markdown パイプライン（YAML フロントマター）、レスポンシブ WebP
  画像最適化、RSS/サイトマップ、開発サーバーをすべて `Kiji` 1 パッケージに同梱する。
  拡張パッケージの組み合わせ探しをユーザーに強いない。
- **minimal-API 風のフラグレス API**: `KijiApp.CreateBuilder(args)` → `Map*` → `RunAsync()`
  という ASP.NET Core に馴染む流れで組み立てる。挙動を切り替えるオプションフラグを
  増やさず、規約と少数の明示的 API で構成する。CLI フラグ（`--force` / `--verbose` /
  `--port`）はコマンドの実行制御に限る。
- **dev と build のパイプライン共有**: 開発サーバーはビルドとまったく同じレンダリング
  パス（`KijiApp.RenderPageAsync`）でページをオンデマンド描画する。dev で見たものが
  そのまま build の出力になる。
- **正しさ優先の高速化**: インクリメンタルビルドを含むすべての最適化は「stale な出力を
  絶対に出さない」制約の下にある。判定が曖昧な状況は常にフルビルドへフォールバックする。
- **1 ファイル 1 型**: ソースファイルは原則 1 ファイルに 1 つの型を置く。

## 全体アーキテクチャ

```
KijiBuilder ──build()──▶ KijiApp ──RunAsync()──▶ build | dev | preview | clean
   │                        │
   │ AddContentSource /     │ MapPages / MapRoutes /
   │ AddMarkdownContent     │ MapDefaultLayout / MapNotFound / MapArtifact
   ▼                        ▼
ContentRuntime          CreateSnapshot()
（遅延マテリアライズ）     ├─ PageDiscovery（@page スキャン）
                          ├─ StaticPagePlanner（動的ルート展開・出力パス計画）
                          └─ PageRenderRequest の一覧（SiteSnapshot）
                                   │
                  ┌────────────────┴───────────────┐
                  ▼ build                          ▼ dev
        IncrementalBuildPlanner            DevServer（オンデマンド描画、
        （スキップ判定・差分同期）            ライブリロード、Hot Reload 連携）
                  │
                  ▼
        StaticSiteGenerator.RenderPagesAsync
        （Parallel.ForEachAsync、ページごとに DI スコープ + HtmlRenderer）
                  │
                  ▼
        PooledUtf8TextWriter → RandomAccess.Write（1 ファイル 1 書き込み）
                  │
                  ▼
        ISiteArtifact（RSS / サイトマップ）→ BuildManifest 保存
```

## 主要コンポーネント

### KijiBuilder / KijiApp（`src/Kiji`）

`KijiBuilder` はサイトメタデータ（`SiteInfo`）、ディレクトリレイアウト（`SitePaths`:
`contents` / `wwwroot` / `dist` / `.kiji`）、DI サービス、コンテンツソース、ビルド入力
（`AddBuildInput`）を集める。`Build()` で `KijiApp` を生成し、`Map*` 系メソッドで
ページ・ルート・アーティファクトを宣言、`RunAsync()` が CLI（`build`（既定）/ `dev` /
`preview` / `clean`）をディスパッチする。

`SitePaths.Root` の既定値は **サイト自身のプロジェクトディレクトリ**（実行中アセンブリの位置、
次に現在のディレクトリから遡って最初に見つかったプロジェクトファイルのあるディレクトリ）で、
見つからなければ `.git` を持つ最近祖先、それも無ければ現在のディレクトリへフォールバックする。
プロジェクトを先に見るのは、**より大きなリポジトリの中に置かれたサイト**
（ライブラリと同居するドキュメントサイト、複数サイトのうちの 1 つ）が自分自身をルートと
するため。リポジトリルートに解決してしまうと `contents/` を別の場所に探しに行き、
しかも黙って失敗する。サイト 1 つだけのリポジトリでは両者が同じディレクトリに落ちるため
挙動は変わらない。

### ページ発見とルーティング（`src/Kiji/Routing`）

- **`PageDiscovery`**: `MapPages()` はエントリアセンブリ（または明示指定アセンブリ）を
  スキャンし、`@page` ルートテンプレート（`RouteAttribute`）を持つ public・非 abstract な
  `IComponent` をページとして登録する。「`@page` を書くことがページである」という
  ファイルベースルーティングの .NET 版。スキャン結果はアセンブリ単位でキャッシュされ、
  Hot Reload 時（`HotReloadHandler`）に破棄される。
- **`StaticPageDefinition`**: ルートテンプレートを正規化しセグメントへパースする。
  catch-all・ルート制約・オプションパラメータ・複合セグメントは非対応（例外で拒否）。
- **`StaticPagePlanner`**: 静的ルートはそのまま、動的ルート（`/blog/{Slug}/`）は
  `MapRoutes` が供給するルート値で展開し、`route/index.html` 形式の
  出力パスへ計画する。ルート重複・出力パス衝突・ルート値の不足/過剰・パス区切り文字
  混入はすべて計画段階で検証して失敗させる（出力を書く前に落とす）。

### コンテンツコレクション（`ContentCollection<T>` / `ContentRuntime`）

`AddContentSource` / `AddMarkdownContent<TFrontMatter>` はローダーを宣言するだけで、
実際のマテリアライズはスナップショット単位で遅延実行される（`ContentRuntime` が
`ConcurrentDictionary` で保持し、dev での変更時に無効化）。コレクションは
`WithKey`（キー付け・重複はエラー）、`OrderBy(Descending)`、`Map`（射影）を持ち、
コンポーネントからは `@inject` で消費する。

戻り値の `ContentCollection<T>` はそのコンテンツソースの**唯一のハンドル**であり、
ルートマッピング（`MapRoutes` のコンテンツオーバーロード）・フィード（`MapFeed`）・
コンポーネントへの DI 注入で同じインスタンスを共有する。builder 段階で宣言した
ハンドルを app 段階へ明示的に受け渡すのは意図的な設計で、DI からの暗黙解決にしない
ことで型安全性を保ち、同じ要素型のコレクションを複数登録しても曖昧にならない。

各アイテムには **provenance（由来ソースファイル）** が付随する。ルートコレクションでは
`IContentSourceFile`（`MarkdownContent<T>` が実装）から導出し、`Map` は射影が位置対応で
あることを利用して伝搬、ソートも provenance を同伴して並べ替える。これにより
`GetRequired(key)` のようなアイテム参照をインクリメンタルビルドの「ファイル単位依存」に
帰着できる（後述）。

### Markdown パイプライン（`src/Kiji/Markdown`）

- **フロントマター**: `MarkdownFrontMatterParser` が span ベースの手書きパーサーで
  `---` 区切りの YAML ブロックを特定し（正規表現不使用・アロケーション最小）、
  YamlDotNet（camelCase 規約、未知プロパティ無視）でサイト定義の任意の型へ
  デシリアライズする。フロントマターの「形」はフレームワークが決めず、サイトが決める。
- **本文**: `MarkdownProcessor` が Markdig（AdvancedExtensions + `SecureLinkExtension`）で
  HTML 化する。パイプラインや HTML 後処理は `MarkdownContentOptions` で拡張可能。
- **ページ同梱画像**: markdown が参照するローカル画像は、レンダリング中のページの
  出力ディレクトリへレスポンシブ WebP バリアントとして実体化され、`./` 相対 URL で
  参照される（サイトをどのベースパスに置いても動く「ページバンドル」レイアウト）。
  現在レンダリング中のページは `PageRenderContext`（`AsyncLocal`）が伝える。
- **`MarkdownContentsBuilder`**: `*.md` を列挙して並列に読む（列挙順を保った決定的
  順序、YamlDotNet はスレッドごとに分離、最初の失敗ファイルをそのままの例外型で
  報告）。読み込みは **1 ファイル 1 回**（`MarkdownSourceReader`）: 同じ read から
  フロントマター、レンダリング用ボディ、インクリメンタルビルド用コンテンツハッシュ
  （`ContentFileHashRegistry` 経由でプランナーと共有）をすべて得る。dev 向けには
  登録ごとの `MarkdownSourceCache`（mtime+サイズのトークン）があり、1 ファイル保存で
  1 ファイルだけ再読込する。
- **`MarkdownContent<T>`**: フロントマター + 遅延レンダリング。レンダリング結果は
  ルート単位でキャッシュされる（同じ markdown を複数ページが埋め込むと、画像の
  実体化先がページごとに異なるため）。

### レンダリング（`src/Kiji/Rendering` / `src/Kiji/Components`）

- **組み込みドキュメントシェル `KijiRoot`**: doctype、`SiteInfo.Language` による
  `<html lang>`、`<head>`（Kiji 独自の `HeadOutlet`）、`<body>`（Kiji 独自の `PageView` +
  既定レイアウト）をフレームワーク側が描画する。ページは `Kiji.Components.HeadContent`
  コンポーネント経由で `<head>` に寄与する。
- **`HeadContent` / `HeadOutlet` / `HeadContentRegistry`**: Blazor 標準の
  `SectionContent`/`SectionOutlet` の置き換え。ページ描画スコープの
  `HeadContentRegistry` を介して `HeadContent`（プロバイダ）が `HeadOutlet`（`<head>` 内）
  へ内容を publish する。outlet は本文より先に（空で）描画され、通知でキューされた
  再レンダーが quiescence 前に処理される — 標準 `SectionRegistry` と同一の
  Dispatcher 拘束メカニズム。意味論は「1 ページ 1 つ、最後にレンダーされたものが勝つ」
  （標準とパリティ）。標準実装と違い状態がレンダラーではなく DI スコープに載る。
- **`PageView`**: Blazor 標準の `RouteView` + `LayoutView` の置き換え。ページ型の
  `LayoutAttribute`（`@layout`）?? 既定レイアウトを解決し、レイアウト型の
  `LayoutAttribute` で入れ子に展開する（循環は例外、標準にない改善）。
  なお .NET 8+ の `[SupplyParameterFromQuery]` カスケードは `Router` コンポーネントが
  担うため、`Router` を使わない Kiji では標準 `RouteView` でも元々機能していなかった
  （SSG では非目標）。
- **`ComponentRenderer`**: ページごとに DI スコープと `HtmlRenderer` を生成して描画する。
  再利用（プーリング）は評価のうえ不採用: (1) `HtmlRenderer` は描画ごとにルート
  コンポーネント状態を蓄積し、公開 API に除去・リセットが存在しない、(2) レンダラーは
  構築時にサービスプロバイダを捕捉するため、ページ毎スコープのサービス
  （initialize-once な `StaticNavigationManager`、`HeadContentRegistry`）を差し替え
  られない、(3) スコープ + レンダラー生成はページ描画コストの実測 ~10%
  （`Kiji.Benchmarks`）に過ぎず、並列ビルドは 1 レンダラー = 1 Dispatcher の制約から
  どのみち CPU 数規模のプールを要する。これは Blazor SSR がリクエストごとに
  レンダラーを作るのと同型の設計。
- **ベースパス（`SiteInfo.BasePath` / `SiteInfo.Path`）**: `SiteInfo.BaseUrl` はパスセグメントを
  持てる（例 `https://user.github.io/repo/`）。`BasePath` はそのパス部分（常に `/` 終端）、
  `Path(path)` はサイトルート相対パスを配信パスへ解決する（`Path("css/app.css")` →
  `/repo/css/app.css`）。**フレームワークは href を生成しない**という原則は維持し、
  作者が明示的に呼ぶ。絶対 URL・プロトコル相対・`#`・`?` はそのまま返し、
  **ドキュメント相対（`./` `../`）は例外**にする（黙って壊れた URL を作る唯一の入力であり、
  ページ同梱画像の不変条件を API 境界で守るため）。
  canonical・RSS・サイトマップは `BaseUrl` 由来なので既にプレフィックス込みで正しく、
  ページ同梱画像はドキュメント相対なので影響を受けない。
  **ベースパスは配信位置のみを表し、`dist/` の出力レイアウトには一切影響しない**
  （`dist/repo/` にはしない）。dev / preview もこのプレフィックス配下で配信する。
- **`PooledUtf8TextWriter`**: ビルド時のページ書き込みは `TextWriter` を継承した
  専用ライターが受ける。UTF-16 の書き込みを `Utf8.FromUtf16` で `ArrayPool<byte>` の
  バッファへ直接トランスコードし（サロゲートペアの分割書き込みにも対応）、描画完了後に
  `File.OpenHandle`（preallocationSize 指定）+ `RandomAccess.Write` の 1 回で書き出す。
  StreamWriter/FileStream の多段バッファと分割 async 書き込みを排除し、BOM は書かない。
  出力ハッシュ（XxHash128）はバッファから直接計算し、マニフェストに記録する。

### 静的サイト生成（`src/Kiji/Generation`）

`StaticSiteGenerator` は全ページの出力パスを事前解決してディレクトリを一括作成し、
`Parallel.ForEachAsync`（並列度 = CPU 数）でページを並列描画する。静的ファイルと
ページ出力の衝突は描画前に検証する。静的ファイルのコピーは `File.Copy`
（Windows では CopyFileEx のカーネルファストパス）の並列実行。

### アーティファクト（`src/Kiji/Feeds` / `src/Kiji/Sitemaps`）

RSS フィードとサイトマップは `ISiteArtifact` の実装としてページ描画後に生成される。
`MapRoutes`（コンテンツオーバーロード）のキー関連付けから各コンテンツの生成ページ
URL・`lastmod` を解決する。
サードパーティは `MapArtifact(ISiteArtifact)` で任意のサイト全体出力を追加できる。

### 開発サーバー（`src/Kiji/Hosting`）

`WebApplication.CreateSlimBuilder` ベース。ページは全生成せず、リクエストごとに
ビルドと同一パイプラインで描画する（`MapFallback`）。

- **スナップショット**: ページ計画は遅延構築 + ロック保護。起動直後にバックグラウンドで
  投機構築するため、初回リクエストはほぼ即応する。
- **ライブリロード**: `FileSystemWatcher`（content/static、250ms デバウンス、変更の
  デデュープ）→ WebSocket（`/_kiji/reload`）で接続ブラウザへ `reload` を配信。
  リロードスクリプトは `</body>` 直前に注入される。
- **Hot Reload 連携**: `MetadataUpdateHandler`（`HotReloadHandler`）がコード更新を受けて
  `PageDiscovery` キャッシュとスナップショットを破棄し、ブラウザをリロードする。
- **コンテンツ変更**: content 変更時はコレクションを無効化して再マテリアライズする。
  フロントマターキャッシュにより実際の再パースは変更ファイルだけで済む。

### CLI（`KijiCommandLine`）

`build`（既定。`--verbose` でファイル単位ログ、`--force` でフルビルド強制）、
`dev [--port <n>]`、`preview [--port <n>]`、`clean`。`preview` は生成済み `dist` を本番同等の
trailing-slash・404 セマンティクスで配信する。`clean` は出力ディレクトリ（`dist`）と
`.kiji`（ビルドマニフェスト・画像キャッシュ・dev サイトミラー）を削除する。フラグは
持たず、次のビルドは必然的にフルビルドになる。ビルド中のコンソール出力は集約サマリが
既定（並列ループからのページごとの出力はコンソールロックで直列化し支配的コストに
なり得るため）。

## インクリメンタルビルド

`kiji build` は既定でインクリメンタルに動作する。中心となる問いは
「このページの前回の出力を、そのまま使ってよいと**証明**できるか」であり、
証明できなければ再レンダリングする。

### ビルドマニフェスト

`.kiji/cache/build-manifest.json`（System.Text.Json source generator でシリアライズ）に
前回ビルドの全記録を残す:

- **グローバルフィンガープリント**: オプションハッシュ（`SiteInfo`、相対パス
  レイアウト、`AddBuildInput` の値/ファイルハッシュ）と、サイトを構成する
  非フレームワークアセンブリの **MVID**（参照閉包、`System.*`/`Microsoft.*` は除外）。
- **ページごと**: 出力相対パス、ルート、パラメータハッシュ、出力ハッシュ
  （XxHash128）、依存一覧、追加出力（画像バリアント等）。
- **静的ファイルごと**: ソースと宛先の (サイズ, mtime) スタンプ。
- **アーティファクト**の出力パス一覧。

### 依存の記録

ページ描画中、`PageRenderContext.Dependencies`（`BuildDependencyRecorder`）へ
アンビエントに記録される:

| 記録元 | 依存の種類 |
|---|---|
| `MarkdownContent.FrontMatter` 読み取り / `RenderAsync` | `file:`（その markdown ファイル） |
| markdown が参照する画像の実体化 | `file:`（画像ソース）+ 追加出力（バリアント） |
| `ContentCollection.Items` の列挙 | `content-set`（contents 配下の全 `*.md` の合成ダイジェスト） |
| `TryGet` / `GetRequired`（キー参照） | provenance があれば `file:`、なければ `content-set`（保守的） |

記事ページ（キー参照 + 本文レンダリング）は自分の markdown ファイルだけに依存し、
一覧ページ（列挙）はコンテンツ集合全体に依存する。結果として「1 記事の編集 =
その記事ページ + 一覧ページ + アーティファクトのみ再生成」になる。

### スキップ判定（すべて成立で温存、ひとつでも崩れたら再レンダリング）

1. グローバルフィンガープリント（スキーマ / オプション / MVID）完全一致
2. ルートとパラメータハッシュ一致
3. 出力ファイルが存在し、内容ハッシュがマニフェストと一致（外部改変の検出）
4. 追加出力がすべて存在
5. 記録された全依存のフィンガープリントが一致

判定 3 と 5 は **スタンプゲート**で短絡する: マニフェストには各出力・各ファイル依存の
(サイズ, mtime) スタンプも記録されており、現在のスタンプが一致する限り記録済み
ハッシュを信頼してファイルを読まない。スタンプ不一致時のみ再ハッシュして比較する
（touch だけで内容が同じならハッシュ一致でスキップは維持される）。これは静的
ファイル同期（下記）と同じトレードオフで、「mtime とサイズを保ったまま内容を
書き換える」加工だけがすり抜けるが、その場合も `--force` で常に復旧できる。
無変更リビルドはこれにより O(全ファイル読込) から O(stat) に落ちる。

### 差分出力同期と保守的フォールバック

- 出力ディレクトリの全削除は行わない。スキップされたページの出力は温存し、
  前回マニフェストにあって今回の出力集合にないファイルは孤児として削除、
  空ディレクトリを回収する。
- 静的ファイルはソース/宛先双方の (サイズ, mtime) スタンプ一致でコピーをスキップする。
- **フルビルドへのフォールバック**: マニフェスト欠損・破損・スキーマ不一致、
  出力ディレクトリにマニフェスト外の未知ファイルが存在、`--force` 指定。
  いずれもクリーンビルドとして扱う。
- アーティファクト（RSS/サイトマップ）は全ページメタデータに依存し生成も安価なため、
  常に再生成する（増分化しない）。

### 追跡できない入力

レンダリングは入力に対して決定的であることが前提。`DateTime.Now` や HTTP 取得など
Kiji が観測できない入力を使うサイトは、`KijiBuilder.AddBuildInput(path)`（ファイル/
ディレクトリの内容ハッシュ）または `AddBuildInput(key, value)`（変化したら全再生成）で
フィンガープリントに参加させるか、`--force` を使う。インメモリデータはコードに
由来するため MVID が変更を捕捉する。`SiteInfo.BuildTime` は意図的にフィンガープリントへ
含めない（キャッシュバスティング用途であり、出力が変わらない限り古い値の温存が正しい）。

## パフォーマンス設計の要点

- ページ描画・markdown パース・静的コピー・画像エンコード（`SemaphoreSlim` で
  CPU 数に制限）はすべて並列。ルート/コンテンツ索引は `FrozenDictionary`。
- Markdig の `HtmlRenderer` はプロセッサ単位でプール（`PooledMarkdigRenderer`）。
  セットアップがレンダリング本体の約 3 倍のコストで、再利用でレンダーステップの
  時間・アロケーションとも約 70% 減（`MarkdownRenderBenchmarks` で実測）。画像
  try-writer は attach-once + コンテキスト差し替え。Blazor の `HtmlRenderer` は
  従来どおりページ毎生成（理由は「意図的に採用しなかった設計」参照 — 別物）。
- 書き込みは「pooled UTF-8 バッファ + 1 syscall + preallocation」。ハッシュは
  書き込みバッファから直接計算し、ファイルを読み戻さない。
- ロギングはページごとではなく集約（`--verbose` でオプトイン）。DI には既定で
  プロバイダなしの logging を登録する（ページごとのレンダラー生成でロガー解決が
  走るため）。
- 測定基盤を同居させる: `src/Kiji.Benchmarks`（BenchmarkDotNet マイクロベンチ）と
  `src/Kiji.SyntheticSite`（N ページの合成サイトでフル / 無変更 / 1 記事編集の
  E2E ビルドを計測、JSON 出力。最適化時のローカル before/after 比較用）。
  最適化は測ってから入れる。本文に出典なしの生の数値を書かない（検証不能なため）。
- CI ではマイクロベンチのみ実行する（`.github/workflows/benchmarks.yml`、
  手動トリガー + 週次。フル実行に時間がかかるため PR をブロックする CI からは
  分離）。E2E レベルの回帰ゲートと他 SSG との競合比較は現時点では導入しない
  （前者は時期尚早、後者は本リポジトリの責務外）。`docs/benchmarks.md` 参照。

## 意図的に採用しなかった設計

| 項目 | 理由 |
|---|---|
| Native AOT / トリミング対応 | Blazor `HtmlRenderer` と `ParameterView` のパラメータ設定、YamlDotNet がリフレクション前提で full-AOT は公式サポート外。SSG はスループットバウンドで、AOT の利点（起動時間・バイナリサイズ）が問題に刺さらない。 |
| ページ発見のソースジェネレーター化 | 1 アセンブリのスキャン + キャッシュで ms オーダー。ビルド複雑性とデバッグ性のコストが利益を桁で上回る。 |
| `HtmlRenderer` のプーリング | ルートコンポーネント状態の除去・リセットが公開 API に存在せず再利用は状態リーク、ページ毎スコープのサービス（`StaticNavigationManager`/`HeadContentRegistry`）は構築時捕捉で差し替え不能。実測でセットアップはページコストの約 10% に過ぎず、並列描画には結局 CPU 数規模のプールが要る。 |
| Blazor 標準コンポーネントの継続利用（`SectionOutlet`/`RouteView`） | `SectionRegistry` の状態がレンダラーに紐づき Kiji から制御不能。.NET 10 の `RouteView` はレイアウト解決以外に Kiji が使う機能を持たない（クエリカスケードは `Router` 必須で元々不動作）。独自の `HeadOutlet`/`HeadContentRegistry`/`PageView` に置き換え、状態を DI スコープへ移した。 |
| `IBufferWriter<byte>` への直接レンダリング | `HtmlRootComponent` の公開 API が `WriteHtmlTo(TextWriter)` のみ。`PooledUtf8TextWriter` による TextWriter 層でのトランスコードが到達可能な上限。 |
| 型単位のコンポーネント変更検出 | レンダリングで実際に使われた型グラフを取得する公開手段がなく、static/定数の変更は型グラフでも捕捉不能。MVID によるアセンブリ単位の無効化が正しさを保てる最小粒度。 |
| 画像の中央 content-addressed 配置（`/_assets/{hash}`） | ページバンドル（記事と画像が同じディレクトリ）のレイアウトと `./` 相対参照を壊す。画像は既に content-hash キャッシュ済みで再エンコードは発生しない。 |
| RSS/サイトマップの増分生成 | 全ページメタデータに依存し、生成コストが元々軽微。 |
| ベースパスのための `<base href>` 発行 | ページ同梱画像は `./` のドキュメント相対 URL を記事の `index.html` の隣へ出す設計であり、`<base>` を入れるとこれが再ルート化されて壊れる。皮肉にも `<base>` は「唯一ベースパス安全だった機能」を壊す。 |
| レンダリング後の href 書き換え | Kiji は href を生成しない設計であり、出力 HTML を後処理で走査して「サイトルート相対だけ」を判別するのは不正確（`#`・クエリ・外部 URL・ドキュメント相対の区別）かつ全ページ再パースのコストがかかる。作者が `SiteInfo.Path` を明示的に呼ぶ方が正確で安い。 |
| `--base-path` CLI フラグ | ベースパスは `SiteInfo.BaseUrl` を単一の真実とする（sitemap/RSS/canonical と同じ源）。CLI フラグは実行制御に限るという方針にも合わない。 |

## セキュリティ上の配慮

- 出力パス解決は常に出力ルート配下であることを検証する（ページ・静的ファイル・
  アーティファクトすべて。パストラバーサル防止）。
- ルート値に `/` `\` `.` `..` を許さない。
- 画像は EXIF/IPTC/XMP メタデータを除去して出力する。
- markdown のリンクは `SecureLinkExtension` で安全化する。

## テスト戦略

xUnit v3 + Microsoft.Testing.Platform。`InternalsVisibleTo` で internal を直接検証する。

- **単体**: ルートパース、slug、フロントマター（旧正規表現をオラクルにした等価性
  コーパス）、`PooledUtf8TextWriter`（バイト一致・サロゲート・プール成長）など。
- **統合**: `TestSite/`（実サイト相当のページ・レイアウト・コンテンツ）を使った
  E2E ビルド、dev サーバー（オンデマンド描画・リロード配信・変更デデュープ）。
- **インクリメンタルビルドの同値性**: 「フルビルド」と「フルビルド → 変更 →
  インクリメンタルビルド」の出力を全ファイルのバイト比較で一致させる。加えて
  スキップの実証（mtime 不変）、孤児回収、未知ファイル・改変・マニフェスト破損の
  フォールバックを検証する。

## リポジトリ構成

- `src/Kiji` — フレームワーク本体（routing / rendering / markdown / assets /
  generation / feeds / sitemaps / hosting）
- `src/Kiji.Tests` — 単体・統合テスト（正しさ検証用フィクスチャ `TestSite/` を含む。
  テストの都合で自由に進化する）
- `src/Kiji.Benchmarks` — ホットパスのマイクロベンチマーク
- `src/Kiji.SyntheticSite` — E2E ビルド性能ハーネス。サイト定義は計測値の比較
  可能性を保つための凍結された代表ワークロードであり、`TestSite/` とは意図的に
  共有しない（テスト都合のサイト変更が計測値へ暗黙に波及する結合を避ける）
