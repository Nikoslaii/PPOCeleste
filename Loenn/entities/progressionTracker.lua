local progressTracker = {}

progressTracker.name = "CelesteCustom/progressionTracker"
progressTracker.depth = 0
progressTracker.nodeLimits = {1, -1}  -- au moins 1 node, pas de limite
progressTracker.fieldInformation = {
    flag = { fieldType = "string" },
    ordered = { fieldType = "boolean" }
}

progressTracker.placements = {
    {
        name = "default",
        data = {
            flag = "progress_stage",
            ordered = true
        }
    }
}

return progressTracker