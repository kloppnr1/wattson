# Security Policy

## Scope

WattsOn handles sensitive data and infrastructure:

- **DataHub OAuth2 credentials** — client ID and secret for Energinet's DataHub 3.0
- **Electricity customer data** — CPR numbers (personal identity), CVR numbers (business identity), metering point GSRNs, consumption time series
- **Settlement financial data** — calculated charges, corrections, price breakdowns

These are high-value targets. Security issues in WattsOn can result in data exposure or unauthorised access to the Danish electricity market infrastructure.

## Supported Versions

Only the latest version on `main` is actively maintained.

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for security vulnerabilities.

Report vulnerabilities via **[GitHub Private Security Advisories](https://github.com/kloppnr1/wattson/security/advisories/new)**.

Include:
- Description of the vulnerability and potential impact
- Steps to reproduce or proof-of-concept (if safe to include)
- Affected component(s) and version

We will acknowledge receipt within **72 hours** and provide an initial assessment within **7 days**.

## In Scope

- Authentication and authorisation bypass
- Exposure of CPR/CVR numbers or DataHub credentials
- SQL injection or other injection flaws
- Logic errors in settlement calculations that could be exploited
- Insecure handling of DataHub tokens

## Out of Scope

- Staging/preprod environment issues that do not affect production
- Social engineering attacks
- Denial-of-service attacks
- Issues in third-party dependencies already reported upstream

## Disclosure Policy

We follow responsible disclosure. After a fix is deployed, we will coordinate with the reporter on public disclosure timing (typically 90 days after the fix, or earlier by mutual agreement).
