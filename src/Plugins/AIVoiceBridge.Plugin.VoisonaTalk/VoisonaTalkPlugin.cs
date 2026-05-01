using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AIVoiceBridge.Core;

namespace AIVoiceBridge.Plugin.VoisonaTalk;

/// <summary>
/// VoisonaTalk プラグイン。
///
/// VoisonaTalk が提供する REST API（デフォルト port 32766）を使って音声合成を行う。
/// 外部 DLL やブリッジプロセスは不要で、.NET 標準の HttpClient のみで動作する。
///
/// 動作フロー:
///   1. ConnectAsync(): GET /voices でボイス一覧（スタイル名含む）を取得
///   2. RefreshEmotionsAsync(): 現在のキャストのスタイル名から感情パラメータを構築
///   3. SynthesizeAsync(): POST /speech-syntheses → UUID → ポーリング → WAV 読み込み → byte[] 返却
///   4. MainViewModel が WAV を AudioOutputService（選択中の出力デバイス）で再生
///
/// 前提条件:
///   - VoisonaTalk が起動していること
///   - 設定 > API タブで REST API を有効化し、パスワードを設定していること
/// </summary>
public sealed class VoisonaTalkPlugin : IVoiceSynthesizerPlugin, IPluginWithConnectionSettings, IPluginWithEmotions
{
    // ---- 接続設定 ----

    private int    _port     = 32766;
    private string _email    = string.Empty;
    private string _password = string.Empty;

    // ---- 状態 ----

    private HttpClient?        _http;
    private readonly List<CastInfo> _casts = [];

    // ボイス名 → バージョンのマッピング（POST リクエスト用）
    private readonly Dictionary<string, string> _voiceVersions = [];
    // ボイス名 → スタイル名一覧（感情パラメータ用）
    private readonly Dictionary<string, List<string>> _voiceStyles = [];

    // ---- 感情パラメータ ----

    // キャッシュ済み感情一覧（RefreshEmotionsAsync() で更新）
    private readonly List<EmotionParameter> _emotionParameters = [];
    // 現在のスタイル値（SetEmotion() で更新、SynthesizeAsync() で送信、0〜100）
    private readonly Dictionary<string, double> _styleValues = [];

    public string Name    => "VoisonaTalk";
    public string Version => "1.0.0";

    public bool IsConnected { get; private set; }

    public string? CurrentCast { get; set; }

    private SynthesisOptions _options = SynthesisOptions.Default;
    public SynthesisOptions Options
    {
        get => _options;
        set => _options = value;
    }

    // ---- IPluginWithConnectionSettings ----

    public IReadOnlyList<ConnectionSettingDefinition> ConnectionSettingDefinitions { get; } =
    [
        new("Port",     "ポート番号",       "32766", IsPassword: false, Placeholder: "32766"),
        new("Email",    "メールアドレス",   "",      IsPassword: false, Placeholder: "VoisonaTalk に登録したメールアドレス"),
        new("Password", "API パスワード",   "",      IsPassword: true,  Placeholder: "VoisonaTalk の設定 > API で設定したパスワード"),
    ];

    public string? GetConnectionSetting(string key) => key switch
    {
        "Port"     => _port.ToString(),
        "Email"    => _email,
        "Password" => _password,
        _          => null,
    };

    public void SetConnectionSetting(string key, string? value)
    {
        switch (key)
        {
            case "Port":
                if (int.TryParse(value, out var p) && p is > 0 and < 65536)
                    _port = p;
                break;
            case "Email":
                _email = value ?? string.Empty;
                break;
            case "Password":
                _password = value ?? string.Empty;
                break;
        }
    }

    // ---- 接続 ----

    public async Task ConnectAsync()
    {
        _http?.Dispose();
        _http = BuildHttpClient();

        VoiceListResponse? res;
        try
        {
            res = await _http.GetFromJsonAsync<VoiceListResponse>("voices", _jsonOptions);
        }
        catch (HttpRequestException ex)
        {
            IsConnected = false;
            throw new InvalidOperationException(
                $"VoisonaTalk に接続できませんでした（port={_port}）。\n" +
                "VoisonaTalk が起動しているか、REST API が有効化されているか確認してください。\n" +
                $"詳細: {ex.Message}", ex);
        }

        if (res?.Items == null || res.Items.Count == 0)
            throw new InvalidOperationException(
                "VoisonaTalk にボイスライブラリが見つかりません。\n" +
                "VoisonaTalk にボイスをインストールしてください。");

        _casts.Clear();
        _voiceVersions.Clear();
        _voiceStyles.Clear();

        foreach (var v in res.Items)
        {
            _casts.Add(new CastInfo(v.VoiceName));
            _voiceVersions.TryAdd(v.VoiceName, v.VoiceVersion ?? string.Empty);
            _voiceStyles[v.VoiceName] = v.StyleNames ?? [];
        }

        if (CurrentCast == null || !_voiceVersions.ContainsKey(CurrentCast))
            CurrentCast = _casts.Count > 0 ? _casts[0].Name : null;

        IsConnected = true;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        _http?.Dispose();
        _http = null;
        return Task.CompletedTask;
    }

    public IReadOnlyList<CastInfo> GetAvailableCasts() => _casts.AsReadOnly();

    // ---- IPluginWithEmotions ----

    /// <summary>
    /// 現在のキャストのスタイル名一覧から感情パラメータを構築する。
    /// VoisonaTalk はスタイル情報を ConnectAsync() 時に取得済みのため、追加の API 呼び出しは不要。
    /// </summary>
    public Task RefreshEmotionsAsync()
    {
        _emotionParameters.Clear();

        var voiceName = CurrentCast ?? string.Empty;
        if (string.IsNullOrEmpty(voiceName)) return Task.CompletedTask;
        if (!_voiceStyles.TryGetValue(voiceName, out var styles) || styles.Count == 0)
            return Task.CompletedTask;

        foreach (var name in styles)
        {
            // 既に SetEmotion() で値が設定されていればそれを優先、なければデフォルト 0
            var currentValue = _styleValues.GetValueOrDefault(name, 0.0);
            _emotionParameters.Add(new EmotionParameter(
                Key:   name,
                Label: name,
                Value: currentValue,
                Min:   0.0,
                Max:   100.0));
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<EmotionParameter> GetEmotions() => _emotionParameters.AsReadOnly();

    public void SetEmotion(string key, double value)
        => _styleValues[key] = Math.Clamp(value, 0.0, 100.0);

    // ---- 合成 ----

    public async Task<byte[]?> SynthesizeAsync(string text)
    {
        if (_http == null || string.IsNullOrWhiteSpace(text)) return null;

        var voiceName    = CurrentCast ?? (_casts.Count > 0 ? _casts[0].Name : string.Empty);
        var voiceVersion = _voiceVersions.GetValueOrDefault(voiceName, string.Empty);
        var tmpWav       = Path.Combine(Path.GetTempPath(), $"aivb_{Guid.NewGuid():N}.wav");
        string? uuid     = null;

        // スタイル重みを構築（0〜100 → 0.0〜1.0 に正規化して送信）
        double[]? styleWeights = null;
        if (_voiceStyles.TryGetValue(voiceName, out var styles) && styles.Count > 0)
        {
            styleWeights = styles
                .Select(s => Math.Clamp(_styleValues.GetValueOrDefault(s, 0.0) / 100.0, 0.0, 1.0))
                .ToArray();
        }

        try
        {
            // ---- 合成リクエスト送信 ----
            // パラメータ変換:
            //   Speed     : SynthesisOptions 0.5-2.0 (center 1.0) → VoisonaTalk 0.2-5.0 (center 1.0) ※ほぼ同スケール
            //   Volume    : SynthesisOptions 0.0-2.0 (center 1.0) → VoisonaTalk -8〜+8 dB (center 0)
            //   Pitch     : SynthesisOptions 0.5-2.0 (center 1.0) → VoisonaTalk -600〜+600 cent (center 0)
            //   Intonation: SynthesisOptions 0.0-2.0 (center 1.0) → VoisonaTalk 0.0-2.0 (center 1.0) ※同スケール
            var request = new SynthesisRequest
            {
                Language        = "ja_JP",
                Text            = text,
                VoiceName       = string.IsNullOrEmpty(voiceName) ? null : voiceName,
                VoiceVersion    = string.IsNullOrEmpty(voiceVersion) ? null : voiceVersion,
                Destination     = "file",
                OutputFilePath  = tmpWav,
                CanOverwriteFile = true,
                GlobalParameters = new GlobalParameters
                {
                    Speed        = Math.Clamp(_options.Speed, 0.2, 5.0),
                    Volume       = Math.Clamp((_options.Volume - 1.0) * 8.0, -8.0, 8.0),
                    Pitch        = Math.Clamp((_options.Pitch  - 1.0) * 600.0, -600.0, 600.0),
                    Intonation   = Math.Clamp(_options.Intonation, 0.0, 2.0),
                    StyleWeights = styleWeights,
                },
            };

            var postResponse = await _http.PostAsJsonAsync("speech-syntheses", request, _jsonOptions);
            postResponse.EnsureSuccessStatusCode();

            var created = await postResponse.Content
                .ReadFromJsonAsync<SynthesisCreatedResponse>(_jsonOptions);
            uuid = created?.Uuid
                ?? throw new InvalidOperationException("合成 UUID を取得できませんでした。");

            // ---- 完了までポーリング（最大 60 秒、500ms 間隔） ----
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(500, cts.Token);

                var status = await _http.GetFromJsonAsync<SynthesisStatusResponse>(
                    $"speech-syntheses/{uuid}", _jsonOptions, cts.Token);

                if (status?.State == "succeeded") break;

                if (status?.State == "failed")
                    throw new InvalidOperationException(
                        $"VoisonaTalk の音声合成が失敗しました。テキスト: {text}");
            }

            if (!File.Exists(tmpWav))
                throw new InvalidOperationException(
                    "VoisonaTalk が WAV ファイルを生成しませんでした。");

            return await File.ReadAllBytesAsync(tmpWav);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("VoisonaTalk の音声合成がタイムアウトしました（60秒）。");
        }
        finally
        {
            // 合成リクエストを削除（後始末）
            if (uuid != null)
            {
                try { await _http.DeleteAsync($"speech-syntheses/{uuid}"); } catch { }
            }
            try { File.Delete(tmpWav); } catch { }
        }
    }

    // SynthesizeAsync が WAV を返すため SpeakAsync は呼ばれない
    public Task SpeakAsync(string text) => Task.CompletedTask;

    // ---- ユーティリティ ----

    private HttpClient BuildHttpClient()
    {
        // VoisonaTalk REST API のベースパスは /api/talk/v1/
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_port}/api/talk/v1/"),
            Timeout     = TimeSpan.FromSeconds(30),
        };

        if (!string.IsNullOrEmpty(_email) && !string.IsNullOrEmpty(_password))
        {
            var creds = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_email}:{_password}"));
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", creds);
        }

        return client;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public void Dispose()
    {
        _http?.Dispose();
        _http = null;
    }

    // ---- API モデル ----

    private sealed class VoiceListResponse
    {
        public List<VoiceItem> Items { get; set; } = [];
    }

    private sealed class VoiceItem
    {
        // API は voice_name / voice_version / style_names を返す（snake_case ポリシーで自動変換）
        public string       VoiceName    { get; set; } = string.Empty;
        public string?      VoiceVersion { get; set; }
        /// <summary>スタイル名一覧。スタイル未対応ボイスは null または空配列。</summary>
        public List<string>? StyleNames  { get; set; }
    }

    private sealed class SynthesisRequest
    {
        /// <summary>必須。言語コード（例: "ja_JP"）。</summary>
        public string  Language         { get; set; } = "ja_JP";
        public string  Text             { get; set; } = string.Empty;
        public string? VoiceName        { get; set; }
        public string? VoiceVersion     { get; set; }
        public string  Destination      { get; set; } = "file";
        public string? OutputFilePath   { get; set; }
        /// <summary>同パスにファイルが存在しても上書きする（一時ファイル用）。</summary>
        public bool    CanOverwriteFile { get; set; } = true;
        public GlobalParameters? GlobalParameters { get; set; }
    }

    private sealed class GlobalParameters
    {
        /// <summary>話速: 0.2〜5.0（デフォルト 1.0）</summary>
        public double    Speed        { get; set; } = 1.0;
        /// <summary>音量: -8〜+8 dB（デフォルト 0）</summary>
        public double    Volume       { get; set; } = 0.0;
        /// <summary>ピッチ: -600〜+600 セント（デフォルト 0）</summary>
        public double    Pitch        { get; set; } = 0.0;
        /// <summary>抑揚: 0〜2（デフォルト 1.0）</summary>
        public double    Intonation   { get; set; } = 1.0;
        /// <summary>スタイル重み: スタイル名の順番に対応した 0.0〜1.0 の配列。null の場合は送信しない。</summary>
        public double[]? StyleWeights { get; set; }
    }

    private sealed class SynthesisCreatedResponse
    {
        public string? Uuid { get; set; }
    }

    private sealed class SynthesisStatusResponse
    {
        /// <summary>queued / running / succeeded / failed</summary>
        public string? State { get; set; }
    }
}
