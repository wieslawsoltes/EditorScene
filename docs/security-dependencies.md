# Application cryptography dependencies

On October 14, 2026, the browser publish at source 53f2dc6338335bef35b3e59c8e41969726dc3068 (workflow 38541146304, job 115775920250) correctly failed during NuGet restore. The audit reported transitive System.Security.Cryptography.Pkcs and System.Security.Cryptography.Xml 10.0.12 as affected by Microsoft advisories published October 13. No browser interaction test ran in that job. The same source's core, native and isolated-package jobs passed; those successes do not override the failed app restore.

The application now directly references version 10.0.13 of both packages, on both target frameworks. This is the patched .NET 10 package release named in all five reported advisories. NuGetAudit remains enabled, explicitly includes transitive dependencies, and inherited TreatWarningsAsErrors remains enabled. No NuGetAuditSuppress, NU1902/NU1903 NoWarn entry or audit disablement is introduced.

Primary advisories:

- Pkcs: https://github.com/advisories/GHSA-2fmv-rv2c-59x7 and https://github.com/advisories/GHSA-846f-f764-76mx
- Xml: https://github.com/advisories/GHSA-7hqp-7c5m-4vq6 , https://github.com/advisories/GHSA-c399-9853-6jmj and https://github.com/advisories/GHSA-r955-8q5j-3h83

These references address the observed restore graph, not evidence that the editor exposed the vulnerable cryptographic operations to remote input. A passing package audit also does not establish that the entire application is secure. The new exact source must pass fresh application build, native, regression, package-consumer and three full browser validation gates before merge/deployment is claimed.
