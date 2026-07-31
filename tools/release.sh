#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
release_temporary_root=""

usage() {
    echo "Usage: tools/release.sh <version> [--dry-run]" >&2
    echo "       tools/release.sh --dry-run <version>" >&2
}

fail() {
    echo "Release check failed: $*" >&2
    exit 1
}

validate_release_version() {
    [[ "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]]
}

numeric_version() {
    printf '%s\n' "${1%%[-+]*}"
}

is_prerelease() {
    [[ "$1" == *-* ]]
}

extract_changelog_section() {
    local changelog_path="$1"
    local version="$2"
    local heading_prefix="## Orb Of Creation ModSuite ${version} — "
    local heading_count
    heading_count="$(
        awk -v prefix="${heading_prefix}" \
            '$0 ~ /^## / && index($0, prefix) == 1 { count++ } END { print count + 0 }' \
            "${changelog_path}"
    )" || return 1
    if [[ "${heading_count}" -ne 1 ]]; then
        echo "expected one changelog heading beginning '${heading_prefix}', found ${heading_count}" >&2
        return 1
    fi

    awk -v prefix="${heading_prefix}" '
        $0 ~ /^## / {
            if (capturing) exit
            if (index($0, prefix) == 1) capturing = 1
        }
        capturing { print }
    ' "${changelog_path}"
}

read_xml_value() {
    local element="$1"
    local path="$2"
    local value
    value="$(sed -n "s:.*<${element}>\\([^<]*\\)</${element}>.*:\\1:p" "${path}")" ||
        return 1
    if [[ -z "${value}" || "${value}" == *$'\n'* ]]; then
        return 1
    fi
    printf '%s\n' "${value}"
}

read_plugin_constant() {
    local constant_name="$1"
    local path="$2"
    local value
    value="$(
        sed -n \
            "s/.*public const string ${constant_name} = \"\\([^\"]*\\)\";.*/\\1/p" \
            "${path}"
    )" || return 1
    if [[ -z "${value}" || "${value}" == *$'\n'* ]]; then
        return 1
    fi
    printf '%s\n' "${value}"
}

check_version_value() {
    local label="$1"
    local actual="$2"
    local expected="$3"
    if [[ "${actual}" == "${expected}" ]]; then
        return 0
    fi
    echo "${label} is '${actual}', expected '${expected}'" >&2
    return 1
}

version_consistency_check() {
    local root="$1"
    local expected="$2"
    local expected_numeric
    expected_numeric="$(numeric_version "${expected}")"
    local expected_assembly="${expected_numeric}.0"
    local failed=0
    local actual

    if ! actual="$(read_xml_value SuiteVersion "${root}/Directory.Build.props")"; then
        echo "could not read one SuiteVersion from Directory.Build.props" >&2
        failed=1
    elif ! check_version_value "Directory.Build.props SuiteVersion" "${actual}" "${expected}"; then
        failed=1
    fi

    if ! actual="$(read_xml_value Version "${root}/src/OrbModSuite.csproj")"; then
        echo "could not read one Version from src/OrbModSuite.csproj" >&2
        failed=1
    elif ! check_version_value "src/OrbModSuite.csproj Version" "${actual}" "${expected}"; then
        failed=1
    fi

    if ! actual="$(read_xml_value InformationalVersion "${root}/src/OrbModSuite.csproj")"; then
        echo "could not read one InformationalVersion from src/OrbModSuite.csproj" >&2
        failed=1
    elif ! check_version_value \
        "src/OrbModSuite.csproj InformationalVersion" "${actual}" "${expected}"; then
        failed=1
    fi

    if ! actual="$(read_xml_value AssemblyVersion "${root}/src/OrbModSuite.csproj")"; then
        echo "could not read one AssemblyVersion from src/OrbModSuite.csproj" >&2
        failed=1
    elif ! check_version_value \
        "src/OrbModSuite.csproj AssemblyVersion" "${actual}" "${expected_assembly}"; then
        failed=1
    fi

    if ! actual="$(read_xml_value FileVersion "${root}/src/OrbModSuite.csproj")"; then
        echo "could not read one FileVersion from src/OrbModSuite.csproj" >&2
        failed=1
    elif ! check_version_value \
        "src/OrbModSuite.csproj FileVersion" "${actual}" "${expected_assembly}"; then
        failed=1
    fi

    if ! actual="$(read_plugin_constant ReleaseVersion "${root}/src/Common/PluginIds.cs")"; then
        echo "could not read PluginIds.ReleaseVersion" >&2
        failed=1
    elif ! check_version_value "PluginIds.ReleaseVersion" "${actual}" "${expected}"; then
        failed=1
    fi

    if ! actual="$(read_plugin_constant Version "${root}/src/Common/PluginIds.cs")"; then
        echo "could not read PluginIds.Version" >&2
        failed=1
    elif ! check_version_value "PluginIds.Version" "${actual}" "${expected_numeric}"; then
        failed=1
    fi

    return "${failed}"
}

resolve_repo_target() {
    local root="$1"
    local origin_url
    origin_url="$(git -C "${root}" remote get-url origin)" || {
        echo "could not read the checkout's origin URL" >&2
        return 1
    }
    if [[ -z "${origin_url}" ]]; then
        echo "the checkout's origin URL is empty" >&2
        return 1
    fi

    local target
    target="$(gh repo view "${origin_url}" --json nameWithOwner --jq .nameWithOwner)" || {
        echo "gh repo view could not resolve origin '${origin_url}'" >&2
        return 1
    }
    if [[ -z "${target}" || "${target}" != */* ]]; then
        echo "gh repo view returned an invalid repository target: '${target}'" >&2
        return 1
    fi
    printf '%s\n' "${target}"
}

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
        return 1
    fi

    local candidate
    local normalized_candidate
    for candidate in \
        'C:\Program Files (x86)\Steam\steamapps\common\Orb of Creation' \
        'C:\Program Files\Steam\steamapps\common\Orb of Creation' \
        "${HOME}/Library/Application Support/Steam/steamapps/common/Orb of Creation" \
        "${HOME}/.local/share/Steam/steamapps/common/Orb of Creation" \
        "${HOME}/.steam/steam/steamapps/common/Orb of Creation"; do
        normalized_candidate="$(normalize_directory "${candidate}" || true)"
        if [[ -n "${normalized_candidate}" && -d "${normalized_candidate}/BepInEx" ]]; then
            printf '%s\n' "${normalized_candidate}"
            return
        fi
    done

    echo "Could not locate Orb of Creation. Set OOC_GAME_DIR to its installation root." >&2
    return 1
}

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
    # Native Windows process inspection is authoritative whenever it exists. Git-for-Windows may
    # also provide pgrep, but its process view cannot be trusted to include native processes.
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

sha256_file() {
    local path="$1"
    local digest
    if command -v shasum >/dev/null 2>&1; then
        digest="$(shasum -a 256 "${path}" | awk '{print $1}')" || return 1
    else
        digest="$(sha256sum "${path}" | awk '{print $1}')" || return 1
    fi
    if [[ ! "${digest}" =~ ^[0-9A-Fa-f]{64}$ ]]; then
        echo "SHA-256 command returned an invalid digest for ${path}" >&2
        return 1
    fi
    printf '%s\n' "${digest}"
}

assert_release_source_unchanged() {
    local root="$1"
    local expected_head="$2"
    local check_name="$3"
    local current_head
    current_head="$(git -C "${root}" rev-parse --verify HEAD)" ||
        fail "${check_name}: could not read HEAD"
    if [[ "${current_head}" != "${expected_head}" ]]; then
        fail "${check_name}: HEAD changed from ${expected_head} to ${current_head}"
    fi
    local tracked_status
    tracked_status="$(git -C "${root}" status --porcelain --untracked-files=no)" ||
        fail "${check_name}: could not inspect the tracked working tree"
    if [[ -n "${tracked_status}" ]]; then
        fail "${check_name}: tracked working tree changed after release validation began"
    fi
}

assert_tag_absent() {
    local root="$1"
    local tag="$2"
    local local_status=0
    git -C "${root}" show-ref --verify --quiet "refs/tags/${tag}" || local_status="$?"
    case "${local_status}" in
        0)
            fail "tag preflight: local tag ${tag} already exists"
            ;;
        1)
            ;;
        *)
            fail "tag preflight: could not inspect local tag ${tag}"
            ;;
    esac

    local remote_status=0
    git -C "${root}" ls-remote --exit-code --tags origin "refs/tags/${tag}" \
        >/dev/null 2>&1 || remote_status="$?"
    case "${remote_status}" in
        0)
            fail "tag preflight: origin already contains ${tag}"
            ;;
        2)
            ;;
        *)
            fail "tag preflight: could not verify whether origin contains ${tag}"
            ;;
    esac
}

main() {
    if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
        usage
        return 0
    fi

    local version=""
    local dry_run=0
    case "$#" in
        1)
            version="$1"
            ;;
        2)
            if [[ "$1" == "--dry-run" ]]; then
                dry_run=1
                version="$2"
            elif [[ "$2" == "--dry-run" ]]; then
                dry_run=1
                version="$1"
            else
                usage
                exit 2
            fi
            ;;
        *)
            usage
            exit 2
            ;;
    esac

    if ! validate_release_version "${version}"; then
        fail "version '${version}' is not valid SemVer"
    fi

    local command_name
    for command_name in git gh dotnet awk sed grep mktemp mkdir ln; do
        if ! command -v "${command_name}" >/dev/null 2>&1; then
            fail "required command is unavailable: ${command_name}"
        fi
    done
    if ! command -v shasum >/dev/null 2>&1 &&
        ! command -v sha256sum >/dev/null 2>&1; then
        fail "required SHA-256 command is unavailable: shasum or sha256sum"
    fi
    if is_windows_environment &&
        ! command -v powershell.exe >/dev/null 2>&1; then
        fail "native Windows process inspection is unavailable: powershell.exe"
    fi
    if ! command -v pgrep >/dev/null 2>&1 &&
        ! command -v powershell.exe >/dev/null 2>&1; then
        fail "required process inspection is unavailable: pgrep or powershell.exe"
    fi

    local dotnet_version
    dotnet_version="$(dotnet --version)" || fail "dotnet preflight could not read the SDK version"
    if [[ "${dotnet_version}" != 10.* ]]; then
        fail "dotnet 10 is required; found ${dotnet_version}"
    fi
    if ! gh auth status --active --hostname github.com >/dev/null 2>&1; then
        fail "gh authentication preflight failed for github.com"
    fi

    local initial_head
    initial_head="$(git -C "${repository_root}" rev-parse --verify HEAD)" ||
        fail "source preflight could not read HEAD"
    local tracked_status
    tracked_status="$(
        git -C "${repository_root}" status --porcelain --untracked-files=no
    )" || fail "source preflight could not inspect the tracked working tree"
    if [[ -n "${tracked_status}" ]]; then
        fail "source preflight requires a clean tracked working tree"
    fi

    local origin_url
    origin_url="$(git -C "${repository_root}" remote get-url origin)" ||
        fail "repository preflight could not read origin"
    local repo_target
    repo_target="$(resolve_repo_target "${repository_root}")" ||
        fail "repository preflight could not resolve origin through gh repo view"
    local tag="suite-v${version}"
    assert_tag_absent "${repository_root}" "${tag}"

    local changelog_section
    changelog_section="$(
        extract_changelog_section "${repository_root}/CHANGELOG.md" "${version}"
    )" || fail "changelog preflight did not find exactly one section for ${version}"
    local changelog_heading="${changelog_section%%$'\n'*}"
    local release_title="${changelog_heading#\#\# }"
    if [[ -z "${release_title}" || "${release_title}" == "${changelog_heading}" ]]; then
        fail "changelog preflight could not derive a release title"
    fi
    if ! version_consistency_check "${repository_root}" "${version}"; then
        fail "suite version metadata is inconsistent with ${version}"
    fi

    release_temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/orbmodsuite-release.XXXXXX")" ||
        fail "temporary-directory preflight failed"
    cleanup() {
        if [[ -n "${release_temporary_root}" && -d "${release_temporary_root}" ]]; then
            rm -rf -- "${release_temporary_root}"
        fi
    }
    trap cleanup EXIT INT TERM

    local notes_file="${release_temporary_root}/release-notes.md"
    printf '%s\n' "${changelog_section}" | sed '1d' >"${notes_file}" ||
        fail "changelog preflight could not extract release notes"
    if ! grep -q '[^[:space:]]' "${notes_file}"; then
        fail "changelog preflight found no release notes for ${version}"
    fi

    local game_root
    game_root="$(cd "$(resolve_game_root)" && pwd)" ||
        fail "game-directory preflight could not locate Orb of Creation"
    local plugins_root="${game_root}/BepInEx/plugins"
    local bepinex_core="${game_root}/BepInEx/core"
    if [[ ! -d "${plugins_root}" || ! -f "${bepinex_core}/BepInEx.dll" ||
        ! -f "${bepinex_core}/0Harmony.dll" ]]; then
        fail "game-directory preflight found an incomplete BepInEx 5 installation at ${game_root}"
    fi
    if game_is_running; then
        fail "game-process preflight found Orb of Creation running"
    fi

    local windows_managed="${game_root}/Orb Of Creation_Data/Managed"
    local mac_managed="${game_root}/Orb Of Creation.app/Contents/Resources/Data/Managed"
    local reference_root=""
    if [[ -d "${windows_managed}" ]]; then
        reference_root="${game_root}"
    elif [[ -d "${mac_managed}" ]]; then
        reference_root="${release_temporary_root}/game-reference"
        mkdir -p "${reference_root}/Orb Of Creation_Data" ||
            fail "game-reference preflight could not create a temporary reference root"
        ln -s "${mac_managed}" "${reference_root}/Orb Of Creation_Data/Managed" ||
            fail "game-reference preflight could not link the managed directory"
        ln -s "${game_root}/BepInEx" "${reference_root}/BepInEx" ||
            fail "game-reference preflight could not link BepInEx"
    else
        fail "game-directory preflight found no supported managed-assembly directory"
    fi

    local required_reference
    for required_reference in \
        "${reference_root}/Orb Of Creation_Data/Managed/Assembly-CSharp.dll" \
        "${reference_root}/Orb Of Creation_Data/Managed/Assembly-CSharp-firstpass.dll" \
        "${reference_root}/Orb Of Creation_Data/Managed/UnityEngine.dll" \
        "${reference_root}/Orb Of Creation_Data/Managed/UnityEngine.CoreModule.dll" \
        "${reference_root}/BepInEx/core/BepInEx.dll" \
        "${reference_root}/BepInEx/core/0Harmony.dll"; do
        if [[ ! -f "${required_reference}" ]]; then
            fail "game-reference preflight is missing ${required_reference}"
        fi
    done

    echo "Release source and target:"
    echo "  Commit: ${initial_head}"
    echo "  Origin: ${origin_url}"
    echo "  GitHub repository: ${repo_target}"
    echo "  Game references: ${game_root}"
    echo

    echo "Running source build with game stubs..."
    if ! dotnet build "${repository_root}/src/OrbModSuite.csproj" \
        --configuration Debug \
        --disable-build-servers \
        -m:1 \
        --no-incremental \
        -p:UseGameStubs=true; then
        fail "source-build gate failed"
    fi

    echo "Running ordinary test-project build with game stubs..."
    if ! dotnet build "${repository_root}/tests/OrbModding.Tests/OrbModding.Tests.csproj" \
        --configuration Debug \
        --disable-build-servers \
        -m:1 \
        --no-incremental \
        -p:UseGameStubs=true; then
        fail "ordinary test-project build gate failed"
    fi

    echo "Running the complete portable and profile gate..."
    if ! ORB_TEST_ATTEMPTS=1 "${repository_root}/script/test"; then
        fail "portable/profile gate failed"
    fi

    echo "Restoring installed-game contracts..."
    if ! OOC_GAME_DIR="${reference_root}" dotnet restore \
        "${repository_root}/tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj" \
        --force-evaluate \
        --disable-build-servers; then
        fail "installed-game contract restore failed"
    fi
    echo "Running installed-game contracts..."
    if ! OOC_GAME_DIR="${reference_root}" dotnet test \
        "${repository_root}/tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj" \
        --configuration Release \
        --no-restore; then
        fail "installed-game contract gate failed"
    fi

    assert_release_source_unchanged \
        "${repository_root}" "${initial_head}" "post-gate source check"

    echo "Restoring the release build..."
    if ! OOC_GAME_DIR="${reference_root}" dotnet restore \
        "${repository_root}/src/OrbModSuite.csproj" \
        --force-evaluate \
        --disable-build-servers \
        -p:EnableServiceCycleProfiler=false; then
        fail "release-build restore failed"
    fi
    echo "Building the release-mode suite..."
    if ! OOC_GAME_DIR="${reference_root}" dotnet build \
        "${repository_root}/src/OrbModSuite.csproj" \
        --configuration Release \
        --disable-build-servers \
        -m:1 \
        --no-incremental \
        -p:EnableServiceCycleProfiler=false; then
        fail "release build failed"
    fi

    local suite_dll="${repository_root}/src/bin/Release/netstandard2.1/OrbModSuite.dll"
    if [[ ! -f "${suite_dll}" ]]; then
        fail "release output is missing: ${suite_dll}"
    fi
    if grep -a -q "AutomataServiceCycleProfileController" "${suite_dll}" ||
        grep -a -q "ServiceCycleProfileRuntimeSession" "${suite_dll}"; then
        fail "release output unexpectedly contains ServiceCycle profiling components"
    fi

    local dll_sha
    dll_sha="$(sha256_file "${suite_dll}")" ||
        fail "DLL SHA-256 calculation failed"
    assert_release_source_unchanged \
        "${repository_root}" "${initial_head}" "post-build source check"

    local release_kind="release"
    if is_prerelease "${version}"; then
        release_kind="prerelease"
    fi

    echo
    echo "Validated publish plan:"
    echo "  Repository: ${repo_target}"
    echo "  Commit: ${initial_head}"
    echo "  Annotated tag: ${tag}"
    echo "  Title: ${release_title}"
    echo "  Kind: ${release_kind}"
    echo "  Asset: ${suite_dll}"
    echo "  DLL SHA-256: ${dll_sha}"
    echo "  Notes: CHANGELOG.md section for ${version}"

    if [[ "${dry_run}" -eq 1 ]]; then
        echo
        echo "Dry run complete. No tag, push, or GitHub release was created."
        echo "A real run would create annotated tag ${tag} at ${initial_head}, push that tag"
        echo "to origin (${repo_target}), and create the ${release_kind} with OrbModSuite.dll attached."
        return 0
    fi

    echo
    echo "This will publish to ${repo_target}."
    printf "Retype the version (%s) to continue: " "${version}"
    local confirmed_version
    if ! IFS= read -r confirmed_version; then
        fail "confirmation input was unavailable"
    fi
    if [[ "${confirmed_version}" != "${version}" ]]; then
        fail "confirmation did not exactly match ${version}"
    fi

    assert_release_source_unchanged \
        "${repository_root}" "${initial_head}" "pre-publish source check"
    assert_tag_absent "${repository_root}" "${tag}"
    if game_is_running; then
        fail "pre-publish process check found Orb of Creation running"
    fi

    if ! git -C "${repository_root}" tag -a "${tag}" "${initial_head}" -m "${release_title}"; then
        fail "publish step could not create annotated tag ${tag}"
    fi
    if ! git -C "${repository_root}" push origin "refs/tags/${tag}"; then
        fail "publish step could not push ${tag} to origin; the local annotated tag remains"
    fi

    local release_arguments=(
        release create "${tag}" "${suite_dll}"
        --repo "${repo_target}"
        --verify-tag
        --target "${initial_head}"
        --title "${release_title}"
        --notes-file "${notes_file}"
    )
    if is_prerelease "${version}"; then
        release_arguments+=(--prerelease)
    fi
    if ! gh "${release_arguments[@]}"; then
        fail "publish step could not create the GitHub release; the pushed tag remains"
    fi

    echo "Published ${tag} from ${initial_head} to ${repo_target}."
    echo "OrbModSuite.dll SHA-256: ${dll_sha}"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
