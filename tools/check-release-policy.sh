#!/usr/bin/env bash
set -euo pipefail

script_repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repository_root="${ORB_RELEASE_POLICY_ROOT:-${script_repository_root}}"
# shellcheck source=release-common.sh
source "${script_repository_root}/tools/release-common.sh"

usage() {
    echo "Usage: tools/check-release-policy.sh <pull-request|push> <base-revision> <head-revision>" >&2
}

fail() {
    echo "Release policy failed: $*" >&2
    exit 1
}

if [[ "$#" -ne 3 ]]; then
    usage
    exit 2
fi

mode="$1"
base_revision="$2"
head_revision="$3"
if [[ "${mode}" != "pull-request" && "${mode}" != "push" ]]; then
    usage
    exit 2
fi

git -C "${repository_root}" rev-parse --verify "${base_revision}^{commit}" >/dev/null ||
    fail "base revision is not a commit: ${base_revision}"
git -C "${repository_root}" rev-parse --verify "${head_revision}^{commit}" >/dev/null ||
    fail "head revision is not a commit: ${head_revision}"
assert_no_unreleased_changelog "${repository_root}/CHANGELOG.md" || exit 1

bootstrap_version_change_is_valid() {
    local before_revision="$1"
    local after_revision="$2"
    local changed_paths="$3"
    if [[ "${changed_paths}" != "VERSION" ]]; then
        return 1
    fi
    if git -C "${repository_root}" cat-file -e "${before_revision}:VERSION" 2>/dev/null; then
        return 1
    fi

    local version
    version="$(read_released_version_at_revision "${repository_root}" "${after_revision}")" ||
        return 1
    local release_tag
    release_tag="$(release_tag_for_version "${version}")" || return 1
    git -C "${repository_root}" rev-parse --verify "refs/tags/${release_tag}^{commit}" >/dev/null ||
        return 1
    git -C "${repository_root}" merge-base --is-ancestor \
        "refs/tags/${release_tag}^{commit}" "${after_revision}"
}

changed_in_range="$(
    git -C "${repository_root}" diff --name-only \
        "${base_revision}" "${head_revision}" -- CHANGELOG.md VERSION
)"

if [[ "${mode}" == "pull-request" ]]; then
    if [[ -z "${changed_in_range}" ]]; then
        echo "Release policy: pull request does not modify VERSION or CHANGELOG.md."
        exit 0
    fi
    if bootstrap_version_change_is_valid \
        "${base_revision}" "${head_revision}" "${changed_in_range}"; then
        echo "Release policy: accepted one-time VERSION bootstrap for an existing release tag."
        exit 0
    fi
    fail "pull requests must not modify VERSION or CHANGELOG.md; use script/promote directly on main"
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

    if bootstrap_version_change_is_valid "${parent}" "${commit}" "${changed_paths}"; then
        echo "Release policy: accepted one-time VERSION bootstrap in ${commit}."
        continue
    fi

    subject="$(git -C "${repository_root}" show -s --format=%s "${commit}")"
    if [[ "${subject}" != release:* ]]; then
        fail "${commit} changes ${changed_paths//$'\n'/, } but its subject does not start with 'release:'"
    fi
done < <(git -C "${repository_root}" rev-list --reverse --first-parent \
    "${base_revision}..${head_revision}")

echo "Release policy: every VERSION or CHANGELOG.md change is promotion-owned."
