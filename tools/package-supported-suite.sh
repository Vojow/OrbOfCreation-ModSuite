#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_directory="${repository_root}/artifacts/releases"
game_root="${OOC_GAME_DIR:-${repository_root}/lib}"

if [[ "$#" -ne 0 ]]; then
    echo "Usage: ./script/package" >&2
    exit 2
fi

for command_name in dotnet git zip unzip shasum; do
    if ! command -v "${command_name}" >/dev/null 2>&1; then
        echo "Required command is unavailable: ${command_name}" >&2
        exit 1
    fi
done

initial_head="$(git -C "${repository_root}" rev-parse HEAD)"
assert_clean_head() {
    local current_head
    current_head="$(git -C "${repository_root}" rev-parse HEAD)"
    if [[ "${current_head}" != "${initial_head}" ]]; then
        echo "Repository HEAD changed during package validation." >&2
        exit 1
    fi
    if [[ -n "$(git -C "${repository_root}" status --porcelain --untracked-files=normal)" ]]; then
        echo "Package rehearsal requires a clean tracked and untracked working tree." >&2
        exit 1
    fi
}
assert_clean_head

managed_directory="${game_root}/Orb Of Creation_Data/Managed"
bepinex_core_directory="${game_root}/BepInEx/core"
for required_reference in \
    "${managed_directory}/Assembly-CSharp.dll" \
    "${managed_directory}/Assembly-CSharp-firstpass.dll" \
    "${bepinex_core_directory}/BepInEx.dll" \
    "${bepinex_core_directory}/0Harmony.dll"; do
    if [[ ! -f "${required_reference}" ]]; then
        echo "Required real-reference file is missing: ${required_reference}" >&2
        exit 1
    fi
done

read_project_version() {
    local project_path="$1"
    local version
    version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "${project_path}")"
    if [[ -z "${version}" || "${version}" == *$'\n'* ]]; then
        echo "Could not read one project version from ${project_path}." >&2
        exit 1
    fi
    printf '%s' "${version}"
}

read_suite_version() {
    local version
    version="$(sed -n 's:.*<SuiteVersion>\([^<]*\)</SuiteVersion>.*:\1:p' \
        "${repository_root}/Directory.Build.props")"
    if [[ -z "${version}" || "${version}" == *$'\n'* ]]; then
        echo "Could not read one SuiteVersion from Directory.Build.props." >&2
        exit 1
    fi
    printf '%s' "${version}"
}

read_plugin_version() {
    local constant_name="$1"
    local version
    version="$(sed -n "s/.*public const string ${constant_name} = \"\([^\"]*\)\";.*/\1/p" \
        "${repository_root}/src/OrbModding.Common/PluginIds.cs")"
    if [[ -z "${version}" || "${version}" == *$'\n'* ]]; then
        echo "Could not read PluginIds.${constant_name}." >&2
        exit 1
    fi
    printf '%s' "${version}"
}

assert_version() {
    local component="$1"
    local project_version="$2"
    local plugin_version="$3"
    if [[ "${project_version}" != "${plugin_version}" ]]; then
        echo "${component} version mismatch: project=${project_version}, PluginIds=${plugin_version}" >&2
        exit 1
    fi
}

automata_version="$(read_project_version "${repository_root}/src/OrbAutomata/OrbAutomata.csproj")"
mod_config_version="$(read_project_version "${repository_root}/src/OrbModConfig/OrbModConfig.csproj")"
mentor_version="$(read_project_version "${repository_root}/src/OrbMentor/OrbMentor.csproj")"
common_version="$(read_project_version "${repository_root}/src/OrbModding.Common/OrbModding.Common.csproj")"
suite_version="$(read_suite_version)"
assert_version "Orb Automata" "${automata_version}" "$(read_plugin_version AutomataVersion)"
assert_version "Orb Mod Config" "${mod_config_version}" "$(read_plugin_version ModConfigVersion)"
assert_version "Orb Mentor" "${mentor_version}" "$(read_plugin_version MentorVersion)"
assert_version "Orb Modding Common" "${common_version}" "$(read_plugin_version Version)"

package_name="OrbOfCreation-ModSuite-${suite_version}"
zip_path="${output_directory}/${package_name}.zip"
checksums_path="${output_directory}/${package_name}-SHA256SUMS.txt"
if [[ -e "${zip_path}" || -e "${checksums_path}" ]]; then
    echo "Package output already exists; refusing to overwrite ${package_name}." >&2
    exit 1
fi

echo "Running the bounded portable gate..."
"${repository_root}/script/test"

echo "Running installed-game contracts against staged references..."
OOC_GAME_DIR="${game_root}" dotnet test \
    "${repository_root}/tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj" \
    --configuration Release --no-restore

echo "Building the supported suite against staged references..."
for project in OrbModding.Common OrbAutomata OrbModConfig OrbMentor; do
    OOC_GAME_DIR="${game_root}" dotnet build \
        "${repository_root}/src/${project}/${project}.csproj" \
        --configuration Release --no-restore
done

assert_plugin_output() {
    local project="$1"
    local plugin="$2"
    local output="${repository_root}/src/${project}/bin/Release/netstandard2.1"
    for required in "${plugin}" OrbModding.Common.dll; do
        if [[ ! -f "${output}/${required}" ]]; then
            echo "Required ${project} output is missing: ${required}" >&2
            exit 1
        fi
    done
    if find "${output}" -maxdepth 1 -type f \( \
        -name 'Assembly-CSharp*.dll' -o \
        -name 'BepInEx.dll' -o \
        -name '0Harmony.dll' -o \
        -name 'UnityEngine*.dll' -o \
        -name 'OrbModding.GameStubs.dll' \) | grep -q .; then
        echo "Game or loader assemblies leaked into ${project} output." >&2
        exit 1
    fi
}

assert_plugin_output OrbAutomata OrbAutomata.dll
assert_plugin_output OrbModConfig OrbModConfig.dll
assert_plugin_output OrbMentor OrbMentor.dll
assert_clean_head

mkdir -p "${output_directory}"
temporary_root="$(mktemp -d "${output_directory}/.package.XXXXXX")"
cleanup() {
    rm -rf -- "${temporary_root}"
}
trap cleanup EXIT INT TERM
stage="${temporary_root}/${package_name}"
temporary_zip="${temporary_root}/${package_name}.zip"
temporary_checksums="${temporary_root}/${package_name}-SHA256SUMS.txt"

mkdir -p \
    "${stage}/BepInEx/plugins/OrbAutomata" \
    "${stage}/BepInEx/plugins/OrbMentor" \
    "${stage}/BepInEx/plugins/OrbModConfig"
cp "${repository_root}/src/OrbAutomata/bin/Release/netstandard2.1/OrbAutomata.dll" \
    "${stage}/BepInEx/plugins/OrbAutomata/"
cp "${repository_root}/src/OrbMentor/bin/Release/netstandard2.1/OrbMentor.dll" \
    "${stage}/BepInEx/plugins/OrbMentor/"
cp "${repository_root}/src/OrbMentor/bin/Release/netstandard2.1/OrbModding.Common.dll" \
    "${stage}/BepInEx/plugins/OrbMentor/"
cp "${repository_root}/src/OrbModConfig/bin/Release/netstandard2.1/OrbModConfig.dll" \
    "${stage}/BepInEx/plugins/OrbModConfig/"
for document in README.md CHANGELOG.md LICENSE THIRD_PARTY_NOTICES.md; do
    cp "${repository_root}/${document}" "${stage}/"
done

expected_entries=(
    "BepInEx/plugins/OrbAutomata/OrbAutomata.dll"
    "BepInEx/plugins/OrbMentor/OrbMentor.dll"
    "BepInEx/plugins/OrbMentor/OrbModding.Common.dll"
    "BepInEx/plugins/OrbModConfig/OrbModConfig.dll"
    "CHANGELOG.md"
    "LICENSE"
    "README.md"
    "THIRD_PARTY_NOTICES.md"
)
printf '%s\n' "${expected_entries[@]}" | LC_ALL=C sort > "${temporary_root}/expected-entries.txt"
(
    cd "${stage}"
    find . -type f -print | sed 's#^\./##' | LC_ALL=C sort | \
        zip -X -q "${temporary_zip}" -@
)
unzip -Z1 "${temporary_zip}" | LC_ALL=C sort > "${temporary_root}/actual-entries.txt"
if ! cmp -s "${temporary_root}/expected-entries.txt" "${temporary_root}/actual-entries.txt"; then
    echo "Package archive entries do not match the supported allowlist:" >&2
    diff -u "${temporary_root}/expected-entries.txt" "${temporary_root}/actual-entries.txt" >&2 || true
    exit 1
fi

for entry in "${expected_entries[@]}"; do
    (
        cd "${stage}"
        shasum -a 256 "${entry}"
    ) >> "${temporary_checksums}"
done
zip_hash="$(shasum -a 256 "${temporary_zip}" | awk '{print $1}')"
printf '%s  %s\n' "${zip_hash}" "${package_name}.zip" >> "${temporary_checksums}"

verification_stage="${temporary_root}/verified"
mkdir "${verification_stage}"
unzip -q "${temporary_zip}" -d "${verification_stage}"
head -n "${#expected_entries[@]}" "${temporary_checksums}" | (
    cd "${verification_stage}"
    shasum -a 256 --check >/dev/null
)
tail -n 1 "${temporary_checksums}" | (
    cd "${temporary_root}"
    shasum -a 256 --check >/dev/null
)

assert_clean_head
if ! ln "${temporary_zip}" "${zip_path}"; then
    echo "Package ZIP appeared during validation; refusing to overwrite it." >&2
    exit 1
fi
if ! ln "${temporary_checksums}" "${checksums_path}"; then
    rm -f -- "${zip_path}"
    echo "Package checksum manifest appeared during validation; refusing partial publication." >&2
    exit 1
fi

echo "Created ${zip_path}"
echo "Created ${checksums_path}"
echo "Commit: ${initial_head}"
echo "Archive entries:"
sed 's/^/  /' "${temporary_root}/actual-entries.txt"
