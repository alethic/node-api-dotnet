// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using System;

/// <summary>
/// Enters everything needed to call Node-API for an embedded environment from the current thread:
/// the V8 locker, isolate scope, handle scope and context scope, and a root
/// <see cref="JSValueScope"/> for the environment's napi_env.
/// </summary>
public sealed class NodeEmbeddingNodeApiScope : IDisposable
{
    private readonly NodeEmbeddingRuntime _runtime;
    private readonly NodeEmbeddingRuntime.V8Scopes _v8Scopes;
    private readonly JSValueScope _valueScope;

    public NodeEmbeddingNodeApiScope(NodeEmbeddingRuntime runtime)
    {
        _runtime = runtime;
        _v8Scopes = runtime.EnterV8Scopes();
        _valueScope = new JSValueScope(
            JSValueScopeType.Root, runtime.Env, NodeEmbedding.JSRuntime);
        runtime.ScopeDepth++;
    }

    /// <summary>
    /// Gets a value indicating whether the Node.js embedding Node-API scope is disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Disposes the Node.js embedding Node-API scope.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        _runtime.ScopeDepth--;
        _valueScope.Dispose();
        NodeEmbeddingRuntime.ExitV8Scopes(_v8Scopes);
    }
}
