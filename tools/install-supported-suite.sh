#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-${repository_root}/artifacts/nuget-http-cache}"

usage() {
    echo "Usage: ./script/install release|perf-debug" >&2
    echo >&2
    echo "  release     Release build without ServiceCycle profiling probes" >&2
    echo "  perf-debug  Debug build with ServiceCycle profiling probes" >&2
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
    usage
    exit 0
fi
if [[ "$#" -ne 1 ]]; then
    usage
    exit 2
fi

configuration=""
output_root=""
profile_build_arguments=()
case "$1" in
    release)
        configuration="Release"
        output_root="bin"
        profile_build_arguments=(-p:EnableServiceCycleProfiler=false)
        ;;
    perf-debug)
        configuration="Debug"
        output_root="bin-profile"
        profile_build_arguments=(-p:EnableServiceCycleProfiler=true)
        ;;
    *)
        usage
        exit 2
        ;;
esac
mode="$1"

for command_name in dotnet git find cp cmp awk grep env; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command is unavailable: ${command_name}" >&2
        exit 1
    fi
done
if ! command -v shasum >/dev/null 2>&1 &&
    ! command -v sha256sum >/dev/null 2>&1; then
    echo "Required SHA-256 command is unavailable: shasum or sha256sum" >&2
    exit 1
fi

is_windows_environment() {
    if [[ -n "${WINDIR:-}" || -n "${SYSTEMROOT:-}" ]]; then
        return 0
    fi
    case "${OSTYPE:-}" in
        cygwin* | msys* | win32*)
            return 0
            ;;
    esac
    return 1
}

if is_windows_environment &&
    ! command -v powershell.exe >/dev/null 2>&1; then
    echo "Native Windows process inspection is unavailable: powershell.exe" >&2
    exit 1
fi
if ! command -v pgrep >/dev/null 2>&1 &&
    ! command -v powershell.exe >/dev/null 2>&1; then
    echo "Required process inspection is unavailable: pgrep or powershell.exe" >&2
    exit 1
fi

normalize_directory() {
    local candidate="$1"
    if [[ -d "${candidate}" ]]; then
        printf '%s\n' "${candidate}"
        return 0
    fi
    if command -v cygpath >/dev/null 2>&1; then
        local unix_candidate
        unix_candidate="$(cygpath -u "${candidate}" 2>/dev/null || true)"
        if [[ -n "${unix_candidate}" && -d "${unix_candidate}" ]]; then
            printf '%s\n' "${unix_candidate}"
            return 0
        fi
    fi
    return 1
}

resolve_game_root() {
    if [[ -n "${OOC_GAME_DIR:-}" ]]; then
        if normalize_directory "${OOC_GAME_DIR}"; then
            return
        fi
        echo "OOC_GAME_DIR is not an existing directory: ${OOC_GAME_DIR}" >&2
        exit 1
    fi

    local candidate
    for candidate in \
        "${HOME}/Library/Application Support/Steam/steamapps/common/Orb of Creation" \
        "${HOME}/.local/share/Steam/steamapps/common/Orb of Creation" \
        "${HOME}/.steam/steam/steamapps/common/Orb of Creation"; do
        if [[ -d "${candidate}/BepInEx" ]]; then
            printf '%s\n' "${candidate}"
            return
        fi
    done

    echo "Could not locate Orb of Creation. Set OOC_GAME_DIR to its installation root." >&2
    exit 1
}

resolve_save_root() {
    if [[ -n "${OOC_SAVE_DIR:-}" ]]; then
        if normalize_directory "${OOC_SAVE_DIR}"; then
            return
        fi
        echo "OOC_SAVE_DIR is not an existing directory: ${OOC_SAVE_DIR}" >&2
        exit 1
    fi

    local candidate
    for candidate in \
        "${HOME}/Library/Application Support/com.marplegames.orb-of-creation" \
        "${HOME}/.local/share/Steam/steamapps/compatdata/1910680/pfx/drive_c/users/steamuser/AppData/LocalLow/MarpleGames/Orb of Creation"; do
        if [[ -d "${candidate}" ]]; then
            printf '%s\n' "${candidate}"
            return
        fi
    done

    echo "Could not locate Orb of Creation saves. Set OOC_SAVE_DIR to the save directory." >&2
    exit 1
}

game_root="$(cd "$(resolve_game_root)" && pwd)"
save_root="$(cd "$(resolve_save_root)" && pwd)"
plugins_root="${game_root}/BepInEx/plugins"
bepinex_core="${game_root}/BepInEx/core"
if [[ ! -d "${plugins_root}" || ! -f "${bepinex_core}/BepInEx.dll" ||
    ! -f "${bepinex_core}/0Harmony.dll" ]]; then
    echo "The selected game root does not contain a complete BepInEx 5 installation." >&2
    exit 1
fi

pgrep_has_process() {
    local pgrep_status=0
    pgrep "$@" >/dev/null 2>&1 || pgrep_status="$?"
    case "${pgrep_status}" in
        0)
            return 0
            ;;
        1)
            return 1
            ;;
        *)
            echo "POSIX process inspection failed; treating the game as running." >&2
            return 2
            ;;
    esac
}

game_is_running() {
    # A native Windows query is authoritative whenever it is available. Git-for-Windows may also
    # provide pgrep, but that process view cannot be trusted to include native Windows processes.
    if command -v powershell.exe >/dev/null 2>&1; then
        local powershell_status=0
        powershell.exe -NoProfile -NonInteractive -Command \
            '$ErrorActionPreference = "Stop"; try { $process = @(Get-Process | Where-Object { $_.ProcessName -eq "Orb Of Creation" }) } catch { exit 2 }; if ($process.Count -gt 0) { exit 0 }; exit 1' \
            >/dev/null 2>&1 || powershell_status="$?"
        case "${powershell_status}" in
            0)
                return 0
                ;;
            1)
                ;;
            *)
                echo "Native Windows process inspection failed; treating the game as running." >&2
                return 0
                ;;
        esac
    fi

    if ! command -v pgrep >/dev/null 2>&1; then
        return 1
    fi

    local pgrep_status=0
    pgrep_has_process -x "Orb Of Creation" || pgrep_status="$?"
    if [[ "${pgrep_status}" -eq 0 || "${pgrep_status}" -eq 2 ]]; then
        return 0
    fi
    pgrep_status=0
    pgrep_has_process -f "[O]rb Of Creation\\.app/Contents/MacOS/Orb Of Creation" ||
        pgrep_status="$?"
    if [[ "${pgrep_status}" -eq 0 || "${pgrep_status}" -eq 2 ]]; then
        return 0
    fi
    pgrep_status=0
    pgrep_has_process -f "[O]rb Of Creation\\.exe" || pgrep_status="$?"
    if [[ "${pgrep_status}" -eq 0 || "${pgrep_status}" -eq 2 ]]; then
        return 0
    fi
    return 1
}

if game_is_running; then
    echo "Orb of Creation is running. Close it before building, backing up, or installing." >&2
    exit 1
fi

temporary_reference_root=""
cleanup() {
    if [[ -n "${temporary_reference_root}" && -d "${temporary_reference_root}" ]]; then
        rm -rf -- "${temporary_reference_root}"
    fi
}
trap cleanup EXIT INT TERM

windows_managed="${game_root}/Orb Of Creation_Data/Managed"
mac_managed="${game_root}/Orb Of Creation.app/Contents/Resources/Data/Managed"
if [[ -d "${windows_managed}" ]]; then
    reference_root="${game_root}"
elif [[ -d "${mac_managed}" ]]; then
    temporary_reference_root="$(mktemp -d "${TMPDIR:-/tmp}/orbmodsuite-install.XXXXXX")"
    mkdir -p "${temporary_reference_root}/Orb Of Creation_Data"
    ln -s "${mac_managed}" "${temporary_reference_root}/Orb Of Creation_Data/Managed"
    ln -s "${game_root}/BepInEx" "${temporary_reference_root}/BepInEx"
    reference_root="${temporary_reference_root}"
else
    echo "The selected game root has no supported managed-assembly directory." >&2
    exit 1
fi

for required_reference in \
    "${reference_root}/Orb Of Creation_Data/Managed/Assembly-CSharp.dll" \
    "${reference_root}/Orb Of Creation_Data/Managed/Assembly-CSharp-firstpass.dll" \
    "${reference_root}/Orb Of Creation_Data/Managed/UnityEngine.dll" \
    "${reference_root}/Orb Of Creation_Data/Managed/UnityEngine.CoreModule.dll" \
    "${reference_root}/Orb Of Creation_Data/Managed/UnityEngine.UI.dll" \
    "${reference_root}/Orb Of Creation_Data/Managed/UnityEngine.UIModule.dll" \
    "${reference_root}/Orb Of Creation_Data/Managed/Unity.TextMeshPro.dll" \
    "${reference_root}/BepInEx/core/BepInEx.dll" \
    "${reference_root}/BepInEx/core/0Harmony.dll"; do
    if [[ ! -f "${required_reference}" ]]; then
        echo "Required real-reference file is missing: ${required_reference}" >&2
        exit 1
    fi
done

source_commit="$(git -C "${repository_root}" rev-parse HEAD)"
source_state="clean"
if [[ -n "$(git -C "${repository_root}" status --porcelain --untracked-files=no)" ]]; then
    source_state="dirty"
    echo "Warning: installing a build with tracked working-tree changes." >&2
fi

echo "Running the complete portable gate..."
"${repository_root}/script/test"

echo "Running installed-game contracts..."
OOC_GAME_DIR="${reference_root}" dotnet restore \
    "${repository_root}/tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj" \
    --force-evaluate \
    --disable-build-servers
OOC_GAME_DIR="${reference_root}" dotnet test \
    "${repository_root}/tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj" \
    --configuration "${configuration}" \
    --no-restore

if [[ "${mode}" == "release" ]]; then
    echo "Building the canonical release artifact from checked-in game references..."
    env -u OOC_GAME_DIR dotnet restore \
        "${repository_root}/src/OrbModSuite.csproj" \
        --force-evaluate \
        --disable-build-servers \
        "${profile_build_arguments[@]}"
    env -u OOC_GAME_DIR dotnet build \
        "${repository_root}/src/OrbModSuite.csproj" \
        --configuration "${configuration}" \
        --disable-build-servers \
        -m:1 \
        --no-incremental \
        --no-restore \
        "${profile_build_arguments[@]}"
else
    echo "Building the supported suite in ${mode} mode against the real game references..."
    OOC_GAME_DIR="${reference_root}" dotnet restore \
        "${repository_root}/src/OrbModSuite.csproj" \
        --force-evaluate \
        --disable-build-servers \
        "${profile_build_arguments[@]}"
    OOC_GAME_DIR="${reference_root}" dotnet build \
        "${repository_root}/src/OrbModSuite.csproj" \
        --configuration "${configuration}" \
        --disable-build-servers \
        -m:1 \
        --no-incremental \
        --no-restore \
        "${profile_build_arguments[@]}"
fi

output_directory() {
    printf '%s/src/%s/%s/netstandard2.1\n' \
        "${repository_root}" "${output_root}" "${configuration}"
}

suite_source="$(output_directory)/OrbModSuite.dll"
if [[ ! -f "${suite_source}" ]]; then
    echo "Required supported build output is missing: ${suite_source}" >&2
    exit 1
fi
if [[ "${mode}" == "perf-debug" ]]; then
    if ! grep -a -q "AutomataServiceCycleProfileController" "${suite_source}" ||
        ! grep -a -q "ServiceCycleProfileRuntimeSession" "${suite_source}"; then
        echo "The perf-debug output does not contain the required ServiceCycle profiling components." >&2
        exit 1
    fi
elif grep -a -q "AutomataServiceCycleProfileController" "${suite_source}" ||
    grep -a -q "ServiceCycleProfileRuntimeSession" "${suite_source}"; then
    echo "The release output unexpectedly contains ServiceCycle profiling components." >&2
    exit 1
fi

suite_target="${plugins_root}/OrbModSuite/OrbModSuite.dll"

reject_duplicate() {
    local assembly_name="$1"
    local expected_path="$2"
    local found_path
    while IFS= read -r found_path; do
        if [[ "${found_path}" != "${expected_path}" ]]; then
            echo "Duplicate supported DLL must be removed before installation: ${found_path}" >&2
            exit 1
        fi
    done < <(find "${plugins_root}" -type f -name "${assembly_name}" -print)
}

reject_duplicate OrbModSuite.dll "${suite_target}"

# The three retired plugins loaded under their own GUIDs. Left in place beside the merged DLL they
# still load, and two Automatas would fight over the same native action families. Refuse rather than
# delete: this script has never removed a user's files and should not start now.
reject_retired() {
    local found_path
    local found_any=0
    while IFS= read -r found_path; do
        if [[ "${found_any}" -eq 0 ]]; then
            echo "Retired suite DLLs are still installed and would load beside the merged plugin." >&2
            echo "Remove these before installing:" >&2
            found_any=1
        fi
        echo "  ${found_path}" >&2
    done < <(find "${plugins_root}" -type f \( \
        -name 'OrbAutomata.dll' -o \
        -name 'OrbMentor.dll' -o \
        -name 'OrbModConfig.dll' -o \
        -name 'OrbModding.Common.dll' \) -print)
    if [[ "${found_any}" -ne 0 ]]; then
        exit 1
    fi
}

reject_retired

if game_is_running; then
    echo "Orb of Creation started during validation. Close it before installation." >&2
    exit 1
fi

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
save_backup="${save_root}/backups/pre-modsuite-install-${timestamp}"
dll_backup="${game_root}/BepInEx/modsuite-backups/pre-modsuite-install-${timestamp}"
if [[ -e "${save_backup}" || -e "${dll_backup}" ]]; then
    echo "A backup already exists for timestamp ${timestamp}; retry in one second." >&2
    exit 1
fi

mkdir -p "${save_backup}"
save_count=0
for save_file in "${save_root}"/*.sav "${save_root}/steam_autocloud.vdf"; do
    if [[ ! -f "${save_file}" ]]; then
        continue
    fi
    cp -p "${save_file}" "${save_backup}/"
    if ! cmp -s "${save_file}" "${save_backup}/$(basename "${save_file}")"; then
        echo "Save backup verification failed for $(basename "${save_file}")." >&2
        exit 1
    fi
    save_count=$((save_count + 1))
done
if [[ "${save_count}" -eq 0 ]]; then
    echo "No active save files were found; refusing installation." >&2
    exit 1
fi

mkdir -p "${dll_backup}/OrbModSuite"
dll_backup_count=0
for installed_dll in "${suite_target}"; do
    if [[ ! -f "${installed_dll}" ]]; then
        continue
    fi
    relative_path="${installed_dll#${plugins_root}/}"
    cp -p "${installed_dll}" "${dll_backup}/${relative_path}"
    if ! cmp -s "${installed_dll}" "${dll_backup}/${relative_path}"; then
        echo "Installed-DLL backup verification failed for ${relative_path}." >&2
        exit 1
    fi
    dll_backup_count=$((dll_backup_count + 1))
done

# The retired four-plugin era left its configuration files behind. BepInEx names a configuration file
# after the plugin GUID, so the suite reads only dev.vojow.orbofcreation.modsuite.cfg and these three
# are dead settings that still read as live to anyone who opens the config folder. Retired DLLs are
# refused because two of them loading at once would fight over the same native action families; these
# do nothing at all, so they are moved rather than made the user's problem. Moved, not deleted: they
# go into the same timestamped backup as the installed DLL, and the copy is verified before the
# original is dropped.
config_root="${game_root}/BepInEx/config"
retired_config_count=0
for retired_config_name in \
    dev.vojow.orbofcreation.automata.cfg \
    dev.vojow.orbofcreation.mentor.cfg \
    dev.vojow.orbofcreation.modconfig.cfg; do
    retired_config="${config_root}/${retired_config_name}"
    if [[ ! -f "${retired_config}" ]]; then
        continue
    fi
    mkdir -p "${dll_backup}/config"
    cp -p "${retired_config}" "${dll_backup}/config/${retired_config_name}"
    if ! cmp -s "${retired_config}" "${dll_backup}/config/${retired_config_name}"; then
        echo "Retired-configuration backup verification failed for ${retired_config_name}." >&2
        exit 1
    fi
    rm -f -- "${retired_config}"
    if [[ -e "${retired_config}" ]]; then
        echo "Retired configuration could not be moved out of the config folder: ${retired_config_name}" >&2
        exit 1
    fi
    retired_config_count=$((retired_config_count + 1))
done

if game_is_running; then
    echo "Orb of Creation started during backup. Close it before installation." >&2
    exit 1
fi

mkdir -p "$(dirname "${suite_target}")"
cp -p "${suite_source}" "${suite_target}"

verify_install() {
    local source_file="$1"
    local installed_file="$2"
    if ! cmp -s "${source_file}" "${installed_file}"; then
        echo "Installed DLL does not match its build output: $(basename "${installed_file}")" >&2
        exit 1
    fi
}

verify_install "${suite_source}" "${suite_target}"

print_hash() {
    local installed_file="$1"
    local digest
    if command -v shasum >/dev/null 2>&1; then
        digest="$(shasum -a 256 "${installed_file}" | awk '{print $1}')"
    else
        digest="$(sha256sum "${installed_file}" | awk '{print $1}')"
    fi
    echo "  ${digest}  $(basename "${installed_file}")"
}

echo
echo "Installed supported ModSuite build."
echo "  Mode: ${mode}"
echo "  Source: ${source_commit} (${source_state})"
echo "  Save backup: backups/$(basename "${save_backup}") (${save_count} files)"
echo "  DLL backup: BepInEx/modsuite-backups/$(basename "${dll_backup}") (${dll_backup_count} files)"
echo "  Retired configuration files moved into that backup: ${retired_config_count}"
echo "  Installed SHA-256:"
print_hash "${suite_target}"
