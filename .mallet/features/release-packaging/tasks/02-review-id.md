# Task: Review the plugin id change
Status: done
Deps: 01
Review: phase 1

## Goal
The widget runs under com.codemonk.devicebattery in all three places, and nothing still refers to the old id.

## Review focus
- No remaining reference to com.example.devicebatteries anywhere in the repository (code, manifest, scripts, README, plan notes that describe current state)
- The consistency test really fails when one of the three ids differs, and cannot pass by reading the wrong file
- Nothing keyed by the id was missed (data folders, cache paths, WidBar settings keys)
- The package Identity Name and AppExtension Id were left unchanged on purpose
