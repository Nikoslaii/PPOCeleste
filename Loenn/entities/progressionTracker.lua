local progressTracker = {}

progressTracker.name = "CelesteCustom/progressionTracker"
progressTracker.depth = 0
progressTracker.nodeLimits = {1, -1}

progressTracker.fieldInformation = {
    flag = { fieldType = "string" },
    ordered = { fieldType = "boolean" },
    onDeath = { fieldType = "integer" }
}

progressTracker.placements = {
    {
        name = "progressTracker",
        data = {
            flag = "progress_stage",
            ordered = true,
            onDeath = -1
        }
    }
}

return progressTracker