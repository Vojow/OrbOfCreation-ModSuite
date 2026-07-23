#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

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

for command_name in dotnet git find cp cmp shasum awk grep pgrep; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command is unavailable: ${command_name}" >&2
        exit 1
    fi
done

resolve_game_root() {
    if [[ -n "${OOC_GAME_DIR:-}" ]]; then
        printf '%s\n' "${OOC_GAME_DIR}"
        return
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
        printf '%s\n' "${OOC_SAVE_DIR}"
        return
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

game_is_running() {
    pgrep -x "Orb Of Creation" >/dev/null 2>&1 ||
        pgrep -f "[O]rb Of Creation\\.app/Contents/MacOS/Orb Of Creation" >/dev/null 2>&1 ||
        pgrep -f "[O]rb Of Creation\\.exe" >/dev/null 2>&1
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
OOC_GAME_DIR="${reference_root}" dotnet test \
    "${repository_root}/tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj" \
    --configuration "${configuration}"

echo "Building the supported suite in ${mode} mode..."
for project in OrbModding.Common OrbAutomata OrbModConfig OrbMentor; do
    OOC_GAME_DIR="${reference_root}" dotnet build \
        "${repository_root}/src/${project}/${project}.csproj" \
        --configuration "${configuration}" \
        --disable-build-servers \
        -m:1 \
        --no-incremental \
        "${profile_build_arguments[@]}"
done

output_directory() {
    printf '%s/src/%s/%s/%s/netstandard2.1\n' \
        "${repository_root}" "$1" "${output_root}" "${configuration}"
}

automata_source="$(output_directory OrbAutomata)/OrbAutomata.dll"
mentor_source="$(output_directory OrbMentor)/OrbMentor.dll"
config_source="$(output_directory OrbModConfig)/OrbModConfig.dll"
common_source="$(output_directory OrbModding.Common)/OrbModding.Common.dll"
for build_output in "${automata_source}" "${mentor_source}" "${config_source}" "${common_source}"; do
    if [[ ! -f "${build_output}" ]]; then
        echo "Required supported build output is missing: ${build_output}" >&2
        exit 1
    fi
done
if [[ "${mode}" == "perf-debug" ]]; then
    if ! grep -a -q "AutomataServiceCycleProfileController" "${automata_source}" ||
        ! grep -a -q "ServiceCycleProfileRuntimeSession" "${common_source}"; then
        echo "The perf-debug outputs do not contain the required ServiceCycle profiling components." >&2
        exit 1
    fi
elif grep -a -q "AutomataServiceCycleProfileController" "${automata_source}" ||
    grep -a -q "ServiceCycleProfileRuntimeSession" "${common_source}"; then
    echo "The release outputs unexpectedly contain ServiceCycle profiling components." >&2
    exit 1
fi
if find "${repository_root}/src" -path "*/${output_root}/${configuration}/netstandard2.1/*" \
    -type f \( -name 'OrbChronomancer.dll' -o -name 'OrbAchievementResonance.dll' \) | grep -q .; then
    echo "A forbidden experimental DLL appeared in the selected build outputs." >&2
    exit 1
fi

automata_target="${plugins_root}/OrbAutomata/OrbAutomata.dll"
mentor_target="${plugins_root}/OrbMentor/OrbMentor.dll"
common_target="${plugins_root}/OrbMentor/OrbModding.Common.dll"
config_target="${plugins_root}/OrbModConfig/OrbModConfig.dll"

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

reject_duplicate OrbAutomata.dll "${automata_target}"
reject_duplicate OrbMentor.dll "${mentor_target}"
reject_duplicate OrbModding.Common.dll "${common_target}"
reject_duplicate OrbModConfig.dll "${config_target}"
if find "${plugins_root}" -type f \
    \( -name 'OrbChronomancer.dll' -o -name 'OrbAchievementResonance.dll' \) | grep -q .; then
    echo "Remove experimental ModSuite DLLs before installing the supported suite." >&2
    exit 1
fi

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

mkdir -p \
    "${dll_backup}/OrbAutomata" \
    "${dll_backup}/OrbMentor" \
    "${dll_backup}/OrbModConfig"
dll_backup_count=0
for installed_dll in \
    "${automata_target}" \
    "${mentor_target}" \
    "${common_target}" \
    "${config_target}"; do
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

if game_is_running; then
    echo "Orb of Creation started during backup. Close it before installation." >&2
    exit 1
fi

mkdir -p \
    "$(dirname "${automata_target}")" \
    "$(dirname "${mentor_target}")" \
    "$(dirname "${config_target}")"
cp -p "${automata_source}" "${automata_target}"
cp -p "${mentor_source}" "${mentor_target}"
cp -p "${common_source}" "${common_target}"
cp -p "${config_source}" "${config_target}"

verify_install() {
    local source_file="$1"
    local installed_file="$2"
    if ! cmp -s "${source_file}" "${installed_file}"; then
        echo "Installed DLL does not match its build output: $(basename "${installed_file}")" >&2
        exit 1
    fi
}

verify_install "${automata_source}" "${automata_target}"
verify_install "${mentor_source}" "${mentor_target}"
verify_install "${common_source}" "${common_target}"
verify_install "${config_source}" "${config_target}"

print_hash() {
    local installed_file="$1"
    local digest
    digest="$(shasum -a 256 "${installed_file}" | awk '{print $1}')"
    echo "  ${digest}  $(basename "${installed_file}")"
}

echo
echo "Installed supported ModSuite build."
echo "  Mode: ${mode}"
echo "  Source: ${source_commit} (${source_state})"
echo "  Save backup: backups/$(basename "${save_backup}") (${save_count} files)"
echo "  DLL backup: BepInEx/modsuite-backups/$(basename "${dll_backup}") (${dll_backup_count} files)"
echo "  Installed SHA-256:"
print_hash "${automata_target}"
print_hash "${mentor_target}"
print_hash "${common_target}"
print_hash "${config_target}"
