#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"

if command -v cygpath >/dev/null 2>&1; then
  UNITY_PROJECT_PATH="$(cygpath -m "$REPO_ROOT")"
else
  UNITY_PROJECT_PATH="$REPO_ROOT"
fi

SUITE=""
FILTER=""
SELF_TEST=0

usage() {
  cat <<'EOF'
Usage: tools/verify-all.sh [--suite <core|engine|art|build>] [--filter <expr>] [--self-test]
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --suite)
      if [[ $# -lt 2 || -z "${2:-}" ]]; then
        echo "ERROR: --suite requires a value" >&2
        exit 1
      fi
      SUITE="$2"
      shift 2
      ;;
    --filter)
      if [[ $# -lt 2 ]]; then
        echo "ERROR: --filter requires a value" >&2
        exit 1
      fi
      FILTER="$2"
      shift 2
      ;;
    --self-test)
      SELF_TEST=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "ERROR: Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -n "$SUITE" ]]; then
  case "$SUITE" in
    core|engine|art|build) ;;
    *)
      echo "ERROR: Unknown suite '$SUITE' (expected core, engine, art, or build)" >&2
      exit 1
      ;;
  esac
fi

read_editor_version() {
  local version_file="${REPO_ROOT}/ProjectSettings/ProjectVersion.txt"
  if [[ ! -f "$version_file" ]]; then
    echo "ERROR: Missing ProjectSettings/ProjectVersion.txt" >&2
    exit 1
  fi
  local ver
  ver="$(awk -F': *' '/^m_EditorVersion:/{gsub(/\r/, "", $2); print $2; exit}' "$version_file")"
  if [[ -z "$ver" ]]; then
    echo "ERROR: Unable to read m_EditorVersion from ProjectSettings/ProjectVersion.txt" >&2
    exit 1
  fi
  printf '%s' "$ver"
}

resolve_editor_path() {
  local version="$1"
  local raw_path
  if [[ -n "${UNITY_EDITOR_PATH:-}" ]]; then
    raw_path="$UNITY_EDITOR_PATH"
  else
    raw_path="C:/Program Files/Unity/Hub/Editor/${version}/Editor/Unity.exe"
  fi
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -u "$raw_path"
  else
    printf '%s' "$raw_path"
  fi
}

resolve_python() {
  if command -v python >/dev/null 2>&1; then
    printf 'python'
  elif command -v python3 >/dev/null 2>&1; then
    printf 'python3'
  else
    echo "ERROR: Python interpreter not found in PATH" >&2
    exit 1
  fi
}

EDITOR_VERSION="$(read_editor_version)"
EDITOR_BIN="$(resolve_editor_path "$EDITOR_VERSION")"

if [[ "$SELF_TEST" -eq 1 ]]; then
  if [[ ! -f "$EDITOR_BIN" ]]; then
    echo "ERROR: Resolved Unity Editor not found for version ${EDITOR_VERSION}" >&2
    exit 1
  fi
  if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet CLI not found in PATH" >&2
    exit 1
  fi
  if [[ ! -f "${REPO_ROOT}/global.json" || ! -f "${REPO_ROOT}/tests/Core/Murim.Core.Tests.csproj" ]]; then
    echo "ERROR: Missing global.json or tests/Core/Murim.Core.Tests.csproj" >&2
    exit 1
  fi
  DOTNET_VERSION="$(dotnet --version | tr -d '\r')"
  echo "[verify-all] Self-test OK"
  echo "[verify-all] Unity Editor version: ${EDITOR_VERSION}"
  echo "[verify-all] .NET SDK version: ${DOTNET_VERSION}"
  exit 0
fi

mkdir -p "$ARTIFACTS_DIR"

run_core() {
  echo "[verify-all] Running suite: core"
  local core_args=(
    test
    "${REPO_ROOT}/tests/Core/Murim.Core.Tests.csproj"
    -c Release
    --results-directory "${ARTIFACTS_DIR}/core"
    --logger "trx;LogFileName=core-results.trx"
  )
  if [[ -n "$FILTER" ]]; then
    core_args+=(--filter "$FILTER")
  fi
  dotnet "${core_args[@]}"
}

validate_nunit_xml() {
  local xml_path="$1"
  local py_bin
  py_bin="$(resolve_python)"
  if [[ ! -f "$xml_path" ]]; then
    echo "ERROR: Missing test results XML: $xml_path" >&2
    exit 1
  fi
  "$py_bin" - "$xml_path" <<'PY'
import sys
import xml.etree.ElementTree as ET

path = sys.argv[1]
tree = ET.parse(path)
root = tree.getroot()
total = int(root.attrib.get("total", "0"))
passed = int(root.attrib.get("passed", "0"))
failed = int(root.attrib.get("failed", "0"))
inconclusive = int(root.attrib.get("inconclusive", "0"))
result = root.attrib.get("result", "")
print(f"[verify-all] NUnit XML: result={result} total={total} passed={passed} failed={failed} inconclusive={inconclusive}")
if failed > 0 or inconclusive > 0 or result == "Failed":
    sys.exit(1)
PY
}

run_unity_tests() {
  local suite_name="$1"
  local category="$2"
  echo "[verify-all] Running suite: ${suite_name} (category=${category})"
  if [[ ! -f "$EDITOR_BIN" ]]; then
    echo "ERROR: Unity Editor not found for version ${EDITOR_VERSION}" >&2
    exit 1
  fi
  local results_xml_posix="${ARTIFACTS_DIR}/${suite_name}-results.xml"
  local results_xml_unity="${UNITY_PROJECT_PATH}/artifacts/${suite_name}-results.xml"
  local log_file_unity="${UNITY_PROJECT_PATH}/artifacts/${suite_name}.log"
  rm -f "$results_xml_posix"

  local unity_args=(
    -batchmode
    -projectPath "$UNITY_PROJECT_PATH"
    -runTests
    -testPlatform PlayMode
    -assemblyNames "Murim.Tests.Engine"
    -testCategory "$category"
    -testResults "$results_xml_unity"
    -logFile "$log_file_unity"
  )
  if [[ -n "$FILTER" ]]; then
    unity_args+=(-testFilter "$FILTER")
  fi

  "$EDITOR_BIN" "${unity_args[@]}"
  validate_nunit_xml "$results_xml_posix"
}

run_build() {
  echo "[verify-all] Running suite: build"
  if [[ ! -f "$EDITOR_BIN" ]]; then
    echo "ERROR: Unity Editor not found for version ${EDITOR_VERSION}" >&2
    exit 1
  fi
  local log_file_unity="${UNITY_PROJECT_PATH}/artifacts/build.log"
  local report_file="${ARTIFACTS_DIR}/build-report.json"
  local exe_file="${REPO_ROOT}/Builds/Windows/MurimLifeGame.exe"
  rm -f "$report_file"

  "$EDITOR_BIN" \
    -batchmode \
    -quit \
    -projectPath "$UNITY_PROJECT_PATH" \
    -executeMethod Murim.Editor.BuildScript.BuildWindowsRelease \
    -logFile "$log_file_unity"

  if [[ ! -s "$exe_file" ]]; then
    echo "ERROR: Expected release executable not found: Builds/Windows/MurimLifeGame.exe" >&2
    exit 1
  fi
  if [[ ! -f "$report_file" ]]; then
    echo "ERROR: Expected build report not found: artifacts/build-report.json" >&2
    exit 1
  fi
  echo "[verify-all] Build output verified: Builds/Windows/MurimLifeGame.exe"
}

case "$SUITE" in
  core)
    run_core
    ;;
  engine)
    run_unity_tests "engine" "!Art"
    ;;
  art)
    run_unity_tests "art" "Art"
    ;;
  build)
    run_build
    ;;
  "")
    run_core
    run_unity_tests "engine" "!Art"
    run_unity_tests "art" "Art"
    run_build
    ;;
esac

echo "[verify-all] All requested verification suites passed."
