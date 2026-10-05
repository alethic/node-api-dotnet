// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.JavaScript.NodeApi.Runtime;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static JSRuntime;

/// <summary>
/// P/Invoke surface of the libnode embedding shim, node-dotnet (node-dotnet.h in alethic/libnode-dotnet), a C ABI that
/// mirrors Node.js's C++ embedding API (node.h) and the v8.h scopes an embedder needs, plus the
/// two libuv functions used to drive the event loop manually (exported by libnode itself).
/// </summary>
/// <remarks>
/// Every function here corresponds to one declaration in node-dotnet.h. A <c>v8_local</c> has the
/// same representation as <see cref="napi_value"/> and is typed as such.
/// </remarks>
public static unsafe class NodeDotNet
{
    /// <summary>Name of the linked binding used to obtain a napi_env for an environment.</summary>
    public const string BootstrapBindingName = "node_api_dotnet_embedding";

    public record struct node_initialization_result(nint Handle);
    public record struct node_multi_isolate_platform(nint Handle);
    public record struct node_common_environment_setup(nint Handle);
    public record struct node_environment(nint Handle);
    public record struct node_string_list(nint Handle);
    public record struct v8_isolate(nint Handle);
    public record struct v8_locker(nint Handle);
    public record struct v8_isolate_scope(nint Handle);
    public record struct v8_handle_scope(nint Handle);
    public record struct v8_context_scope(nint Handle);

    /// <summary>node::ProcessInitializationFlags::Flags</summary>
    [Flags]
    public enum NodeProcessInitializationFlags : uint
    {
        None = 0,
        EnableStdioInheritance = 1 << 0,
        DisableNodeOptionsEnv = 1 << 1,
        DisableCliOptions = 1 << 2,
        NoIcu = 1 << 3,
        NoStdioInitialization = 1 << 4,
        NoDefaultSignalHandling = 1 << 5,
        NoInitializeV8 = 1 << 6,
        NoInitializeNodeV8Platform = 1 << 7,
        NoInitOpenSsl = 1 << 8,
        NoParseGlobalDebugVariables = 1 << 9,
        NoAdjustResourceLimits = 1 << 10,
        NoUseLargePages = 1 << 11,
        NoPrintHelpOrVersionOutput = 1 << 12,
        NoInitializeCppgc = 1 << 13,
        GeneratePredictableSnapshot = 1 << 14,
    }

    /// <summary>node::EnvironmentFlags::Flags</summary>
    [Flags]
    public enum NodeEnvironmentFlags : ulong
    {
        None = 0,
        DefaultFlags = 1 << 0,
        OwnsProcessState = 1 << 1,
        OwnsInspector = 1 << 2,
        NoRegisterEsmLoader = 1 << 3,
        TrackUnmanagedFds = 1 << 4,
        HideConsoleWindows = 1 << 5,
        NoNativeAddons = 1 << 6,
        NoGlobalSearchPaths = 1 << 7,
        NoBrowserGlobals = 1 << 8,
        NoCreateInspector = 1 << 9,
        NoStartDebugSignalHandler = 1 << 10,
        NoWaitForInspectorFrontend = 1 << 11,
    }

    /// <summary>node::StopFlags::Flags</summary>
    [Flags]
    public enum NodeStopFlags : uint
    {
        None = 0,
        DoNotTerminateIsolate = 1 << 0,
    }

    /// <summary>uv_run_mode</summary>
    public enum UvRunMode : int
    {
        Default = 0,
        Once = 1,
        NoWait = 2,
    }

    private static nint s_shimHandle;
    private static nint s_libnodeHandle;

    public static bool IsInitialized => s_shimHandle != default;

    /// <summary>
    /// Resolves every import from the loaded shim and libnode libraries. Called once per process by
    /// <see cref="NodeEmbedding.Initialize"/>.
    /// </summary>
    public static void Initialize(nint libnodeHandle, nint shimHandle)
    {
        if (s_shimHandle != default)
        {
            throw new InvalidOperationException("node-dotnet is already initialized.");
        }
        s_libnodeHandle = libnodeHandle;
        s_shimHandle = shimHandle;

        // Strings
        node_string_list_count = (delegate* unmanaged[Cdecl]<nint, nuint>)Shim("node_string_list_count");
        node_string_list_get = (delegate* unmanaged[Cdecl]<nint, nuint, byte*>)Shim("node_string_list_get");
        node_string_list_delete = (delegate* unmanaged[Cdecl]<nint, void>)Shim("node_string_list_delete");

        // Process initialization
        node_initialize_once_per_process = (delegate* unmanaged[Cdecl]<int, nint, uint, nint>)Shim("node_initialize_once_per_process");
        node_initialization_result_exit_code = (delegate* unmanaged[Cdecl]<nint, int>)Shim("node_initialization_result_exit_code");
        node_initialization_result_early_return = (delegate* unmanaged[Cdecl]<nint, c_bool>)Shim("node_initialization_result_early_return");
        node_initialization_result_args = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("node_initialization_result_args");
        node_initialization_result_exec_args = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("node_initialization_result_exec_args");
        node_initialization_result_errors = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("node_initialization_result_errors");
        node_initialization_result_platform = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("node_initialization_result_platform");
        node_initialization_result_delete = (delegate* unmanaged[Cdecl]<nint, void>)Shim("node_initialization_result_delete");
        node_tear_down_once_per_process = (delegate* unmanaged[Cdecl]<void>)Shim("node_tear_down_once_per_process");

        // Platform
        node_multi_isolate_platform_create = (delegate* unmanaged[Cdecl]<int, nint>)Shim("node_multi_isolate_platform_create");
        node_multi_isolate_platform_delete = (delegate* unmanaged[Cdecl]<nint, void>)Shim("node_multi_isolate_platform_delete");
        node_multi_isolate_platform_drain_tasks = (delegate* unmanaged[Cdecl]<nint, nint, void>)Shim("node_multi_isolate_platform_drain_tasks");
        v8_initialize_platform = (delegate* unmanaged[Cdecl]<nint, void>)Shim("v8_initialize_platform");
        v8_initialize = (delegate* unmanaged[Cdecl]<c_bool>)Shim("v8_initialize");
        v8_dispose = (delegate* unmanaged[Cdecl]<c_bool>)Shim("v8_dispose");
        v8_dispose_platform = (delegate* unmanaged[Cdecl]<void>)Shim("v8_dispose_platform");

        // Environment setup
        node_common_environment_setup_create = (delegate* unmanaged[Cdecl]<nint, nint*, int, nint, int, nint, ulong, nint>)Shim("node_common_environment_setup_create");
        node_common_environment_setup_isolate = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("node_common_environment_setup_isolate");
        node_common_environment_setup_env = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("node_common_environment_setup_env");
        node_common_environment_setup_context = (delegate* unmanaged[Cdecl]<nint, napi_value>)Shim("node_common_environment_setup_context");
        node_common_environment_setup_event_loop = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("node_common_environment_setup_event_loop");
        node_common_environment_setup_delete = (delegate* unmanaged[Cdecl]<nint, void>)Shim("node_common_environment_setup_delete");

        // Environment
        node_load_environment = (delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, napi_value*, c_bool>)Shim("node_load_environment");
        node_load_environment_script = (delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, napi_value*, c_bool>)Shim("node_load_environment_script");
        node_spin_event_loop = (delegate* unmanaged[Cdecl]<nint, int*, c_bool>)Shim("node_spin_event_loop");
        node_emit_process_before_exit = (delegate* unmanaged[Cdecl]<nint, c_bool*, c_bool>)Shim("node_emit_process_before_exit");
        node_emit_process_exit = (delegate* unmanaged[Cdecl]<nint, int*, c_bool>)Shim("node_emit_process_exit");
        node_stop = (delegate* unmanaged[Cdecl]<nint, uint, int>)Shim("node_stop");
        node_get_main_context = (delegate* unmanaged[Cdecl]<nint, napi_value>)Shim("node_get_main_context");
        node_get_current_environment = (delegate* unmanaged[Cdecl]<napi_value, nint>)Shim("node_get_current_environment");
        node_get_current_event_loop = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("node_get_current_event_loop");
        node_add_linked_binding_napi = (delegate* unmanaged[Cdecl]<nint, byte*, nint, int, void>)Shim("node_add_linked_binding_napi");
        node_add_linked_binding = (delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, void>)Shim("node_add_linked_binding");
        node_add_environment_cleanup_hook = (delegate* unmanaged[Cdecl]<nint, nint, nint, void>)Shim("node_add_environment_cleanup_hook");
        node_remove_environment_cleanup_hook = (delegate* unmanaged[Cdecl]<nint, nint, nint, void>)Shim("node_remove_environment_cleanup_hook");

        // V8 scopes and scripts
        v8_isolate_get_current = (delegate* unmanaged[Cdecl]<nint>)Shim("v8_isolate_get_current");
        v8_locker_new = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("v8_locker_new");
        v8_locker_delete = (delegate* unmanaged[Cdecl]<nint, void>)Shim("v8_locker_delete");
        v8_locker_is_locked = (delegate* unmanaged[Cdecl]<nint, c_bool>)Shim("v8_locker_is_locked");
        v8_isolate_scope_new = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("v8_isolate_scope_new");
        v8_isolate_scope_delete = (delegate* unmanaged[Cdecl]<nint, void>)Shim("v8_isolate_scope_delete");
        v8_handle_scope_new = (delegate* unmanaged[Cdecl]<nint, nint>)Shim("v8_handle_scope_new");
        v8_handle_scope_delete = (delegate* unmanaged[Cdecl]<nint, void>)Shim("v8_handle_scope_delete");
        v8_context_scope_new = (delegate* unmanaged[Cdecl]<napi_value, nint>)Shim("v8_context_scope_new");
        v8_context_scope_delete = (delegate* unmanaged[Cdecl]<nint, void>)Shim("v8_context_scope_delete");
        v8_script_compile_and_run = (delegate* unmanaged[Cdecl]<nint, napi_value, byte*, byte*, napi_value*, c_bool>)Shim("v8_script_compile_and_run");

        // libuv, exported by libnode
        uv_run = (delegate* unmanaged[Cdecl]<nint, int, int>)LibNode("uv_run");
        uv_loop_alive = (delegate* unmanaged[Cdecl]<nint, int>)LibNode("uv_loop_alive");
    }

    private static nint Shim(string name) => NativeLibrary.GetExport(s_shimHandle, name);
    private static nint LibNode(string name) => NativeLibrary.GetExport(s_libnodeHandle, name);

#pragma warning disable IDE1006 // Naming: these mirror the C declarations.

    private static delegate* unmanaged[Cdecl]<nint, nuint> node_string_list_count;
    private static delegate* unmanaged[Cdecl]<nint, nuint, byte*> node_string_list_get;
    private static delegate* unmanaged[Cdecl]<nint, void> node_string_list_delete;

    private static delegate* unmanaged[Cdecl]<int, nint, uint, nint> node_initialize_once_per_process;
    private static delegate* unmanaged[Cdecl]<nint, int> node_initialization_result_exit_code;
    private static delegate* unmanaged[Cdecl]<nint, c_bool> node_initialization_result_early_return;
    private static delegate* unmanaged[Cdecl]<nint, nint> node_initialization_result_args;
    private static delegate* unmanaged[Cdecl]<nint, nint> node_initialization_result_exec_args;
    private static delegate* unmanaged[Cdecl]<nint, nint> node_initialization_result_errors;
    private static delegate* unmanaged[Cdecl]<nint, nint> node_initialization_result_platform;
    private static delegate* unmanaged[Cdecl]<nint, void> node_initialization_result_delete;
    private static delegate* unmanaged[Cdecl]<void> node_tear_down_once_per_process;

    private static delegate* unmanaged[Cdecl]<int, nint> node_multi_isolate_platform_create;
    private static delegate* unmanaged[Cdecl]<nint, void> node_multi_isolate_platform_delete;
    private static delegate* unmanaged[Cdecl]<nint, nint, void> node_multi_isolate_platform_drain_tasks;
    private static delegate* unmanaged[Cdecl]<nint, void> v8_initialize_platform;
    private static delegate* unmanaged[Cdecl]<c_bool> v8_initialize;
    private static delegate* unmanaged[Cdecl]<c_bool> v8_dispose;
    private static delegate* unmanaged[Cdecl]<void> v8_dispose_platform;

    private static delegate* unmanaged[Cdecl]<nint, nint*, int, nint, int, nint, ulong, nint> node_common_environment_setup_create;
    private static delegate* unmanaged[Cdecl]<nint, nint> node_common_environment_setup_isolate;
    private static delegate* unmanaged[Cdecl]<nint, nint> node_common_environment_setup_env;
    private static delegate* unmanaged[Cdecl]<nint, napi_value> node_common_environment_setup_context;
    private static delegate* unmanaged[Cdecl]<nint, nint> node_common_environment_setup_event_loop;
    private static delegate* unmanaged[Cdecl]<nint, void> node_common_environment_setup_delete;

    private static delegate* unmanaged[Cdecl]<nint, nint, nint, nint, nint, napi_value*, c_bool> node_load_environment;
    private static delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, napi_value*, c_bool> node_load_environment_script;
    private static delegate* unmanaged[Cdecl]<nint, int*, c_bool> node_spin_event_loop;
    private static delegate* unmanaged[Cdecl]<nint, c_bool*, c_bool> node_emit_process_before_exit;
    private static delegate* unmanaged[Cdecl]<nint, int*, c_bool> node_emit_process_exit;
    private static delegate* unmanaged[Cdecl]<nint, uint, int> node_stop;
    private static delegate* unmanaged[Cdecl]<nint, napi_value> node_get_main_context;
    private static delegate* unmanaged[Cdecl]<napi_value, nint> node_get_current_environment;
    private static delegate* unmanaged[Cdecl]<nint, nint> node_get_current_event_loop;
    private static delegate* unmanaged[Cdecl]<nint, byte*, nint, int, void> node_add_linked_binding_napi;
    private static delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, void> node_add_linked_binding;
    private static delegate* unmanaged[Cdecl]<nint, nint, nint, void> node_add_environment_cleanup_hook;
    private static delegate* unmanaged[Cdecl]<nint, nint, nint, void> node_remove_environment_cleanup_hook;

    private static delegate* unmanaged[Cdecl]<nint> v8_isolate_get_current;
    private static delegate* unmanaged[Cdecl]<nint, nint> v8_locker_new;
    private static delegate* unmanaged[Cdecl]<nint, void> v8_locker_delete;
    private static delegate* unmanaged[Cdecl]<nint, c_bool> v8_locker_is_locked;
    private static delegate* unmanaged[Cdecl]<nint, nint> v8_isolate_scope_new;
    private static delegate* unmanaged[Cdecl]<nint, void> v8_isolate_scope_delete;
    private static delegate* unmanaged[Cdecl]<nint, nint> v8_handle_scope_new;
    private static delegate* unmanaged[Cdecl]<nint, void> v8_handle_scope_delete;
    private static delegate* unmanaged[Cdecl]<napi_value, nint> v8_context_scope_new;
    private static delegate* unmanaged[Cdecl]<nint, void> v8_context_scope_delete;
    private static delegate* unmanaged[Cdecl]<nint, napi_value, byte*, byte*, napi_value*, c_bool> v8_script_compile_and_run;

    private static delegate* unmanaged[Cdecl]<nint, int, int> uv_run;
    private static delegate* unmanaged[Cdecl]<nint, int> uv_loop_alive;

#pragma warning restore IDE1006

    //==============================================================================================
    // Managed wrappers
    //==============================================================================================

    public static string[] ToStringArray(node_string_list list)
    {
        if (list.Handle == default) return [];
        nuint count = node_string_list_count(list.Handle);
        string[] result = new string[count];
        for (nuint i = 0; i < count; i++)
        {
            result[i] = Utf8StringArray.PtrToStringUTF8(node_string_list_get(list.Handle, i)) ?? string.Empty;
        }
        return result;
    }

    public static void StringListDelete(node_string_list list)
    {
        if (list.Handle != default) node_string_list_delete(list.Handle);
    }

    public static node_initialization_result InitializeOncePerProcess(
        ReadOnlySpan<string> args, NodeProcessInitializationFlags flags)
    {
        using Utf8StringArray utf8Args = new(args);
        fixed (nint* argsPtr = utf8Args)
        {
            return new(node_initialize_once_per_process(args.Length, (nint)argsPtr, (uint)flags));
        }
    }

    public static int InitializationResultExitCode(node_initialization_result result)
        => node_initialization_result_exit_code(result.Handle);
    public static bool InitializationResultEarlyReturn(node_initialization_result result)
        => (bool)node_initialization_result_early_return(result.Handle);
    public static node_string_list InitializationResultArgs(node_initialization_result result)
        => new(node_initialization_result_args(result.Handle));
    public static node_string_list InitializationResultExecArgs(node_initialization_result result)
        => new(node_initialization_result_exec_args(result.Handle));
    public static node_string_list InitializationResultErrors(node_initialization_result result)
        => new(node_initialization_result_errors(result.Handle));
    public static node_multi_isolate_platform InitializationResultPlatform(node_initialization_result result)
        => new(node_initialization_result_platform(result.Handle));
    public static void InitializationResultDelete(node_initialization_result result)
        => node_initialization_result_delete(result.Handle);
    public static void TearDownOncePerProcess() => node_tear_down_once_per_process();

    public static node_multi_isolate_platform MultiIsolatePlatformCreate(int threadPoolSize)
        => new(node_multi_isolate_platform_create(threadPoolSize));
    public static void MultiIsolatePlatformDelete(node_multi_isolate_platform platform)
        => node_multi_isolate_platform_delete(platform.Handle);
    public static void MultiIsolatePlatformDrainTasks(node_multi_isolate_platform platform, v8_isolate isolate)
        => node_multi_isolate_platform_drain_tasks(platform.Handle, isolate.Handle);
    public static void V8InitializePlatform(node_multi_isolate_platform platform) => v8_initialize_platform(platform.Handle);
    public static bool V8Initialize() => (bool)v8_initialize();
    public static bool V8Dispose() => (bool)v8_dispose();
    public static void V8DisposePlatform() => v8_dispose_platform();

    public static node_common_environment_setup CommonEnvironmentSetupCreate(
        node_multi_isolate_platform platform,
        out node_string_list errors,
        ReadOnlySpan<string> args,
        ReadOnlySpan<string> execArgs,
        NodeEnvironmentFlags flags)
    {
        using Utf8StringArray utf8Args = new(args);
        using Utf8StringArray utf8ExecArgs = new(execArgs);
        nint errorsHandle = default;
        fixed (nint* argsPtr = utf8Args)
        fixed (nint* execArgsPtr = utf8ExecArgs)
        {
            nint setup = node_common_environment_setup_create(
                platform.Handle,
                &errorsHandle,
                args.Length,
                (nint)argsPtr,
                execArgs.Length,
                (nint)execArgsPtr,
                (ulong)flags);
            errors = new(errorsHandle);
            return new(setup);
        }
    }

    public static v8_isolate CommonEnvironmentSetupIsolate(node_common_environment_setup setup)
        => new(node_common_environment_setup_isolate(setup.Handle));
    public static node_environment CommonEnvironmentSetupEnv(node_common_environment_setup setup)
        => new(node_common_environment_setup_env(setup.Handle));
    public static napi_value CommonEnvironmentSetupContext(node_common_environment_setup setup)
        => node_common_environment_setup_context(setup.Handle);
    public static nint CommonEnvironmentSetupEventLoop(node_common_environment_setup setup)
        => node_common_environment_setup_event_loop(setup.Handle);
    public static void CommonEnvironmentSetupDelete(node_common_environment_setup setup)
        => node_common_environment_setup_delete(setup.Handle);

    public static bool LoadEnvironment(
        node_environment env,
        nint startExecution,
        nint startExecutionData,
        nint preload,
        nint preloadData,
        out napi_value result)
    {
        napi_value value = default;
        bool ok = (bool)node_load_environment(env.Handle, startExecution, startExecutionData, preload, preloadData, &value);
        result = value;
        return ok;
    }

    public static bool LoadEnvironmentScript(
        node_environment env,
        ReadOnlySpan<char> mainScript,
        nint preload,
        nint preloadData,
        out napi_value result)
    {
        using PooledBuffer scriptBuffer = PooledBuffer.FromSpanUtf8(mainScript);
        napi_value value = default;
        bool ok;
        fixed (byte* scriptPtr = scriptBuffer)
        {
            ok = (bool)node_load_environment_script(env.Handle, scriptPtr, preload, preloadData, &value);
        }
        result = value;
        return ok;
    }

    public static bool SpinEventLoop(node_environment env, out int exitCode)
    {
        int code = 0;
        bool ok = (bool)node_spin_event_loop(env.Handle, &code);
        exitCode = code;
        return ok;
    }

    public static bool EmitProcessBeforeExit(node_environment env, out bool result)
    {
        c_bool value = default;
        bool ok = (bool)node_emit_process_before_exit(env.Handle, &value);
        result = (bool)value;
        return ok;
    }

    public static bool EmitProcessExit(node_environment env, out int exitCode)
    {
        int code = 0;
        bool ok = (bool)node_emit_process_exit(env.Handle, &code);
        exitCode = code;
        return ok;
    }

    public static int Stop(node_environment env, NodeStopFlags flags) => node_stop(env.Handle, (uint)flags);
    public static napi_value GetMainContext(node_environment env) => node_get_main_context(env.Handle);
    public static node_environment GetCurrentEnvironment(napi_value context) => new(node_get_current_environment(context));
    public static nint GetCurrentEventLoop(v8_isolate isolate) => node_get_current_event_loop(isolate.Handle);

    public static void AddLinkedBindingNapi(node_environment env, ReadOnlySpan<char> name, nint init, int moduleApiVersion)
    {
        using PooledBuffer nameBuffer = PooledBuffer.FromSpanUtf8(name);
        fixed (byte* namePtr = nameBuffer)
        {
            node_add_linked_binding_napi(env.Handle, namePtr, init, moduleApiVersion);
        }
    }

    public static void AddLinkedBinding(node_environment env, ReadOnlySpan<char> name, nint fn, nint priv)
    {
        using PooledBuffer nameBuffer = PooledBuffer.FromSpanUtf8(name);
        fixed (byte* namePtr = nameBuffer)
        {
            node_add_linked_binding(env.Handle, namePtr, fn, priv);
        }
    }

    public static void AddEnvironmentCleanupHook(v8_isolate isolate, nint fun, nint arg)
        => node_add_environment_cleanup_hook(isolate.Handle, fun, arg);
    public static void RemoveEnvironmentCleanupHook(v8_isolate isolate, nint fun, nint arg)
        => node_remove_environment_cleanup_hook(isolate.Handle, fun, arg);

    public static v8_isolate IsolateGetCurrent() => new(v8_isolate_get_current());
    public static v8_locker LockerNew(v8_isolate isolate) => new(v8_locker_new(isolate.Handle));
    public static void LockerDelete(v8_locker locker) => v8_locker_delete(locker.Handle);
    public static bool LockerIsLocked(v8_isolate isolate) => (bool)v8_locker_is_locked(isolate.Handle);
    public static v8_isolate_scope IsolateScopeNew(v8_isolate isolate) => new(v8_isolate_scope_new(isolate.Handle));
    public static void IsolateScopeDelete(v8_isolate_scope scope) => v8_isolate_scope_delete(scope.Handle);
    public static v8_handle_scope HandleScopeNew(v8_isolate isolate) => new(v8_handle_scope_new(isolate.Handle));
    public static void HandleScopeDelete(v8_handle_scope scope) => v8_handle_scope_delete(scope.Handle);
    public static v8_context_scope ContextScopeNew(napi_value context) => new(v8_context_scope_new(context));
    public static void ContextScopeDelete(v8_context_scope scope) => v8_context_scope_delete(scope.Handle);

    public static bool ScriptCompileAndRun(
        v8_isolate isolate,
        napi_value context,
        ReadOnlySpan<char> source,
        ReadOnlySpan<char> resourceName,
        out napi_value result)
    {
        using PooledBuffer sourceBuffer = PooledBuffer.FromSpanUtf8(source);
        using PooledBuffer nameBuffer = PooledBuffer.FromSpanUtf8(resourceName);
        napi_value value = default;
        bool ok;
        fixed (byte* sourcePtr = sourceBuffer)
        fixed (byte* namePtr = nameBuffer)
        {
            ok = (bool)v8_script_compile_and_run(isolate.Handle, context, sourcePtr, namePtr, &value);
        }
        result = value;
        return ok;
    }

    public static int UvRun(nint loop, UvRunMode mode) => uv_run(loop, (int)mode);
    public static bool UvLoopAlive(nint loop) => uv_loop_alive(loop) != 0;

    //==============================================================================================
    // Callbacks from native code
    //==============================================================================================

    /// <summary>napi_addon_register_func for the bootstrap linked binding.</summary>
    public delegate napi_value NapiAddonRegisterDelegate(napi_env env, napi_value exports);

    /// <summary>node_addon_context_register_func for user linked bindings.</summary>
    public delegate void AddonContextRegisterDelegate(napi_value exports, napi_value module, napi_value context, nint priv);

    /// <summary>node_start_execution_callback.</summary>
    public delegate napi_value StartExecutionDelegate(nint data, nint env, napi_value process, napi_value require, napi_value runCjs);

    /// <summary>node_embedder_preload_callback.</summary>
    public delegate void PreloadDelegate(nint data, nint env, napi_value process, napi_value require);

#if UNMANAGED_DELEGATES
    /// <summary>Native function pointer for <see cref="NodeEmbedding.BootstrapRegisterCallback"/>.</summary>
    public static nint BootstrapRegisterPointer => (nint)(delegate* unmanaged[Cdecl]<napi_env, napi_value, napi_value>)&NodeEmbedding.BootstrapRegisterCallback;
    public static nint ModuleRegisterPointer => (nint)(delegate* unmanaged[Cdecl]<napi_value, napi_value, napi_value, nint, void>)&NodeEmbedding.ModuleRegisterCallback;
    public static nint StartExecutionPointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, napi_value, napi_value, napi_value, napi_value>)&NodeEmbedding.StartExecutionCallback;
    public static nint PreloadPointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, napi_value, napi_value, void>)&NodeEmbedding.PreloadCallbackAdapter;
#else
    // Delegates are kept alive for the life of the process because native code holds their pointers.
    private static readonly NapiAddonRegisterDelegate s_bootstrapRegister = NodeEmbedding.BootstrapRegisterCallback;
    private static readonly AddonContextRegisterDelegate s_moduleRegister = NodeEmbedding.ModuleRegisterCallback;
    private static readonly StartExecutionDelegate s_startExecution = NodeEmbedding.StartExecutionCallback;
    private static readonly PreloadDelegate s_preload = NodeEmbedding.PreloadCallbackAdapter;
    public static nint BootstrapRegisterPointer { get; } = Marshal.GetFunctionPointerForDelegate(s_bootstrapRegister);
    public static nint ModuleRegisterPointer { get; } = Marshal.GetFunctionPointerForDelegate(s_moduleRegister);
    public static nint StartExecutionPointer { get; } = Marshal.GetFunctionPointerForDelegate(s_startExecution);
    public static nint PreloadPointer { get; } = Marshal.GetFunctionPointerForDelegate(s_preload);
#endif
}
