using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIVoiceBridge.Core;

/// <summary>
/// 感情値・スタイル重みを公開するオプショナルインターフェース。
/// IVoiceSynthesizerPlugin に加えて実装することで、設定ウィンドウに感情スライダーが表示される。
///
/// 対応プラグイン:
///   - CeVIO AI  : Talker.Components（Joy / Sadness / Anger / ... 各 0〜100）
///   - VoisonaTalk: style_weights（各スタイル 0〜100、内部で 0.0〜1.0 に変換して送信）
/// </summary>
public interface IPluginWithEmotions
{
    /// <summary>
    /// 現在のキャスト（CurrentCast）の感情一覧を取得してキャッシュする。
    /// キャスト変更時や再接続時に呼ばれる。
    /// </summary>
    Task RefreshEmotionsAsync();

    /// <summary>キャッシュ済みの感情パラメータ一覧を返す。</summary>
    IReadOnlyList<EmotionParameter> GetEmotions();

    /// <summary>キーに対応する感情値を設定する。</summary>
    /// <param name="key">感情名（EmotionParameter.Key）。</param>
    /// <param name="value">新しい値（Min〜Max の範囲）。</param>
    void SetEmotion(string key, double value);
}

/// <summary>感情・スタイルの1パラメータ。</summary>
/// <param name="Key">内部キー（感情名など）。SetEmotion に渡す。</param>
/// <param name="Label">UI 表示用ラベル。</param>
/// <param name="Value">現在値。</param>
/// <param name="Min">スライダー最小値（デフォルト 0.0）。</param>
/// <param name="Max">スライダー最大値（デフォルト 100.0）。</param>
public record EmotionParameter(
    string Key,
    string Label,
    double Value,
    double Min = 0.0,
    double Max = 100.0);
