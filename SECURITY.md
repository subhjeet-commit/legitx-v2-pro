# Security policy

## Reporting a vulnerability

Do not open a public issue for suspected credential exposure, data collection,
destructive behavior, or a working exploit. Use a private GitHub Security
Advisory for this repository, or contact the repository owner through GitHub
with the affected commit, file, reproduction steps, and relevant logs.

Never include passwords, API keys, private device identifiers, or personal data
in a report.

## Release review

LegitX V2 can request administrator rights, install a low-level input driver,
inspect hardware identifiers, change registry values, and access Firebase
services configured by the operator. Review `firestore.rules`, environment
variables, bundled drivers, and release scripts before distributing a build.

Build from a clean checkout, scan the resulting artifact, and publish checksums
for the exact commit. A VirusTotal result is not a guarantee of safety.
