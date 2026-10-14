# Security Policy

## Supported versions

Security fixes are provided for the latest published PRDC release and the current `main` branch.

## Reporting a vulnerability

Do not disclose vulnerabilities, credentials, private `.rdg` files, internal hostnames, or proof-of-concept exploits in a public issue.

Use **Report a vulnerability** in the repository's **Security** tab to submit a private report. If private vulnerability reporting is unavailable, open a minimal public issue requesting a private contact channel without including sensitive details.

Include the affected PRDC version, Windows version, impact, shortest reproduction, and any known mitigation. Remove unrelated local paths and infrastructure data from logs or screenshots.

## Security boundary

PRDC hosts the Windows Remote Desktop ActiveX control from the local operating system. Authentication, server-certificate prompts, and the RDP protocol are handled by that Windows component. PRDC does not store plaintext passwords.

RDG files can contain sensitive hostnames, usernames, gateway configuration, and encrypted credential material. Treat them as confidential, restrict filesystem access, and never attach production RDG files to public issues.

PRDC is not a sandbox. Opening an untrusted RDG file parses XML and may expose infrastructure metadata in the interface. Connecting to a server initiates network activity with the current user's permissions and must only be done with authorization.
