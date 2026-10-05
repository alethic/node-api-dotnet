# Embed a JS runtime in a .NET application

::: warning :warning: WARNING
This functionality is still experimental. It works, but the process is not as streamlined as it
should be.
:::

## Acquiring the the required `libnode` binary
Hosting Node.js from .NET needs a shared `libnode` and `node-dotnet`, a small C shim over Node's
C++ embedding API built against that exact `libnode`. Both come from the `Alethic.LibNode` NuGet
package ([alethic/libnode-dotnet](https://github.com/alethic/libnode-dotnet)), which builds
unpatched Node.js for Windows, macOS and Linux. It depends on the runtime ID specific
`Alethic.LibNode.runtime.<rid>` packages, which can be used directly if needed.

The package is published to the alethic GitHub Packages feed, which requires authentication even
to read. See the `Directory.Packages.props` file in the root of the `node-api-dotnet` project for
the matching version of the `Alethic.LibNode` package.

## Importing JS modules into .NET

1. Load `libnode` and initialize a Node.js "platform" and "runtime" instance:
```C#
// Find the path to the libnode binary for the current platform.
string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
string libnodePath = Path.Combine(baseDir, "libnode.dll");
NodeEmbeddingPlatform nodejsPlatform = new(libnodePath, null);
NodeEmbeddingThreadRuntime nodejsRuntime = nodejsPlatform.CreateThreadRuntime(baseDir,
    new NodeEmbeddingRuntimeSettings
    {
        MainScript =
            "globalThis.require = require('module').createRequire(process.execPath);\n"
    });
```

There can only be one
[`NodeEmbeddingPlatform`](../reference/dotnet/Microsoft.JavaScript.NodeApi.Runtime/NodeEmbeddingPlatform)
instance per process, so typically it would be stored in a static variable. From the platform,
multiple
[`NodeEmbeddingRuntime`](../reference/dotnet/Microsoft.JavaScript.NodeApi.Runtime/NodeEmbeddingRuntime)
or
[`NodeEmbeddingThreadRuntime`](../reference/dotnet/Microsoft.JavaScript.NodeApi.Runtime/NodeEmbeddingThreadRuntime)
instances may be created and disposed.

The directory provided to the
[`NodeEmbeddingThreadRuntime`](../reference/dotnet/Microsoft.JavaScript.NodeApi.Runtime/NodeEmbeddingThreadRuntime)
instance is the base for package resolution. Any packages or modules imported in the next step must
be installed (in a `node_modules` directory) in that base directory or a parent directory.

2. Invoke a simple script from C#:
```C#
await nodejsRuntime.RunAsync(() =>
{
    JSValue.RunScript("console.log('Hello from JS!');");
});
```

Be aware of JavaScript's single-threaded execution model. **All JavaScript operations must be
performed from the JS environment thread**, unless otherwise noted. Use any of the `Run()`,
`RunAsync()`, `Post()`, or `PostAsync()` methods on the JS `NodeEmbeddingThreadRuntime` instance
to switch to the JS thread. Also keep in mind any JavaScript values of type `JSValue` (or any of
the more specific JS value struct types) are not valid after switching off the JS thread.
A `JSReference` can hold on to a JS value for future use. See also
[JS Threading and Async Continuations](../features/js-threading-async) and
[JS References](../features/js-references).

3. Import modules or module properties:
```C#
// Import * from the `fluid-framework` module. Items exported from the module will be
// available as properties on the returned JS object.
JSValue fluidPackage = nodejsRuntime.Import("fluid-framework");

// Import just the `SharedString` class from the `fluid-framework` module.
JSValue sharedStringClass = nodejsRuntime.Import("fluid-framework", "SharedString");
```
As explained above, importing modules must be done on the JS thread.

## Debugging the JavaScript code in a .NET process
```C#
int pid = Process.GetCurrentProcess().Id;
Uri inspectionUri = nodejsRuntime.StartInspector();
Debug.WriteLine($"Node.js ({pid}) inspector listening at {inspectionUri}");
```
Then attach a JavaScript debugger such as VS Code or Chrome to the inspection URI.
