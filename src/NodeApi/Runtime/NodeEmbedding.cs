// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using System;
using System.IO;
#if UNMANAGED_DELEGATES
using System.Runtime.CompilerServices;
#endif
using System.Runtime.InteropServices;

using static JSRuntime;
using static NodeDotNet;

/// <summary>
/// Shared code for the Node.js embedding classes: loading libnode and the embedding shim, the
/// callback delegate types, and the native-to-managed callback adapters.
/// </summary>
public static class NodeEmbedding
{
    /// <summary>Node-API version requested for the embedded environment's napi_env.</summary>
    public static readonly int NodeApiVersion = 8;

    private static JSRuntime? s_jsRuntime;

    public static JSRuntime JSRuntime
    {
        get
        {
            if (s_jsRuntime == null)
            {
                throw new InvalidOperationException("The JSRuntime is not initialized.");
            }
            return s_jsRuntime;
        }
    }

    /// <summary>
    /// Discovers the fallback RID of the current platform.
    /// </summary>
    static string? GetFallbackRuntimeIdentifier()
    {
        string? arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x64",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => null,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return arch is not null ? $"win-{arch}" : "win";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return arch is not null ? $"linux-{arch}" : "linux";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return arch is not null ? $"osx-{arch}" : "osx";

        return null;
    }

    /// <summary>
    /// Returns a version of the library name with the OS specific prefix and suffix.
    /// </summary>
    static string MapLibraryName(string name)
    {
        if (Path.HasExtension(name))
            return name;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return name + ".dll";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "lib" + name + ".dylib";

        return "lib" + name + ".so";
    }

    /// <summary>
    /// Scans the runtimes/{rid}/native directory relative to the application base directory for a
    /// native library.
    /// </summary>
    static string? FindLocalLibrary(string name)
    {
        if (GetFallbackRuntimeIdentifier() is string rid)
        {
            string libPath = Path.Combine(
                AppContext.BaseDirectory, "runtimes", rid, "native", MapLibraryName(name));
            if (File.Exists(libPath))
                return libPath;
        }

        return null;
    }

    /// <summary>
    /// Loads a native library using the discovery logic appropriate for the platform: the
    /// application's dependency context (runtimes/{rid}/native assets) on .NET, the
    /// runtimes/{rid}/native directory on .NET Framework, then the OS default search paths.
    /// </summary>
    static nint LoadDefaultLibrary(string name, string? besideLibraryPath)
    {
        // Beside an explicitly located companion library (e.g. the shim next to libnode).
        if (besideLibraryPath is not null && Path.GetDirectoryName(besideLibraryPath) is string dir)
        {
            string besidePath = Path.Combine(dir, MapLibraryName(name));
            if (File.Exists(besidePath) && NativeLibrary.TryLoad(besidePath, out nint besideHandle))
                return besideHandle;
        }

#if !(NETFRAMEWORK || NETSTANDARD)
        // search using the application's dependency context (runtimes/<rid>/native assets from packages)
        if (NativeLibrary.TryLoad(name, typeof(NodeEmbedding).Assembly, null, out nint handle))
            return handle;
#endif

        // search runtimes/<rid>/native relative to the application: .NET Framework has no dependency
        // context, and in-repo builds place native project outputs there without a .deps.json entry.
        string? path = FindLocalLibrary(name);
        if (path is not null)
            if (NativeLibrary.TryLoad(path, out nint localHandle))
                return localHandle;

        // attempt to load from default OS search paths
        if (NativeLibrary.TryLoad(MapLibraryName(name), out nint defaultHandle))
            return defaultHandle;

        throw new DllNotFoundException($"The JSRuntime cannot locate the {name} shared library.");
    }

    /// <summary>
    /// Loads libnode and the embedding shim. Called once per process by
    /// <see cref="NodeEmbeddingPlatform"/>.
    /// </summary>
    /// <param name="libNodePath">Path to the libnode shared library, or null to discover it.</param>
    /// <param name="nodeDotNetPath">Path to the embedding shim shared library (node-dotnet), or null
    /// to discover it (beside libnode when its path is known, else like libnode).</param>
    public static void Initialize(string? libNodePath, string? nodeDotNetPath = null)
    {
        if (s_jsRuntime != null)
        {
            throw new InvalidOperationException(
                "The JSRuntime can be initialized only once per process.");
        }

        // On Windows libnode is loaded first, by name, and satisfies the shim's import of libnode.dll.
        // On Linux and macOS libnode has a versioned file name (libnode.so.147, libnode.147.dylib),
        // which the shim records and finds beside itself through its rpath: loading the shim loads
        // libnode, and libnode's exports resolve through the shim's handle, because dlsym also
        // searches the libraries that the handle's library depends on.
        nint libnodeHandle = libNodePath is not null
            ? NativeLibrary.Load(libNodePath)
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? LoadDefaultLibrary("libnode", null)
                : default;
        nint shimHandle = nodeDotNetPath is null
            ? LoadDefaultLibrary("node-dotnet", libNodePath)
            : NativeLibrary.Load(nodeDotNetPath);
        if (libnodeHandle == default)
        {
            libnodeHandle = shimHandle;
        }

        NodeDotNet.Initialize(libnodeHandle, shimHandle);
        s_jsRuntime = new NodejsRuntime(libnodeHandle);
    }

    //==============================================================================================
    // Callback delegate types
    //==============================================================================================

    public delegate void PreloadCallback(
        NodeEmbeddingRuntime runtime, JSValue process, JSValue require);
    public delegate JSValue LoadingCallback(
        NodeEmbeddingRuntime runtime, JSValue process, JSValue require, JSValue runCommonJS);
    public delegate void LoadedCallback(
        NodeEmbeddingRuntime runtime, JSValue loadResult);
    public delegate JSValue InitializeModuleCallback(
        NodeEmbeddingRuntime runtime, string moduleName, JSValue exports);
    public delegate void RunNodeApiCallback();

    //==============================================================================================
    // Bootstrap: obtaining a napi_env for an environment
    //==============================================================================================

    // Node calls the bootstrap binding's register function synchronously while the bootstrap
    // script runs on the current thread, so a thread-static slot carries the napi_env back.
    [ThreadStatic] private static napi_env s_bootstrapEnv;
    [ThreadStatic] private static bool s_bootstrapCaptured;

    /// <summary>
    /// Evaluates <c>process._linkedBinding(BootstrapBindingName)</c> in the given context, which
    /// makes Node create a napi_env for the environment and pass it to the bootstrap register
    /// callback. The caller must have entered the isolate, a handle scope and the context.
    /// </summary>
    internal static napi_env BootstrapNodeApi(v8_isolate isolate, napi_value context)
    {
        s_bootstrapEnv = default;
        s_bootstrapCaptured = false;
        bool ok = ScriptCompileAndRun(
            isolate,
            context,
            $"process._linkedBinding('{BootstrapBindingName}')".AsSpan(),
            "node-api-dotnet:bootstrap".AsSpan(),
            out _);
        if (!ok || !s_bootstrapCaptured)
        {
            throw new JSException(
                "Failed to initialize Node-API for the embedded Node.js environment " +
                "(process._linkedBinding threw or did not call the register function).");
        }
        return s_bootstrapEnv;
    }

    internal sealed class ModuleRegistration
    {
        public ModuleRegistration(NodeEmbeddingRuntime runtime, NodeEmbeddingModuleInfo module)
        {
            Runtime = runtime;
            Module = module;
        }

        public NodeEmbeddingRuntime Runtime { get; }
        public NodeEmbeddingModuleInfo Module { get; }
    }

    //==============================================================================================
    // Native-to-managed callback adapters (see NodeDotNet.*Pointer)
    //==============================================================================================

#if UNMANAGED_DELEGATES
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
    internal static napi_value BootstrapRegisterCallback(napi_env env, napi_value exports)
    {
        s_bootstrapEnv = env;
        s_bootstrapCaptured = true;
        return exports;
    }

#if UNMANAGED_DELEGATES
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
    internal static void ModuleRegisterCallback(
        napi_value exports, napi_value module, napi_value context, nint priv)
    {
        var registration = (ModuleRegistration)GCHandle.FromIntPtr(priv).Target!;
        NodeEmbeddingRuntime runtime = registration.Runtime;
        using var jsValueScope = new JSValueScope(JSValueScopeType.Root, runtime.Env, JSRuntime);
        try
        {
            JSValue exportsValue = new(exports);
            JSValue result = registration.Module.OnInitialize(
                runtime, registration.Module.Name, exportsValue);

            // Like Node-API module registration: a different, non-null return value replaces
            // module.exports.
            if (!result.IsNullOrUndefined() && (napi_value)result != exports)
            {
                new JSValue(module).SetProperty("exports", result);
            }
        }
        catch (Exception ex)
        {
            JSError.ThrowError(ex);
        }
    }

#if UNMANAGED_DELEGATES
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
    internal static napi_value StartExecutionCallback(
        nint data, nint env, napi_value process, napi_value require, napi_value runCjs)
    {
        var runtime = (NodeEmbeddingRuntime)GCHandle.FromIntPtr(data).Target!;
        using var jsValueScope = new JSValueScope(JSValueScopeType.Root, runtime.Env, JSRuntime);
        try
        {
            return (napi_value)runtime.InvokeLoading(
                new JSValue(process), new JSValue(require), new JSValue(runCjs));
        }
        catch (Exception ex)
        {
            JSError.ThrowError(ex);
            return default; // Empty MaybeLocal: the exception is pending.
        }
    }

#if UNMANAGED_DELEGATES
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
#endif
    internal static void PreloadCallbackAdapter(
        nint data, nint env, napi_value process, napi_value require)
    {
        var runtime = (NodeEmbeddingRuntime)GCHandle.FromIntPtr(data).Target!;
        napi_env nodeApiEnv;
        if (env == runtime.Environment.Handle)
        {
            nodeApiEnv = runtime.Env;
        }
        else
        {
            // A worker thread's environment: workers inherit the parent's linked bindings, so the
            // same bootstrap yields a napi_env for the worker. The worker thread has entered its
            // isolate and a handle scope when preload runs.
            node_environment workerEnv = new(env);
            nodeApiEnv = BootstrapNodeApi(IsolateGetCurrent(), GetMainContext(workerEnv));
        }

        using var jsValueScope = new JSValueScope(JSValueScopeType.Root, nodeApiEnv, JSRuntime);
        try
        {
            runtime.InvokePreload(new JSValue(process), new JSValue(require));
        }
        catch (Exception ex)
        {
            JSError.ThrowError(ex);
        }
    }
}
