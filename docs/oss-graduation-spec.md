# ZeroEngine OSS Graduation Spec

## Goal

Make ZeroEngine credible as a public open-source Unity package repository and
as the primary project for an OSS support application.

## Graduation Criteria

- The root repository explains what ZeroEngine is, why it exists, what packages
  matter first, and how to install them.
- The repository declares the MIT License.
- External contributors can find contribution, support, and security paths.
- GitHub issues and pull requests collect enough information for maintainers to
  reproduce and review work.
- Important packages have valid UPM metadata: license, author, repository URL,
  and repository directory.
- At least the high-signal packages have README files with install and usage
  guidance.
- CI presence is documented so reviewers can see how tests are intended to run.

## Primary Packages For Reviewers

- `com.zerogamestudio.zeroengine.core`
- `com.zerogamestudio.zeroengine.data-toolkit`
- `com.zerogamestudio.zeroengine.pathfinding2d`
- `com.zerogamestudio.zeroengine.narrative`
- `com.zerogamestudio.zeroengine.ui`
- `com.zerogamestudio.analytics`

## Non-goals

- Do not rewrite runtime APIs as part of graduation.
- Do not claim broad ecosystem adoption until there is public evidence.
- Do not convert every package README into full reference documentation in the
  first pass.

## Verification

- All package manifests parse as JSON.
- Package metadata scan reports license and repository data for public packages.
- Markdown entry files exist at the root and for primary packages.
- GitHub templates exist for issues and pull requests.
