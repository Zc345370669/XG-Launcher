# XG Launcher

**An independent, open-source Minecraft: Java Edition launcher for Windows.**

XG Launcher (瞎搞启动器) is a desktop application developed using C#/.NET 10 and Avalonia UI.

The project aims to provide a convenient, customizable, and reliable experience for managing Minecraft: Java Edition installations, game versions, Java runtimes, mods, modpacks, and player accounts.

## Project Status

**Version:** v0.1.0-alpha  
**Platform:** Windows x64  
**Development status:** Active development and testing  
**License:** GNU General Public License v3.0

XG Launcher is currently under development. Some features are experimental or require further testing.

## Features

- Minecraft: Java Edition version installation and management.
- Multiple game instances with independent configurations.
- Java runtime detection, selection, and management.
- Forge, NeoForge, Fabric, and Quilt integration.
- Mod and modpack management.
- Game import and instance migration tools.
- Download management and resource verification.
- Customizable user interface themes.
- Offline profile support for local use.
- Microsoft account authentication integration.

Feature availability and compatibility may vary during alpha development.

## Microsoft Authentication

XG Launcher is designed to use Microsoft's official OAuth authentication process, followed by Xbox Live, XSTS, and Minecraft Services authentication.

The intended authentication flow includes:

1. Microsoft account authorization.
2. Xbox Live authentication.
3. XSTS authorization.
4. Minecraft Services authentication.
5. Minecraft game ownership verification.
6. Minecraft player profile retrieval.

The launcher is designed to require valid authentication and game ownership verification before enabling authenticated Minecraft gameplay.

XG Launcher does not intend to bypass Microsoft authentication, Minecraft ownership verification, licensing requirements, or server security mechanisms.

**Microsoft account integration is currently under development. Minecraft Services AppID approval is being requested.**

## Security and Privacy

XG Launcher is designed to:

- Use browser-based Microsoft authentication.
- Avoid collecting Microsoft account passwords.
- Protect authentication tokens and credentials.
- Redact sensitive information from diagnostic logs.
- Avoid exposing OAuth authorization codes on completion pages.
- Use secure authentication and account verification procedures.

Security features remain subject to ongoing testing and review.

## Development and Source Code

XG Launcher is an independently developed open-source project.

The project is in its alpha development stage. Source code and development materials will be published as they are prepared for public release.

## License

XG Launcher is licensed under the **GNU General Public License v3.0 (GPL-3.0)**.

See the [LICENSE](LICENSE) file for details.

## Disclaimer

XG Launcher is an independent third-party project.

It is not affiliated with, endorsed by, sponsored by, or officially associated with Microsoft or Mojang Studios.

Minecraft and related trademarks are the property of their respective owners.

Users must comply with the Minecraft End User License Agreement and Usage Guidelines.

XG Launcher is intended for legitimate use with Minecraft: Java Edition.
