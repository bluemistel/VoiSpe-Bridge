# VoiSpe-Bridge

マイクの音声をリアルタイムに認識し、音声合成ソフトへ転送して読み上げる Windows アプリです。  
OBS 配信中に「声を変えて喋る」「別のキャラクターに代読させる」といった用途を想定しています。

---

## 機能一覧

### 音声認識エンジン（3 種類から選択）

| エンジン | 方式 | GPU | インターネット | 特徴 |
|---------|------|-----|--------------|------|
| **Whisper** | ローカル実行 | 使用 (任意) | 不要 | OpenAI Whisper。高精度。初回のみモデルをダウンロード |
| **ブラウザ（Web Speech API）** | Google クラウド | 不使用 | 必要 | GPU 非使用のためゲーム中の負荷ゼロ。音声は Google に送信される |
| **ReazonSpeech** | ローカル実行 | 不使用 (CPU) | 不要 | 日本語特化。GPU 不要。初回のみモデルをダウンロード（数百 MB） |

### VAD（音声アクティビティ検出）

Whisper / ReazonSpeech エンジン使用時、以下のパラメータで発話区間を自動検出します。

| 設定 | 説明 |
|------|------|
| 発話検出レベル | この音量を超えると「発話開始」と判定（dB） |
| ノイズゲート | 発話中にこの音量を下回ったフレームを除外（dB） |
| 無音で確定 (ms) | 発話後の無音がこの時間続くと認識確定 |
| プリバッファ | 発話検出前 500ms を遡って取り込む（冒頭切れ防止） |

### Whisper モデル

| モデル | サイズ | 特徴 |
|--------|--------|------|
| Tiny | 約 77 MB | 最速・精度低め |
| Base | 約 142 MB | 高速・軽量 |
| Small | 約 467 MB | バランス型 |
| **Large v3 Turbo** | 約 809 MB | ★推奨。高精度・高速（蒸留版 Large v3） |
| Medium | 約 1.5 GB | 最高精度・高負荷 |

GPU アクセラレーション：NVIDIA (CUDA) / AMD・Intel (Vulkan) を自動選択。

### 配信用字幕ウィンドウ

ヘッダーの「🖥 配信用字幕」ボタンで起動。音声認識テキストをグリーンバック背景に大きく表示します。

| 設定 | 内容 |
|------|------|
| フォント | インストール済みフォントから選択（初期値: メイリオ） |
| フォントサイズ | 20〜200px をスライダーで調整 |
| 縁取りの太さ | 0〜40px をスライダーで調整 |
| 文字色 | Windows カラーピッカーで自由に設定 |
| 縁取り色 | Windows カラーピッカーで自由に設定 |

**OBS 設定手順：**
1. 「ウィンドウキャプチャ」→「VoiSpe-Bridge - 配信用字幕」を選択
2. フィルタ → 「クロマキー」→ 緑 (`#00FF00`) を設定
3. 必要に応じて上部の帯（28px）をクロップで除外

### その他の機能

- **出力デバイス選択** — 仮想オーディオデバイス（VB-Cable 等）経由で OBS に直接ルーティング
- **辞書プリセット** — 認識テキストの自動変換ルールをプリセット管理
- **発話履歴** — 過去の認識・読み上げ内容を時刻付きで表示
- **手動入力** — テキストボックスから直接読み上げ（Enter キー / ボタン）
- **感情パラメータ** — 対応プラグインのキャラクター感情をスライダーで調整
- **プラグイン方式** — DLL を `plugins\` フォルダに置くだけで対応ソフトを追加

---

## 対応音声合成ソフト

| プラグイン | 必要なもの | 接続方式 |
|-----------|-----------|---------|
| **A.I.VOICE2** | A.I.VOICE2 本体 | クリップボード＋キー操作 |
| **VOICEPEAK** | VOICEPEAK 本体 | 公式 CLI API |
| **CeVIO AI** | CeVIO AI 本体 + `CeVIO.Talk.RemoteService2.dll` | .NET Framework ブリッジ |
| **VoisonaTalk** | VoisonaTalk 本体（REST API 有効化） | REST API |
| A.I.VOICE v1 | A.I.VOICE 本体 + `AI.Talk.Editor.Api.dll` | .NET Framework ブリッジ |

---

## 動作環境

- **OS**: Windows 10 / 11 (x64)
- **.NET ランタイム**: .NET 8（`--self-contained false` ビルド）
- **WebView2**: Microsoft Edge WebView2 ランタイム（Windows 11 標準搭載・Windows 10 は Edge と共に自動インストール）
- **.NET Framework 4.8**: CeVIO AI / A.I.VOICE v1 プラグイン使用時のみ

---

## インストール

### 1. アプリ本体の配置

1. [Releases](../../releases) から `VoiSpeBridge-vX.X.X.zip` をダウンロード
2. 任意のフォルダに展開
3. `VoiSpeBridge.exe` を実行

> **初回起動時の自動ダウンロード:**  
> Whisper エンジン: モデルファイル（〜809 MB）を `%APPDATA%\VoiSpeBridge\models\whisper\` にダウンロードします。  
> ReazonSpeech エンジン: モデルファイル（〜数百 MB）を `%APPDATA%\VoiSpeBridge\models\reazonspeech\` にダウンロードします。

### 2. プラグインの追加

使用する音声合成ソフトに対応したプラグインパッケージを `plugins\` フォルダに展開します。

```
VoiSpeBridge.exe
plugins\
  VoiSpeBridge.Plugin.AIVoice2.dll     ← A.I.VOICE2 用
  VoiSpeBridge.Plugin.Voicepeak.dll    ← VOICEPEAK 用
  VoiSpeBridge.Plugin.VoisonaTalk.dll  ← VoisonaTalk 用
  VoiSpeBridge.Plugin.CeVIOAI.dll      ← CeVIO AI 用
  VoiSpeBridge.CeVIOBridge.exe         ← CeVIO AI 用（.NET 4.8 ブリッジ）
  VoiSpeBridge.CeVIOBridge.exe.config
```

---

## プラグイン別セットアップ

### A.I.VOICE2

追加 DLL は不要です。A.I.VOICE2 を起動した状態でアプリから接続してください。

### VOICEPEAK

追加 DLL は不要です。VOICEPEAK のインストール先が標準パス  
（`C:\Program Files\VOICEPEAK\voicepeak.exe`）であれば自動検出されます。

### VoisonaTalk

1. VoisonaTalk の「設定 > API タブ」で REST API を有効化
2. アプリの「音声合成設定 > 接続設定」にメールアドレスと API パスワードを入力
3. 「再接続」をクリック

### CeVIO AI

`CeVIO.Talk.RemoteService2.dll` を CeVIO AI のインストールフォルダから `plugins\` へコピーしてください。

```powershell
Copy-Item "C:\Program Files\CeVIO\CeVIO AI\CeVIO.Talk.RemoteService2.dll" .\plugins\
```

> CeVIO AI の .NET API は .NET Framework 4.8 製ブリッジプロセス経由で連携します。  
> CeVIO AI は先に起動しておいてください。

### A.I.VOICE v1（レガシー）

`AI.Talk.Editor.Api.dll` を A.I.VOICE のインストールフォルダから `plugins\` へコピーしてください。

```powershell
Copy-Item "C:\Program Files\AI\AIVoice\AIVoiceEditor\AI.Talk.Editor.Api.dll" .\plugins\
```

---

## OBS での音声ルーティング

```
マイク → VoiSpeBridge → 仮想オーディオデバイス → OBS
```

1. **VB-Cable** などの仮想オーディオデバイスをインストール
2. アプリの「出力デバイス」で仮想デバイスを選択
3. OBS のオーディオソースで同じ仮想デバイスを入力に設定

---

## 使い方

1. 右ペインで音声認識エンジン・プラグイン・音声パラメータを設定
2. 「● 認識開始」をクリック
3. マイクに向かって話すと自動で認識・読み上げ
4. 字幕が必要な場合はヘッダーの「🖥 配信用字幕」をクリックしてウィンドウを表示

---

## 開発者向け

### ビルド

```powershell
# リリースビルド（デフォルト）
.\build.ps1

# デバッグビルド
.\build.ps1 -Configuration Debug

# ZIP 作成をスキップ
.\build.ps1 -SkipZip
```

出力先:
- `dist\app\` — メインアプリ (`VoiSpeBridge.exe`)
- `dist\plugins\` — 各プラグイン（フォルダ別）

### プロジェクト構成

```
src\
  AIVoiceBridge.Core\               IVoiceSynthesizerPlugin インターフェース
  AIVoiceBridge.App\                WPF メインアプリ
  CeVIOBridge\                      CeVIO AI ブリッジ（.NET Framework 4.8）
  AIVoice1Bridge\                   A.I.VOICE v1 ブリッジ（.NET Framework 4.8）
  Plugins\
    AIVoiceBridge.Plugin.AIVoice2\    A.I.VOICE2 プラグイン
    AIVoiceBridge.Plugin.AIVoice\     A.I.VOICE v1 プラグイン
    AIVoiceBridge.Plugin.Voicepeak\   VOICEPEAK プラグイン
    AIVoiceBridge.Plugin.CeVIOAI\     CeVIO AI プラグイン
    AIVoiceBridge.Plugin.VoisonaTalk\ VoisonaTalk プラグイン
```

### 新しいプラグインの作り方

[docs/plugin-howto.md](docs/plugin-howto.md) を参照してください。

---

## 既知の問題

- CeVIO AI 接続時は CeVIO AI が先に起動している必要があります
- 外部連携 API の同時使用は 1 アプリのみ（CeVIO AI の制限）
- ブラウザエンジンはマイク権限を Chromium が管理するため、初回起動時にブラウザのマイク許可が求められる場合があります

---

## ライセンス

MIT License

各プラグインが連携する音声合成ソフト（A.I.VOICE2、VOICEPEAK、CeVIO AI、VoisonaTalk 等）の  
ライセンスはそれぞれのソフトウェアに従います。
