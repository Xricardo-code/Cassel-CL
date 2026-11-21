# Cassel-CL

**Original author: Longteng Maoyue (龙腾猫跃). Derivative author: Ricardo.**

Cassel-CL is an independent, third-party fan modification of Plain Craft Launcher (PCL), inspired by Jiang Nan's *Dragon Raja*. Ricardo's work focuses on the visual theme, interface, animations, and launcher update policy. This project is not affiliated with PCL's official development team and is not an official PCL release.

[Original project](https://github.com/Meloong-Git/PCL) · [Original author's page and support link](https://meloong.com/afd/a/LTCat)

About Ricardo: a little-known underdog, using the same name and avatar on GitHub.

## Source availability

This repository provides the Windows modification's theme and patch source code, assets, build scripts, and verification harnesses. Building requires an original PCL executable obtained separately. **The complete PCL core source code and precompiled original or modified launcher executables are not included.**

The source baseline corresponds to the existing Windows executable named Cassell CL, based on PCL 2.13.1.1 and .NET Framework 4.8. Its SHA-256 is:

`65aa22fae684aab2c6604adcbeddee092b07ed0c4046ea28aae056498167c231`

## Changes from the original launcher

| Area | Changes in this modification |
| --- | --- |
| Branding and icon | Custom branding, a monochrome academy crest, and rounded icons |
| Visual theme | Obsidian, antique gold, and ivory colors; a rainy academy and World Tree background; a headline meaning “Darkness Beneath the Torrential Rain” |
| Home and navigation | An academy executive archive layout, with themed Launch, Archives, Settings, and Executive Division navigation while retaining access to native functions |
| Pointer lighting | A soft light that follows the pointer |
| Rain effects | Perspective particles with depth layers, wind, and foreground ripples; an on/off switch and 0–100% intensity control; animation pauses when minimized. This is an interface effect, not an in-game weather feature |
| Startup and entrance | An academy crest introduction and staggered home-page entrance animations; the introduction can be skipped and respects reduced-animation settings |
| Game-ready animation | An internal animation triggered by native launch state, a live game process, and game-window readiness. Historical checks include simulated scenarios and are not a complete real-game validation |
| About and credits | Original-author attribution and support link, a Ricardo profile card and avatar, and a derivative-work statement |
| Launcher updates | Launcher update checks, self-updating, executable replacement, and update notifications are disabled; game and mod update implementations are retained |

Minecraft launching, account management, game downloads, and mod-related capabilities originate from PCL. They are not a new launcher core written by Ricardo. The table describes changes supported by the local source and historical verification records.

[User-provided introduction video: “PCL2 Derivative — A Dragon Raja-Themed Launcher”](https://www.bilibili.com/video/BV1ZMxs6PEMr/). The video could not be accessed during documentation preparation, so unverified claims from it are not presented as implemented features.

## Building

Requirements: Windows, PowerShell, and the .NET Framework 4.8 compiler with WPF support. The included dnlib.dll retains its MIT license; its checksum is recorded in SOURCE-MANIFEST.json.

Obtain the matching original executable through an authorized source and place it at `input/Plain Craft Launcher 2.exe`, or specify its location:

```powershell
.\Build.ps1 -Original 'D:\PCL\Plain Craft Launcher 2.exe'
```

Original executable SHA-256:

`30eb93c62fc51ede0898648e67455478a3d5a4f279938ae65aec77b84cb9703f`

Other versions may fail because their methods or resources differ. Output is written to `release-no-updates`, leaving the input intact. The build performs structural integrity checks. Byte-for-byte reproducibility is not guaranteed because compiler metadata can vary.

`RunVerification.ps1` runs the interface verification harnesses. Historical structural checks retained all 6,116 original methods: eight launcher-update methods were explicitly modified, the entry point received theme-loading instructions, and the remaining 6,107 non-entry method bodies were preserved. Online login, network downloads, and actual Windows game launching have not received complete end-to-end validation.

## Licensing, naming, and assets

The upstream project uses the PCL Limited Distribution License and publishes repository usage guidelines. The original text is preserved in LICENCE with its official links. **This repository does not relicense PCL as MIT, GPL, or another general-purpose open-source license. Visible source code does not grant unrestricted redistribution rights.**

The project's custom name and black-and-gold Windows theme differ from the upstream guidelines concerning name prefixes and a blue primary color. No exception authorization is included, and this repository does not claim to satisfy every condition for public binary distribution. Before distributing binaries or redistributing the project, review the upstream license and resolve these differences. Ricardo does not grant additional rights on behalf of the original author.

This is a free fan modification that respects the original author's rights. The existing interface statement expresses that the project is free, is only a derivative, and credits the original author. It does not transfer ownership of dnlib, other third-party components, or artwork to that author.

The rainy academy background was generated for the project. The academy crest and Ricardo avatar came from user-provided images; the original author's avatar came from the original launcher. No unrestricted relicensing authorization for these images is included, and publishing them alongside source code does not automatically grant commercial use or standalone redistribution rights. *Dragon Raja*, Minecraft, and related names and properties belong to their respective rights holders. This project is not an official product or authorized collaboration of those properties.
