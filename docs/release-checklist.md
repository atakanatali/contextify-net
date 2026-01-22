# Release Checklist

This checklist must be completed for every Contextify release to ensure quality and consistency.

## Pre-Release Verification

### CI/CD Status

- [ ] **Verify CI green** - All GitHub Actions workflows must pass on the target branch
  - Check `.github/workflows/ci.yml` status
  - Ensure all tests pass (unit + integration)
  - Verify code formatting checks pass
  - Confirm dependency validation passes

### Package Artifacts

- [ ] **Verify pack output includes nuget-readme.md**
  - NuGet packages must contain the README file
  - Extract `.nupkg` and verify `docs/nuget-readme.md` is included at package root
  - Package README should display correctly on NuGet.org

### Documentation

- [ ] **Verify changelog updated**
  - CHANGELOG.md must have a new version section (e.g., `## [0.1.0] - 2026-01-21`)
  - All unreleased items moved to the new version
  - New `[Unreleased]` section created for next release

- [ ] **Verify new APIs documented**
  - New public types, methods, and properties have XML documentation comments
  - README.md includes usage examples for new features
  - architecture.md updated if architectural changes were made
  - NuGet-specific documentation in docs/nuget-readme.md is current

### Samples

- [ ] **Verify samples compile and run**
  - `samples/MinimalApi.Sample` builds without errors
  - `samples/Mvc.Sample` builds without errors
  - `samples/Sidecar.Stdio.Sample` builds without errors
  - All samples can start and respond to MCP requests

### Gateway

- [ ] **Verify gateway host can start and aggregate at least one upstream**
  - `Contextify.Gateway.Host` executable starts without errors
  - Gateway can fetch tool catalog from an upstream MCP server
  - Gateway exposes the `/mcp` endpoint
  - Gateway responds to `tools/list` requests
  - Namespace prefixes are applied correctly

## Release Workflow Dry Run

### Build and Test

```bash
# Build the entire solution
dotnet build Contextify.sln -c Release

# Run all tests
dotnet test Contextify.sln -c Release --no-build

# Create NuGet packages
dotnet pack Contextify.sln -c Release -o ./artifacts
```

### Version Injection Verification

- [ ] **Verify version injection works via /p:Version from tag**
  - Test with: `dotnet pack -p:Version=0.1.0 -p:PackageVersion=0.1.0`
  - Verify all packages have correct version in `.nupkg` filename
  - Verify assembly versions match expected version
  - Verify nuspec files contain correct version

### Package Integrity

```bash
# Generate SHA256 checksums (for verification)
sha256sum ./artifacts/*.nupkg > checksums.txt

# Verify checksums
sha256sum -c checksums.txt
```

## Release Execution

### Tag Creation

```bash
# Create and push version tag
git tag -a v0.1.0 -m "Release v0.1.0"
git push origin v0.1.0
```

### Post-Release Verification

- [ ] **GitHub release created** - Automated workflow creates release
- [ ] **NuGet packages published** - All packages available on NuGet.org
- [ ] **Symbol packages published** - `.snupkg` files available for debugging
- [ ] **Release notes generated** - Based on PR labels, includes CHANGELOG reference
- [ ] **Checksums published** - SHA256 checksums attached to release

## Rollback Procedure

If critical issues are discovered after release:

1. **Unlist NuGet packages** - Hide packages from NuGet.org (do not delete)
2. **Update GitHub release** - Add prominent warning about the issue
3. **Create patch release** - Fix the issue and release `v0.1.1` promptly
4. **Communicate** - Notify users via issues, discussions, or announcements

## Release Notes Template

When creating releases manually (if automation fails), use this template:

```markdown
# Contextify v0.1.0

## Installation

```bash
dotnet add package Contextify.AspNetCore
dotnet add package Contextify.Transport.Http
```

## What's Changed

*Full changelog: https://github.com/contextify/contextify/blob/main/CHANGELOG.md*

### Added
- New feature 1 (#123)
- New feature 2 (#124)

### Fixed
- Bug fix 1 (#125)

### Security
- Security fix 1 (#126)

## Verification

### SHA256 Checksums

```
<checksums>
```

Verify:
```bash
sha256sum -c checksums.txt
```

## Documentation

- [Full Documentation](https://github.com/contextify/contextify)
- [Architecture Guide](https://github.com/contextify/contextify/blob/main/docs/architecture.md)
- [CHANGELOG](https://github.com/contextify/contextify/blob/main/CHANGELOG.md)
```

## Continuous Improvement

After each release, review this checklist and update:

- Add missing verification steps discovered during release
- Remove obsolete steps
- Update version examples to latest release version
- Improve automation where possible to reduce manual steps
