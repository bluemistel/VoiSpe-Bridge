using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoiSpeBridge.Core;

namespace VoiSpeBridge.Plugin.AIVoice2;

/// <summary>
/// A.I.VOICE2 (Flutter製) プラグイン。
///
/// A.I.VOICE2 は外部公開APIを持たない Flutter アプリ（FLUTTER_RUNNER_WIN32_WINDOW）のため、
/// クリップボード＋キー操作でテキストを送り込む方式を採用しています。
///
/// 動作フロー:
///   1. A.I.VOICE2 ウィンドウを検索（起動済み必須）
///   2. テキストをクリップボードにセット
///   3. A.I.VOICE2 を前面に出してテキストエリアをクリックしてフォーカスを確保
///   4. Ctrl+A → Ctrl+V でテキストを置換貼り付け
///   5. F5 で発声をトリガー
///   6. 推定再生時間後、元のウィンドウにフォーカスを戻す
/// </summary>
public sealed class AIVoice2Plugin : IVoiceSynthesizerPlugin
{
    private const string WindowClassName = "FLUTTER_RUNNER_WIN32_WINDOW";

    public string Name => "A.I.VOICE2";
    public string Version => "1.0.0";
    public bool IsConnected => FindAIVoice2Window() != IntPtr.Zero;

    private SynthesisOptions _options = SynthesisOptions.Default;
    public SynthesisOptions Options
    {
        get => _options;
        set => _options = value;
    }

    public string? CurrentCast { get; set; }

    public async Task ConnectAsync()
    {
        await Task.Run(() =>
        {
            var hwnd = FindAIVoice2Window();
            if (hwnd != IntPtr.Zero) return;

            var exePath = @"C:\Program Files\AI\AIVoice2\AIVoice2Editor\aivoice.exe";
            if (!System.IO.File.Exists(exePath))
                throw new InvalidOperationException(
                    "A.I.VOICE2 が見つかりません。インストール済みか確認してください。");

            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (FindAIVoice2Window() == IntPtr.Zero && DateTime.UtcNow < deadline)
                Thread.Sleep(500);

            if (FindAIVoice2Window() == IntPtr.Zero)
                throw new TimeoutException("A.I.VOICE2 の起動がタイムアウトしました。");

            Thread.Sleep(2000);
        });
    }

    public Task DisconnectAsync() => Task.CompletedTask;

    public IReadOnlyList<CastInfo> GetAvailableCasts()
        => [new CastInfo("（A.I.VOICE2 側で選択中のキャスト）")];

    public Task<byte[]?> SynthesizeAsync(string text) => Task.FromResult<byte[]?>(null);

    public async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // クリップボード操作は STAThread 必須
        Exception? clipError = null;
        var clipSet = new ManualResetEventSlim(false);

        var staThread = new Thread(() =>
        {
            try
            {
                NativeClipboard.SetText(text);
                clipSet.Set();
            }
            catch (Exception ex)
            {
                clipError = ex;
                clipSet.Set();
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        clipSet.Wait(3000);

        if (clipError != null)
            throw new InvalidOperationException($"クリップボードへの書き込みに失敗しました: {clipError.Message}");

        await Task.Run(() =>
        {
            var hwnd = FindAIVoice2Window();
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException(
                    "A.I.VOICE2 のウィンドウが見つかりません。起動してください。");

            var prevForeground = GetForegroundWindow();
            try
            {
                InjectTextAndPlay(hwnd, text);
            }
            finally
            {
                if (prevForeground != IntPtr.Zero && prevForeground != hwnd)
                {
                    Thread.Sleep(150);
                    SetForegroundWindow(prevForeground);
                }
            }
        });
    }

    private void InjectTextAndPlay(IntPtr hwnd, string text)
    {
        // A.I.VOICE2 を前面に出す
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        Thread.Sleep(300);

        // Flutter アプリはフォーカスがボタン等に移動している場合があるため
        // テキストエリア推定位置をクリックしてフォーカスを確保する
        if (GetWindowRect(hwnd, out var rect))
        {
            var cx = (rect.Left + rect.Right) / 2;
            // テキスト入力エリアはウィンドウ上部 40% 付近にある想定
            var cy = rect.Top + (rect.Bottom - rect.Top) * 2 / 5;
            SetCursorPos(cx, cy);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(200);
        }

        // Ctrl+A で全選択、Ctrl+V で置換貼り付け
        KeyCombo(VK_CONTROL, VK_A);
        Thread.Sleep(150);
        KeyCombo(VK_CONTROL, VK_V);
        Thread.Sleep(300);

        // F5 で再生
        KeyDown(VK_F5);
        Thread.Sleep(50);
        KeyUp(VK_F5);

        Thread.Sleep(EstimatePlaytimeMs(text, _options.Speed));
    }

    private static int EstimatePlaytimeMs(string text, double speed)
    {
        var charsPerSecond = 6.0 * speed;
        var seconds = text.Length / charsPerSecond;
        var ms = (int)(seconds * 1000) + 800;
        return Math.Max(800, Math.Min(ms, 120_000));
    }

    private static IntPtr FindAIVoice2Window()
        => FindWindow(WindowClassName, null);

    // ---- キー操作ヘルパー ----

    private const byte VK_CONTROL = 0x11;
    private const byte VK_A = 0x41;
    private const byte VK_V = 0x56;
    private const byte VK_F5 = 0x74;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const int SW_RESTORE = 9;

    private static void KeyDown(byte vk) => keybd_event(vk, 0, 0, UIntPtr.Zero);
    private static void KeyUp(byte vk) => keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

    private static void KeyCombo(byte modifier, byte key)
    {
        KeyDown(modifier);
        Thread.Sleep(30);
        KeyDown(key);
        Thread.Sleep(30);
        KeyUp(key);
        Thread.Sleep(30);
        KeyUp(modifier);
    }

    // ---- Win32 P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public void Dispose() { }
}

file static class NativeClipboard
{
    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public static void SetText(string text)
    {
        var bytes = (text.Length + 1) * 2;
        var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
        if (hMem == IntPtr.Zero)
            throw new OutOfMemoryException("GlobalAlloc failed");

        var ptr = GlobalLock(hMem);
        try
        {
            Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
            Marshal.WriteInt16(ptr, text.Length * 2, 0);
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        if (!OpenClipboard(IntPtr.Zero))
            throw new InvalidOperationException("クリップボードを開けませんでした。");
        try
        {
            EmptyClipboard();
            if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
                throw new InvalidOperationException("クリップボードへの書き込みに失敗しました。");
        }
        finally
        {
            CloseClipboard();
        }
    }
}
