---
title: Game Design
aliases:
  - GDD
tags:
  - game-design
status: working-draft
---

# Game Design

Related: [[First Mission]] · [[Technical Foundations]]

> [!abstract] Concept
> A programming sandbox in which the player remotely operates a planetary mining and research expedition. They automate machines, develop an industry, manage energy, and study local minerals and flora to reach 100% exploration of the expedition area.

## Player fantasy and setting

The player is a developer aboard a space station, connected to a hub on an unexplored planet. The station is the player's vantage point and connection to the expedition. No research, manufacturing, or resource processing takes place aboard it.

All operations happen on the planet. The starting hub integrates basic facilities; specialized buildings constructed later expand the expedition's capabilities and capacity.

The player's principal creation is an autonomous operation. Machines provide physical capabilities, but the player supplies their behavior and coordination.

Inspirations include the programming-centered play of *Screeps: World* and *The Farmer Was Replaced*, and the industrial complexity of peaceful *Factorio*.

## Design pillars

### Automation is the main activity

Progress comes from writing behavior, observing its results, and improving it. Manual commands support experimentation and intervention, while repeatable work creates reasons to automate.

### Simple capabilities create complex operations

Individual machines are understandable. Difficulty emerges from their relationships: shared energy, transport, limited storage, processing dependencies, research requirements, and increasing distances.

New equipment should regularly introduce a new coordination problem, alongside upgrades that improve existing capabilities.

### A forgiving workshop

There is no deadline, enemy pressure, or requirement for quick reactions. Players choose when to expand and introduce complexity. Mistakes create recoverable operational problems rather than routine destruction of progress.

### The player builds the tools

Interaction is through commands and player-written scripts. A terminal workspace provides a command input field, an output panel, and a small session-name/current-tick status bar. It provides no map renderer, equipment dashboard, or graphical management controls. If a player wants a 2D map, fleet report, or management tool, they write it themselves.

The information needed to understand the world and diagnose a stalled operation must remain available through queries. This freedom depends on accessible information and clear machine behavior.

## Expedition objective

Complete three survey tracks within a finite expedition area:

| Track | Completion goal | Typical challenges |
| --- | --- | --- |
| Cartography | Map all survey sectors and required terrain features. | Coverage, routes, access, instrument deployment. |
| Biodiversity | Discover and document all required local flora species. | Habitat investigation, sampling, analysis. |
| Geology | Characterize all required formations and mineral deposits. | Scanning, subsurface investigation, sampling, analysis. |

Geological completion requires understanding deposits, not extracting all their contents. Biodiversity completion must not depend on destroying or permanently exhausting a species.

The exact completion metrics and world-generation rules remain to be designed. Missing discoveries should become investigable rather than requiring a blind search for the final percentage point.

## Core loop

1. Survey an area and discover opportunities.
2. Collect samples and construction resources.
3. Analyze samples to advance survey records and earn research credits.
4. Spend credits to unlock equipment designs.
5. Manufacture equipment using physical resources.
6. Deploy and automate the new capabilities.
7. Expand into further areas and improve the operation.

## Research and industry

Mineral and flora analysis both contribute to equipment progression. Research credits unlock designs; resources pay for manufacturing. An unlocked design does not provide a free machine.

The working starting model uses a shared research-credit pool. Completing a distinct analysis awards credits once; repeating identical sample analysis does not create an unlimited source. Further research on a known discovery may have explicit additional requirements and rewards.

Mining supplies construction materials. Production, research, and field equipment consume energy and create transport and scheduling needs. Specialized surface buildings later extend the basic capabilities initially housed in the hub.

Essential progression must remain recoverable. Spending credits or consuming materials must not permanently prevent completion of the expedition.

## Energy and recovery

The starting hub provides efficient rover charging. The rover also carries a small solar panel that recharges its battery slowly away from the hub.

A depleted rover can eventually resume work without rescue. Poor energy planning costs expedition time and throughput. Field charging can also become a deliberate part of the player's strategy.

Power shortages and full buffers generally cause equipment to wait. Their causes must be inspectable. Exact power allocation and environmental rules remain open.

## Programming as progression

The player begins with individual commands, then builds reusable behaviors and coordination systems. Examples include resource collection loops, charging policies, route planning, power scheduling, and fleet assignment.

Movement illustrates the principle: vehicles provide directional movement within their speed capabilities. Destination-based navigation is a player-created behavior. Improved equipment expands physical capabilities; the player develops the intelligence that uses them.

Scripts are written in an external editor chosen by the player. Reference documentation is available inline in supporting IDEs; conceptual explanations and examples live in Markdown files. Documentation is not provided through an in-game interpreter help system.

The player receives one minimal starting script. Organizing automation into controllers, reusable modules, or event systems is their responsibility. Equipment does not arrive with an automatically managed player-script lifecycle.

## Pace and optional challenges

Players may take as long as they need. The expedition is complete when all three survey tracks reach 100%.

Players can optionally pursue the fastest completion. The proposed comparison uses simulation time so time spent paused for coding or inspection does not count. Other possible measures include energy consumption, material extraction, and equipment built; these are optional future design directions.

## Initial scope and open design work

[[First Mission]] defines the introductory landing-sector milestone.

Further design work includes the equipment catalogue, research economy, specialized buildings, production chains, exploration scale, terrain rules, and detailed survey requirements. [[Technical Foundations]] records implementation decisions separately from this design.
