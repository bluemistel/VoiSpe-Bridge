using System.Collections.Generic;

namespace VoiSpeBridge.Core;

/// <summary>
/// プラグイン固有の接続設定（ホスト名・ポート・パスワード等）を公開するオプショナルインターフェース。
/// IVoiceSynthesizerPlugin に加えて実装することで、設定ウィンドウに動的な入力フォームが表示される。
/// </summary>
public interface IPluginWithConnectionSettings
{
    /// <summary>設定項目の定義一覧（表示順）。</summary>
    IReadOnlyList<ConnectionSettingDefinition> ConnectionSettingDefinitions { get; }

    /// <summary>キーに対応する現在値を返す。未設定なら null。</summary>
    string? GetConnectionSetting(string key);

    /// <summary>キーに対応する値を設定する。</summary>
    void SetConnectionSetting(string key, string? value);
}

/// <summary>接続設定の1項目定義。</summary>
/// <param name="Key">内部キー名（英数字）。</param>
/// <param name="Label">UI 表示ラベル（日本語可）。</param>
/// <param name="DefaultValue">デフォルト値。</param>
/// <param name="IsPassword">true のときパスワードとして扱う（入力値をマスク表示）。</param>
/// <param name="Placeholder">入力欄のプレースホルダー文字列。</param>
public record ConnectionSettingDefinition(
    string Key,
    string Label,
    string DefaultValue,
    bool IsPassword = false,
    string? Placeholder = null);
