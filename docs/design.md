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
KijiBuilder ──build()──▶ KijiApp ──RunAsync()──▶ build | dev | preview
   │                        │
   │ AddContentSource /     │ MapPages / MapContent / MapRoutes /
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
`preview`）をディスパッチする。

### ページ発見とルーティング（`src/Kiji/Routing`）

- **`PageDiscovery`**: `MapPages()` はエントリアセンブリ（または明示指定アセンブリ）を
  スキャンし、`@page` ルートテンプレート（`RouteAttribute`）を持つ public・非 abstract な
  `IComponent` をページとして登録する。「`@page` を書くことがページである」という
  ファイルベースルーティングの .NET 版。スキャン結果はアセンブリ単位でキャッシュされ、
  Hot Reload 時（`HotReloadHandler`）に破棄される。
- **`StaticPageDefinition`**: ルートテンプレートを正規化しセグメントへパースする。
  catch-all・ルート制約・オプションパラメータ・複合セグメントは非対応（例外で拒否）。
- **`StaticPagePlanner`**: 静的ルートはそのまま、動的ルート（`/blog/{Slug}/`）は
  `MapContent` / `MapRoutes` が供給するルート値で展開し、`route/index.html` 形式の
  出力パスへ計画する。ルート重複・出力パス衝突・ルート値の不足/過剰・パス区切り文字
  混入はすべて計画段階で検証して失敗させる（出力を書く前に落とす）。

### コンテンツコレクション（`ContentCollection<T>` / `ContentRuntime`）

`AddContentSource` / `AddMarkdownContent<TFrontMatter>` はローダーを宣言するだけで、
実際のマテリアライズはスナップショット単位で遅延実行される（`ContentRuntime` が
`ConcurrentDictionary` で保持し、dev での変更時に無効化）。コレクションは
`WithKey`（キー付け・重複はエラー）、`OrderBy(Descending)`、`Map`（射影）を持ち、
コンポーネントからは `@inject` で消費する。

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
- **`MarkdownContentsBuilder`**: `*.md` を列挙して並列にフロントマターをパースする
  （列挙順を保った決定的順序、YamlDotNet はスレッドごとに分離、最初の失敗ファイルを
  そのままの例外型で報告）。dev 向けには登録ごとの `MarkdownFrontMatterCache`
  （mtime+サイズ's トークン）があり、1 ファイル保存で 1 ファイルだけ再パースする。
- **`MarkdownContent<T>`**: フロントマター + 遅延レンダリング。レンダリング結果は
  ルート単位でキャッシュされる（同じ markdown を複数ページが埋め込むと、画像の
  実体化先がページごとに異なるため）。

### レンダリング（`src/Kiji/Rendering` / `src/Kiji/Components`）

- **組み込みドキュメントシェル `KijiRoot`**: doctype、`SiteInfo.Language` による
  `<html lang>`、`<head>`（`SectionOutlet`）、`<body>`（`RouteView` + 既定レイアウト）を
  フレームワーク側が描画する。ページは `Kiji.Components.Head` コンポーネント経由で
  `<head>` に寄与する（`SectionContent` ベース）。
- **`ComponentRenderer`**: ページごとに DI スコープと `HtmlRenderer` を生成して描画する。
  `HeadOutlet`/`SectionOutlet` の購読状態がレンダラーインスタンスに紐づくため、
  レンダラーの再利用はページ間で `<head>` が漏れる。プーリングは実測
  （スコープ + レンダラー生成 ≒ ページ描画コストの 6%、`Kiji.Benchmarks`）に基づき
  採用しない。これは Blazor SSR がリクエストごとにレンダラーを作るのと同型の設計。
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
`MapContent` のキー関連付けから各コンテンツの生成ページ URL・`lastmod` を解決する。
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
`dev [--port <n>]`、`preview [--port <n>]`。`preview` は生成済み `dist` を本番同等の
trailing-slash・404 セマンティクスで配信する。ビルド中のコンソール出力は集約サマリが
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
- 書き込みは「pooled UTF-8 バッファ + 1 syscall + preallocation」。ハッシュは
  書き込みバッファから直接計算し、ファイルを読み戻さない。
- ロギングはページごとではなく集約（`--verbose` でオプトイン）。DI には既定で
  プロバイダなしの logging を登録する（ページごとのレンダラー生成でロガー解決が
  走るため）。
- 測定基盤を同居させる: `src/Kiji.Benchmarks`（BenchmarkDotNet マイクロベンチ）と
  `tools/Kiji.SyntheticSite`（N ページの合成サイトでフル / 無変更 / 1 記事編集の
  E2E ビルドを計測、JSON 出力）。最適化は測ってから入れる。
  参考値（1,000 ページ、Ryzen 級デスクトップ）: フルビルド約 1.3〜1.6 秒、
  1 記事編集後のリビルド約 0.2 秒。

## 意図的に採用しなかった設計

| 項目 | 理由 |
|---|---|
| Native AOT / トリミング対応 | Blazor `HtmlRenderer` と `ParameterView` のパラメータ設定、YamlDotNet がリフレクション前提で full-AOT は公式サポート外。SSG はスループットバウンドで、AOT の利点（起動時間・バイナリサイズ）が問題に刺さらない。 |
| ページ発見のソースジェネレーター化 | 1 アセンブリのスキャン + キャッシュで ms オーダー。ビルド複雑性とデバッグ性のコストが利益を桁で上回る。 |
| `HtmlRenderer` のプーリング | `SectionRegistry`（internal）の状態分離が公開 API で解決できず、実測でセットアップはページコストの約 6% に過ぎない。 |
| `IBufferWriter<byte>` への直接レンダリング | `HtmlRootComponent` の公開 API が `WriteHtmlTo(TextWriter)` のみ。`PooledUtf8TextWriter` による TextWriter 層でのトランスコードが到達可能な上限。 |
| 型単位のコンポーネント変更検出 | レンダリングで実際に使われた型グラフを取得する公開手段がなく、static/定数の変更は型グラフでも捕捉不能。MVID によるアセンブリ単位の無効化が正しさを保てる最小粒度。 |
| 画像の中央 content-addressed 配置（`/_assets/{hash}`） | ページバンドル（記事と画像が同じディレクトリ）のレイアウトと `./` 相対参照を壊す。画像は既に content-hash キャッシュ済みで再エンコードは発生しない。 |
| RSS/サイトマップの増分生成 | 全ページメタデータに依存し、生成コストが元々軽微。 |

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
- `src/Kiji.Tests` — 単体・統合テスト（`TestSite/` を含む）
- `src/Kiji.Benchmarks` — ホットパスのマイクロベンチマーク
- `tools/Kiji.SyntheticSite` — E2E ビルド性能ハーネス
