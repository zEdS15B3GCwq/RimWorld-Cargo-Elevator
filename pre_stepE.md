0\. Let's do manifests in E; VFE can be F

1. Since we're using the same storage and we're transporting items from that storage, will the manifest items be also transported away the same way? What happens to these items that were delivered but transported - will they be counted as missing and delivered again, or will they count as done
2. Similar to 1. Previously I asked whether all the items from the manifest must be kept together in order for the manifest to complete - does it? Do we need to keep the items in buffer (perhaps marked somehow not to be transported away)?
3. A1 cancel/unload: similar to 2 in terms of "do we need to know whether items came in for a manifest or just as a normal delivery" - if we need to mark items' origin/purpose, then we can also just patch the unload/cancel vanilla function to ignore items marked as not-for-manifest. Or we can write our own unload/cancel, but for that I need to know the complexity of the vanilla functions doing that - any changes there would have to be reflected in our code otherwise things could break.
4. A2 - similar to 1 and 2, when is the "load" process done, are we tracking it or is the game doing it and we need to follow its rules? When I understand that, I can decide how to "end" the loading process, and then I would understand what the proposal would actually do.
5. A3 - what is the vanilla message? Hard to decide without knowing that detail.
6. A4 - do not defer Harmony. That notion was valid while Harmony patches were discouraged, but now that we've had to do one anyway, we can explore another. I want to know how difficult it would be to patch the UI to cancel automatic adding of supplies, not to show supplies just items, not to show caravan/shuttle specific info such as travel time, and to allow pawns (add to future configurable options) or animals (also should be configurable). Mass is also irrelevant.
7. How complicated/safe can animals and pawns be transported? Also, for animals and pawns, our storage rules (no accepting storage etc.) can't apply, they must be dropped somewhere asap. Can we drop pawns/animals on the top of each other? In the game, pawns occupying the same cell certainly happens.
8. The contents tab shows two columns: one for load and one for the usual buffer. Who set it up like that? Does it mean that loaded items and normal deliveries are separate and distinguishable in some way? Or the load column shows what is required (not what's already contained)?
9. B5 - OK, show when working
10. B6 - OK, continue
11. B7 - allow loading to go over the limit, normal deliveries should stop due to overfilled slots, I think this is reasonable
12. B8 - Do manifest deliveries ignore the filter (i.e. can items be still delivered if not in filter)? If so, then I think it's a good choice that manifests ignore the filter. Filters are for normal deliveries.
13. B9 - let's not change the vanilla way
14. B10 - OK
15. C11 - I agree, E1 does items, part 2 of E can handle pawns and animals
16. E1 - if we're going to accept pawns then let's not waste time on patching the pawn part of the loading UI. Just ignore them for now.
17. E2 - need to discover where pawns can be placed; if just empty cells and on elevator (cannot be in filter anyway so won't be reabsorbed), then try adjacent empty cells; no need to have them arrive exactly on the elevator; no cell free - blocked delivery; I guess they do count as slots as in they block other items from arriving if slots are full
18. Pawns in the buffer - I don't expect to keep them there very long. However, as an alternative option in the future, we can monitor their vitals and start sending notifications if pawns are left in "too long"(TM).



OK, start writing up the plan file for step E.

