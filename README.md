# AINE Paint

Windows向け 無料お絵描きソフト（StudioAINE）

現在: **v0.1.0 / STEP 5 — アプリが起動するところまで**

## 技術構成

| 領域 | 採用 |
|---|---|
| 言語 / ランタイム | C# / .NET 9 (net9.0-windows) |
| UI | WPF |
| 描画エンジン | SkiaSharp (SkiaSharp.Views.WPF) |
| 筆圧 | WPF Stylus API（次ステップ以降で実装） |
| 配布 | 自己完結publish + インストーラー（後日） |

## ビルドと起動

```powershell
cd C:\Users\a\source\AINEPaint
dotnet restore
dotnet run --project src\AINEPaint\AINEPaint.csproj
```

Visual Studio / VS Code で `AINEPaint.sln` を開いても可。

## プロジェクト構造（責務分離）

```
src/AINEPaint/
  App.xaml            アプリ起動・テーマ読み込み
  MainWindow.xaml     UIシェル（ツール / キャンバス / レイヤー / 下部バー）
  Themes/             テーマ定義（色は全てここに集約）
  Drawing/            キャンバス表示・ストローク描画
  Brushes/            ブラシ定義（ペン / 鉛筆 / 消しゴム）
  Layers/             レイヤーモデルと合成
  History/            Undo / Redo（操作履歴ベース）
  IO/                 PNG / JPG 読み書き、.ainpaint 形式
  Color/              色管理・カラーピッカー
  Settings/           設定の保存・読み込み
```

`Layers/` `IO/` `Settings/` はまだ空。1機能ずつ埋めていく。

### 設計上の決めごと

- **キャンバスの画素は常に透明で始める。** 白背景は表示・書き出し時に敷くものとして扱う。
  焼き込むと消しゴムとPNG透明書き出しが両立しない。
- **ストロークは専用バッファに不透明で描き、指を離した時点で1回だけ合成する。**
  直接描くと、同じストローク内で線が重なった箇所だけ濃くなる。
- **Undo はタイル差分方式。** 変更されたタイルの変更前の中身だけを保存し、
  Undo / Redo は「入れ替え」という同一操作で処理する。

## 実装ロードマップ（MVP）

- [x] STEP 5: 空アプリが起動する
- [x] STEP 6: キャンバス表示（サイズ指定・白/透明背景・中央表示・ズーム・パン）
- [x] STEP 7: 1本の線を描く（ペン）
- [x] STEP 8: ブラシサイズ・不透明度・ツール切り替え・色（ピッカー / HEX / RGB / スポイト）
- [x] STEP 9: Undo / Redo（タイル差分方式・50手 または 512MB）
- [ ] STEP 10: レイヤー
- [ ] STEP 11: 保存（.ainpaint / PNG書き出し / PNG・JPG読み込み）

Phase 2以降はMVP完成後に着手する。

## ライセンス

TBD
