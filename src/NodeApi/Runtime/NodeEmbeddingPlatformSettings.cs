// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using static LibNodeShim;

public class NodeEmbeddingPlatformSettings
{
    /// <summary>Path to the libnode shared library, or null to discover it.</summary>
    public string? LibNodePath { get; set; }

    /// <summary>Path to the embedding shim shared library (nodeshim), or null to discover it.</summary>
    public string? LibNodeShimPath { get; set; }

    /// <summary>node::ProcessInitializationFlags passed to InitializeOncePerProcess.</summary>
    public NodeProcessInitializationFlags? PlatformFlags { get; set; }

    /// <summary>Process arguments, starting with the program name. Defaults to ["node"].</summary>
    public string[]? Args { get; set; }
}
