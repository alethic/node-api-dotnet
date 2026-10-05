// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static JSRuntime;
using static NodeDotNet;
using static NodeEmbedding;

/// <summary>
/// A Node.js environment (node::CommonEnvironmentSetup) with a Node-API environment for it.
/// </summary>
/// <remarks>
/// Multiple Node.js environments may be created (concurrently) in the same process, each used
/// from a single thread. Node-API calls require an open <see cref="NodeEmbeddingNodeApiScope"/>
/// (the methods here open one temporarily when none is open).
/// </remarks>
public sealed class NodeEmbeddingRuntime : IDisposable
{
    private readonly NodeEmbeddingPlatform _platform;
    private readonly NodeEmbeddingRuntimeSettings? _settings;
    private readonly List<GCHandle> _gcHandles = new();
    private node_common_environment_setup _setup;

    /// <summary>Creates and loads a Node.js environment on the current thread.</summary>
    public static NodeEmbeddingRuntime Create(
        NodeEmbeddingPlatform platform, NodeEmbeddingRuntimeSettings? settings = null)
    {
        NodeEmbeddingRuntime runtime = new(platform, settings);
        runtime.Initialize();
        return runtime;
    }

    /// <summary>Creates a Node.js environment, runs its event loop to completion, and disposes it.</summary>
    public static int Run(
        NodeEmbeddingPlatform platform, NodeEmbeddingRuntimeSettings? settings = null)
    {
        using NodeEmbeddingRuntime runtime = Create(platform, settings);
        runtime.RunEventLoop();
        return runtime.ExitCode ?? 0;
    }

    private NodeEmbeddingRuntime(NodeEmbeddingPlatform platform, NodeEmbeddingRuntimeSettings? settings)
    {
        _platform = platform;
        _settings = settings;
    }

    /// <summary>The V8 isolate of the environment.</summary>
    public v8_isolate Isolate { get; private set; }

    /// <summary>The node::Environment.</summary>
    public node_environment Environment { get; private set; }

    /// <summary>The libuv event loop (uv_loop_t*) of the environment.</summary>
    public nint EventLoop { get; private set; }

    /// <summary>The Node-API environment for the main context.</summary>
    public napi_env Env { get; private set; }

    /// <summary>Exit code after the event loop finished, or null if it has not finished (or was
    /// stopped before emitting exit).</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Number of <see cref="NodeEmbeddingNodeApiScope"/>s currently open on this runtime.</summary>
    internal int ScopeDepth { get; set; }

    private void Initialize()
    {
        string[] args = _settings?.Args ?? _platform.GetParsedArgs();
        string[] execArgs = _settings?.RuntimeArgs ?? _platform.GetRuntimeParsedArgs();
        NodeEnvironmentFlags flags = _settings?.RuntimeFlags ?? NodeEnvironmentFlags.DefaultFlags;

        _setup = CommonEnvironmentSetupCreate(
            _platform.Platform, out node_string_list errors, args, execArgs, flags);
        if (_setup.Handle == default)
        {
            string[] messages = ToStringArray(errors);
            StringListDelete(errors);
            throw new JSException(
                "Failed to create the Node.js environment. " + string.Join("\n", messages));
        }
        StringListDelete(errors);

        Isolate = CommonEnvironmentSetupIsolate(_setup);
        Environment = CommonEnvironmentSetupEnv(_setup);
        EventLoop = CommonEnvironmentSetupEventLoop(_setup);

        GCHandle selfHandle = GCHandle.Alloc(this);
        _gcHandles.Add(selfHandle);

        // Linked bindings must be registered before the environment is loaded.
        AddLinkedBindingNapi(
            Environment,
            BootstrapBindingName.AsSpan(),
            BootstrapRegisterPointer,
            _settings?.NodeApiVersion ?? NodeEmbedding.NodeApiVersion);
        if (_settings?.Modules != null)
        {
            foreach (NodeEmbeddingModuleInfo module in _settings.Modules)
            {
                GCHandle moduleHandle = GCHandle.Alloc(new ModuleRegistration(this, module));
                _gcHandles.Add(moduleHandle);
                AddLinkedBinding(Environment, module.Name.AsSpan(), ModuleRegisterPointer, (nint)moduleHandle);
            }
        }

        V8Scopes scopes = EnterV8Scopes();
        try
        {
            Env = BootstrapNodeApi(Isolate, scopes.Context);

            bool hasEntryPoint = _settings?.MainScript != null || _settings?.OnLoading != null;
            nint preload = _settings?.OnPreload != null ? PreloadPointer : default;
            bool loaded = LoadEnvironment(
                Environment,
                hasEntryPoint ? StartExecutionPointer : default,
                (nint)selfHandle,
                preload,
                (nint)selfHandle,
                out napi_value loadResult);
            if (!loaded)
            {
                throw new JSException("Failed to load the Node.js environment.");
            }

            if (_settings?.OnLoaded != null)
            {
                using var jsValueScope = new JSValueScope(
                    JSValueScopeType.Root, Env, NodeEmbedding.JSRuntime);
                _settings.OnLoaded(this, new JSValue(loadResult));
            }
        }
        finally
        {
            ExitV8Scopes(scopes);
        }
    }

    internal JSValue InvokeLoading(JSValue process, JSValue require, JSValue runCommonJS)
    {
        if (_settings?.MainScript != null)
        {
            return runCommonJS.Call(JSValue.Null, (JSValue)_settings.MainScript);
        }
        return _settings!.OnLoading!(this, process, require, runCommonJS);
    }

    internal void InvokePreload(JSValue process, JSValue require)
        => _settings?.OnPreload?.Invoke(this, process, require);

    /// <summary>
    /// Gets a value indicating whether the Node.js environment is disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Disposes the Node.js environment (node::Stop, then the CommonEnvironmentSetup).
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        if (ScopeDepth != 0)
        {
            throw new InvalidOperationException(
                "All Node-API scopes must be disposed before the runtime is disposed.");
        }

        if (_setup.Handle != default)
        {
            V8Scopes scopes = EnterV8Scopes();
            try
            {
                Stop(Environment, NodeStopFlags.None);
            }
            finally
            {
                ExitV8Scopes(scopes);
            }
            CommonEnvironmentSetupDelete(_setup);
            _setup = default;
        }

        foreach (GCHandle handle in _gcHandles) handle.Free();
        _gcHandles.Clear();
    }

    /// <summary>Runs the event loop to completion (node::SpinEventLoop), emitting beforeExit and
    /// exit. Sets <see cref="ExitCode"/>.</summary>
    public void RunEventLoop()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(NodeEmbeddingRuntime));

        using NodeEmbeddingNodeApiScope? scope = EnsureScope();
        if (SpinEventLoop(Environment, out int exitCode))
        {
            ExitCode = exitCode;
        }
    }

    /// <summary>Stops the environment (node::Stop); the event loop returns.</summary>
    public void TerminateEventLoop()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(NodeEmbeddingRuntime));

        using NodeEmbeddingNodeApiScope? scope = EnsureScope();
        Stop(Environment, NodeStopFlags.None);
    }

    /// <summary>Runs one iteration of the event loop, waiting for events (uv_run UV_RUN_ONCE),
    /// then drains platform tasks.</summary>
    /// <returns>True if the loop still has work.</returns>
    public bool RunEventLoopOnce() => RunEventLoop(UvRunMode.Once);

    /// <summary>Runs pending events without waiting (uv_run UV_RUN_NOWAIT), then drains platform
    /// tasks.</summary>
    /// <returns>True if the loop still has work.</returns>
    public bool RunEventLoopNoWait() => RunEventLoop(UvRunMode.NoWait);

    private bool RunEventLoop(UvRunMode mode)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(NodeEmbeddingRuntime));

        using NodeEmbeddingNodeApiScope? scope = EnsureScope();
        UvRun(EventLoop, mode);
        MultiIsolatePlatformDrainTasks(_platform.Platform, Isolate);
        return UvLoopAlive(EventLoop);
    }

    /// <summary>Runs a callback with a Node-API scope open for this environment.</summary>
    public void RunNodeApi(RunNodeApiCallback runNodeApi)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(NodeEmbeddingRuntime));

        using var scope = new NodeEmbeddingNodeApiScope(this);
        runNodeApi();
    }

    private NodeEmbeddingNodeApiScope? EnsureScope()
        => ScopeDepth == 0 ? new NodeEmbeddingNodeApiScope(this) : null;

    //==============================================================================================
    // V8 scopes: Locker, Isolate::Scope, HandleScope, Context::Scope (strictly LIFO)
    //==============================================================================================

    internal readonly struct V8Scopes
    {
        public v8_locker Locker { get; init; }
        public v8_isolate_scope IsolateScope { get; init; }
        public v8_handle_scope HandleScope { get; init; }
        public napi_value Context { get; init; }
        public v8_context_scope ContextScope { get; init; }
    }

    internal V8Scopes EnterV8Scopes()
    {
        v8_locker locker = LockerNew(Isolate);
        v8_isolate_scope isolateScope = IsolateScopeNew(Isolate);
        v8_handle_scope handleScope = HandleScopeNew(Isolate);
        napi_value context = CommonEnvironmentSetupContext(_setup);
        v8_context_scope contextScope = ContextScopeNew(context);
        return new V8Scopes
        {
            Locker = locker,
            IsolateScope = isolateScope,
            HandleScope = handleScope,
            Context = context,
            ContextScope = contextScope,
        };
    }

    internal static void ExitV8Scopes(V8Scopes scopes)
    {
        ContextScopeDelete(scopes.ContextScope);
        HandleScopeDelete(scopes.HandleScope);
        IsolateScopeDelete(scopes.IsolateScope);
        LockerDelete(scopes.Locker);
    }
}
