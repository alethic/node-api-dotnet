// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// See libnodeshim.h. Each function is a thin, mechanical wrapper over one node.h / v8.h
// declaration; there is deliberately no logic here beyond type conversion.

#include "libnodeshim.h"

#include <cstring>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

#include "node.h"
#include "uv.h"
#include "v8.h"

// ---------------------------------------------------------------------------------------------
// Flag values must match node.h exactly.
// ---------------------------------------------------------------------------------------------

#define NODESHIM_ASSERT_FLAG(c_value, cpp_value) \
  static_assert(static_cast<uint64_t>(c_value) == static_cast<uint64_t>(cpp_value), #c_value " != " #cpp_value)

NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_flags, node::ProcessInitializationFlags::kNoFlags);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_enable_stdio_inheritance, node::ProcessInitializationFlags::kEnableStdioInheritance);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_disable_node_options_env, node::ProcessInitializationFlags::kDisableNodeOptionsEnv);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_disable_cli_options, node::ProcessInitializationFlags::kDisableCLIOptions);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_icu, node::ProcessInitializationFlags::kNoICU);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_stdio_initialization, node::ProcessInitializationFlags::kNoStdioInitialization);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_default_signal_handling, node::ProcessInitializationFlags::kNoDefaultSignalHandling);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_initialize_v8, node::ProcessInitializationFlags::kNoInitializeV8);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_initialize_node_v8_platform, node::ProcessInitializationFlags::kNoInitializeNodeV8Platform);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_init_openssl, node::ProcessInitializationFlags::kNoInitOpenSSL);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_parse_global_debug_variables, node::ProcessInitializationFlags::kNoParseGlobalDebugVariables);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_adjust_resource_limits, node::ProcessInitializationFlags::kNoAdjustResourceLimits);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_use_large_pages, node::ProcessInitializationFlags::kNoUseLargePages);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_print_help_or_version_output, node::ProcessInitializationFlags::kNoPrintHelpOrVersionOutput);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_no_initialize_cppgc, node::ProcessInitializationFlags::kNoInitializeCppgc);
NODESHIM_ASSERT_FLAG(node_process_initialization_flags_generate_predictable_snapshot, node::ProcessInitializationFlags::kGeneratePredictableSnapshot);

NODESHIM_ASSERT_FLAG(node_environment_flags_no_flags, node::EnvironmentFlags::kNoFlags);
NODESHIM_ASSERT_FLAG(node_environment_flags_default_flags, node::EnvironmentFlags::kDefaultFlags);
NODESHIM_ASSERT_FLAG(node_environment_flags_owns_process_state, node::EnvironmentFlags::kOwnsProcessState);
NODESHIM_ASSERT_FLAG(node_environment_flags_owns_inspector, node::EnvironmentFlags::kOwnsInspector);
NODESHIM_ASSERT_FLAG(node_environment_flags_no_register_esm_loader, node::EnvironmentFlags::kNoRegisterESMLoader);
NODESHIM_ASSERT_FLAG(node_environment_flags_track_unmanaged_fds, node::EnvironmentFlags::kTrackUnmanagedFds);
NODESHIM_ASSERT_FLAG(node_environment_flags_hide_console_windows, node::EnvironmentFlags::kHideConsoleWindows);
NODESHIM_ASSERT_FLAG(node_environment_flags_no_native_addons, node::EnvironmentFlags::kNoNativeAddons);
NODESHIM_ASSERT_FLAG(node_environment_flags_no_global_search_paths, node::EnvironmentFlags::kNoGlobalSearchPaths);
NODESHIM_ASSERT_FLAG(node_environment_flags_no_browser_globals, node::EnvironmentFlags::kNoBrowserGlobals);
NODESHIM_ASSERT_FLAG(node_environment_flags_no_create_inspector, node::EnvironmentFlags::kNoCreateInspector);
NODESHIM_ASSERT_FLAG(node_environment_flags_no_start_debug_signal_handler, node::EnvironmentFlags::kNoStartDebugSignalHandler);
NODESHIM_ASSERT_FLAG(node_environment_flags_no_wait_for_inspector_frontend, node::EnvironmentFlags::kNoWaitForInspectorFrontend);

NODESHIM_ASSERT_FLAG(node_stop_flags_no_flags, node::StopFlags::kNoFlags);
NODESHIM_ASSERT_FLAG(node_stop_flags_do_not_terminate_isolate, node::StopFlags::kDoNotTerminateIsolate);

// ---------------------------------------------------------------------------------------------
// Handle representations
// ---------------------------------------------------------------------------------------------

struct node_initialization_result_s {
  std::shared_ptr<node::InitializationResult> result;
};

// Owned platform (from node_multi_isolate_platform_create); a borrowed one from
// InitializationResult::platform() is just the raw pointer cast, so both go through Platform().
struct node_multi_isolate_platform_s {
  std::unique_ptr<node::MultiIsolatePlatform> owned;
};

struct node_common_environment_setup_s {
  std::unique_ptr<node::CommonEnvironmentSetup> setup;
};

struct node_string_list_s {
  std::vector<std::string> strings;
};

// Scope wrappers. v8::HandleScope cannot itself be heap allocated (it deletes operator new), but a
// struct holding it as a member can; this is what Node-API's own handle scopes do.
struct v8_locker_s {
  explicit v8_locker_s(v8::Isolate* isolate) : locker(isolate) {}
  v8::Locker locker;
};
struct v8_isolate_scope_s {
  explicit v8_isolate_scope_s(v8::Isolate* isolate) : scope(isolate) {}
  v8::Isolate::Scope scope;
};
struct v8_handle_scope_s {
  explicit v8_handle_scope_s(v8::Isolate* isolate) : scope(isolate) {}
  v8::HandleScope scope;
};
struct v8_context_scope_s {
  explicit v8_context_scope_s(v8::Local<v8::Context> context) : scope(context) {}
  v8::Context::Scope scope;
};

namespace {

inline v8::Isolate* Isolate(v8_isolate isolate) {
  return reinterpret_cast<v8::Isolate*>(isolate);
}

inline v8_isolate ToIsolate(v8::Isolate* isolate) {
  return reinterpret_cast<v8_isolate>(isolate);
}

inline node::Environment* Env(node_environment env) {
  return reinterpret_cast<node::Environment*>(env);
}

inline node_environment ToEnv(node::Environment* env) {
  return reinterpret_cast<node_environment>(env);
}

// A platform handle is either an owning wrapper (created here) or a borrowed raw pointer handed
// out by node_initialization_result_platform; the tag bit distinguishes them.
constexpr uintptr_t kBorrowedPlatformTag = 1;

inline node::MultiIsolatePlatform* Platform(node_multi_isolate_platform platform) {
  uintptr_t bits = reinterpret_cast<uintptr_t>(platform);
  if (bits & kBorrowedPlatformTag) {
    return reinterpret_cast<node::MultiIsolatePlatform*>(bits & ~kBorrowedPlatformTag);
  }
  return platform->owned.get();
}

inline node_multi_isolate_platform ToBorrowedPlatform(node::MultiIsolatePlatform* platform) {
  return reinterpret_cast<node_multi_isolate_platform>(reinterpret_cast<uintptr_t>(platform) | kBorrowedPlatformTag);
}

// v8::Local<T> is a pointer to a handle slot; Node-API represents napi_value the same way.
template <typename T>
inline v8_local ToLocal(v8::Local<T> local) {
  return reinterpret_cast<v8_local>(*local);
}

template <typename T>
inline v8::Local<T> FromLocal(v8_local handle) {
  v8::Local<T> local;
  static_assert(sizeof(local) == sizeof(handle), "v8::Local must be a single pointer");
  std::memcpy(static_cast<void*>(&local), &handle, sizeof(handle));
  return local;
}

std::vector<std::string> ToStrings(int argc, const char* const* argv) {
  std::vector<std::string> strings;
  strings.reserve(argc > 0 ? argc : 0);
  for (int i = 0; i < argc; i++) strings.emplace_back(argv[i]);
  return strings;
}

// Borrowed string lists point straight at the vector inside the InitializationResult.
inline node_string_list BorrowStrings(const std::vector<std::string>& strings) {
  return reinterpret_cast<node_string_list>(const_cast<std::vector<std::string>*>(&strings));
}

inline const std::vector<std::string>& Strings(node_string_list list) {
  return *reinterpret_cast<const std::vector<std::string>*>(list);
}

node::EmbedderPreloadCallback MakePreload(node_embedder_preload_callback preload, void* data) {
  if (preload == nullptr) return nullptr;
  return [preload, data](node::Environment* env, v8::Local<v8::Value> process, v8::Local<v8::Value> require) {
    preload(data, ToEnv(env), ToLocal(process), ToLocal(require));
  };
}

}  // namespace

// ---------------------------------------------------------------------------------------------
// Strings
// ---------------------------------------------------------------------------------------------

size_t NODESHIM_CDECL node_string_list_count(node_string_list list) {
  return Strings(list).size();
}

const char* NODESHIM_CDECL node_string_list_get(node_string_list list, size_t index) {
  const auto& strings = Strings(list);
  return index < strings.size() ? strings[index].c_str() : nullptr;
}

void NODESHIM_CDECL node_string_list_delete(node_string_list list) {
  delete reinterpret_cast<std::vector<std::string>*>(list);
}

// ---------------------------------------------------------------------------------------------
// Process initialization
// ---------------------------------------------------------------------------------------------

node_initialization_result NODESHIM_CDECL node_initialize_once_per_process(
    int argc, const char* const* argv, uint32_t flags) {
  auto result = node::InitializeOncePerProcess(
      ToStrings(argc, argv), static_cast<node::ProcessInitializationFlags::Flags>(flags));
  return new node_initialization_result_s{std::move(result)};
}

int NODESHIM_CDECL node_initialization_result_exit_code(node_initialization_result result) {
  return result->result->exit_code();
}

bool NODESHIM_CDECL node_initialization_result_early_return(node_initialization_result result) {
  return result->result->early_return();
}

node_string_list NODESHIM_CDECL node_initialization_result_args(node_initialization_result result) {
  return BorrowStrings(result->result->args());
}

node_string_list NODESHIM_CDECL node_initialization_result_exec_args(node_initialization_result result) {
  return BorrowStrings(result->result->exec_args());
}

node_string_list NODESHIM_CDECL node_initialization_result_errors(node_initialization_result result) {
  return BorrowStrings(result->result->errors());
}

node_multi_isolate_platform NODESHIM_CDECL node_initialization_result_platform(node_initialization_result result) {
  node::MultiIsolatePlatform* platform = result->result->platform();
  return platform != nullptr ? ToBorrowedPlatform(platform) : nullptr;
}

void NODESHIM_CDECL node_initialization_result_delete(node_initialization_result result) {
  delete result;
}

void NODESHIM_CDECL node_tear_down_once_per_process(void) {
  node::TearDownOncePerProcess();
}

// ---------------------------------------------------------------------------------------------
// Platform
// ---------------------------------------------------------------------------------------------

node_multi_isolate_platform NODESHIM_CDECL node_multi_isolate_platform_create(int thread_pool_size) {
  return new node_multi_isolate_platform_s{node::MultiIsolatePlatform::Create(thread_pool_size)};
}

void NODESHIM_CDECL node_multi_isolate_platform_delete(node_multi_isolate_platform platform) {
  if ((reinterpret_cast<uintptr_t>(platform) & kBorrowedPlatformTag) == 0) delete platform;
}

void NODESHIM_CDECL node_multi_isolate_platform_drain_tasks(node_multi_isolate_platform platform, v8_isolate isolate) {
  Platform(platform)->DrainTasks(Isolate(isolate));
}

void NODESHIM_CDECL v8_initialize_platform(node_multi_isolate_platform platform) {
  v8::V8::InitializePlatform(Platform(platform));
}

bool NODESHIM_CDECL v8_initialize(void) {
  return v8::V8::Initialize();
}

bool NODESHIM_CDECL v8_dispose(void) {
  return v8::V8::Dispose();
}

void NODESHIM_CDECL v8_dispose_platform(void) {
  v8::V8::DisposePlatform();
}

// ---------------------------------------------------------------------------------------------
// Environment setup
// ---------------------------------------------------------------------------------------------

node_common_environment_setup NODESHIM_CDECL node_common_environment_setup_create(
    node_multi_isolate_platform platform,
    node_string_list* errors,
    int argc,
    const char* const* argv,
    int exec_argc,
    const char* const* exec_argv,
    uint64_t flags) {
  std::vector<std::string> error_strings;
  std::unique_ptr<node::CommonEnvironmentSetup> setup = node::CommonEnvironmentSetup::Create(
      Platform(platform),
      &error_strings,
      ToStrings(argc, argv),
      ToStrings(exec_argc, exec_argv),
      static_cast<node::EnvironmentFlags::Flags>(flags));
  if (errors != nullptr) {
    *errors = reinterpret_cast<node_string_list>(new std::vector<std::string>(std::move(error_strings)));
  }
  if (setup == nullptr) return nullptr;
  return new node_common_environment_setup_s{std::move(setup)};
}

v8_isolate NODESHIM_CDECL node_common_environment_setup_isolate(node_common_environment_setup setup) {
  return ToIsolate(setup->setup->isolate());
}

node_environment NODESHIM_CDECL node_common_environment_setup_env(node_common_environment_setup setup) {
  return ToEnv(setup->setup->env());
}

v8_local NODESHIM_CDECL node_common_environment_setup_context(node_common_environment_setup setup) {
  return ToLocal(setup->setup->context());
}

uv_loop_t* NODESHIM_CDECL node_common_environment_setup_event_loop(node_common_environment_setup setup) {
  return setup->setup->event_loop();
}

void NODESHIM_CDECL node_common_environment_setup_delete(node_common_environment_setup setup) {
  delete setup;
}

// ---------------------------------------------------------------------------------------------
// Environment
// ---------------------------------------------------------------------------------------------

bool NODESHIM_CDECL node_load_environment(
    node_environment env,
    node_start_execution_callback start_execution,
    void* start_execution_data,
    node_embedder_preload_callback preload,
    void* preload_data,
    v8_local* result) {
  node::StartExecutionCallback start;
  if (start_execution != nullptr) {
    start = [start_execution, start_execution_data, env](const node::StartExecutionCallbackInfo& info)
        -> v8::MaybeLocal<v8::Value> {
      v8_local value = start_execution(
          start_execution_data,
          env,
          ToLocal(info.process_object),
          ToLocal(info.native_require),
          ToLocal(info.run_cjs));
      if (value == nullptr) return {};
      return FromLocal<v8::Value>(value);
    };
  }
  v8::Local<v8::Value> value;
  if (!node::LoadEnvironment(Env(env), std::move(start), MakePreload(preload, preload_data)).ToLocal(&value)) {
    return false;
  }
  if (result != nullptr) *result = ToLocal(value);
  return true;
}

bool NODESHIM_CDECL node_load_environment_script(
    node_environment env,
    const char* main_script_source_utf8,
    node_embedder_preload_callback preload,
    void* preload_data,
    v8_local* result) {
  v8::Local<v8::Value> value;
  if (!node::LoadEnvironment(Env(env), std::string_view(main_script_source_utf8), MakePreload(preload, preload_data))
           .ToLocal(&value)) {
    return false;
  }
  if (result != nullptr) *result = ToLocal(value);
  return true;
}

bool NODESHIM_CDECL node_spin_event_loop(node_environment env, int* exit_code) {
  return node::SpinEventLoop(Env(env)).To(exit_code);
}

bool NODESHIM_CDECL node_emit_process_before_exit(node_environment env, bool* result) {
  return node::EmitProcessBeforeExit(Env(env)).To(result);
}

bool NODESHIM_CDECL node_emit_process_exit(node_environment env, int* exit_code) {
  return node::EmitProcessExit(Env(env)).To(exit_code);
}

int NODESHIM_CDECL node_stop(node_environment env, uint32_t flags) {
  return node::Stop(Env(env), static_cast<node::StopFlags::Flags>(flags));
}

v8_local NODESHIM_CDECL node_get_main_context(node_environment env) {
  return ToLocal(node::GetMainContext(Env(env)));
}

node_environment NODESHIM_CDECL node_get_current_environment(v8_local context) {
  return ToEnv(node::GetCurrentEnvironment(FromLocal<v8::Context>(context)));
}

uv_loop_t* NODESHIM_CDECL node_get_current_event_loop(v8_isolate isolate) {
  return node::GetCurrentEventLoop(Isolate(isolate));
}

void NODESHIM_CDECL node_add_linked_binding_napi(
    node_environment env, const char* name, napi_addon_register_func init, int32_t module_api_version) {
  // node_module keeps the name pointer for the life of the environment; it is intentionally never freed.
  size_t length = std::strlen(name) + 1;
  char* copy = new char[length];
  std::memcpy(copy, name, length);
  node::AddLinkedBinding(Env(env), copy, init, module_api_version);
}

void NODESHIM_CDECL node_add_environment_cleanup_hook(v8_isolate isolate, node_cleanup_hook_callback fun, void* arg) {
  node::AddEnvironmentCleanupHook(Isolate(isolate), fun, arg);
}

void NODESHIM_CDECL node_remove_environment_cleanup_hook(v8_isolate isolate, node_cleanup_hook_callback fun, void* arg) {
  node::RemoveEnvironmentCleanupHook(Isolate(isolate), fun, arg);
}

// ---------------------------------------------------------------------------------------------
// V8 scopes
// ---------------------------------------------------------------------------------------------

v8_locker NODESHIM_CDECL v8_locker_new(v8_isolate isolate) {
  return new v8_locker_s(Isolate(isolate));
}

void NODESHIM_CDECL v8_locker_delete(v8_locker locker) {
  delete locker;
}

bool NODESHIM_CDECL v8_locker_is_locked(v8_isolate isolate) {
  return v8::Locker::IsLocked(Isolate(isolate));
}

v8_isolate_scope NODESHIM_CDECL v8_isolate_scope_new(v8_isolate isolate) {
  return new v8_isolate_scope_s(Isolate(isolate));
}

void NODESHIM_CDECL v8_isolate_scope_delete(v8_isolate_scope scope) {
  delete scope;
}

v8_handle_scope NODESHIM_CDECL v8_handle_scope_new(v8_isolate isolate) {
  return new v8_handle_scope_s(Isolate(isolate));
}

void NODESHIM_CDECL v8_handle_scope_delete(v8_handle_scope scope) {
  delete scope;
}

v8_context_scope NODESHIM_CDECL v8_context_scope_new(v8_local context) {
  return new v8_context_scope_s(FromLocal<v8::Context>(context));
}

void NODESHIM_CDECL v8_context_scope_delete(v8_context_scope scope) {
  delete scope;
}

// ---------------------------------------------------------------------------------------------
// V8 scripts
// ---------------------------------------------------------------------------------------------

bool NODESHIM_CDECL v8_script_compile_and_run(
    v8_isolate isolate_handle,
    v8_local context_handle,
    const char* source_utf8,
    const char* resource_name_utf8,
    v8_local* result) {
  v8::Isolate* isolate = Isolate(isolate_handle);
  v8::Local<v8::Context> context = FromLocal<v8::Context>(context_handle);
  v8::EscapableHandleScope handle_scope(isolate);
  v8::TryCatch try_catch(isolate);

  v8::Local<v8::String> source;
  if (!v8::String::NewFromUtf8(isolate, source_utf8).ToLocal(&source)) return false;

  v8::ScriptOrigin origin(
      v8::String::NewFromUtf8(isolate, resource_name_utf8 != nullptr ? resource_name_utf8 : "<embedder>")
          .ToLocalChecked());
  v8::Local<v8::Script> script;
  if (!v8::Script::Compile(context, source, &origin).ToLocal(&script)) return false;

  v8::Local<v8::Value> value;
  if (!script->Run(context).ToLocal(&value)) return false;

  if (result != nullptr) *result = ToLocal(handle_scope.Escape(value));
  return true;
}
