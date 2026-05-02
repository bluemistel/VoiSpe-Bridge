using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoiSpeBridge.Core;

/// <summary>
/// 合成音声プラグインのインターフェース。
/// 新しい音声合成ソフトに対応するには、このインターフェースを実装したDLLをpluginsフォルダに配置する。
/// </summary>
public interface IVoiceSynthesizerPlugin : IDisposable
{
    /// <summary>プラグインの表示名（例: "A.I.VOICE2"）</summary>
    string Name { get; }

    /// <summary>プラグインのバージョン文字列</summary>
    string Version { get; }

    /// <summary>合成ソフトが起動・接続可能な状態かどうか</summary>
    bool IsConnected { get; }

    /// <summary>
    /// 合成ソフトへの接続を確立する。
    /// 呼び出し元は使用前に必ずこのメソッドを呼ぶこと。
    /// </summary>
    Task ConnectAsync();

    /// <summary>合成ソフトから切断する。</summary>
    Task DisconnectAsync();

    /// <summary>
    /// 利用可能なキャスト（ボイス）の一覧を返す。
    /// ConnectAsync() の後に有効な値が返る。
    /// </summary>
    IReadOnlyList<CastInfo> GetAvailableCasts();

    /// <summary>現在選択中のキャスト名</summary>
    string? CurrentCast { get; set; }

    /// <summary>現在の音声パラメータ</summary>
    SynthesisOptions Options { get; set; }

    /// <summary>
    /// テキストを合成して WAV データを返す。
    /// 合成ソフトがデータ取得をサポートしない場合は null を返してもよい。
    /// null の場合はアプリ側が SpeakAsync() にフォールバックする。
    /// </summary>
    Task<byte[]?> SynthesizeAsync(string text);

    /// <summary>
    /// テキストを合成し、合成ソフト自身の出力デバイスから再生する。
    /// SynthesizeAsync() が null を返すプラグインはこちらを実装すること。
    /// </summary>
    Task SpeakAsync(string text);
}

/// <summary>キャスト（ボイスキャラクター）の情報</summary>
public record CastInfo(string Name, string? Category = null);

/// <summary>音声合成パラメータ</summary>
public record SynthesisOptions
{
    /// <summary>話速 (0.5 〜 2.0、標準 = 1.0)</summary>
    public double Speed { get; init; } = 1.0;

    /// <summary>音量 (0.0 〜 2.0、標準 = 1.0)</summary>
    public double Volume { get; init; } = 1.0;

    /// <summary>ピッチ (0.5 〜 2.0、標準 = 1.0)</summary>
    public double Pitch { get; init; } = 1.0;

    /// <summary>抑揚 (0.0 〜 2.0、標準 = 1.0)</summary>
    public double Intonation { get; init; } = 1.0;

    public static SynthesisOptions Default { get; } = new();
}
