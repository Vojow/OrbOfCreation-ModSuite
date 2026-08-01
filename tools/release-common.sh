#!/usr/bin/env bash

validate_release_version() {
    [[ "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]]
}

validate_stable_release_version() {
    validate_release_version "$1" && [[ "$1" != *-* && "$1" != *+* ]]
}

numeric_version() {
    printf '%s\n' "${1%%[-+]*}"
}

read_released_version() {
    local version_path="$1"
    if [[ ! -f "${version_path}" ]]; then
        echo "released-version file is missing: ${version_path}" >&2
        return 1
    fi

    local line_count
    line_count="$(awk 'END { print NR + 0 }' "${version_path}")" || return 1
    local version
    version="$(sed -n '1p' "${version_path}")" || return 1
    if [[ "${line_count}" -ne 1 ]] || ! validate_stable_release_version "${version}"; then
        echo "${version_path} must contain exactly one stable SemVer version" >&2
        return 1
    fi
    printf '%s\n' "${version}"
}

read_released_version_at_revision() {
    local repository_root="$1"
    local revision="$2"
    local contents
    contents="$(git -C "${repository_root}" show "${revision}:VERSION")" || {
        echo "could not read VERSION at ${revision}" >&2
        return 1
    }
    if [[ "${contents}" == *$'\n'* ]] ||
        ! validate_stable_release_version "${contents}"; then
        echo "VERSION at ${revision} must contain exactly one stable SemVer version" >&2
        return 1
    fi
    printf '%s\n' "${contents}"
}

release_tag_for_version() {
    local version="$1"
    if ! validate_stable_release_version "${version}"; then
        echo "cannot form a release tag from invalid stable version '${version}'" >&2
        return 1
    fi
    printf 'suite-v%s\n' "${version}"
}

derive_beta_tag() {
    local repository_root="$1"
    local version="$2"
    local revision="$3"
    local release_tag
    release_tag="$(release_tag_for_version "${version}")" || return 1

    local description
    description="$(
        git -C "${repository_root}" describe --long --match "${release_tag}" "${revision}"
    )" || {
        echo "could not describe ${revision} from ${release_tag}" >&2
        return 1
    }
    local prefix="${release_tag}-"
    if [[ "${description}" != "${prefix}"* ]]; then
        echo "unexpected git describe result for ${release_tag}: ${description}" >&2
        return 1
    fi
    local suffix="${description#${prefix}}"
    local commit_count="${suffix%%-*}"
    local abbreviated_commit="${suffix#*-}"
    if [[ ! "${commit_count}" =~ ^[0-9]+$ ||
        ! "${abbreviated_commit}" =~ ^g[0-9a-f]+$ ]]; then
        echo "unexpected git describe result for ${release_tag}: ${description}" >&2
        return 1
    fi
    printf '%s+main.%s\n' "${release_tag}" "${commit_count}"
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

assert_no_unreleased_changelog() {
    local changelog_path="$1"
    if grep -Eiq '^##[[:space:]]+Unreleased([[:space:]]|$)' "${changelog_path}"; then
        echo "${changelog_path} must not contain an Unreleased section" >&2
        return 1
    fi
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

    if ! actual="$(read_released_version "${root}/VERSION")"; then
        failed=1
    elif ! check_version_value "VERSION" "${actual}" "${expected}"; then
        failed=1
    fi

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

sha256_file() {
    local path="$1"
    local digest
    if command -v shasum >/dev/null 2>&1; then
        digest="$(shasum -a 256 "${path}" | awk '{print $1}')" || return 1
    elif command -v sha256sum >/dev/null 2>&1; then
        digest="$(sha256sum "${path}" | awk '{print $1}')" || return 1
    else
        echo "required SHA-256 command is unavailable: shasum or sha256sum" >&2
        return 1
    fi
    if [[ ! "${digest}" =~ ^[0-9A-Fa-f]{64}$ ]]; then
        echo "SHA-256 command returned an invalid digest for ${path}" >&2
        return 1
    fi
    printf '%s\n' "${digest}" | tr 'A-F' 'a-f'
}
