# Security policy

## Supported versions

Only the latest release receives fixes. Before 1.0, that is the latest `0.x` version.

## Reporting a vulnerability

Please do **not** open a public issue.

Report privately through GitHub: **Security → Report a vulnerability** on this repository ([private vulnerability reporting](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)).

This covers:

- a bug in Bulwark itself, such as an analyzer crash or hang on some input;
- a missed detection that leaves a real app exposed and that you do not want to describe in public yet.

Include the Bulwark version, the .NET SDK version and the smallest code that shows the problem. You should get a reply within 7 days.

Ordinary false positives and missed patterns are welcome as public issues.

## Scanning other people's code

If Bulwark finds a misconfiguration in someone else's project, report it to that project privately, following its security policy, before you publish anything about it.
