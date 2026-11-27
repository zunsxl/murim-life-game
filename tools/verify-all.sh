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

validate_unity_results_xml() {
  local results_file="$1"
  if [[ ! -f "$results_file" ]]; then
    echo "ERROR: Expected Unity test results XML missing: $results_file" >&2
    exit 1
  fi

  local header
  header="$(sed -n '/<test-run[[:space:]]/,/>/p' "$results_file" | tr '\r\n' '  ')"
  if [[ -z "$header" ]]; then
    echo "ERROR: Unity test results XML missing <test-run> element: $results_file" >&2
    exit 1
  fi

  local result_val total_val
  result_val="$(printf '%s' "$header" | sed -nE 's/.*[[:space:]]result="([^"]*)".*/\1/p')"
  total_val="$(printf '%s' "$header" | sed -nE 's/.*[[:space:]]total="([0-9]+)".*/\1/p')"

  if [[ "$result_val" != "Passed" ]]; then
    echo "ERROR: Unity test results XML did not report result=\"Passed\" (got '${result_val:-missing}'): $results_file" >&2
    exit 1
  fi

  if [[ -z "$total_val" || "$total_val" -lt 1 ]]; then
    echo "ERROR: Unity test results XML reported zero or missing total test cases (total='${total_val:-missing}'): $results_file" >&2
    exit 1
  fi
}

contains_whole_token() {
  local haystack="$1"
  local needle="$2"
  if [[ ${#needle} -lt 3 ]]; then
    return 1
  fi

  awk -v h="$haystack" -v n="$needle" '
    BEGIN {
      hl = tolower(h);
      nl = tolower(n);
      nlen = length(nl);
      hlen = length(hl);
      pos = 1;
      while (pos <= hlen) {
        idx = index(substr(hl, pos), nl);
        if (idx == 0) exit 1;
        start = pos + idx - 1;
        end = start + nlen;
        before = (start == 1) ? "" : substr(hl, start - 1, 1);
        after = (end > hlen) ? "" : substr(hl, end, 1);
        if ((before == "" || before !~ /[a-z0-9]/) && (after == "" || after !~ /[a-z0-9]/)) {
          exit 0;
        }
        pos = start + 1;
      }
      exit 1;
    }'
}

run_core_suite() {
  local filter="$1"
  require_dotnet
  mkdir -p "$ARTIFACTS_DIR"
  rm -rf "$ARTIFACTS_DIR/core-results"

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
  local results_file="$ARTIFACTS_DIR/$results_name"
  local log_file="$ARTIFACTS_DIR/$log_name"
  rm -f "$results_file" "$log_file"

  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "fail" ]]; then
    echo "[stub] unity $platform failed" >&2
    exit 1
  fi
  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "pass" ]]; then
    printf '<test-run id="0" result="Passed" total="1" passed="1" failed="0" />\n' > "$results_file"
    validate_unity_results_xml "$results_file"
    echo "[stub] unity $platform pass (category=$category, filter=$filter)"
    return 0
  fi
  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "zero-tests" ]]; then
    printf '<test-run id="0" result="Passed" total="0" passed="0" failed="0" />\n' > "$results_file"
    validate_unity_results_xml "$results_file"
    return 0
  fi
  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "no-xml" ]]; then
    validate_unity_results_xml "$results_file"
    return 0
  fi

  local project_path results_path log_path
  project_path="$(to_native_path "$REPO_ROOT")"
  results_path="$(to_native_path "$results_file")"
  log_path="$(to_native_path "$log_file")"

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

  validate_unity_results_xml "$results_file"
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
  rm -f "$ARTIFACTS_DIR/build-windows.log" "$ARTIFACTS_DIR/player-smoke.log"

  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "fail" ]]; then
    echo "[stub] build failed" >&2
    exit 1
  fi
  if [[ "${MURIM_VERIFY_STUB_MODE:-}" == "pass" ]]; then
    echo "[stub] build pass"
    return 0
  fi

  rm -rf "$REPO_ROOT/Builds/Windows"

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

  local built_data_dir="$REPO_ROOT/Builds/Windows/MurimLifeGame_Data/StreamingAssets/Data"
  if [[ ! -f "$built_data_dir/world.json" || ! -f "$built_data_dir/camera.json" ]]; then
    echo "ERROR: Built StreamingAssets/Data missing world.json or camera.json: $built_data_dir" >&2
    exit 1
  fi

  run_player_smoke_test "$exe_path"
}

run_player_smoke_test() {
  # T-BLD-02
  local exe_path="$1"
  local smoke_log="$ARTIFACTS_DIR/player-smoke.log"
  local smoke_log_native
  smoke_log_native="$(to_native_path "$smoke_log")"
  rm -f "$smoke_log"

  "$exe_path" -seed 1 -logFile "$smoke_log_native" &
  local player_pid=$!

  local found=0
  local i
  for ((i = 0; i < 120; i++)); do
    if [[ -f "$smoke_log" ]] && grep -qE '\[Murim\] World ready ' "$smoke_log"; then
      found=1
      break
    fi
    if ! kill -0 "$player_pid" 2>/dev/null; then
      break
    fi
    sleep 0.5
  done

  if kill -0 "$player_pid" 2>/dev/null; then
    local kill_rc=0
    kill "$player_pid" 2>/dev/null || kill_rc=$?
    wait "$player_pid" 2>/dev/null || kill_rc=$?
  fi

  if [[ "$found" -ne 1 ]]; then
    echo "ERROR: Timed out or exited before [Murim] World ready appeared in $smoke_log" >&2
    exit 1
  fi

  local golden_file="$REPO_ROOT/Assets/Tests/Engine/Shared/GoldenHashes.cs"
  local seed1_hex
  seed1_hex="$(sed -nE 's/.*Seed1[[:space:]]*=[[:space:]]*0x([0-9A-Fa-f]{16})UL.*/\1/p' "$golden_file" | tr '[:lower:]' '[:upper:]')"
  if [[ ${#seed1_hex} -ne 16 ]]; then
    echo "ERROR: Failed to read 16-hex Seed1 from $golden_file" >&2
    exit 1
  fi

  if ! grep -qE "\\[Murim\\] World ready seed=1 source=cli hash=${seed1_hex} props=[0-9]+ ms=[0-9]+" "$smoke_log"; then
    echo "ERROR: player-smoke.log missing expected seed=1 source=cli hash=${seed1_hex} line." >&2
    exit 1
  fi

  local cur_user cur_host
  cur_user="${USERNAME:-${USER:-}}"
  cur_host="${COMPUTERNAME:-${HOSTNAME:-}}"

  local line
  while IFS= read -r line; do
    if [[ "$line" == *"/"* || "$line" == *"\\"* ]]; then
      echo "ERROR: [Murim] log line contains path separator: $line" >&2
      exit 1
    fi
    if contains_whole_token "$line" "$cur_user"; then
      echo "ERROR: [Murim] log line contains user name." >&2
      exit 1
    fi
    if contains_whole_token "$line" "$cur_host"; then
      echo "ERROR: [Murim] log line contains machine name." >&2
      exit 1
    fi
  done < <(grep -F '[Murim]' "$smoke_log")
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
  trap 'rm -rf "$REPO_ROOT/artifacts/self-test"' EXIT

  # Valid forms from §3.6.3 with stubbed pass (requires --self-test-child)
  assert_exit_code 0 "core" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --suite core
  assert_exit_code 0 "core-filter" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --suite core --filter SeedArgument
  assert_exit_code 0 "engine" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite engine
  assert_exit_code 0 "engine-filter" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite engine --filter ConfigLoading
  assert_exit_code 0 "art" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite art
  assert_exit_code 0 "build" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite build
  assert_exit_code 0 "all-suites" env MURIM_VERIFY_STUB_MODE=pass UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child

  # Stub mode outside --self-test-child exits 2
  assert_exit_code 2 "stub-outside-self-test-core" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --suite core
  assert_exit_code 2 "stub-outside-self-test-all" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path"

  # Unknown suite or invalid arguments exit 2
  assert_exit_code 2 "unknown-suite" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --suite unknown
  assert_exit_code 2 "unknown-flag" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --bogus
  assert_exit_code 2 "missing-suite-arg" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --suite
  assert_exit_code 2 "filter-without-suite" env MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --filter Foo

  # Failing stub suite command exits 1
  assert_exit_code 1 "failing-core" env MURIM_VERIFY_STUB_MODE=fail bash "$self_path" --self-test-child --suite core
  assert_exit_code 1 "failing-engine" env MURIM_VERIFY_STUB_MODE=fail UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite engine
  assert_exit_code 1 "failing-build" env MURIM_VERIFY_STUB_MODE=fail UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite build

  # Zero-test and missing-XML Unity results exit 1
  assert_exit_code 1 "unity-zero-tests-art" env MURIM_VERIFY_STUB_MODE=zero-tests UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite art
  assert_exit_code 1 "unity-no-xml-art" env MURIM_VERIFY_STUB_MODE=no-xml UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite art
  assert_exit_code 1 "unity-zero-tests-engine" env MURIM_VERIFY_STUB_MODE=zero-tests UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite engine
  assert_exit_code 1 "unity-no-xml-engine" env MURIM_VERIFY_STUB_MODE=no-xml UNITY_EDITOR="/stub/Unity" bash "$self_path" --self-test-child --suite engine

  # Missing UNITY_EDITOR exits 2 for engine, art, build
  assert_exit_code 2 "missing-unity-engine" env -u UNITY_EDITOR MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --suite engine
  assert_exit_code 2 "missing-unity-art" env -u UNITY_EDITOR MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --suite art
  assert_exit_code 2 "missing-unity-build" env -u UNITY_EDITOR MURIM_VERIFY_STUB_MODE=pass bash "$self_path" --self-test-child --suite build

  # T-BLD-02 whole-token matching
  if contains_whole_token "[Murim] hash=AABBCCDD" "ab"; then
    echo "SELF-TEST FAIL [token-short]: matched <3 char token" >&2
    exit 1
  fi
  if contains_whole_token "[Murim] sandy123" "andy"; then
    echo "SELF-TEST FAIL [token-substring]: matched non-boundary token" >&2
    exit 1
  fi
  if ! contains_whole_token "[Murim] user=ANDY!" "andy"; then
    echo "SELF-TEST FAIL [token-whole]: failed to match whole token" >&2
    exit 1
  fi

  echo "T-TOOL-01 self-test PASSED"
}

main() {
  local suite=""
  local filter=""
  local self_test=0
  local self_test_child=0

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
      --self-test-child)
        self_test_child=1
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

  if [[ "$self_test" -eq 1 && "$self_test_child" -eq 1 ]]; then
    echo "ERROR: --self-test cannot be combined with --self-test-child." >&2
    exit 2
  fi

  if [[ "$self_test_child" -ne 1 && -n "${MURIM_VERIFY_STUB_MODE+x}" ]]; then
    echo "MURIM_VERIFY_STUB_MODE is set outside --self-test" >&2
    exit 2
  fi

  if [[ "$self_test_child" -eq 1 ]]; then
    ARTIFACTS_DIR="$REPO_ROOT/artifacts/self-test"
  fi

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
