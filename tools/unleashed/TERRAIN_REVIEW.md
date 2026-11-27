# Unleashed terrain review

All 64 assignments were reviewed against the original UI preview pictures and decoded ground maps. Material names and bump resources were verified against the installed Reflex databases.

| Source | Track | Reflex terrain profile |
| --- | --- | --- |
| FS01 | Little Rock FMX | stadium |
| FS02 | Austin FMX | stadium |
| FS03 | Boston FMX | stadium |
| FS04 | St. Paul FMX | stadium |
| FS05 | New Orleans FMX | stadium |
| FS06 | Las Vegas FMX | stadium |
| FS07 | Portland FMX | stadium |
| FS08 | Orlando FMX | stadium |
| FS09 | Ajo Mining FMX | arid |
| FS10 | Woodland FMX | woodland |
| FS11 | Laredo Plains FMX | arid |
| FS12 | Fort Dwire FMX | woodland |
| FS13 | Calgary Lodge FMX | snow |
| MC01 | Killington | snow_mud |
| MC02 | Desert Oasis | dunes |
| MC03 | Red Country | red_clay |
| MC04 | Glamis South | dunes |
| MC05 | Woodstock | dry_woodland |
| MC06 | Monster Stadium | stadium |
| MC07 | Trophy Stadium | stadium |
| MC08 | 19th Hole | stadium |
| MC09 | Quechee Gulch | dry_woodland |
| MC10 | Land and Air | woodland |
| Nat01 | Goblin Gulch | arid |
| Nat02 | Tipperary | woodland |
| Nat03 | Glamis | dunes |
| Nat04 | Oceanside | coastal |
| Nat05 | Lago | arid |
| Nat06 | Juniper Park | red_clay |
| Nat07 | Valhalla | snow |
| Nat08 | Redwood Valley | woodland |
| Nat09 | Deadman Wash | arid |
| Nat10 | Maple Hill | woodland |
| Nat11 | Sutter Raceway | dry_woodland |
| Nat12 | Temple of Apophis | dunes |
| Nat13 | Sienna Ridge | red_clay |
| Nat14 | Santiago | woodland |
| Nat15 | Smuggler's Notch | dry_woodland |
| Nat16 | Northern Lights | snow_mud |
| Nat17 | Keweni Falls | woodland |
| Nat19 | Spokane | woodland |
| SM01 | Mesa | supermoto |
| SM02 | Highlands | supermoto |
| SM03 | The Glades | supermoto |
| SX01 | Los Angeles | stadium |
| SX02 | Denver | stadium |
| SX03 | Savannah | stadium |
| SX04 | San Diego | stadium |
| SX05 | Austin | stadium |
| SX06 | Little Rock | stadium |
| SX07 | St. Paul | stadium |
| SX08 | Boston | stadium |
| SX09 | New Orleans | stadium |
| SX10 | Toronto | stadium |
| SX11 | Orlando | stadium |
| SX12 | Philadelphia | stadium |
| SX13 | Las Vegas | stadium |
| SX14 | Mexico City | stadium |
| SX15 | Portland | stadium |
| SX16 | Tucson | stadium |
| SX17 | Charleston | stadium |
| SX18 | Boulder | stadium |
| SX19 | Raleigh | stadium |
| SX20 | Corona | stadium |

Snow is limited to Valhalla, Calgary Lodge, Killington, and Northern Lights. Temple of Apophis is a desert at night: its blue source lighting must not be mistaken for snow. Northern Lights and Killington retain darker mud where the ground map shows it. Desert crests stay sand instead of becoming slate because of their slope. Dry woodland uses Reflex dry grass, while green woodland uses its green grass. Supermoto pavement uses a separate Reflex asphalt layer.

The profile files are explicit: new tracks require a reviewed assignment. Surface inference still uses source material IDs, color, and slope where the original marks everything as dirt; it does not reproduce every painted patch exactly. No source ground color pixels are rendered as the new material.
