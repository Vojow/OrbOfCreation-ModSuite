#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=release.sh
source "${repository_root}/tools/release.sh"

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/orbmodsuite-release-helpers.XXXXXX")"
cleanup() {
    rm -rf -- "${temporary_root}"
}
trap cleanup EXIT INT TERM

if ! validate_release_version "0.5.0-beta.1" ||
    validate_release_version "01.5.0" ||
    validate_release_version "0.5.0-beta..1"; then
    echo "release-version validation failed" >&2
    exit 1
fi

fixture_root="${temporary_root}/fixture"
mkdir -p "${fixture_root}/src/Common"

cat >"${fixture_root}/CHANGELOG.md" <<'EOF'
# Changelog

## Orb Of Creation ModSuite 0.5.0-beta.1 — 2026-07-31

- First release note.
- Second release note.

## Orb Of Creation ModSuite 0.4.0 Beta 1 — 2026-07-29

- Older note.
EOF
cat >"${fixture_root}/Directory.Build.props" <<'EOF'
<Project>
  <PropertyGroup>
    <SuiteVersion>0.5.0-beta.1</SuiteVersion>
  </PropertyGroup>
</Project>
EOF
cat >"${fixture_root}/src/OrbModSuite.csproj" <<'EOF'
<Project>
  <PropertyGroup>
    <Version>0.5.0-beta.1</Version>
    <AssemblyVersion>0.5.0.0</AssemblyVersion>
    <FileVersion>0.5.0.0</FileVersion>
    <InformationalVersion>0.5.0-beta.1</InformationalVersion>
  </PropertyGroup>
</Project>
EOF
cat >"${fixture_root}/src/Common/PluginIds.cs" <<'EOF'
public static class PluginIds
{
    public const string Version = "0.5.0";
    public const string ReleaseVersion = "0.5.0-beta.1";
}
EOF

expected_section='## Orb Of Creation ModSuite 0.5.0-beta.1 — 2026-07-31

- First release note.
- Second release note.'
actual_section="$(extract_changelog_section "${fixture_root}/CHANGELOG.md" "0.5.0-beta.1")"
if [[ "${actual_section}" != "${expected_section}" ]]; then
    echo "changelog extraction returned unexpected text" >&2
    exit 1
fi
if extract_changelog_section "${fixture_root}/CHANGELOG.md" "9.9.9" >/dev/null 2>&1; then
    echo "changelog extraction accepted a missing version" >&2
    exit 1
fi

if ! version_consistency_check "${fixture_root}" "0.5.0-beta.1"; then
    echo "matching version fixture failed consistency" >&2
    exit 1
fi
if version_consistency_check "${fixture_root}" "0.5.0-beta.2" >/dev/null 2>&1; then
    echo "mismatched version fixture passed consistency" >&2
    exit 1
fi

if ! is_prerelease "0.5.0-beta.1"; then
    echo "prerelease detection missed a prerelease" >&2
    exit 1
fi
if is_prerelease "0.5.0"; then
    echo "prerelease detection classified a stable version as prerelease" >&2
    exit 1
fi

fake_bin="${temporary_root}/fake-bin"
mkdir -p "${fake_bin}"
cat >"${fake_bin}/git" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$*" != *"remote get-url origin"* ]]; then
    echo "unexpected git arguments: $*" >&2
    exit 1
fi
echo "https://github.com/Vojow/OrbOfCreation-ModSuite.git"
EOF
cat >"${fake_bin}/gh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$*" != "repo view https://github.com/Vojow/OrbOfCreation-ModSuite.git --json nameWithOwner --jq .nameWithOwner" ]]; then
    echo "unexpected gh arguments: $*" >&2
    exit 1
fi
echo "Vojow/OrbOfCreation-ModSuite"
EOF
chmod +x "${fake_bin}/git" "${fake_bin}/gh"

original_path="${PATH}"
PATH="${fake_bin}:${PATH}"
resolved_target="$(resolve_repo_target "${fixture_root}")"
PATH="${original_path}"
if [[ "${resolved_target}" != "Vojow/OrbOfCreation-ModSuite" ]]; then
    echo "repo-target resolution returned '${resolved_target}'" >&2
    exit 1
fi

echo "changelog-extraction=pass version-consistency=pass prerelease-detection=pass repo-target-resolution=pass"
