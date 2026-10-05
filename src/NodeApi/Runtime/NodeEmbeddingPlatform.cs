// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using System;
using static NodeDotNet;

/// <summary>
/// Manages the per-process Node.js platform (node::InitializeOncePerProcess), provided by libnode.
/// </summary>
/// <remarks>
/// Only one Node.js platform instance can be created per process. Once the platform is disposed,
/// another platform instance cannot be re-initialized. One or more
/// <see cref="NodeEmbeddingThreadRuntime" /> instances may be created using the platform.
/// </remarks>
public sealed class NodeEmbeddingPlatform : IDisposable
{
    private readonly node_initialization_result _result;

    /// <summary>
    /// Initializes the Node.js platform.
    /// </summary>
    /// <param name="settings">Optional platform settings (library paths, process initialization
    /// flags, arguments).</param>
    /// <exception cref="InvalidOperationException">A Node.js platform instance has already been
    /// loaded in the current process, or Node.js failed to initialize.</exception>
    public NodeEmbeddingPlatform(NodeEmbeddingPlatformSettings? settings)
    {
        if (Current != null)
        {
            throw new InvalidOperationException(
                "Only one Node.js platform instance per process is allowed.");
        }
        Current = this;
        NodeEmbedding.Initialize(settings?.LibNodePath, settings?.NodeDotNetPath);

        string[] args = settings?.Args ?? new string[] { "node" };
        NodeProcessInitializationFlags flags =
            settings?.PlatformFlags ?? NodeProcessInitializationFlags.None;
        _result = InitializeOncePerProcess(args, flags);

        int exitCode = InitializationResultExitCode(_result);
        if (exitCode != 0 || InitializationResultEarlyReturn(_result))
        {
            string[] errors = ToStringArray(InitializationResultErrors(_result));
            InitializationResultDelete(_result);
            Current = null;
            throw new InvalidOperationException(
                $"Node.js initialization failed (exit code {exitCode}). " +
                string.Join("\n", errors));
        }

        Platform = InitializationResultPlatform(_result);
        if (Platform.Handle == default)
        {
            throw new NotSupportedException(
                "Initializing the V8 platform separately (NoInitializeNodeV8Platform) is not supported.");
        }
    }

    /// <summary>The node::MultiIsolatePlatform created by Node.js.</summary>
    public node_multi_isolate_platform Platform { get; }

    /// <summary>
    /// Gets a value indicating whether the current platform has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets the Node.js platform instance for the current process, or null if not initialized.
    /// </summary>
    public static NodeEmbeddingPlatform? Current { get; private set; }

    /// <summary>
    /// Disposes the platform (node::TearDownOncePerProcess). After disposal, another platform
    /// instance may not be initialized in the current process.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        TearDownOncePerProcess();
        InitializationResultDelete(_result);
    }

    /// <summary>
    /// Creates a new Node.js embedding runtime with a dedicated main thread.
    /// </summary>
    /// <param name="baseDir">Optional directory that is used as the base directory when resolving
    /// imported modules, and also as the value of the global `__dirname` property. If unspecified,
    /// importing modules is not enabled and `__dirname` is undefined.</param>
    /// <param name="settings">Optional runtime settings.</param>
    /// <returns>A new <see cref="NodeEmbeddingThreadRuntime" /> instance.</returns>
    public NodeEmbeddingThreadRuntime CreateThreadRuntime(
        string? baseDir = null,
        NodeEmbeddingRuntimeSettings? settings = null)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(NodeEmbeddingPlatform));

        return new NodeEmbeddingThreadRuntime(this, baseDir, settings);
    }

    /// <summary>The non-Node.js arguments as parsed by Node.js (InitializationResult::args).</summary>
    public string[] GetParsedArgs()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(NodeEmbeddingPlatform));
        return ToStringArray(InitializationResultArgs(_result));
    }

    /// <summary>The Node.js arguments as parsed by Node.js (InitializationResult::exec_args).</summary>
    public string[] GetRuntimeParsedArgs()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(NodeEmbeddingPlatform));
        return ToStringArray(InitializationResultExecArgs(_result));
    }
}
