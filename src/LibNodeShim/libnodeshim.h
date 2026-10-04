// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// libnode embedding shim.
//
// A C ABI that mirrors Node.js's public C++ embedding API (src/node.h in the Node.js source tree)
// one-to-one, plus the small slice of v8.h an embedder needs: Locker, Isolate/Handle/Context scopes
// and running a script. Nothing here is a new abstraction: each function corresponds to one C++
// declaration, named node_<type>_<method> / v8_<type>_<method>, so that keeping the shim current
// with a new Node.js major is a mechanical diff against node.h.
//
// Rules carried over from the C++ API:
//   - Every v8_local is only valid while the v8_handle_scope it was created in is open, and must be
//     used on the isolate's thread with the v8_locker / v8_isolate_scope entered, exactly like
//     v8::Local<T>. A v8_local has the same representation as napi_value (a pointer to the handle
//     slot), so it can be passed straight to Node-API functions given a napi_env.
//   - Scopes are strictly LIFO: close them in reverse order of opening.
//   - Handles returned by *_create/*_new are owned by the caller and released with the matching
//     *_delete; handles returned by other getters are borrowed.
//
// Node-API access for an embedder: register a linked binding (node_add_linked_binding_napi) before
// loading the environment, then have the main script call process._linkedBinding(name); Node calls
// the registered napi_addon_register_func with a napi_env for the environment.

#ifndef LIBNODESHIM_H_
#define LIBNODESHIM_H_

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "node_api.h"

#if defined(_WIN32)
#define NODESHIM_CDECL __cdecl
#if defined(NODESHIM_EXPORTS)
#define NODESHIM_API __declspec(dllexport)
#else
#define NODESHIM_API __declspec(dllimport)
#endif
#else
#define NODESHIM_CDECL
#define NODESHIM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ---------------------------------------------------------------------------------------------
// Handles
// ---------------------------------------------------------------------------------------------

typedef struct node_initialization_result_s* node_initialization_result;    // std::shared_ptr<node::InitializationResult>
typedef struct node_multi_isolate_platform_s* node_multi_isolate_platform;  // node::MultiIsolatePlatform*
typedef struct node_common_environment_setup_s* node_common_environment_setup;  // std::unique_ptr<node::CommonEnvironmentSetup>
typedef struct node_environment_s* node_environment;                        // node::Environment* (borrowed)
typedef struct node_string_list_s* node_string_list;                        // std::vector<std::string>
typedef struct uv_loop_s uv_loop_t;                                         // libuv (exported by libnode itself)

typedef struct v8_isolate_s* v8_isolate;                                    // v8::Isolate* (borrowed)
typedef struct v8_local_s* v8_local;                                        // v8::Local<T> slot; same representation as napi_value
typedef struct v8_locker_s* v8_locker;                                      // v8::Locker
typedef struct v8_isolate_scope_s* v8_isolate_scope;                        // v8::Isolate::Scope
typedef struct v8_handle_scope_s* v8_handle_scope;                          // v8::HandleScope
typedef struct v8_context_scope_s* v8_context_scope;                        // v8::Context::Scope

// ---------------------------------------------------------------------------------------------
// Flags (values mirror node.h; the implementation static_asserts them)
// ---------------------------------------------------------------------------------------------

// node::ProcessInitializationFlags::Flags
typedef enum {
  node_process_initialization_flags_no_flags = 0,
  node_process_initialization_flags_enable_stdio_inheritance = 1 << 0,
  node_process_initialization_flags_disable_node_options_env = 1 << 1,
  node_process_initialization_flags_disable_cli_options = 1 << 2,
  node_process_initialization_flags_no_icu = 1 << 3,
  node_process_initialization_flags_no_stdio_initialization = 1 << 4,
  node_process_initialization_flags_no_default_signal_handling = 1 << 5,
  node_process_initialization_flags_no_initialize_v8 = 1 << 6,
  node_process_initialization_flags_no_initialize_node_v8_platform = 1 << 7,
  node_process_initialization_flags_no_init_openssl = 1 << 8,
  node_process_initialization_flags_no_parse_global_debug_variables = 1 << 9,
  node_process_initialization_flags_no_adjust_resource_limits = 1 << 10,
  node_process_initialization_flags_no_use_large_pages = 1 << 11,
  node_process_initialization_flags_no_print_help_or_version_output = 1 << 12,
  node_process_initialization_flags_no_initialize_cppgc = 1 << 13,
  node_process_initialization_flags_generate_predictable_snapshot = 1 << 14,
} node_process_initialization_flags;

// node::EnvironmentFlags::Flags
typedef enum {
  node_environment_flags_no_flags = 0,
  node_environment_flags_default_flags = 1 << 0,
  node_environment_flags_owns_process_state = 1 << 1,
  node_environment_flags_owns_inspector = 1 << 2,
  node_environment_flags_no_register_esm_loader = 1 << 3,
  node_environment_flags_track_unmanaged_fds = 1 << 4,
  node_environment_flags_hide_console_windows = 1 << 5,
  node_environment_flags_no_native_addons = 1 << 6,
  node_environment_flags_no_global_search_paths = 1 << 7,
  node_environment_flags_no_browser_globals = 1 << 8,
  node_environment_flags_no_create_inspector = 1 << 9,
  node_environment_flags_no_start_debug_signal_handler = 1 << 10,
  node_environment_flags_no_wait_for_inspector_frontend = 1 << 11,
} node_environment_flags;

// node::StopFlags::Flags
typedef enum {
  node_stop_flags_no_flags = 0,
  node_stop_flags_do_not_terminate_isolate = 1 << 0,
} node_stop_flags;

// ---------------------------------------------------------------------------------------------
// Callbacks
// ---------------------------------------------------------------------------------------------

// node::StartExecutionCallback: receives StartExecutionCallbackInfo{process_object, native_require,
// run_cjs}; returns the environment's load result, or NULL for an empty MaybeLocal.
typedef v8_local(NODESHIM_CDECL* node_start_execution_callback)(
    void* data,
    node_environment env,
    v8_local process_object,
    v8_local native_require,
    v8_local run_cjs);

// node::EmbedderPreloadCallback: runs before the entry point in the main environment and in every
// worker thread's environment.
typedef void(NODESHIM_CDECL* node_embedder_preload_callback)(
    void* data,
    node_environment env,
    v8_local process,
    v8_local require);

// node::AddEnvironmentCleanupHook callback.
typedef void(NODESHIM_CDECL* node_cleanup_hook_callback)(void* arg);

// ---------------------------------------------------------------------------------------------
// Strings
// ---------------------------------------------------------------------------------------------

NODESHIM_API size_t NODESHIM_CDECL node_string_list_count(node_string_list list);
NODESHIM_API const char* NODESHIM_CDECL node_string_list_get(node_string_list list, size_t index);
NODESHIM_API void NODESHIM_CDECL node_string_list_delete(node_string_list list);

// ---------------------------------------------------------------------------------------------
// Process initialization (node.h: InitializeOncePerProcess / TearDownOncePerProcess)
// ---------------------------------------------------------------------------------------------

// node::InitializeOncePerProcess(args, flags). The result is owned by the caller.
NODESHIM_API node_initialization_result NODESHIM_CDECL node_initialize_once_per_process(
    int argc,
    const char* const* argv,
    uint32_t flags);

// node::InitializationResult accessors. String lists are borrowed from the result.
NODESHIM_API int NODESHIM_CDECL node_initialization_result_exit_code(node_initialization_result result);
NODESHIM_API bool NODESHIM_CDECL node_initialization_result_early_return(node_initialization_result result);
NODESHIM_API node_string_list NODESHIM_CDECL node_initialization_result_args(node_initialization_result result);
NODESHIM_API node_string_list NODESHIM_CDECL node_initialization_result_exec_args(node_initialization_result result);
NODESHIM_API node_string_list NODESHIM_CDECL node_initialization_result_errors(node_initialization_result result);
// The platform Node.js created, unless no_initialize_node_v8_platform was set. Borrowed.
NODESHIM_API node_multi_isolate_platform NODESHIM_CDECL node_initialization_result_platform(node_initialization_result result);
NODESHIM_API void NODESHIM_CDECL node_initialization_result_delete(node_initialization_result result);

// node::TearDownOncePerProcess()
NODESHIM_API void NODESHIM_CDECL node_tear_down_once_per_process(void);

// ---------------------------------------------------------------------------------------------
// Platform (node.h: MultiIsolatePlatform; v8.h: V8::InitializePlatform etc.)
// For embedders that opt out of Node's platform/V8 initialization via the process flags.
// ---------------------------------------------------------------------------------------------

// node::MultiIsolatePlatform::Create(thread_pool_size). Owned by the caller.
NODESHIM_API node_multi_isolate_platform NODESHIM_CDECL node_multi_isolate_platform_create(int thread_pool_size);
NODESHIM_API void NODESHIM_CDECL node_multi_isolate_platform_delete(node_multi_isolate_platform platform);
// node::MultiIsolatePlatform::DrainTasks(isolate)
NODESHIM_API void NODESHIM_CDECL node_multi_isolate_platform_drain_tasks(node_multi_isolate_platform platform, v8_isolate isolate);

// v8::V8::InitializePlatform / Initialize / Dispose / DisposePlatform
NODESHIM_API void NODESHIM_CDECL v8_initialize_platform(node_multi_isolate_platform platform);
NODESHIM_API bool NODESHIM_CDECL v8_initialize(void);
NODESHIM_API bool NODESHIM_CDECL v8_dispose(void);
NODESHIM_API void NODESHIM_CDECL v8_dispose_platform(void);

// ---------------------------------------------------------------------------------------------
// Environment setup (node.h: CommonEnvironmentSetup)
// ---------------------------------------------------------------------------------------------

// node::CommonEnvironmentSetup::Create(platform, &errors, args, exec_args, flags).
// Returns NULL on failure; *errors (owned by the caller, may be NULL) receives any messages.
NODESHIM_API node_common_environment_setup NODESHIM_CDECL node_common_environment_setup_create(
    node_multi_isolate_platform platform,
    node_string_list* errors,
    int argc,
    const char* const* argv,
    int exec_argc,
    const char* const* exec_argv,
    uint64_t flags);

NODESHIM_API v8_isolate NODESHIM_CDECL node_common_environment_setup_isolate(node_common_environment_setup setup);
NODESHIM_API node_environment NODESHIM_CDECL node_common_environment_setup_env(node_common_environment_setup setup);
// Requires an open v8_handle_scope.
NODESHIM_API v8_local NODESHIM_CDECL node_common_environment_setup_context(node_common_environment_setup setup);
NODESHIM_API uv_loop_t* NODESHIM_CDECL node_common_environment_setup_event_loop(node_common_environment_setup setup);
// Destroys the environment, isolate and loop (CommonEnvironmentSetup destructor).
NODESHIM_API void NODESHIM_CDECL node_common_environment_setup_delete(node_common_environment_setup setup);

// ---------------------------------------------------------------------------------------------
// Environment (node.h: LoadEnvironment, SpinEventLoop, Stop, ...)
// All of these require the locker, isolate scope, a handle scope and the context scope to be entered.
// ---------------------------------------------------------------------------------------------

// node::LoadEnvironment(env, StartExecutionCallback, EmbedderPreloadCallback).
// Returns false for an empty MaybeLocal (exception / termination); otherwise *result is the value.
NODESHIM_API bool NODESHIM_CDECL node_load_environment(
    node_environment env,
    node_start_execution_callback start_execution,
    void* start_execution_data,
    node_embedder_preload_callback preload,
    void* preload_data,
    v8_local* result);

// node::LoadEnvironment(env, main_script_source_utf8, EmbedderPreloadCallback)
NODESHIM_API bool NODESHIM_CDECL node_load_environment_script(
    node_environment env,
    const char* main_script_source_utf8,
    node_embedder_preload_callback preload,
    void* preload_data,
    v8_local* result);

// node::SpinEventLoop(env): runs the loop to completion, emitting beforeExit/exit.
// Returns false for Nothing (node::Stop was called); otherwise *exit_code is the exit code.
NODESHIM_API bool NODESHIM_CDECL node_spin_event_loop(node_environment env, int* exit_code);

// node::EmitProcessBeforeExit / EmitProcessExit (for embedders driving uv_run themselves).
// Both return false for Nothing (an exception was thrown while emitting).
NODESHIM_API bool NODESHIM_CDECL node_emit_process_before_exit(node_environment env, bool* result);
NODESHIM_API bool NODESHIM_CDECL node_emit_process_exit(node_environment env, int* exit_code);

// node::Stop(env, flags)
NODESHIM_API int NODESHIM_CDECL node_stop(node_environment env, uint32_t flags);

// node::GetMainContext(env). Requires an open v8_handle_scope.
NODESHIM_API v8_local NODESHIM_CDECL node_get_main_context(node_environment env);
// node::GetCurrentEnvironment(context)
NODESHIM_API node_environment NODESHIM_CDECL node_get_current_environment(v8_local context);
// node::GetCurrentEventLoop(isolate)
NODESHIM_API uv_loop_t* NODESHIM_CDECL node_get_current_event_loop(v8_isolate isolate);

// node::AddLinkedBinding(env, name, napi_addon_register_func, module_api_version).
// Makes process._linkedBinding(name) available; name is copied.
NODESHIM_API void NODESHIM_CDECL node_add_linked_binding_napi(
    node_environment env,
    const char* name,
    napi_addon_register_func init,
    int32_t module_api_version);

// node::AddEnvironmentCleanupHook / RemoveEnvironmentCleanupHook
NODESHIM_API void NODESHIM_CDECL node_add_environment_cleanup_hook(v8_isolate isolate, node_cleanup_hook_callback fun, void* arg);
NODESHIM_API void NODESHIM_CDECL node_remove_environment_cleanup_hook(v8_isolate isolate, node_cleanup_hook_callback fun, void* arg);

// ---------------------------------------------------------------------------------------------
// V8 scopes (v8.h: Locker, Isolate::Scope, HandleScope, Context::Scope)
// ---------------------------------------------------------------------------------------------

NODESHIM_API v8_locker NODESHIM_CDECL v8_locker_new(v8_isolate isolate);
NODESHIM_API void NODESHIM_CDECL v8_locker_delete(v8_locker locker);
NODESHIM_API bool NODESHIM_CDECL v8_locker_is_locked(v8_isolate isolate);

NODESHIM_API v8_isolate_scope NODESHIM_CDECL v8_isolate_scope_new(v8_isolate isolate);
NODESHIM_API void NODESHIM_CDECL v8_isolate_scope_delete(v8_isolate_scope scope);

NODESHIM_API v8_handle_scope NODESHIM_CDECL v8_handle_scope_new(v8_isolate isolate);
NODESHIM_API void NODESHIM_CDECL v8_handle_scope_delete(v8_handle_scope scope);

NODESHIM_API v8_context_scope NODESHIM_CDECL v8_context_scope_new(v8_local context);
NODESHIM_API void NODESHIM_CDECL v8_context_scope_delete(v8_context_scope scope);

// ---------------------------------------------------------------------------------------------
// V8 scripts (v8.h: Script::Compile + Run), for running JS before a napi_env exists.
// Returns false if compilation or execution threw; *result receives the completion value.
// ---------------------------------------------------------------------------------------------

NODESHIM_API bool NODESHIM_CDECL v8_script_compile_and_run(
    v8_isolate isolate,
    v8_local context,
    const char* source_utf8,
    const char* resource_name_utf8,
    v8_local* result);

#ifdef __cplusplus
}  // extern "C"
#endif

#endif  // LIBNODESHIM_H_
