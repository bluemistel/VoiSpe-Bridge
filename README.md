# A.I.VOICE Bridge

マイクから音声を認識し、リアルタイムで音声合成ソフトに送って読み上げる Windows アプリです。  
OBS 配信中の「声を変えて喋る」「別のキャラクターに代読させる」といった用途を想定しています。

---

## 機能

- **ローカル音声認識** — OpenAI Whisper をローカル実行（ネット接続不要）
- **GPU アクセラレーション** — NVIDIA (CUDA) / AMD・Intel (Vulkan) を自動選択
- **プラグイン方式** — DLL を plugins フォルダに置くだけで対応ソフトを追加可能
- **出力デバイス選択** — 仮想オーディオデバイス（VB-Cable 等）経由で OBS に直接ルーティング
- **発話履歴** — 過去の認識・読み上げ内容を表示

### 対応音声合成ソフト

| プラグイン | 必要なもの | 備考 |
|---|---|---|
| **A.I.VOICE2** | A.I.VOICE2 本体 | クリップボード＋キー操作で連携 |
| **VOICEPEAK** | VOICEPEAK 本体 | 公式 CLI API 経由 |
| **CeVIO AI** | CeVIO AI 本体 + `CeVIO.Talk.RemoteService2.dll` | .NET Framework ブリッジ経由 |
| A.I.VOICE v1 | A.I.VOICE 本体 + `AI.Talk.Editor.Api.dll` | レガシー対応 |

---

## 動作環境

- Windows 10 / 11 (x64)
- .NET 8 ランタイム（`--self-contained false` ビルド）
- .NET Framework 4.8（CeVIO AI プラグイン使用時のみ）

---

## インストール

### 1. アプリ本体の配置

1. [Releases](../../releases) から `AIVoiceBridge-vX.X.X.zip` をダウンロード
2. 任意のフォルダに展開
3. `AIVoiceBridge.exe` を実行

> 初回起動時に Whisper モデル（約 500 MB）を自動ダウンロードします。  
> ダウンロード先: `%APPDATA%\AIVoiceBridge\models\`

### 2. プラグインの追加

使用する音声合成ソフトに対応したプラグインパッケージを `plugins\` フォルダに展開します。

```
AIVoiceBridge.exe
plugins\
  AIVoiceBridge.Plugin.AIVoice2.dll   ← A.I.VOICE2 用
  AIVoiceBridge.Plugin.Voicepeak.dll  ← VOICEPEAK 用
  AIVoiceBridge.Plugin.CeVIOAI.dll    ← CeVIO AI 用
  AIVoiceBridge.CeVIOBridge.exe       ← CeVIO AI 用（ブリッジ）
  AIVoiceBridge.CeVIOBridge.exe.config
```

---

## プラグイン別セットアップ

### A.I.VOICE2

追加 DLL は不要です。A.I.VOICE2 を起動した状態でアプリから接続してください。

### VOICEPEAK

追加 DLL は不要です。VOICEPEAK のインストール先が標準パス  
（`C:\Program Files\VOICEPEAK\voicepeak.exe`）であれば自動検出されます。

### CeVIO AI

`CeVIO.Talk.RemoteService2.dll` を CeVIO AI のインストールフォルダから `plugins\` へコピーしてください。

```powershell
Copy-Item "C:\Program Files\CeVIO\CeVIO AI\CeVIO.Talk.RemoteService2.dll" .\plugins\
```

> CeVIO AI の .NET API は System.Runtime.Remoting を使用しているため、  
> .NET Framework 4.8 製のブリッジプロセス（`AIVoiceBridge.CeVIOBridge.exe`）経由で連携します。

### A.I.VOICE v1（レガシー）

`AI.Talk.Editor.Api.dll` を A.I.VOICE のインストールフォルダから `plugins\` へコピーしてください。

```powershell
Copy-Item "C:\Program Files\AI\AIVoice\AIVoiceEditor\AI.Talk.Editor.Api.dll" .\plugins\
```

---

## OBS での使い方

1. **VB-Cable** などの仮想オーディオデバイスをインストール
2. アプリの「出力デバイス」で仮想デバイスを選択
3. OBS のオーディオソースで同じ仮想デバイスを入力に設定

```
マイク → AIVoiceBridge → 仮想オーディオデバイス → OBS
```

---

## 使い方

1. 右ペインでプラグイン・モデル・音声パラメータを設定
2. 「● 認識開始」をクリック
3. マイクに向かって話すと自動で認識・読み上げ
4. 手動で読み上げたい場合はテキストボックスに入力して「読み上げ」

### 音声認識モデル

| モデル | サイズ | 精度 | 推奨 |
|---|---|---|---|
| Tiny | 約 77 MB | 低 | 低スペック PC |
| Small | 約 467 MB | 中〜高 | **通常はこちら** |
| Medium | 約 1.5 GB | 高 | 高精度が必要な場合 |

---

## 開発者向け

### ビルド

```powershell
# リリースビルド
.\build.ps1

# デバッグビルド
.\build.ps1 -Configuration Debug
```

出力先:
- `dist\app\` — メインアプリ
- `dist\plugins\` — 各プラグイン

### 新しいプラグインの作り方

[docs/plugin-howto.md](docs/plugin-howto.md) を参照してください。

### プロジェクト構成

```
src\
  AIVoiceBridge.Core\          IVoiceSynthesizerPlugin インターフェース
  AIVoiceBridge.App\           WPF メインアプリ（Whisper 音声認識）
  CeVIOBridge\                 CeVIO AI ブリッジ（.NET Framework 4.8）
  Plugins\
    AIVoiceBridge.Plugin.AIVoice2\    A.I.VOICE2 プラグイン
    AIVoiceBridge.Plugin.AIVoice\     A.I.VOICE v1 プラグイン
    AIVoiceBridge.Plugin.Voicepeak\   VOICEPEAK プラグイン
    AIVoiceBridge.Plugin.CeVIOAI\     CeVIO AI プラグイン
```

---

## 既知の問題

- ウィンドウのリサイズ時に稀に異常終了することがあります（調査中）
- CeVIO AI 接続時は CeVIO AI が先に起動している必要があります
- 外部連携 API の同時使用は 1 アプリのみ（CeVIO AI の制限）

---

## ライセンス

MIT License

各プラグインが連携する音声合成ソフト（A.I.VOICE2、VOICEPEAK、CeVIO AI 等）のライセンスは  
それぞれのソフトウェアに従います。
