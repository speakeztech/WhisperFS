# GitHub Setup for WhisperFS

## Current Workflow Configuration

You now have **two separate workflows** that won't interfere with each other:

### 1. `ci.yml` - Continuous Integration (Safe for Regular Development)
- **Triggers on**: Regular pushes to `main` or `dev` branches
- **Does NOT trigger on**: Version tags
- **Actions**: Build and test only
- **No packaging, no publishing, no releases**
- This runs on every push to ensure code quality

### 2. `release.yml` - Release Pipeline (Your Manual Control)
- **ONLY triggers on**: Version tags (`v*`) or manual workflow dispatch
- **Does NOT trigger on**: Regular branch pushes
- **Actions**: Build, test, package, publish to NuGet, create GitHub release
- This is your "human in the loop" controlled release process

## What Happens When You Push Code

When you push regular commits (without tags):
```bash
git add .
git commit -m "Some feature"
git push
```
**Result**: Only `ci.yml` runs - builds and tests on all platforms. No packages created, nothing published.

## How to Trigger a Release (When You're Ready)

When you're ready to release v0.1.0:
```bash
git tag v0.1.0
git push origin v0.1.0
```
**Result**: `release.yml` runs - builds, tests, creates packages, publishes to NuGet, creates GitHub release.

## Required GitHub Secrets

Before your first release, you need to configure these in your GitHub repository:

### 1. NUGET_API_KEY
**Required for**: Publishing packages to NuGet.org

**How to set up**:
1. Go to https://www.nuget.org/account/apikeys
2. Sign in or create an account
3. Click "Create" to create a new API key
4. Settings for the key:
   - **Key Name**: `WhisperFS GitHub Actions`
   - **Expiration**: 365 days (or your preference)
   - **Package Owner**: Your NuGet username
   - **Scopes**:
     - Select "Push new packages and package versions"
     - Package Glob Pattern: `WhisperFS*` (to limit to just WhisperFS packages)
5. Copy the generated key

**Add to GitHub**:
1. Go to your GitHub repository
2. Settings → Secrets and variables → Actions
3. Click "New repository secret"
4. Name: `NUGET_API_KEY`
5. Value: Paste your NuGet API key
6. Click "Add secret"

## Pre-Release Checklist

Before creating your first release tag:

- [ ] Test library locally with Mel application
- [ ] Verify all tests pass: `dotnet test`
- [ ] Verify clean build: `dotnet build --configuration Release`
- [ ] Update version in `Directory.Build.props` if needed (currently 0.1.0)
- [ ] Create and configure `NUGET_API_KEY` secret in GitHub
- [ ] Consider creating a test tag first (e.g., `v0.1.0-alpha1`) to verify the pipeline

## Version Tag Format

Your version tags should follow this format:
- `v0.1.0` - For releases
- `v0.1.0-preview1` - For preview releases
- `v0.1.0-alpha1` - For alpha releases
- `v0.1.0-beta1` - For beta releases

The tag MUST start with `v` to trigger the release workflow.

## What Gets Published

When you create a version tag, the release workflow will:

1. **Create 4 NuGet packages**:
   - `WhisperFS.Core` - Core types and interfaces
   - `WhisperFS.Native` - Native library loader
   - `WhisperFS.Runtime` - Model downloader and runtime detection
   - `WhisperFS` - Main package (depends on all others)

2. **Create a GitHub Release** with:
   - All NuGet packages attached
   - Auto-generated release notes
   - Marked as pre-release if tag contains `preview`, `alpha`, or `beta`

## Manual Preview Releases

You can also trigger a preview release manually without a tag:
1. Go to Actions tab in GitHub
2. Select "Release Pipeline"
3. Click "Run workflow"
4. Check "Publish preview package to NuGet"
5. Click "Run workflow"

This will create preview packages with version like `0.1.0-preview-{build-number}`.

## Safety Features

- Regular pushes will NOT trigger any packaging or publishing
- Only version tags starting with `v` trigger full releases
- The `--skip-duplicate` flag prevents re-publishing existing versions
- Preview packages have unique version numbers to avoid conflicts

## Testing the Setup (Optional)

If you want to test the release process without publishing to NuGet:
1. Comment out the "Publish to NuGet.org" step in `release.yml`
2. Create a test tag: `git tag v0.0.1-test && git push origin v0.0.1-test`
3. Watch the Actions tab to see the workflow run
4. Download the artifacts to verify packages were created correctly
5. Delete the test tag: `git push --delete origin v0.0.1-test && git tag -d v0.0.1-test`
6. Uncomment the publish step when satisfied

## Your Next Steps

1. **Continue local development** - Push code freely, only CI will run
2. **Test with Mel application** - Ensure WhisperFS works as expected
3. **When ready for first release**:
   - Set up NuGet API key in GitHub secrets
   - Tag with `v0.1.0`
   - Push the tag
   - Monitor the Actions tab
   - Verify packages appear on NuGet.org