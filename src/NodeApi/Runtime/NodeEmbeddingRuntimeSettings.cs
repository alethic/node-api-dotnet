// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using System.Collections.Generic;
using static LibNodeShim;
using static NodeEmbedding;

public class NodeEmbeddingRuntimeSettings
{
    /// <summary>Node-API version for the environment's napi_env. Defaults to
    /// <see cref="NodeEmbedding.NodeApiVersion"/>.</summary>
    public int? NodeApiVersion { get; set; }

    /// <summary>node::EnvironmentFlags for the environment. Defaults to DefaultFlags.</summary>
    public NodeEnvironmentFlags? RuntimeFlags { get; set; }

    /// <summary>Script arguments for the environment. Defaults to the platform's parsed args.</summary>
    public string[]? Args { get; set; }

    /// <summary>Node.js (exec) arguments for the environment. Defaults to the platform's parsed
    /// exec args.</summary>
    public string[]? RuntimeArgs { get; set; }

    /// <summary>Runs before the entry point in the main environment and in every worker thread.</summary>
    public PreloadCallback? OnPreload { get; set; }

    /// <summary>CommonJS source to run as the entry point (not a path). Mutually exclusive with
    /// <see cref="OnLoading"/>.</summary>
    public string? MainScript { get; set; }

    /// <summary>Custom entry point: receives process, require and the run-CommonJS function.</summary>
    public LoadingCallback? OnLoading { get; set; }

    /// <summary>Receives the entry point's result after the environment is loaded.</summary>
    public LoadedCallback? OnLoaded { get; set; }

    /// <summary>Linked modules, available to scripts through process._linkedBinding(name).</summary>
    public IEnumerable<NodeEmbeddingModuleInfo>? Modules { get; set; }
}
