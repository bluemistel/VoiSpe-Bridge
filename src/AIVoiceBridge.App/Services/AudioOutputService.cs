using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace VoiSpeBridge.App.Services;

/// <summary>
/// NAudio を用いて任意の出力デバイスに WAV データを再生する。
/// OBS連携：VB-Cable などの仮想オーディオデバイスを選択することで
/// OBS の「音声入力キャプチャ」で合成音声を取り込める。
/// </summary>
public sealed class AudioOutputService : IDisposable
{
    private WaveOutEvent? _waveOut;
    private int _deviceIndex = -1; // -1 = システムデフォルト

    /// <summary>利用可能な出力デバイスの一覧を返す。</summary>
    public static IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        devices.Add(new AudioDeviceInfo(-1, "システム既定"));

        int count = WaveOut.DeviceCount;
        for (int i = 0; i < count; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            devices.Add(new AudioDeviceInfo(i, caps.ProductName));
        }
        return devices;
    }

    /// <summary>
    /// 使用する出力デバイスを設定する。
    /// </summary>
    /// <param name="deviceIndex">GetOutputDevices() で得たインデックス（-1でデフォルト）</param>
    public void SetOutputDevice(int deviceIndex) => _deviceIndex = deviceIndex;

    /// <summary>WAVバイト列を指定デバイスで再生し、完了まで待機する。</summary>
    public async Task PlayWavAsync(byte[] wavData)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _waveOut?.Dispose();
        _waveOut = new WaveOutEvent { DeviceNumber = _deviceIndex };

        using var ms = new MemoryStream(wavData);
        using var reader = new WaveFileReader(ms);

        _waveOut.Init(reader);
        _waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult(true);
        _waveOut.Play();

        await tcs.Task;
    }

    /// <summary>再生中の音声を即座に停止する。</summary>
    public void Stop() => _waveOut?.Stop();

    public void Dispose()
    {
        _waveOut?.Dispose();
        _waveOut = null;
    }
}

public record AudioDeviceInfo(int Index, string Name);
