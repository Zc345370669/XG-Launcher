# Security and private configuration

Do not attach access/refresh/Xbox/XSTS tokens, OAuth authorization codes, state,
PKCE verifiers, cookies, password data, account caches or unredacted logs to issues.
Use the launcher's redacted diagnostic export. Report vulnerabilities through a
private channel agreed with the repository maintainer; no reporting address is
invented by this project.

The Microsoft public-client application ID is intentionally public. This desktop
client does not require a Client Secret. Do not add a secret to the source,
project properties, command-line arguments or CI logs. If a future server-side
integration requires secrets, keep them on the server or in a protected secret
store, never in the desktop distribution.

Only publish reviewed source. Exclude runtime data, bin/obj, local logs, account
caches and checkpoints. Run the offline scanner before building; strict layout
mode rejects generated/private files. The scanner is heuristic and does not
prove the absence of encrypted or deliberately obfuscated secrets.

Minecraft Services HTTP 403 is a real failure and remains visible. This review
does not confer API access or bypass ownership/authentication checks.
