#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=release-common.sh
source "${repository_root}/tools/release-common.sh"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/orbmodsuite-release-helpers.XXXXXX")"
cleanup() {
    rm -rf -- "${temporary_root}"
}
trap cleanup EXIT INT TERM

fail() {
    echo "release-helper test failed: $*" >&2
    exit 1
}

write_version_fixture() {
    local root="$1"
    local version="$2"
    local numeric="${version%%[-+]*}"
    mkdir -p "${root}/src/Common"
    printf '%s\n' "${version}" >"${root}/VERSION"
    cat >"${root}/Directory.Build.props" <<EOF
<Project><PropertyGroup><SuiteVersion>${version}</SuiteVersion></PropertyGroup></Project>
EOF
    cat >"${root}/src/OrbModSuite.csproj" <<EOF
<Project><PropertyGroup>
  <Version>${version}</Version>
  <AssemblyVersion>${numeric}.0</AssemblyVersion>
  <FileVersion>${numeric}.0</FileVersion>
  <InformationalVersion>${version}</InformationalVersion>
</PropertyGroup></Project>
EOF
    cat >"${root}/src/Common/PluginIds.cs" <<EOF
public static class PluginIds
{
    public const string Version = "${numeric}";
    public const string ReleaseVersion = "${version}";
}
EOF
}

initialize_repository() {
    local root="$1"
    git -C "${root}" init -q -b main
    git -C "${root}" config user.name "Release helper test"
    git -C "${root}" config user.email "release-helper@example.invalid"
    git -C "${root}" remote add origin https://github.com/Example/fixture.git
}

if ! validate_release_version "0.5.0-beta.1" ||
    validate_release_version "01.5.0" ||
    validate_release_version "0.5.0-beta..1"; then
    fail "SemVer validation"
fi
if ! validate_stable_release_version "0.5.0" ||
    validate_stable_release_version "0.5.0-beta.1" ||
    validate_stable_release_version "0.5.0+build.1"; then
    fail "stable-version validation"
fi

fixture_root="${temporary_root}/fixture"
write_version_fixture "${fixture_root}" "0.5.0"
cat >"${fixture_root}/CHANGELOG.md" <<'EOF'
# Changelog

## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

- First release note.
- Second release note.

## Orb Of Creation ModSuite 0.4.0 Beta 1 — 2026-07-29

- Older note.
EOF

expected_section='## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

- First release note.
- Second release note.'
actual_section="$(extract_changelog_section "${fixture_root}/CHANGELOG.md" "0.5.0")"
[[ "${actual_section}" == "${expected_section}" ]] || fail "changelog extraction"
if extract_changelog_section "${fixture_root}/CHANGELOG.md" "9.9.9" >/dev/null 2>&1; then
    fail "missing changelog section was accepted"
fi
assert_no_unreleased_changelog "${fixture_root}/CHANGELOG.md" ||
    fail "released changelog was rejected"
printf '\n## Unreleased\n' >>"${fixture_root}/CHANGELOG.md"
if assert_no_unreleased_changelog "${fixture_root}/CHANGELOG.md" >/dev/null 2>&1; then
    fail "Unreleased changelog section was accepted"
fi
sed -i.bak '$d' "${fixture_root}/CHANGELOG.md"
rm "${fixture_root}/CHANGELOG.md.bak"

version_consistency_check "${fixture_root}" "0.5.0" ||
    fail "matching version fixture"
if version_consistency_check "${fixture_root}" "0.6.0" >/dev/null 2>&1; then
    fail "mismatched version fixture was accepted"
fi

bootstrap_root="${temporary_root}/bootstrap"
mkdir -p "${bootstrap_root}"
initialize_repository "${bootstrap_root}"
cat >"${bootstrap_root}/CHANGELOG.md" <<'EOF'
# Changelog

## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

- Released.
EOF
git -C "${bootstrap_root}" add CHANGELOG.md
git -C "${bootstrap_root}" commit -q -m "initial release"
git -C "${bootstrap_root}" tag -a suite-v0.5.0 -m "0.5.0"
bootstrap_base="$(git -C "${bootstrap_root}" rev-parse HEAD)"
printf '0.5.0\n' >"${bootstrap_root}/VERSION"
git -C "${bootstrap_root}" add VERSION
git -C "${bootstrap_root}" commit -q -m "build: record last release"
bootstrap_head="$(git -C "${bootstrap_root}" rev-parse HEAD)"
derived_beta_tag="$(derive_beta_tag "${bootstrap_root}" "0.5.0" "${bootstrap_head}")"
[[ "${derived_beta_tag}" == "suite-v0.5.0+main.1" ]] ||
    fail "git-describe beta tag derivation returned ${derived_beta_tag}"
ORB_RELEASE_POLICY_ROOT="${bootstrap_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${bootstrap_base}" "${bootstrap_head}" >/dev/null ||
    fail "valid VERSION bootstrap was rejected for a pull request"
ORB_RELEASE_POLICY_ROOT="${bootstrap_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    push "${bootstrap_base}" "${bootstrap_head}" >/dev/null ||
    fail "valid VERSION bootstrap was rejected for a push"

policy_root="${temporary_root}/policy"
mkdir -p "${policy_root}"
initialize_repository "${policy_root}"
printf '0.5.0\n' >"${policy_root}/VERSION"
cat >"${policy_root}/CHANGELOG.md" <<'EOF'
# Changelog

## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

- Released.
EOF
git -C "${policy_root}" add VERSION CHANGELOG.md
git -C "${policy_root}" commit -q -m "initial release"
policy_base="$(git -C "${policy_root}" rev-parse HEAD)"
printf '\nOrdinary edit.\n' >>"${policy_root}/CHANGELOG.md"
git -C "${policy_root}" add CHANGELOG.md
git -C "${policy_root}" commit -q -m "docs: edit released notes"
policy_bad_head="$(git -C "${policy_root}" rev-parse HEAD)"
if ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    pull-request "${policy_base}" "${policy_bad_head}" >/dev/null 2>&1; then
    fail "pull request changelog edit was accepted"
fi
if ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    push "${policy_base}" "${policy_bad_head}" >/dev/null 2>&1; then
    fail "non-release changelog commit was accepted"
fi
git -C "${policy_root}" switch -q -c release-case "${policy_base}"
printf '0.6.0\n' >"${policy_root}/VERSION"
sed 's/0\.5\.0/0.6.0/' "${policy_root}/CHANGELOG.md" >"${policy_root}/CHANGELOG.md.next"
mv "${policy_root}/CHANGELOG.md.next" "${policy_root}/CHANGELOG.md"
git -C "${policy_root}" add VERSION CHANGELOG.md
git -C "${policy_root}" commit -q -m "release: promote 0.6.0"
policy_release_head="$(git -C "${policy_root}" rev-parse HEAD)"
ORB_RELEASE_POLICY_ROOT="${policy_root}" \
    "${repository_root}/tools/check-release-policy.sh" \
    push "${policy_base}" "${policy_release_head}" >/dev/null ||
    fail "release-owned version and changelog edit was rejected"

promote_root="${temporary_root}/promote"
mkdir -p "${promote_root}"
initialize_repository "${promote_root}"
write_version_fixture "${promote_root}" "0.5.0"
cat >"${promote_root}/CHANGELOG.md" <<'EOF'
# Changelog

## Orb Of Creation ModSuite 0.5.0 — 2026-08-01

- Released.
EOF
git -C "${promote_root}" add .
git -C "${promote_root}" commit -q -m "initial release"
git -C "${promote_root}" tag -a suite-v0.5.0 -m "0.5.0"
printf 'feature\n' >"${promote_root}/feature.txt"
git -C "${promote_root}" add feature.txt
git -C "${promote_root}" commit -q -m "feature: add fixture" -m "Fallback body"
promote_head="$(git -C "${promote_root}" rev-parse HEAD)"

fake_bin="${temporary_root}/fake-bin"
mkdir -p "${fake_bin}"
cat >"${fake_bin}/gh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == "repo" && "$2" == "view" ]]; then
    echo "Example/fixture"
    exit 0
fi
if [[ "$1" == "api" ]]; then
    cat <<'JSON'
[{"number":42,"title":"Fixture feature","body":"Fixture pull request body.","merged_at":"2026-08-01T00:00:00Z"}]
JSON
    exit 0
fi
echo "unexpected gh arguments: $*" >&2
exit 1
EOF
chmod +x "${fake_bin}/gh"

PATH="${fake_bin}:${PATH}" \
ORB_PROMOTE_REPOSITORY_ROOT="${promote_root}" \
ORB_PROMOTE_DATE="2026-08-02" \
    "${repository_root}/script/promote" 0.6.0 >/dev/null ||
    fail "promotion drafting"
[[ "$(cat "${promote_root}/VERSION")" == "0.6.0" ]] ||
    fail "promotion did not update VERSION"
version_consistency_check "${promote_root}" "0.6.0" ||
    fail "promotion produced inconsistent version surfaces"
grep -q '^## Orb Of Creation ModSuite 0.6.0 — 2026-08-02$' \
    "${promote_root}/CHANGELOG.md" || fail "promotion heading"
grep -q '^### #42 Fixture feature$' "${promote_root}/CHANGELOG.md" ||
    fail "promotion PR title"
grep -q '^Fixture pull request body\.$' "${promote_root}/CHANGELOG.md" ||
    fail "promotion PR body"
[[ "$(git -C "${promote_root}" rev-parse HEAD)" == "${promote_head}" ]] ||
    fail "promotion changed HEAD"
if git -C "${promote_root}" rev-parse --verify refs/tags/suite-v0.6.0 >/dev/null 2>&1; then
    fail "promotion created a tag"
fi

echo "release-helpers=pass policy-red-green=pass promotion-draft=pass"
