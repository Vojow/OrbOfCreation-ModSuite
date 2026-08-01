#!/usr/bin/env bash
set -euo pipefail

script_repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository_root="${ORB_RELEASE_POLICY_ROOT:-${script_repository_root}}"
# shellcheck source=release-common.sh
source "${script_repository_root}/tools/release-common.sh"

usage() {
    echo "Usage: tools/check-release-policy.sh <pull-request|push> <base-revision> <head-revision> [pull-request-title]" >&2
}

fail() {
    echo "Release policy failed: $*" >&2
    exit 1
}

if [[ "$#" -lt 3 || "$#" -gt 4 ]]; then
    usage
    exit 2
fi

mode="$1"
base_revision="$2"
head_revision="$3"
pull_request_title="${4:-}"
if [[ "${mode}" != "pull-request" && "${mode}" != "push" ]]; then
    usage
    exit 2
fi
if [[ "${mode}" == "pull-request" && "$#" -ne 4 ]]; then
    usage
    exit 2
fi

git -C "${repository_root}" rev-parse --verify "${base_revision}^{commit}" >/dev/null ||
    fail "base revision is not a commit: ${base_revision}"
git -C "${repository_root}" rev-parse --verify "${head_revision}^{commit}" >/dev/null ||
    fail "head revision is not a commit: ${head_revision}"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/orbmodsuite-release-policy.XXXXXX")"
cleanup() {
    rm -rf -- "${temporary_root}"
}
trap cleanup EXIT INT TERM
head_changelog="${temporary_root}/CHANGELOG.md"
git -C "${repository_root}" show "${head_revision}:CHANGELOG.md" >"${head_changelog}" ||
    fail "could not read CHANGELOG.md at ${head_revision}"
assert_no_unreleased_changelog "${head_changelog}" || exit 1

changed_in_range="$(
    git -C "${repository_root}" diff --name-only \
        "${base_revision}" "${head_revision}" -- CHANGELOG.md VERSION
)"

path_changed() {
    local expected="$1"
    grep -Fxq "${expected}" <<<"${changed_in_range}"
}

validate_new_version() {
    local new_version
    new_version="$(read_released_version_at_revision "${repository_root}" "${head_revision}")" ||
        fail "new VERSION is not valid stable SemVer"

    local newest_version
    newest_version="$(newest_stable_release_version "${repository_root}")" ||
        fail "could not find the newest stable release tag"
    if ! stable_version_is_greater "${new_version}" "${newest_version}"; then
        fail "VERSION ${new_version} must be strictly greater than newest stable tag suite-v${newest_version}"
    fi
    if ! path_changed CHANGELOG.md; then
        fail "a VERSION promotion must add its curated CHANGELOG.md section in the same release PR"
    fi
    extract_changelog_section "${head_changelog}" "${new_version}" >/dev/null ||
        fail "CHANGELOG.md must contain exactly one section for VERSION ${new_version}"
}

if [[ "${mode}" == "pull-request" ]]; then
    if [[ -z "${changed_in_range}" ]]; then
        echo "Release policy: pull request does not modify VERSION or CHANGELOG.md."
        exit 0
    fi
    if [[ "${pull_request_title}" != release:* ]]; then
        fail "VERSION and CHANGELOG.md changes require a pull request title starting with 'release:'"
    fi
    if path_changed VERSION; then
        validate_new_version
    fi
    echo "Release policy: release PR owns its VERSION and CHANGELOG.md changes."
    exit 0
fi

if [[ -z "${changed_in_range}" ]]; then
    echo "Release policy: push does not modify VERSION or CHANGELOG.md."
    exit 0
fi

empty_tree="$(git -C "${repository_root}" hash-object -t tree /dev/null)"
while IFS= read -r commit; do
    [[ -n "${commit}" ]] || continue
    parent="${empty_tree}"
    if git -C "${repository_root}" rev-parse --verify "${commit}^1" >/dev/null 2>&1; then
        parent="$(git -C "${repository_root}" rev-parse "${commit}^1")"
    fi
    changed_paths="$(
        git -C "${repository_root}" diff --name-only \
            "${parent}" "${commit}" -- CHANGELOG.md VERSION
    )"
    [[ -n "${changed_paths}" ]] || continue

    subject="$(git -C "${repository_root}" show -s --format=%s "${commit}")"
    if [[ "${subject}" != release:* ]]; then
        fail "${commit} changes ${changed_paths//$'\n'/, } but its subject does not start with 'release:'"
    fi
done < <(git -C "${repository_root}" rev-list --reverse --first-parent \
    "${base_revision}..${head_revision}")

echo "Release policy: every VERSION or CHANGELOG.md change is release-commit-owned."
