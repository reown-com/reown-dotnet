# Release

Create a GitHub release for the latest Unity AppKit or WalletKit version tag.

## Arguments

$ARGUMENTS — optional package selector:

- `appkit` — Unity AppKit only
- `walletkit` — WalletKit only
- empty — both packages, one at a time (AppKit first); a package that already has a release for its latest tag is skipped

## Packages

| Selector | Tag prefix | Release title | Latest flag | Changelog scope |
| --- | --- | --- | --- | --- |
| `appkit` | `com.reown.appkit.unity/` | `Unity AppKit v<version>` | `--latest` | Everything that reaches Unity users: AppKit, the Unity packages, and the shared core |
| `walletkit` | `com.reown.walletkit/` | `WalletKit v<version>` | `--latest=false` | The NuGet surface only: `src/`, excluding every `*Unity*` path |

The release CI workflow tags every UPM package with the same version on each merge to `main`, so both tags always exist for a given version. Only these two get GitHub releases.

The Latest badge belongs on the Unity AppKit release — always pass `--latest=false` for WalletKit, otherwise the newer WalletKit release steals it.

Run the steps below once per selected package. When both are selected, finish AppKit (including approval and creation) before starting WalletKit.

## Steps

### 1. Find the latest unreleased tag

First, fetch the latest tags from the remote:

```bash
git fetch --tags
```

Then find the latest tag for the package's prefix:

```bash
git tag --sort=-creatordate | grep "^<tag-prefix>" | head -1
```

Check if a release already exists for that tag:

```bash
gh release view "<tag>" --repo reown-com/reown-dotnet 2>&1
```

If the release already exists, tell the user "Release already exists for `<tag>`" and move on to the next selected package (or stop, if it was the only one).

### 2. Determine the previous release tag

Find the previous tag with the same prefix to establish the comparison range:

```bash
git tag --sort=-creatordate | grep "^<tag-prefix>" | head -2 | tail -1
```

This is the base for the changelog comparison.

### 3. Gather changes

Get all commits between the previous tag and the new tag:

```bash
git log <previous-tag>..<new-tag> --oneline --no-merges
```

Also get the merge commits to understand PR context:

```bash
git log <previous-tag>..<new-tag> --merges --oneline
```

For commits that reference PRs, fetch PR details to understand the full scope of changes:

```bash
gh pr view <pr-number> --json title,body,labels
```

Read the actual code diffs for significant changes to understand what they do from a developer's perspective. Focus on understanding what changed in the public API surface.

For AppKit, review the whole source tree:

```bash
git diff <previous-tag>..<new-tag> -- src/
```

For WalletKit, exclude the Unity packages — they are not part of the NuGet surface:

```bash
git diff <previous-tag>..<new-tag> -- src/ ':(exclude)src/*Unity*'
```

If that filtered diff is empty, WalletKit has no user-facing changes for this version. Say so and suggest skipping its release.

### 4. Write release notes

Use this exact format, matching the style of previous releases. The title comes from the Packages table above.

Structure the notes using these sections (include only sections that have content):

#### Section: Added / New
New features and capabilities. Use `Added` as the section name (some older releases used `New` — prefer `Added` for consistency).

For significant new features, include C# code examples showing usage. Examples should be concise, practical, and copy-pasteable. Wrap them in ```csharp blocks.

For AppKit, prefix each item with the platform scope: `Native:`, `Web:`, or `Native & Web:`.

For WalletKit, do not use platform prefixes — it is a plain .NET library with no platform split. Keep code examples free of Unity types and APIs.

#### Section: Changed
Behavioral changes, dependency upgrades, improvements. For AppKit, always include the latest `@reown/appkit-cdn` version bump if present (format: `Web: Upgrade \`@reown/appkit-cdn\` to v<version>`).

#### Section: Fixed
Bug fixes. Describe them from the user's perspective — what was broken, not what code changed.

If a fix came from a community contribution, credit it: `in https://github.com/reown-com/reown-dotnet/pull/<number>. Thanks @<username>!`

#### Filtering rules

Include:
- New public API methods, properties, events
- Bug fixes that affect SDK users
- Dependency upgrades that matter (Nethereum, BouncyCastle, appkit-cdn)
- Platform compatibility changes (Unity versions, OS support)
- Breaking changes or behavioral changes

Exclude:
- CI/CD changes, workflow updates, infra improvements
- Sample app updates (unless they demonstrate a new feature pattern)
- Internal refactoring that doesn't affect the public API
- Test changes
- Documentation-only changes
- Version bump commits

Additionally, for WalletKit exclude anything that only reaches Unity consumers: AppKit changes, Unity package changes, WebGL and JavaScript bridge work, and Unity editor tooling. Changes in `Reown.Sign` and the `Reown.Core.*` packages do belong in WalletKit notes — they ship as NuGet packages.

Additionally, for AppKit exclude changes that only affect wallet-side WalletKit APIs.

#### Footer

Always end with:

```
**Full Changelog**: https://github.com/reown-com/reown-dotnet/compare/<previous-tag>...<new-tag>
```

### 5. Present release notes for approval

Show the complete release notes to the user formatted as they will appear in the GitHub release. Then present three options using AskUserQuestion:

```
Here are the release notes for <release title>:

---
<release notes>
---

How would you like to proceed?
1. Approve — create the release as-is
2. Reject — cancel without creating a release
3. Edit — provide instructions to modify the notes
```

If the user chooses option 3 (edit), apply their feedback to the release notes and present again with the same three options. Repeat until they approve or reject.

### 6. Create the GitHub release

Once approved, create the release. Use a heredoc for the notes to preserve formatting, and pass the Latest flag from the Packages table:

```bash
gh release create "<tag>" \
  --repo reown-com/reown-dotnet \
  --title "<release title>" \
  --latest \
  --notes "$(cat <<'EOF'
<release notes here>
EOF
)"
```

For WalletKit, use `--latest=false` in place of `--latest`.

After creation, show the release URL to the user.

### Important

- Only `com.reown.appkit.unity/*` and `com.reown.walletkit/*` tags get GitHub releases. All other package tags (core, sign, dependencies, etc.) do not.
- The tag must already exist before running this command. Tags are created by the release CI workflow when code is merged to `main`.
- Never create or modify git tags. This command only creates GitHub releases for existing tags.
- After creating both releases, verify the Latest badge sits on the Unity AppKit release.
- If there are no meaningful user-facing changes (e.g., only infra/CI changes), mention this to the user and suggest whether a release is still warranted.
