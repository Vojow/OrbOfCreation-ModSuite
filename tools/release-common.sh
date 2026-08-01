#!/usr/bin/env bash

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

release_dll_sha_line() {
    local sha="$1"
    if [[ ! "${sha}" =~ ^[0-9a-f]{64}$ ]]; then
        echo "invalid lowercase DLL SHA-256: '${sha}'" >&2
        return 1
    fi
    printf 'OrbModSuite.dll SHA-256: %s\n' "${sha}"
}

extract_release_dll_sha() {
    local annotation="$1"
    local matches
    matches="$(
        printf '%s\n' "${annotation}" |
            sed -n 's/^OrbModSuite\.dll SHA-256: \([0-9a-f]\{64\}\)$/\1/p'
    )" || return 1
    if [[ -z "${matches}" || "${matches}" == *$'\n'* ]]; then
        echo "tag annotation must contain exactly one OrbModSuite.dll SHA-256 line" >&2
        return 1
    fi
    printf '%s\n' "${matches}"
}
