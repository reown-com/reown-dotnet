# Version Bump

Bump the SDK version and create a PR against `develop`.

## Arguments

$ARGUMENTS — optional version number (e.g., `1.6.0`). If not provided, prompt the user to choose.

## Steps

### 1. Read the current version

Read `src/Directory.Build.props` and extract the value inside `<DefaultVersion>...</DefaultVersion>`.

### 2. Determine the new version

If `$ARGUMENTS` contains a version number (three dot-separated numbers like `1.6.0`), use it directly.

Otherwise, parse the current version as `major.minor.patch` and present the user with three choices using AskUserQuestion:

- **patch** → `major.minor.(patch+1)` — bug fixes, small changes
- **minor** → `major.(minor+1).0` — new features, non-breaking changes
- **major** → `(major+1).0.0` — breaking changes

Format the question clearly showing the current version and what each option resolves to, e.g.:

```
Current version: 1.5.2

Which version bump?
1. patch → 1.5.3
2. minor → 1.6.0
3. major → 2.0.0
```

### 3. Update the version

Edit `src/Directory.Build.props`, replacing the old `<DefaultVersion>` value with the new version. Use the Edit tool — do not rewrite the entire file.

### 4. Create branch, commit, and PR

Run these steps:

1. Check for uncommitted changes first. If `git diff --quiet && git diff --cached --quiet` fails (dirty working tree), warn the user and stop — do not switch branches with uncommitted changes.

   Then create and switch to a new branch named `chore/version-bump` from the current `develop` branch:
   ```
   git checkout develop && git pull origin develop && git checkout -b chore/version-bump
   ```
   If the branch already exists locally, check if it has commits not on any remote branch (`git log chore/version-bump --not --remotes`). If it has unpushed work, warn the user and stop. Otherwise, delete it with `git branch -D chore/version-bump` and recreate.

2. Stage only `src/Directory.Build.props`:
   ```
   git add src/Directory.Build.props
   ```

3. Commit with message: `chore: version bump <new-version>`

4. Push the branch:
   ```
   git push -u origin chore/version-bump
   ```

5. Create a PR against `develop`:
   ```
   gh pr create --base develop --title "chore: version bump <new-version>" --body ""
   ```

6. Return the PR URL to the user.

### Important

- Only commit `src/Directory.Build.props`. No other files should be staged or committed.
- The CI pipeline (`sync-unity-package-version.yml`) will automatically propagate the version to Unity package.json files and other markers after the PR is created.
- If there are other locally modified files, leave them as-is — do not stash, reset, or discard them.
- After creating the PR, switch back to the branch the user was on before (usually `develop`).
