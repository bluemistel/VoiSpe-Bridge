# 新しい合成音声プラグインの作り方

## 概要

`AIVoiceBridge.Core` の `IVoiceSynthesizerPlugin` インターフェースを実装した
クラスライブラリ（DLL）を `plugins\` フォルダに配置するだけで対応ソフトを追加できます。

## 手順

### 1. プロジェクト作成

```bash
dotnet new classlib -n AIVoiceBridge.Plugin.MyVoiceSoft -f net8.0-windows
```

### 2. Core を参照

```xml
<!-- .csproj -->
<ItemGroup>
  <ProjectReference Include="..\..\AIVoiceBridge.Core\AIVoiceBridge.Core.csproj"
                    Private="false" ExcludeAssets="runtime"/>
</ItemGroup>
```

### 3. インターフェースを実装

```csharp
using AIVoiceBridge.Core;

public sealed class MyVoiceSoftPlugin : IVoiceSynthesizerPlugin
{
    public string Name => "MyVoiceSoft";
    public string Version => "1.0.0";
    public bool IsConnected { get; private set; }

    private SynthesisOptions _options = SynthesisOptions.Default;
    public SynthesisOptions Options
    {
        get => _options;
        set { _options = value; /* パラメータをソフトに適用 */ }
    }

    public string? CurrentCast { get; set; }

    public async Task ConnectAsync()
    {
        // 合成ソフトへの接続処理
        IsConnected = true;
        await Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        await Task.CompletedTask;
    }

    public IReadOnlyList<CastInfo> GetAvailableCasts()
    {
        // 利用可能なキャスト一覧を返す
        return [new CastInfo("デフォルト")];
    }

    /// <summary>
    /// WAVデータを返せる場合はここで返す → 出力デバイス選択が有効になる。
    /// 返せない場合は null を返し、SpeakAsync() を実装する。
    /// </summary>
    public async Task<byte[]?> SynthesizeAsync(string text)
    {
        // 例: 合成ソフトのAPIでWAVファイルを生成して読み込む
        var wavPath = Path.GetTempFileName();
        await MyVoiceSoftApi.SaveWavAsync(text, wavPath, _options);
        return await File.ReadAllBytesAsync(wavPath);
    }

    /// <summary>
    /// SynthesizeAsync() が null の場合に呼ばれる。
    /// 合成ソフト自身の出力デバイスで再生する。
    /// </summary>
    public async Task SpeakAsync(string text)
    {
        await MyVoiceSoftApi.PlayAsync(text, _options);
    }

    public void Dispose() { /* クリーンアップ */ }
}
```

### 4. DLL を plugins フォルダに配置

```
AIVoiceBridge.exe
plugins\
  AIVoiceBridge.Plugin.MyVoiceSoft.dll   ← これを配置
  MyVoiceSoftApi.dll                      ← 合成ソフトのAPIもここに
```

アプリを再起動するとプラグインが自動検出され、プラグイン選択リストに表示されます。

## 音声出力について

| SynthesizeAsync() の戻り値 | 動作 |
|---|---|
| `byte[]`（WAVデータ） | アプリの出力デバイス選択に従って再生。OBSへの直接ルーティング可能 |
| `null` | `SpeakAsync()` を呼び出す。合成ソフト側の出力デバイス設定に依存 |

OBSで取り込む場合は **VB-Cable** などの仮想オーディオデバイスを使用し、
アプリの「出力デバイス」でその仮想デバイスを選択してください。

## A.I.VOICE2 APIについての注意

`AIVoice2Plugin.cs` は公式APIに基づいて実装していますが、
A.I.VOICE2 のバージョンによってAPIのシグネチャが異なる場合があります。
ビルドエラーが出た場合は以下を確認してください:

- `_tts.SaveAudioToFile(path)` → 実際のメソッド名に合わせて修正
- `GetParam()` / `SetParam()` のパラメータ構造体名
- `TtsController.StatusCode` の値名
