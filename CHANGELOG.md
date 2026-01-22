# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-01-21

### Added
- Initial release of Contextify - A modular, enterprise-grade .NET library for Model Context Protocol (MCP) server and tool management
- API to MCP transformation - Expose ASP.NET Core endpoints as MCP tools
- Policy engine - Deny-by-default security with whitelist/blacklist support
- Action system - Middleware pipeline for tool invocation (rate limiting, caching, validation)
- HTTP transport - JSON-RPC 2.0 over HTTP/HTTPS
- STDIO transport - JSON-RPC 2.0 over standard input/output for CLI tools
- Gateway MVP - Multi-backend aggregation with namespace isolation and partial availability
- Initial mono-repo structure
- Core abstractions for MCP protocol (IMcpRuntime, IMcpTool, IMcpResource)
- Native MCP runtime implementation
- Official MCP SDK adapter (ModelContextProtocol v0.27.0)
- HTTP/HTTPS transport implementation
- STDIO transport implementation for CLI tools
- ASP.NET Core integration (AddContextify, MapContextifyMcp)
- Action system abstractions (IAction, IActionHandler)
- Default action implementations (rate limiting, caching, validation)
- Configuration provider abstractions
- AppSettings configuration provider
- Consul distributed configuration provider
- Logging abstractions with structured logging support
- OpenAPI/Swagger integration
- Gateway core and host for multi-backend support
- Security: deny-by-default policy
- Security: whitelist/blacklist with glob pattern support
- Configuration snapshot immutability
- Thread-safe public APIs with concurrent collections
- DI container configuration and service registration

## [0.1.0] - TBD

### Release Automation Guidelines

Contextify uses **Conventional Commits** for automated release notes generation. This section documents the release automation approach that will be implemented in GitHub Actions.

### Conventional Commits Format

Commit messages must follow the Conventional Commits specification:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

#### Commit Types

| Type | Description | Creates Release? |
|------|-------------|------------------|
| `feat` | New feature | Yes (minor version) |
| `fix` | Bug fix | Yes (patch version) |
| `docs` | Documentation only | No |
| `style` | Code style changes (formatting, etc.) | No |
| `refactor` | Code refactoring | No |
| `perf` | Performance improvement | Yes (patch version) |
| `test` | Adding or updating tests | No |
| `build` | Build system or dependencies | Yes (patch version) |
| `ci` | CI/CD changes | No |
| `chore` | Maintenance tasks | No |
| `revert` | Reverts a previous commit | Yes (patch version) |

#### Commit Scopes

Scopes help categorize changes by module:

- `core` - Contextify.Core
- `mcp` - Contextify.Mcp.*
- `transport` - Contextify.Transport.*
- `aspnet` - Contextify.AspNetCore
- `config` - Contextify.Config.*
- `actions` - Contextify.Actions.*
- `logging` - Contextify.Logging
- `openapi` - Contextify.OpenApi
- `gateway` - Contextify.Gateway.*
- `security` - Security-related changes

#### Examples

```
feat(transport): add WebSocket transport implementation
fix(core): resolve race condition in tool registration
docs(changelog): add Keep a Changelog format and release note guidelines
feat(aspnet): add health check endpoint for MCP runtime
fix(security): prevent bypass of whitelist patterns
```

### Release Workflow (Planned Implementation)

A GitHub Actions workflow will:

1. **Trigger on push to main branch**
2. **Analyze commits since last tag** using Conventional Commits parser
3. **Determine next version** based on commit types:
   - `feat` → increment MINOR version
   - `fix`/`perf`/`revert` → increment PATCH version
   - `BREAKING CHANGE` footer → increment MAJOR version
4. **Generate release notes** from commit messages
5. **Update CHANGELOG.md** with new version section
6. **Create Git tag** (e.g., `v0.1.0`)
7. **Create GitHub Release** with auto-generated notes
8. **Build and publish NuGet packages**

### Manual Release Process (Before CI/CD Implementation)

For manual releases, follow these steps:

1. **Version Determination**
   - Review commits since last release
   - Determine version bump based on changes (MAJOR.MINOR.PATCH)

2. **Update CHANGELOG.md**
   - Move Unreleased items to new version section
   - Add release date
   - Keep new Unreleased section empty

3. **Commit and Tag**
   ```bash
   git add CHANGELOG.md
   git commit -m "chore(release): prepare release v0.1.0"
   git tag -a v0.1.0 -m "Release v0.1.0"
   git push origin main --tags
   ```

4. **Create GitHub Release**
   - Go to Releases → Draft a new release
   - Select the tag
   - Copy CHANGELOG section to release notes
   - Publish release

5. **Publish NuGet Packages**
   ```bash
   dotnet pack src/**/*.csproj -c Release -o ./artifacts
   dotnet nuget push ./artifacts/*.nupkg --source nuget.org --api-key <key>
   ```

### CHANGELOG.md Maintenance Rules

1. **Unreleased Section**
   - Add items as commits are made
   - Categorize by: Added, Changed, Fixed, Deprecated, Removed, Security
   - Include module scope for clarity: `feat(transport): ...`

2. **Versioned Sections**
   - Created during release
   - Items moved from Unreleased
   - Release date added: `[0.1.0] - 2026-01-20`

3. **Categories**
   - **Added** - New features
   - **Changed** - Changes to existing functionality
   - **Deprecated** - Features marked for removal
   - **Removed** - Features removed in this release
   - **Fixed** - Bug fixes
   - **Security** - Security-related changes

4. **Linking**
   - Link issues/PRs in entries: `feat(core): add tool registry (#123)`

### Versioning Strategy

Contextify follows Semantic Versioning 2.0.0:

- **MAJOR**: Incompatible API changes
- **MINOR**: New functionality (backwards compatible)
- **PATCH**: Bug fixes (backwards compatible)

Example: `1.2.3`
- `1` = MAJOR (breaking changes)
- `2` = MINOR (new features)
- `3` = PATCH (bug fixes)

### Breaking Changes Policy

Breaking changes must:

1. Use `feat` type with `BREAKING CHANGE` footer
2. Document migration path in CHANGELOG
3. Provide deprecation period when possible
4. Update all affected samples and documentation

Example commit:
```
feat(core): rename IMcpRuntime.Start to StartAsync

BREAKING CHANGE: IMcpRuntime.Start has been renamed to StartAsync
to follow async naming conventions. Migrate by updating all calls
from Start() to StartAsync().
```
