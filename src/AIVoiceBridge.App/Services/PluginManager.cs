using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using VoiSpeBridge.Core;

namespace VoiSpeBridge.App.Services;

/// <summary>
/// pluginsフォルダ内のDLLを走査し、IVoiceSynthesizerPlugin 実装を動的に読み込む。
/// 新しい合成ソフト対応はDLLを追加するだけでよい。
/// </summary>
public sealed class PluginManager
{
    private readonly string _pluginsDirectory;
    private readonly List<IVoiceSynthesizerPlugin> _loaded = [];

    public IReadOnlyList<IVoiceSynthesizerPlugin> Plugins => _loaded.AsReadOnly();

    public PluginManager(string? pluginsDirectory = null)
    {
        _pluginsDirectory = pluginsDirectory
            ?? Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    /// <summary>
    /// pluginsフォルダを走査してプラグインを読み込む。
    /// アプリ起動時に1回呼ぶこと。
    /// </summary>
    public void LoadPlugins()
    {
        _loaded.Clear();

        if (!Directory.Exists(_pluginsDirectory))
        {
            Directory.CreateDirectory(_pluginsDirectory);
            return;
        }

        foreach (var dllPath in Directory.EnumerateFiles(_pluginsDirectory, "AIVoiceBridge.Plugin.*.dll"))
        {
            try
            {
                LoadFromAssembly(dllPath);
            }
            catch (Exception ex)
            {
                // 読み込み失敗したプラグインは無視してログだけ残す
                System.Diagnostics.Debug.WriteLine($"[PluginManager] {Path.GetFileName(dllPath)} の読み込みに失敗: {ex.Message}");
            }
        }
    }

    private void LoadFromAssembly(string dllPath)
    {
        // プラグインのDLLがAIVoiceBridge.Core を参照していてもバージョン差異で失敗しないよう
        // 同一AppDomain内で読み込む（シンプルさ優先）
        var context = new PluginLoadContext(dllPath);
        var assembly = context.LoadFromAssemblyPath(dllPath);

        var pluginInterface = typeof(IVoiceSynthesizerPlugin);
        foreach (var type in assembly.GetExportedTypes())
        {
            if (!type.IsAbstract && pluginInterface.IsAssignableFrom(type))
            {
                var instance = (IVoiceSynthesizerPlugin)Activator.CreateInstance(type)!;
                _loaded.Add(instance);
                System.Diagnostics.Debug.WriteLine($"[PluginManager] 読み込み成功: {instance.Name} v{instance.Version}");
            }
        }
    }

    public void DisposeAll()
    {
        foreach (var plugin in _loaded)
        {
            try { plugin.Dispose(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PluginManager] {plugin.Name} の破棄中にエラー: {ex.Message}");
            }
        }
        _loaded.Clear();
    }
}

/// <summary>
/// プラグインごとに独立したアセンブリロードコンテキスト。
/// プラグインのDLL依存関係がホストと衝突しないようにする。
/// </summary>
file sealed class PluginLoadContext(string pluginPath)
    : System.Runtime.Loader.AssemblyLoadContext(isCollectible: false)
{
    private readonly string _pluginDir = Path.GetDirectoryName(pluginPath)!;
    private readonly System.Runtime.Loader.AssemblyDependencyResolver _resolver = new(pluginPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // AIVoiceBridge.Core はホスト側のものを使う（インターフェースの型同一性を保つ）
        if (assemblyName.Name == "AIVoiceBridge.Core")
            return null;

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolved != null)
            return LoadFromAssemblyPath(resolved);

        // プラグインフォルダにあるDLL（例: AITalkEditor.API.dll）を探す
        var localPath = Path.Combine(_pluginDir, $"{assemblyName.Name}.dll");
        if (File.Exists(localPath))
            return LoadFromAssemblyPath(localPath);

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (resolved != null)
            return LoadUnmanagedDllFromPath(resolved);
        return IntPtr.Zero;
    }
}
