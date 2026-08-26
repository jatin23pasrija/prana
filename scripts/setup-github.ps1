#!/usr/bin/env pwsh
#
# One-time GitHub setup for the Prana repository.
#
# Creates the label set, the seven milestones, the seventeen Phase 1 feature issues,
# the repository description and topics, the merge settings, and branch protection
# on main. Optionally creates the Project board and adds every feature issue to it.
#
# Prerequisites:
#   1. GitHub CLI installed:   winget install --id GitHub.cli
#   2. Authenticated:          gh auth login --scopes "repo,read:org,project"
#
# Run from the repository root:
#   pwsh -File scripts/setup-github.ps1
#
# Safe to run more than once. Labels are updated in place, and milestones, issues and an
# existing branch ruleset are skipped rather than duplicated.

param(
    [string]$Repo = 'jatin23pasrija/prana',
    [switch]$SkipProject
)

$ErrorActionPreference = 'Stop'

function Step($text) { Write-Host "`n=== $text" -ForegroundColor Cyan }
function Ok($text)   { Write-Host "    ok   $text" -ForegroundColor Green }
function Skip($text) { Write-Host "    skip $text" -ForegroundColor DarkGray }

# Fail early with a clear message rather than halfway through.
gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Not authenticated. Run: gh auth login --scopes ""repo,read:org,project"""
}

# ----------------------------------------------------------------------------
Step "Repository description and topics"

gh repo edit $Repo `
    --description "Know what is in your food. Offline, open source, community maintained. India first. A .NET MAUI app with a GitHub-native food data platform. No server, no account, no subscription." `
    --add-topic "food" --add-topic "nutrition" --add-topic "open-data" `
    --add-topic "offline-first" --add-topic "dotnet-maui" --add-topic "csharp" `
    --add-topic "android" --add-topic "ios" --add-topic "barcode-scanner" `
    --add-topic "india" --add-topic "open-food-facts" --add-topic "sqlite" `
    --enable-issues --enable-discussions --enable-projects | Out-Null
Ok "description and topics set"

# ----------------------------------------------------------------------------
Step "Merge settings"

# Squash only, so main keeps a linear history of one commit per feature.
gh api -X PATCH "repos/$Repo" `
    -F allow_squash_merge=true `
    -F allow_merge_commit=false `
    -F allow_rebase_merge=false `
    -F delete_branch_on_merge=true `
    -F allow_auto_merge=true | Out-Null
Ok "squash only, branches deleted on merge"

# ----------------------------------------------------------------------------
Step "Labels"

$labels = @(
    # type
    @{ n = 'feature';            c = '0e8a16'; d = 'A planned Phase 1 feature' }
    @{ n = 'bug';                c = 'd73a4a'; d = 'Something is broken' }
    @{ n = 'data';               c = '1d76db'; d = 'Product or reference data' }
    @{ n = 'docs';               c = '0075ca'; d = 'Documentation' }
    @{ n = 'automation';         c = '5319e7'; d = 'GitHub Actions and the research pipeline' }
    @{ n = 'security';           c = 'b60205'; d = 'Security relevant' }
    # area
    @{ n = 'app';                c = 'c2e0c6'; d = 'The MAUI application' }
    @{ n = 'catalogue';          c = 'c5def5'; d = 'Catalogue format, build or distribution' }
    @{ n = 'pipeline';           c = 'bfd4f2'; d = 'Tooling: validator, importer, builder' }
    @{ n = 'ci';                 c = 'ededed'; d = 'Continuous integration' }
    @{ n = 'schema';             c = 'd4c5f9'; d = 'Data schema and provenance model' }
    # state
    @{ n = 'needs-discussion';   c = 'fbca04'; d = 'Questions round not finished' }
    @{ n = 'ready';              c = '0e8a16'; d = 'Scope agreed, dependencies merged' }
    @{ n = 'in-progress';        c = 'fef2c0'; d = 'Being built right now' }
    @{ n = 'blocked';            c = 'b60205'; d = 'Waiting on something else' }
    @{ n = 'needs-review';       c = 'fbca04'; d = 'Waiting for review' }
    @{ n = 'needs-human-review'; c = 'e99695'; d = 'Automation could not decide, a person must' }
    @{ n = 'needs-triage';       c = 'fbca04'; d = 'Not yet looked at' }
    @{ n = 'needs-research';     c = 'fbca04'; d = 'Waiting for the research workflow' }
    @{ n = 'good first issue';   c = '7057ff'; d = 'A good place to start contributing' }
    # intake
    @{ n = 'product-request';    c = '1d76db'; d = 'A missing product was requested' }
    @{ n = 'correction';         c = '1d76db'; d = 'A catalogue value is wrong' }
    @{ n = 'source-proposal';    c = '1d76db'; d = 'A new data source was proposed' }
    # phase
    @{ n = 'phase-1';            c = '5319e7'; d = 'Phase 1, the core deliverable' }
    @{ n = 'phase-2';            c = '8e7cc3'; d = 'Phase 2, community utility' }
    @{ n = 'phase-3';            c = 'b4a7d6'; d = 'Phase 3, broader food intelligence' }
    @{ n = 'phase-4';            c = 'd9d2e9'; d = 'Phase 4, ecosystem' }
    # confidence, used by the research automation
    @{ n = 'confidence-high';    c = '0e8a16'; d = 'Evidence is strong, eligible for auto-merge' }
    @{ n = 'confidence-medium';  c = 'fbca04'; d = 'Some uncertainty, needs review' }
    @{ n = 'confidence-low';     c = 'd93f0b'; d = 'Weak evidence, do not publish as verified' }
)

foreach ($l in $labels) {
    gh label create $l.n --repo $Repo --color $l.c --description $l.d --force | Out-Null
    Ok $l.n
}

# ----------------------------------------------------------------------------
Step "Milestones"

$milestones = @(
    @{ t = 'M0 Foundation';                 d = 'Repository, product schema and validator. CI rejects invalid data.' }
    @{ t = 'M1 Data spine';                 d = 'Import, catalogue builder and a signed release downloadable from GitHub Releases.' }
    @{ t = 'M2 Offline app';                d = 'Scan a barcode on a real phone in aeroplane mode and see the product.' }
    @{ t = 'M3 Sync';                       d = 'Background catalogue update that survives every failure drill.' }
    @{ t = 'M4 Discovery and contribution'; d = 'Unknown product goes from scan to merged pull request with no maintainer typing.' }
    @{ t = 'M5 Everyday utility';           d = 'Alternatives and grocery list, fully offline.' }
    @{ t = 'M6 Public release';             d = 'Signed APK on GitHub Releases, readiness matrix passed, docs complete.' }
)

$existingMilestones = gh api "repos/$Repo/milestones?state=all&per_page=100" | ConvertFrom-Json
foreach ($m in $milestones) {
    if ($existingMilestones.title -contains $m.t) {
        Skip $m.t
        continue
    }
    gh api "repos/$Repo/milestones" -f title=$m.t -f description=$m.d -f state=open | Out-Null
    Ok $m.t
}

# ----------------------------------------------------------------------------
Step "Feature issues"

$features = @(
    @{ id = 'F01'; t = 'Repository foundation and governance'; m = 'M0 Foundation';                 l = @('feature','docs','phase-1');                          dep = 'none' }
    @{ id = 'F02'; t = 'Product data schema and provenance model'; m = 'M0 Foundation';             l = @('feature','schema','phase-1');                        dep = 'F01' }
    @{ id = 'F03'; t = 'Validator CLI and data CI'; m = 'M0 Foundation';                            l = @('feature','pipeline','ci','phase-1');                 dep = 'F02' }
    @{ id = 'F04'; t = 'Data acquisition and importer'; m = 'M1 Data spine';                        l = @('feature','data','pipeline','phase-1');               dep = 'F03' }
    @{ id = 'F05'; t = 'Catalogue builder'; m = 'M1 Data spine';                                    l = @('feature','catalogue','pipeline','phase-1');          dep = 'F04' }
    @{ id = 'F06'; t = 'Signed release pipeline'; m = 'M1 Data spine';                              l = @('feature','catalogue','ci','security','phase-1');     dep = 'F05' }
    @{ id = 'F07'; t = 'MAUI application skeleton'; m = 'M2 Offline app';                           l = @('feature','app','phase-1');                           dep = 'F06' }
    @{ id = 'F08'; t = 'Local catalogue data layer'; m = 'M2 Offline app';                          l = @('feature','app','catalogue','phase-1');               dep = 'F07' }
    @{ id = 'F09'; t = 'Barcode scanner'; m = 'M2 Offline app';                                     l = @('feature','app','phase-1');                           dep = 'F08' }
    @{ id = 'F10'; t = 'Product details and analysis'; m = 'M2 Offline app';                        l = @('feature','app','phase-1');                           dep = 'F09' }
    @{ id = 'F11'; t = 'Catalogue sync and atomic install'; m = 'M3 Sync';                          l = @('feature','app','catalogue','security','phase-1');    dep = 'F10' }
    @{ id = 'F12'; t = 'Online product discovery'; m = 'M4 Discovery and contribution';             l = @('feature','app','phase-1');                           dep = 'F11' }
    @{ id = 'F13'; t = 'Community contribution flow'; m = 'M4 Discovery and contribution';          l = @('feature','app','security','phase-1');                dep = 'F12' }
    @{ id = 'F14'; t = 'Research automation and auto-PR'; m = 'M4 Discovery and contribution';      l = @('feature','automation','pipeline','phase-1');         dep = 'F13' }
    @{ id = 'F15'; t = 'Alternatives engine'; m = 'M5 Everyday utility';                            l = @('feature','app','phase-1');                           dep = 'F14' }
    @{ id = 'F16'; t = 'Grocery list and basket summary'; m = 'M5 Everyday utility';                l = @('feature','app','phase-1');                           dep = 'F15' }
    @{ id = 'F17'; t = 'Hardening and first public release'; m = 'M6 Public release';               l = @('feature','app','ci','phase-1');                      dep = 'F16' }
)

$existingIssues = gh issue list --repo $Repo --state all --limit 200 --json title | ConvertFrom-Json

foreach ($f in $features) {
    $title = "[$($f.id)] $($f.t)"

    # Match on the feature id, not the whole title, so a renamed or hand-created issue
    # is never duplicated by a rerun.
    if ($existingIssues.title -match "\[$($f.id)\]|^$($f.id)\b") {
        Skip "$($f.id) already exists"
        continue
    }

    $slug = ($f.t.ToLower() -replace '[^a-z0-9]+', '-').Trim('-')
    $branch = "feat/$($f.id.ToLower())-$slug"

    $body = @"
Full scope, out of scope, Definition of Done and test round:
[docs/planning/FEATURES.md](../blob/main/docs/planning/FEATURES.md), section **$($f.id)**.

| | |
|---|---|
| Milestone | $($f.m) |
| Branch | ``$branch`` |
| Depends on | $($f.dep) |

Before any code is written, the questions round happens in this issue. See
[docs/planning/WORKFLOW.md](../blob/main/docs/planning/WORKFLOW.md) for the six-step loop.

Decisions already locked for this work are in
[docs/planning/DECISIONS.md](../blob/main/docs/planning/DECISIONS.md). Changing one of them is
a separate pull request against that file, not a surprise in this branch.
"@

    $labelArgs = @()
    foreach ($lab in $f.l) { $labelArgs += '--label'; $labelArgs += $lab }

    gh issue create --repo $Repo --title $title --body $body --milestone $f.m @labelArgs | Out-Null
    Ok $title
}

# ----------------------------------------------------------------------------
Step "Branch protection on main"

# A repository ruleset targeting main is the modern equivalent and takes precedence.
# If one is already active, leave it alone rather than stacking a second, older-style
# rule on top of it.
$activeRules = gh api "repos/$Repo/rules/branches/main" | ConvertFrom-Json
if ($activeRules.Count -gt 0) {
    Skip "a ruleset already protects main ($($activeRules.type -join ', '))"
    Write-Host "    Remove the ruleset first if you want classic branch protection instead." -ForegroundColor DarkGray
    $skipProtection = $true
}

# No required approvals, because a solo maintainer would be unable to merge anything.
# What is enforced: every change goes through a pull request, CI must pass, history stays
# linear, and force pushes and deletion are impossible.
$protection = @'
{
  "required_status_checks": {
    "strict": true,
    "contexts": ["Build and test", "Repository hygiene"]
  },
  "enforce_admins": false,
  "required_pull_request_reviews": null,
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_conversation_resolution": true
}
'@

if (-not $skipProtection) {
    $protection | gh api -X PUT "repos/$Repo/branches/main/protection" --input - | Out-Null
    Ok "main protected, CI required"
}

# ----------------------------------------------------------------------------
if (-not $SkipProject) {
    Step "Project board"

    $owner = (gh api user | ConvertFrom-Json).login
    $projects = gh project list --owner $owner --format json | ConvertFrom-Json
    $project = $projects.projects | Where-Object { $_.title -eq 'Prana Phase 1' }

    if (-not $project) {
        $created = gh project create --owner $owner --title 'Prana Phase 1' --format json | ConvertFrom-Json
        $projectNumber = $created.number
        Ok "created project $projectNumber"
    }
    else {
        $projectNumber = $project.number
        Skip "project $projectNumber already exists"
    }

    $issues = gh issue list --repo $Repo --state all --limit 200 --json number,title,url | ConvertFrom-Json
    foreach ($i in ($issues | Where-Object { $_.title -match '^F\d\d - ' } | Sort-Object title)) {
        gh project item-add $projectNumber --owner $owner --url $i.url | Out-Null
        Ok "added $($i.title)"
    }

    Write-Host "`n    One manual step is left. GitHub creates the board with Todo / In Progress / Done." -ForegroundColor Yellow
    Write-Host "    Open the board, edit the Status field, and set the options to:" -ForegroundColor Yellow
    Write-Host "      Backlog, Questions, Ready, In progress, In review, Testing, Done" -ForegroundColor Yellow
}

Write-Host "`nDone.`n" -ForegroundColor Green
Write-Host "Next: open the F01 pull request." -ForegroundColor White
Write-Host "  gh pr create --repo $Repo --base main --head feat/f01-repo-foundation --fill" -ForegroundColor White
