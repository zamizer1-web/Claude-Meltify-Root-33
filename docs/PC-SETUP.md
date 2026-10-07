# Continuing Root 33 on your PC

The design, the rules code and the title art are made in the cloud. Building the mod into Root, testing it and taking screenshots need your PC, because that is where Root and Clair Obscur are installed. Everything goes on your D: drive.

## What you do (about 2 minutes)

1. Open PowerShell (Windows key, type `PowerShell`, press Enter).

   - If you have never used Claude Code on this PC, install it and sign in once:

     ```powershell
     irm https://claude.ai/install.ps1 | iex
     ```

     Close PowerShell, open a new one, type `claude`, then `/login` and sign in with your Claude account. Type `/exit` when done.

2. Start Claude in a folder on D: and connect it to the Claude app:

   ```powershell
   mkdir D:\Mods -Force; cd D:\Mods; claude remote-control
   ```

   When it asks `Trust D:\Mods? [y/N]`, type `y`. The session then shows up in the Claude app, so you can keep talking to it from there.

3. Send it this message:

   > Clone https://github.com/zamizer1-web/Claude-Meltify-Root-33 into D:\Mods, check out the branch claude/clair-obscur-root-mashup-l7qcqt, and continue Root 33 from HANDOFF.md.

4. When it gets to publishing, open Melty Studio, copy a fresh **Publish** prompt and paste it in. The Melty key is never saved in the project.

## What Claude does from there

- Scans your PC (read-only) to find Root and Clair Obscur and see how each is built.
- Copies Root into a work folder on D: (your real Root install and your online games stay untouched), installs the mod loader there, and reads how Root keeps its board, turns and bots.
- Builds the mod, plays test games against the bots, and takes real screenshots.
- Packages it for Melty and checks it plays in one click, then asks you to press **Test** in the Melty app before anything is published.

It stops and asks you whenever it needs you: a Windows permission prompt, a login, or a choice.
