#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ARTIFACTS_DIR="$REPO_ROOT/artifacts"

usage() {
  cat <<'EOF'
Usage: tools/verify-all.sh [--suite <core|engine|art|build> [--filter <expr>]] [--self-test]
EOF
}

to_native_path() {
  local p="$1"
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$p"
  else
    printf '%s' "$p"
  fi
}

to_posix_path() {
  local p="$1"
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -u "$p"
  else
    printf '%s' "$p"
  fi
}

RESOLVED_UNITY_EDITOR=""

require_dotnet() {
  if [[ -n "${MURIM_VERIFY_STUB_MODE:-}" ]]; then
    return 0
  fi
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet CLI not found in PATH." >&2
    exit 2
  fi
}

resolve_unity_editor() {
  if [[ -n "${MURIM_VERIFY_STUB_MODE:-}" && -n "${UNITY_EDITOR:-}" ]]; then
    RESOLVED_UNITY_EDITOR="$UNITY_EDITOR"
    return 0
  fi
  if [[ -z "${UNITY_EDITOR:-}" ]]; then
    echo "ERROR: UNITY_EDITOR environment variable is not set." >&2
    exit 2
  fi
  local posix_unity
  posix_unity="$(to_posix_path "$UNITY_EDITOR")"
  if [[ ! -f "$posix_unity" ]]; then
    echo "ERROR: UNITY_EDITOR does not point to an existing executable: $UNITY_EDITOR" >&2
    exit 2
  fi
  RESOLVED_UNITY_EDITOR="$posix_unity"
}

run_core_suite() {
  local filter="$1"
  require_dotnet
  mkdir -p "$ARTIFACTS_DIR"

  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "fail" ]]; then
    echo "[stub] core failed" >&2
    exit 1
  fi
  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "pass" ]]; then
    echo "[stub] core pass (filter=$filter)"
    return 0
  fi

  local -a cmd=(
    dotnet test "$REPO_ROOT/tests/Core/Murim.Core.Tests.csproj"
    -c Release
    --results-directory "$ARTIFACTS_DIR/core-results"
  )
  if [[ -n "$filter" ]]; then
    cmd+=(--filter "$filter")
  fi

  if ! "${cmd[@]}"; then
    exit 1
  fi
}

run_unity_tests() {
  local platform="$1"
  local category="$2"
  local filter="$3"
  local results_name="$4"
  local log_name="$5"

  resolve_unity_editor
  local unity_exe="$RESOLVED_UNITY_EDITOR"
  mkdir -p "$ARTIFACTS_DIR"

  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "fail" ]]; then
    echo "[stub] unity $platform failed" >&2
    exit 1
  fi
  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "pass" ]]; then
    echo "[stub] unity $platform pass (category=$category, filter=$filter)"
    return 0
  fi

  local project_path results_path log_path
  project_path="$(to_native_path "$REPO_ROOT")"
  results_path="$(to_native_path "$ARTIFACTS_DIR/$results_name")"
  log_path="$(to_native_path "$ARTIFACTS_DIR/$log_name")"

  local -a cmd=(
    "$unity_exe"
    -batchmode
    -nographics
    -projectPath "$project_path"
    -runTests
    -testPlatform "$platform"
    -testCategory "$category"
    -testResults "$results_path"
    -logFile "$log_path"
  )
  if [[ -n "$filter" ]]; then
    cmd+=(-testFilter "$filter")
  fi

  if ! "${cmd[@]}"; then
    exit 1
  fi
}

run_engine_suite() {
  local filter="$1"
  run_unity_tests "EditMode" "!Art" "$filter" "engine-editmode-results.xml" "engine-editmode.log"
  run_unity_tests "PlayMode" "!Art" "$filter" "engine-playmode-results.xml" "engine-playmode.log"
}

run_art_suite() {
  run_unity_tests "EditMode" "Art" "" "art-results.xml" "art.log"
}

run_build_suite() {
  resolve_unity_editor
  local unity_exe="$RESOLVED_UNITY_EDITOR"
  mkdir -p "$ARTIFACTS_DIR"

  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "fail" ]]; then
    echo "[stub] build failed" >&2
    exit 1
  fi
  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "pass" ]]; then
    echo "[stub] build pass"
    return 0
  fi

  local project_path log_path exe_path
  project_path="$(to_native_path "$REPO_ROOT")"
  log_path="$(to_native_path "$ARTIFACTS_DIR/build-windows.log")"
  exe_path="$REPO_ROOT/Builds/Windows/MurimLifeGame.exe"

  if ! "$unity_exe" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "$project_path" \
    -executeMethod "Murim.Editor.BuildScript.BuildWindowsRelease" \
    -logFile "$log_path"; then
    exit 1
  fi

  # T-BLD-01
  if [[ ! -f "$exe_path" ]]; then
    echo "ERROR: Expected build output missing: $exe_path" >&2
    exit 1
  fi

  if ! grep -qE '\[Murim\] Build result=Succeeded .* options=None ' "$ARTIFACTS_DIR/build-windows.log"; then
    echo "ERROR: Build report missing non-debug release summary (options=None)." >&2
    exit 1
  fi

  if [[ ! -f "$REPO_ROOT/Assets/StreamingAssets/Data/world.json" || ! -f "$REPO_ROOT/Assets/StreamingAssets/Data/camera.json" ]]; then
    echo "INFO: StreamingAssets/Data/world.json and/or camera.json absent (owned by ARCH-002/ARCH-003)."
  fi
}

assert_exit_code() {
  local expected="$1"
  local label="$2"
  shift 2
  local rc=0
  "$@" >/dev/null 2>&1 || rc=$?
  if [[ "$rc" -ne "$expected" ]]; then
    echo "SELF-TEST FAIL [$label]: expected exit $expected, got $rc" >&2
    exit 1
  fi
}

run_self_test() {
  # T-TOOL-01
  if ! grep -qE '^set -euo pipefail$' "$0"; then
    echo "SELF-TEST FAIL: script must contain set -euo pipefail" >&2
    exit 1
  fi

  local self_path="$0"

  # Valid forms from §3.6.3 with stubbed pass
  assert_exit_code 0 "core" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --suite core
  assert_exit_code 0 "core-filter" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --suite core --filter SeedArgument
  assert_exit_code 0 "engine" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --suite engine
  assert_exit_code 0 "engine-filter" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --suite engine --filter ConfigLoading
  assert_exit_code 0 "art" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --suite art
  assert_exit_code 0 "build" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --suite build
  assert_exit_code 0 "all-suites" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path"

  # Unknown suite or invalid arguments exit 2
  assert_exit_code 2 "unknown-suite" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --suite unknown
  assert_exit_code 2 "unknown-flag" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --bogus
  assert_exit_code 2 "missing-suite-arg" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --suite
  assert_exit_code 2 "filter-without-suite" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --filter Foo

  # Failing stub suite command exits 1
  assert_exit_code 1 "failing-core" env MURIM_VERIFY_STUB_MODE=fail bash "$self_path" --suite core
  assert_exit_code 1 "failing-engine" env MURIM_VERIFY_STUB_MODE=fail UNITY_EDITOR="/stub/Unity" bash "$self_path" --suite engine
  assert_exit_code 1 "failing-build" env MURIM_VERIFY_STUB_MODE=fail UNITY_EDITOR="/stub/Unity" bash "$self_path" --suite build

  # Missing UNITY_EDITOR exits 2 for engine, art, build
  assert_exit_code 2 "missing-unity-engine" env -u UNITY_EDITOR MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --suite engine
  assert_exit_code 2 "missing-unity-art" env -u UNITY_EDITOR MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --suite art
  assert_exit_code 2 "missing-unity-build" env -u UNITY_EDITOR MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --suite build

  echo "T-TOOL-01 self-test PASSED"
}

main() {
  local suite=""
  local filter=""
  local self_test=0

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --suite)
        if [[ $# -lt 2 || -z "$2" ]]; then
          echo "ERROR: --suite requires an argument." >&2
          usage >&2
          exit 2
        fi
        suite="$2"
        shift 2
        ;;
      --filter)
        if [[ $# -lt 2 || -z "$2" ]]; then
          echo "ERROR: --filter requires an argument." >&2
          usage >&2
          exit 2
        fi
        filter="$2"
        shift 2
        ;;
      --self-test)
        self_test=1
        shift
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "ERROR: Unknown argument: $1" >&2
        usage >&2
        exit 2
        ;;
    esac
  done

  if [[ "$self_test" -eq 1 ]]; then
    if [[ -n "$suite" || -n "$filter" ]]; then
      echo "ERROR: --self-test cannot be combined with --suite or --filter." >&2
      exit 2
    fi
    run_self_test
    exit 0
  fi

  if [[ -n "$filter" && -z "$suite" ]]; then
    echo "ERROR: --filter requires --suite." >&2
    exit 2
  fi

  cd "$REPO_ROOT"

  if [[ -z "$suite" ]]; then
    run_core_suite ""
    run_engine_suite ""
    run_art_suite
    run_build_suite
    exit 0
  fi

  case "$suite" in
    core)
      run_core_suite "$filter"
      ;;
    engine)
      run_engine_suite "$filter"
      ;;
    art)
      if [[ -n "$filter" ]]; then
        echo "ERROR: --filter is not supported with --suite art." >&2
        exit 2
      fi
      run_art_suite
      ;;
    build)
      if [[ -n "$filter" ]]; then
        echo "ERROR: --filter is not supported with --suite build." >&2
        exit 2
      fi
      run_build_suite
      ;;
    *)
      echo "ERROR: Unknown suite: $suite" >&2
      usage >&2
      exit 2
      ;;
  esac
}

main "$@"
