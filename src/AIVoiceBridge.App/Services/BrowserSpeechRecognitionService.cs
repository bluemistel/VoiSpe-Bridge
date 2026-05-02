using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace VoiSpeBridge.App.Services;

/// <summary>
/// WebView2（Chromium ベース）に組み込んだ Web Speech API を使用するブラウザ音声認識サービス。
///
/// 特徴:
///   - GPU リソースを使用しない（Google のクラウドサーバーで処理）
///   - インターネット接続が必要
///   - WebView2 ランタイム必須（Windows 11 標準搭載・Windows 10 は Edge と共に自動取得）
///   - Chromium ベースのため、Chrome/Edge 互換。Firefox には非対応
///
/// 注意:
///   - 音声はGoogleのサーバーに送信されます
///   - マイクの権限はコード側で自動許可します
/// </summary>
public sealed class BrowserSpeechRecognitionService : IDisposable
{
    private WebView2? _webView;
    private bool      _isListening;

    // 仮想ホスト名（Chromium が HTTPS コンテキストとして扱う → Web Speech API のセキュアコンテキスト要件を満たす）
    private const string VirtualHost = "voispe.local";
    private static readonly string WebViewDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiSpeBridge", "webview");

    public bool IsInitialized { get; private set; }

    public event EventHandler<string>? TextRecognized;
    public event EventHandler<string>? StatusChanged;

    // ---- 初期化 ----

    /// <summary>
    /// WebView2 コントロールを受け取り、Web Speech API 用 HTML を読み込む。
    /// MainWindow.OnLoaded から呼ぶこと。
    ///
    /// NavigateToString() は about:blank 扱いになり Chromium のセキュアコンテキスト判定を通過できない
    /// ことがある。代わりに SetVirtualHostNameToFolderMapping で仮想 HTTPS ホストを使用する。
    /// </summary>
    public async Task InitializeAsync(WebView2 webView)
    {
        _webView = webView;

        try
        {
            await webView.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "WebView2 ランタイムの初期化に失敗しました。\n" +
                "Microsoft Edge WebView2 ランタイムがインストールされているか確認してください。\n" +
                $"詳細: {ex.Message}", ex);
        }

        // HTML を一時ディレクトリに書き出し
        Directory.CreateDirectory(WebViewDir);
        File.WriteAllText(
            Path.Combine(WebViewDir, "speech.html"),
            GetRecognitionHtml(),
            Encoding.UTF8);

        // 仮想ホスト → ローカルフォルダ のマッピング
        // これにより https://voispe.local/speech.html が HTTPS コンテキストとして扱われる
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHost, WebViewDir,
            CoreWebView2HostResourceAccessKind.Allow);

        // マイクのアクセス許可を自動承認
        webView.CoreWebView2.PermissionRequested += OnPermissionRequested;
        webView.CoreWebView2.WebMessageReceived  += OnWebMessageReceived;

        // 仮想 HTTPS ホストへナビゲート（NavigateToString の代わり）
        webView.CoreWebView2.Navigate($"https://{VirtualHost}/speech.html");

        IsInitialized = true;
    }

    // ---- 操作 ----

    public void StartListening()
    {
        if (!IsInitialized || _isListening) return;
        _isListening = true;
        _webView?.CoreWebView2.PostWebMessageAsString("start");
    }

    public void StopListening()
    {
        if (!_isListening) return;
        _isListening = false;
        _webView?.CoreWebView2.PostWebMessageAsString("stop");
    }

    // ---- イベントハンドラ ----

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind == CoreWebView2PermissionKind.Microphone)
        {
            e.State   = CoreWebView2PermissionState.Allow;
            // Handled = true: OS のデフォルトダイアログを抑制し、設定を即時適用する
            e.Handled = true;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            var msg  = JsonSerializer.Deserialize<WebViewMessage>(json);
            if (msg == null) return;

            switch (msg.Type)
            {
                case "result":
                    if (!string.IsNullOrWhiteSpace(msg.Text))
                        TextRecognized?.Invoke(this, msg.Text!);
                    break;
                case "status":
                    StatusChanged?.Invoke(this, msg.Message ?? string.Empty);
                    break;
                case "error":
                    _isListening = false;
                    StatusChanged?.Invoke(this, $"ブラウザ音声認識エラー: {msg.Message}");
                    break;
            }
        }
        catch { /* JSON 解析失敗は無視 */ }
    }

    // ---- HTML/JS ----

    private static string GetRecognitionHtml() => """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body>
        <script>
        var recognition = null;
        var shouldListen = false;

        function start() {
            shouldListen = true;
            if (!recognition) createAndStart();
        }

        function stop() {
            shouldListen = false;
            if (recognition) { recognition.abort(); recognition = null; }
            post({ type: 'status', message: '認識停止' });
        }

        function createAndStart() {
            var R = window.SpeechRecognition || window.webkitSpeechRecognition;
            if (!R) {
                post({ type: 'error', message: 'Web Speech API が利用できません。WebView2 ランタイムを確認してください。' });
                return;
            }
            recognition = new R();
            recognition.lang = 'ja-JP';
            recognition.continuous = true;
            recognition.interimResults = false;
            recognition.maxAlternatives = 1;

            recognition.onresult = function(e) {
                for (var i = e.resultIndex; i < e.results.length; i++) {
                    if (e.results[i].isFinal) {
                        var t = e.results[i][0].transcript.trim();
                        if (t) post({ type: 'result', text: t });
                    }
                }
            };

            recognition.onerror = function(e) {
                // no-speech / aborted は正常扱い（静寂や手動停止）
                if (e.error === 'aborted' || e.error === 'no-speech') return;
                post({ type: 'error', message: e.error });
            };

            recognition.onend = function() {
                recognition = null;
                // continuous=true でも一定時間後に自動停止するため再起動する
                if (shouldListen) setTimeout(createAndStart, 200);
            };

            recognition.start();
            post({ type: 'status', message: '認識中...（話しかけてください）' });
        }

        function post(obj) {
            window.chrome.webview.postMessage(JSON.stringify(obj));
        }

        window.chrome.webview.addEventListener('message', function(e) {
            if      (e.data === 'start') start();
            else if (e.data === 'stop')  stop();
        });
        </script>
        </body>
        </html>
        """;

    // ---- クリーンアップ ----

    public void Dispose()
    {
        if (_webView?.CoreWebView2 != null)
        {
            _webView.CoreWebView2.PermissionRequested -= OnPermissionRequested;
            _webView.CoreWebView2.WebMessageReceived  -= OnWebMessageReceived;
        }
        _webView = null;
    }

    // ---- JS → C# メッセージモデル ----

    private sealed class WebViewMessage
    {
        [JsonPropertyName("type")]    public string? Type    { get; set; }
        [JsonPropertyName("text")]    public string? Text    { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
