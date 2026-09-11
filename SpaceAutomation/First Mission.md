---
title: First Mission
aliases:
  - Landing Sector
tags:
  - game-design
  - progression
status: working-draft
---

# First Mission

Related: [[Game Design]] · [[Technical Foundations]]

> [!note] Implementation sequence
> [[First Prototype]] validates the execution architecture before this gameplay milestone is implemented. Its small test world does not represent the landing sector.

> [!abstract] Objective
> Fully survey the landing sector and build the equipment needed to investigate its buried geological formation. Finish with a repeatable collection operation and tools that support further exploration.

## Role in the game

This is the first sandbox milestone, not a timed assignment. It introduces a complete discovery-to-manufacturing cycle in a small, forgiving area.

The sequence below is a teaching progression. Players may combine steps or solve them differently where dependencies allow. Success is measured by expedition results, not by a prescribed script structure.

## Starting area

The working introductory scenario uses a fixed, finite sector containing:

- A surface landing hub and accessible nearby terrain.
- Several sites to map and investigate.
- Two surface mineral types.
- One flora species.
- A buried formation requiring an instrument the player does not initially own.

Exact site counts, distances, rewards, and resource quantities remain unbalanced. Travel distances should allow comfortable early experimentation.

## Starting equipment

| Equipment | Initial role |
| --- | --- |
| Landing hub | Surface storage, sample reception, and efficient rover charging. |
| Hub analysis facility | Identifies mineral and flora samples, records discoveries, and awards research credits. |
| Hub fabricator | Builds unlocked equipment using collected resources. |
| Solar array and battery | Supply and buffer the expedition's initial power. |
| Survey scanner | Reveals terrain and flags mineral or biological signatures. |
| General-purpose rover | Travels, samples, collects materials, and delivers cargo. |
| Rover solar panel | Slowly restores charge in the field, allowing recovery from energy mistakes. |

Research and production are integrated into the hub at this stage. Nothing is processed on the orbital station. Specialized surface buildings arrive later.

## Progression

### 1. Discover nearby sites

The player inspects the available equipment and issues scans of the landing sector. Results reveal terrain and unresolved signatures.

**Outcome:** cartographic progress and at least one site worth sampling.

**Learning:** issuing commands, retrieving results, and interpreting world information. A map display is not supplied; the player can work directly from returned data or build a representation.

### 2. Retrieve and analyze a sample

The player moves the rover to a mineral signature, collects a sample, and brings it to the hub for analysis.

**Outcome:** a geological record, research credits, and knowledge of a usable material source.

**Learning:** sampling provides knowledge; bulk collection provides construction resources. The deposit does not need to be exhausted to count toward the survey.

### 3. Inspect and choose an unlock

The player examines available equipment designs and their credit costs. The subsurface probe is introduced as the instrument needed for the buried formation.

**Outcome:** understanding the distinction between unlocking a design and constructing equipment.

The starter catalogue and accessible analysis rewards must allow the player to obtain every offered starter design, including the required probe. Unlock order can vary without creating a dead end.

### 4. Automate material collection

Fabrication requires multiple collection trips. The player writes repeatable behavior for moving, gathering, delivering cargo, and charging.

**Outcome:** a collection operation that can repeat without individual travel commands and stop when its resource target is met.

**Learning:** limited cargo, vehicle movement limits, battery management, and conditions for starting or stopping work.

If the rover runs out of energy away from the hub, its solar panel enables slow recovery. No rescue vehicle is required.

### 5. Extend analysis and coordinate energy

Further investigation supplies samples of the second mineral and the local flora. Both analyses advance survey records and award research credits.

The proposed initial power balance supports individual facilities but cannot sustain all major loads indefinitely at once. The battery absorbs temporary overlap. The player can sequence scanning, charging, analysis, and fabrication or write a power policy.

**Outcome:** biological and geological progress, enough credits for starter unlocks, and a workable energy policy.

**Learning:** shared infrastructure creates coordination needs. Power interruptions cause waiting rather than damage or sample loss in this milestone.

### 6. Build and deploy the subsurface probe

The player unlocks the probe using research credits, fabricates it at the hub using gathered materials, and equips the rover to investigate the buried formation.

**Outcome:** the final required geological characterization and completion of the landing-sector survey, once the other survey requirements are met.

**Learning:** discoveries fund unlocks, industry makes them usable, and new instruments extend exploration.

## Completion criteria

- [ ] Every required site and terrain feature in the sector is mapped.
- [ ] Both surface minerals are characterized.
- [ ] The local flora species is sampled and documented.
- [ ] The buried geological formation is characterized using the manufactured probe.

These complete the sector's three survey tracks. Working automation is the intended learning outcome; it does not impose a single required code structure or implementation.

## What the player carries forward

- A hub supporting an active surface expedition.
- Reusable collection and charging behavior.
- Experience coordinating power and facility work.
- A manufactured subsurface probe.
- Research records and player-written inspection or navigation tools.

The next sector should increase distances and the number of sites, providing reasons to improve existing scripts and expand infrastructure.

## Remaining mission decisions

- Exact layout and scanner coverage rules.
- Sample quantities, analysis durations, and research-credit rewards.
- Starter unlock catalogue and manufacturing costs.
- Probe installation and use requirements.
- Initial energy, cargo, and vehicle speed values.
- Content of the external introductory guide and minimal script examples.
