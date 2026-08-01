#!/usr/bin/env bash

validate_release_version() {
    [[ "$1" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]]
}

validate_stable_release_version() {
    validate_release_version "$1" && [[ "$1" != *-* && "$1" != *+* ]]
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

stable_version_is_greater() {
    local candidate="$1"
    local baseline="$2"
    if ! validate_stable_release_version "${candidate}" ||
        ! validate_stable_release_version "${baseline}"; then
        return 1
    fi

    local candidate_major candidate_minor candidate_patch
    local baseline_major baseline_minor baseline_patch
    IFS=. read -r candidate_major candidate_minor candidate_patch <<<"${candidate}"
    IFS=. read -r baseline_major baseline_minor baseline_patch <<<"${baseline}"
    if ((candidate_major != baseline_major)); then
        ((candidate_major > baseline_major))
        return
    fi
    if ((candidate_minor != baseline_minor)); then
        ((candidate_minor > baseline_minor))
        return
    fi
    ((candidate_patch > baseline_patch))
}

newest_stable_release_version() {
    local repository_root="$1"
    local newest=""
    local tag
    while IFS= read -r tag; do
        [[ -n "${tag}" ]] || continue
        local version="${tag#suite-v}"
        if ! validate_stable_release_version "${version}"; then
            continue
        fi
        if [[ -z "${newest}" ]] || stable_version_is_greater "${version}" "${newest}"; then
            newest="${version}"
        fi
    done < <(git -C "${repository_root}" tag --list 'suite-v*')

    if [[ -z "${newest}" ]]; then
        echo "repository contains no stable suite-v tag" >&2
        return 1
    fi
    printf '%s\n' "${newest}"
}

publication_kind() {
    local repository_root="$1"
    local version="$2"
    local release_tag
    release_tag="$(release_tag_for_version "${version}")" || return 1
    if git -C "${repository_root}" show-ref --verify --quiet "refs/tags/${release_tag}"; then
        printf 'beta\n'
    else
        printf 'release\n'
    fi
}

derive_beta_tag() {
    local repository_root="$1"
    local version="$2"
    local revision="$3"
    local beta_prefix
    beta_prefix="$(release_tag_for_version "${version}")" || return 1

    local description
    description="$(
        git -C "${repository_root}" describe --tags --long \
            --match 'suite-v*' --exclude '*+main.*' --exclude 'suite-v*-*' \
            "${revision}"
    )" || {
        echo "could not describe ${revision} from an existing stable suite-v tag" >&2
        return 1
    }
    local abbreviated_commit="${description##*-}"
    local tag_and_count="${description%-*}"
    local commit_count="${tag_and_count##*-}"
    local described_tag="${tag_and_count%-*}"
    local described_version="${described_tag#suite-v}"
    if [[ ! "${commit_count}" =~ ^[0-9]+$ ||
        ! "${abbreviated_commit}" =~ ^g[0-9a-f]+$ ]] ||
        ! validate_stable_release_version "${described_version}"; then
        echo "unexpected stable git describe result: ${description}" >&2
        return 1
    fi
    printf '%s+main.%s\n' "${beta_prefix}" "${commit_count}"
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
