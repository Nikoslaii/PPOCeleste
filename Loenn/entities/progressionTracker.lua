local progressTracker = {}

progressTracker.name = "CelesteCustom/progressionTracker"
progressTracker.depth = 0
progressTracker.nodeLimits = {1, -1} -- garde les node entre 1 et l'infini

-- Champs généraux de l'entité
progressTracker.fieldInformation = {
    flag = { fieldType = "string" }, -- le nom de notre entité
}

progressTracker.placements = {--valeurs par défaut pour le placement de l'entité
    {
        name = "progressTracker",
        data = {
            flag = "progress_stage",
        }
    }
}

return progressTracker