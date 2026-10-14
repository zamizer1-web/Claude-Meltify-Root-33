# What the research changed

All 65 research findings are now in the sheets. Each confirmed or corrected row has a `_source` note with its links, and each corrected row says what changed in its `_notes`. The core check still passes with 0 blocking issues.

## The big changes

1. **Two Vagabonds need no DLC.** Root digital has allowed two Vagabonds in any game since update 1.27.0, with no Riverfolk purchase needed ([patch notes](https://news.direwolfdigital.com/root-update-1-27-0-the-riverfolk-expansion/)). This means a base-game table can seat two of Esquie, Verso and Maelle, and the Soarrie hand-off from Verso to Esquie works without DLC. Riverfolk is now needed only for Clea. In a two-Vagabond game each ruin holds 2 items and the Vagabonds share one hand of quests ([Dire Wolf](https://news.direwolfdigital.com/whats-new-in-the-riverfolk-expansion/)). Dorrie's rule was fixed to match, and a test was added for it. Rows updated: table seating, DLC detection, the dlc base and riverfolk rows, the three Vagabond engines, the Soarrie hand-off, the Soarrie and Dorrie spawns, and the Verso, Esquie and Maelle notes.
2. **DLC is checked through Root first.** Root DLC belongs to your Dire Wolf account and unlocks on Steam, mobile and Switch, so a check by Steam app id alone would hide characters from people who bought the DLC on another device. The mod now reads Root's own entitlements first and uses the Steam app ids only as a fallback. All six app ids are confirmed from the store pages: base 965580, Riverfolk 1586100, Underworld 2253060, Marauder 3346620, Exiles & Partisans & Vagabonds 1935900, Hirelings & Landmarks 3874110.
3. **Native AI exists for 9 of the 10 factions.** Dire Wolf's patch notes name an AI for every faction except the Lizard Cult, so the fallback planner probably matters only for Clea.
4. **Clair Obscur mode is offline only.** It runs solo or as pass-and-play. Online opponents would not run the mod, and Root gives a timed-out online seat to its Hard AI. The save hook no longer mentions online turns.
5. **Reskinned text is swapped at runtime.** A modder edited Root's localisation files and nothing changed in the game ([thread](https://steamcommunity.com/app/965580/discussions/0/802342156092132314/)). Reskins will therefore patch the places where Root looks up its text while the game runs. All 67 reskin rows now say so.
6. **The Reacher is not an Axon.** The Axons in Clair Obscur are Visages and Sirène. The Reacher is just a place name: Maelle's flight-only quest area, where she fights Alicia ([wiki](https://expedition33.wiki.fextralife.com/The_Reacher)). The wording is fixed everywhere it appeared. The rules themselves don't change.
7. **Lore fixes.**
   - The Curator is fought just before the final choice between Maelle and Verso, so he isn't literally the last fight.
   - The story that Clea "captured and repainted" her painted copy had no source, so it was removed. The Flying Manor fight is optional, not a "secret superboss".
   - Only Lune's mother, Brigitte, is confirmed on Expedition 46. Lune's quest at Sirène's Dress goes looking for her parents.
   - "Sciel finds Dorrie" now has one source.
   - Esquie wins Urrie from François in a level-6 duel.
   - The Paintress's "head of the Painters' Council" title is verified, but it is finished-mode only.
   - A few verified spoilers were added as [finished] sentences for Gustave, Verso, Maelle and Monoco.
8. **Housekeeping.** The autumn.01 note no longer calls the board facts guesses. Corner clearings now carry a note about how landmarks change them.

## Changes for files I did not edit

These two files are read-only for me, so here are the changes for you to make:

- **design/roster-draft.json**
  - Set Esquie's, Verso's and Maelle's `needs_dlc` to "None" and say a second Vagabond needs no DLC.
  - In the V1 SCOPE and TABLE notes, replace the "Riverfolk opens a second Vagabond seat" and "only one Vagabond without Riverfolk" lines with "Up to two of Esquie, Verso and Maelle can be seated in any game; Root digital supports two Vagabonds without DLC."
  - Wherever the notes call The Reacher an Axon, change it to "the Axons Visages and Sirène, The Reacher".
  - Update the cautious-wording list: the Painters' Council title is now verified but finished-mode only.
  - Florrie's "one config line moves Florrie to the Nest" fallback is no longer needed, because Florrie at Stone Wave Cliffs is confirmed ([wiki](https://expedition33.wiki.fextralife.com/Stone_Wave_Cliffs)).
- **design/autumn-map-verified.json `_about`**: replace "(the digital edition uses the same board)" with "(in the digital edition the Autumn suits are fixed every game, per a community report; whether they match this printed layout is still to confirm on the creator's PC, along with Root's clearing ids)". Nothing shows that the digital board differs from the printed one, but nothing confirms it matches either ([thread](https://steamcommunity.com/app/965580/discussions/0/3719440678462607815/)).

## Design notes worth knowing

- **Hirelings and landmarks (v1.1).**
  - They are only switched on through Advanced Setup, and only players who own 6 or more factions can use it.
  - Hirelings unlock at 4, 8 and 12 VP, which falls inside your Act I and II windows.
  - A faction's hireling never appears while that faction is seated, so the Forest Patrol (the Expedition 33 survivors) only shows up without Gustave.
  - Elder Treetop and Lost City change slots and suits on the corners Root 33 uses as homes. Either read the board live or keep landmarks off in this mode.
  - The Ferry, 8 of the 13 hirelings, and the Street Band have no theme yet.
- **Pacing.** In Clair Obscur, Lune's Sirène's Dress quest, Monoco's Sacred River quest, the Flying Manor, the Endless Tower and The Reacher all need flight, which comes at the end of Act II. The mod opens the Parents' Journal and Monoco's goals in its own Act II. That is fine as a creative liberty, and it is noted on the row.
- **Difficulty.** Root's AI levels are Easy, Normal and Hard, the same names as the mod's, and Root already asks for one when you create a solo game. The mod can reuse that choice instead of asking twice. On Hard, watch for Root's AI and the spoiler layer both piling onto the leader.
- **Clair Obscur art.** Clair Obscur is Unreal Engine 5, so its portraits and sounds must be pulled out of the game's packed files on your PC. Keep reading them from the player's own install at runtime, or use original art, rather than shipping extracted art.
- **Root version.** The target is Root 2.1.x (2.1.2 is the latest patch). The 2.0 update once broke resuming saved games, so the plugin should check Root's version at startup and record it with its save data.

## Still unknown: needs your PC

**Root's files**
- Whether Root's Steam build is Mono or IL2CPP, and which Unity version it uses.
- Root's own names for the 12 engines, the hooks, board reads, board actions and the 67 reskin text keys.
- Root's number for each of the 12 Autumn clearings, and whether the digital Autumn board matches the printed one.
- Whether the Lizard Cult has a native AI.
- How Root stores the AI difficulty, and how it records DLC ownership.
- Whether a faction whose DLC you don't own can still sit at an offline table as an AI.
- Three-Vagabond games: whether they are possible, how two Vagabonds share ruins and quests, and whether the two must be different characters.
- The full digital hireling list.
- Whether hirelings and landmarks work outside Advanced Setup, and whether Advanced Setup clashes with Root 33's forced homes.
- Where saves live and what format they use.
- The order of start-of-Birdsong abilities and the timing of Field Hospital.
- The existing week-1 spike items: the legal-action API, placing another seat's warriors, relic visibility, Warlord, Aid cost and Duchy setup.

**Clair Obscur's files**
- The 21 asset paths inside its packed files (latest patch 1.5.6).
- In-game wording the wiki can't settle:
  - a name for Gustave's arm, and whether Emma is his twin
  - Lune's father and the 'Dorrie'/'Dorie' spelling
  - whether the Curator is "trapped since the Fracture"
  - Clea's details
  - any "Regent" title, and whether "Painted Renoir" is an in-game label
  - who erases at the Monolith
  - how Monoco and Esquie speak
  - what The Reacher looks like
  - whether the Mimes and Petanks exist under those names

## Main sources

- Root patch notes: [1.27.0](https://news.direwolfdigital.com/root-update-1-27-0-the-riverfolk-expansion/), [Apr 2023](https://news.direwolfdigital.com/root-patch-4-14-2023/), [1.34.0](https://news.direwolfdigital.com/root-patch-1-34-0/), [all Steam news](https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=965580&count=300&maxlength=0&format=json)
- Dire Wolf articles: [Riverfolk](https://news.direwolfdigital.com/whats-new-in-the-riverfolk-expansion/), [Underworld](https://news.direwolfdigital.com/root-the-underworld-expansion-now-available/), [landmarks](https://news.direwolfdigital.com/landmarks-in-root-digital/), [hirelings](https://news.direwolfdigital.com/an-introduction-to-hirelings/)
- Steam store pages: [Root](https://store.steampowered.com/app/965580/Root/), [its DLC list](https://store.steampowered.com/dlc/965580/)
- Clair Obscur wiki: [Walkthrough](https://expedition33.wiki.fextralife.com/Walkthrough), [Esquie](https://expedition33.wiki.fextralife.com/Esquie), [Clea](https://expedition33.wiki.fextralife.com/Clea), [Lune](https://expedition33.wiki.fextralife.com/Lune), [Bosses](https://expedition33.wiki.fextralife.com/Bosses)
