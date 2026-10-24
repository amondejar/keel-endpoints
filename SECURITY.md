# Security policy

## Reporting a vulnerability

Please do not open a public issue. Report it privately through GitHub:
**Security** tab of this repository, **Report a vulnerability**. You will get an answer within a week.

Include the package version, what an attacker can do, and the smallest app or test that shows it. A fix is
released as a patch version and credited to you in the changelog, unless you prefer otherwise.

## Supported versions

| Version | Supported |
| --- | --- |
| Latest 1.x release | Yes |
| 1.0 previews and release candidates | Until 1.0.0 ships; then upgrade to it |
| 0.x previews | No: upgrade to the latest version |

Keel.Endpoints runs inside ASP.NET Core; vulnerabilities in ASP.NET Core itself go to
[Microsoft](https://github.com/dotnet/aspnetcore/security/policy).
