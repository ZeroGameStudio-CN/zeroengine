# Security Policy

## Supported Scope

Security reports should focus on vulnerabilities in ZeroEngine package code,
editor tooling, self-hosted analytics upload flows, and generated runtime
configuration that could expose project data or execute unintended code.

Unity project-specific content, game assets, and downstream game logic are
outside this repository unless the issue is caused by reusable ZeroEngine code.

## Reporting

Do not open a public GitHub issue for security concerns. Send a private report
to the maintainers with:

- The affected package name and version or commit hash.
- Reproduction steps.
- Expected and observed impact.
- Any relevant logs, stack traces, or minimal sample project details.

If a private security advisory is available on the GitHub repository, prefer
that channel.

## Response Expectations

Maintainers will triage reports based on reproducibility, affected packages,
and severity. Fixes should include focused tests when the behavior is
testable without exposing sensitive data.
