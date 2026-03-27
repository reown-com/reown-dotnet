# Release

Create a GitHub release for the latest Unity AppKit version tag.

## Arguments

$ARGUMENTS — not used.

## Steps

### 1. Find the latest unreleased Unity AppKit tag

First, fetch the latest tags from the remote:

```bash
git fetch --tags
```

Then find the latest `com.reown.appkit.unity/*` tag:

```bash
git tag --sort=-creatordate | grep "^com.reown.appkit.unity/" | head -1
```

Check if a release already exists for that tag:

```bash
gh release view "<tag>" --repo reown-com/reown-dotnet 2>&1
```

If the release already exists, tell the user "Release already exists for `<tag>`" and stop.

### 2. Determine the previous release tag

Find the previous `com.reown.appkit.unity/*` tag to establish the comparison range:

```bash
git tag --sort=-creatordate | grep "^com.reown.appkit.unity/" | head -2 | tail -1
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

Read the actual code diffs for significant changes to understand what they do from a developer's perspective. Use `git diff <previous-tag>..<new-tag> -- src/` to review the source changes. Focus on understanding what changed in the public API surface.

### 4. Write release notes

Use this exact format, matching the style of previous releases. The title is always `Unity AppKit v<version>`.

Structure the notes using these sections (include only sections that have content):

#### Section: Added / New
New features and capabilities. Use `Added` as the section name (some older releases used `New` — prefer `Added` for consistency).

For significant new features, include C# code examples showing usage. Examples should be concise, practical, and copy-pasteable. Wrap them in ```csharp blocks.

Prefix each item with the platform scope: `Native:`, `Web:`, or `Native & Web:`.

#### Section: Changed
Behavioral changes, dependency upgrades, improvements. Always include the latest `@reown/appkit-cdn` version bump if present (format: `Web: Upgrade \`@reown/appkit-cdn\` to v<version>`).

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

#### Footer

Always end with:

```
**Full Changelog**: https://github.com/reown-com/reown-dotnet/compare/<previous-tag>...<new-tag>
```

### 5. Present release notes for approval

Show the complete release notes to the user formatted as they will appear in the GitHub release. Then present three options using AskUserQuestion:

```
Here are the release notes for Unity AppKit v<version>:

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

Once approved, create the release:

```bash
gh release create "<tag>" \
  --repo reown-com/reown-dotnet \
  --title "Unity AppKit v<version>" \
  --notes "<approved-release-notes>"
```

Use a heredoc for the notes to preserve formatting:

```bash
gh release create "<tag>" \
  --repo reown-com/reown-dotnet \
  --title "Unity AppKit v<version>" \
  --notes "$(cat <<'EOF'
<release notes here>
EOF
)"
```

After creation, show the release URL to the user.

### Important

- Only create releases for `com.reown.appkit.unity/*` tags. Other package tags (core, sign, walletkit, etc.) do not get GitHub releases.
- The tag must already exist before running this command. Tags are created by the release CI workflow when code is merged to `main`.
- Never create or modify git tags. This command only creates GitHub releases for existing tags.
- If there are no meaningful user-facing changes (e.g., only infra/CI changes), mention this to the user and suggest whether a release is still warranted.
