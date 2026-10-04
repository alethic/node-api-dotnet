// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// End-to-end smoke test of the libnode embedding shim from plain C: initialize the process, create
// an environment, load a script, spin the event loop, run a second script, tear everything down.
// Exit code 0 means every step succeeded.

#include <stdio.h>
#include <stdlib.h>

#include "libnodeshim.h"

static int fail(const char* step, node_string_list errors) {
  fprintf(stderr, "FAILED: %s\n", step);
  if (errors != NULL) {
    for (size_t i = 0; i < node_string_list_count(errors); i++) {
      fprintf(stderr, "  %s\n", node_string_list_get(errors, i));
    }
  }
  return 1;
}

int main(int argc, char* argv[]) {
  (void)argc;
  (void)argv;
  const char* args[] = {"node"};

  node_initialization_result init = node_initialize_once_per_process(
      1, args, node_process_initialization_flags_no_flags);
  if (node_initialization_result_exit_code(init) != 0 || node_initialization_result_early_return(init)) {
    return fail("node_initialize_once_per_process", node_initialization_result_errors(init));
  }
  node_multi_isolate_platform platform = node_initialization_result_platform(init);
  if (platform == NULL) return fail("node_initialization_result_platform", NULL);

  node_string_list errors = NULL;
  node_common_environment_setup setup = node_common_environment_setup_create(
      platform, &errors, 1, args, 0, NULL, node_environment_flags_default_flags);
  if (setup == NULL) return fail("node_common_environment_setup_create", errors);
  node_string_list_delete(errors);

  v8_isolate isolate = node_common_environment_setup_isolate(setup);
  node_environment env = node_common_environment_setup_env(setup);

  v8_locker locker = v8_locker_new(isolate);
  v8_isolate_scope isolate_scope = v8_isolate_scope_new(isolate);
  v8_handle_scope handle_scope = v8_handle_scope_new(isolate);
  v8_local context = node_common_environment_setup_context(setup);
  v8_context_scope context_scope = v8_context_scope_new(context);

  int rc = 0;
  v8_local load_result = NULL;
  if (!node_load_environment_script(
          env,
          "const answer = 6 * 7;\n"
          "console.log('hello from', process.version, process.arch, 'answer', answer);\n"
          "globalThis.answer = answer;\n"
          "setTimeout(() => console.log('timer fired'), 10);\n",
          NULL,
          NULL,
          &load_result)) {
    rc = fail("node_load_environment_script", NULL);
  }

  int exit_code = -1;
  if (rc == 0 && !node_spin_event_loop(env, &exit_code)) rc = fail("node_spin_event_loop", NULL);
  if (rc == 0 && exit_code != 0) {
    fprintf(stderr, "FAILED: event loop exit code %d\n", exit_code);
    rc = 1;
  }

  v8_local value = NULL;
  if (rc == 0 && !v8_script_compile_and_run(isolate, context, "globalThis.answer * 2", "shimtest", &value)) {
    rc = fail("v8_script_compile_and_run", NULL);
  }
  if (rc == 0) printf("script ran; v8_local=%p; main context env=%p (expected %p)\n",
                      (void*)value, (void*)node_get_current_environment(context), (void*)env);

  node_stop(env, node_stop_flags_no_flags);

  v8_context_scope_delete(context_scope);
  v8_handle_scope_delete(handle_scope);
  v8_isolate_scope_delete(isolate_scope);
  v8_locker_delete(locker);

  node_common_environment_setup_delete(setup);
  node_tear_down_once_per_process();
  node_initialization_result_delete(init);

  if (rc == 0) printf("OK\n");
  return rc;
}
