# HUMAIN Coordination Tracker

Local Navisworks clash-history tracker for the HUMAIN project.

## Current MVP

The console application imports Navisworks Clash Detective XML reports and builds local historical CSV datasets suitable for Power BI.

### Daily workflow

1. Refresh the NWC files in the coordination NWF.
2. Run all Clash Detective tests.
3. Export reports as **All tests (separate) / XML**.
4. Put the XML files in one folder.
5. Run `HUMAIN.CoordinationTracker`.
6. Enter the XML folder and snapshot date.

The tracker compares clash GUIDs with the previous snapshot and classifies current results as `New` or `Existing`, while previous GUIDs that are missing are emitted as `Disappeared`.

## Local output

The application stores its data under:

`%LOCALAPPDATA%\HUMAIN.CoordinationTracker`

Generated outputs include:

- `Snapshots\yyyy-MM-dd\snapshot.csv`
- `clash_history.csv`
- `PowerBI\CurrentClashes.csv`
- `PowerBI\ClashHistory.csv`
- `PowerBI\DailyProgress.csv`
- `PowerBI\TestPerformance.csv`

## Power BI intent

The exported model is intended to support:

- open clash trends
- new vs disappeared clashes
- net burn rate
- clash-test performance
- aging and status analysis
- future forecast measures

## Framework

C# / .NET Framework 4.8.
