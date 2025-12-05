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

-- Caractéristiques par node (type attendu)
progressTracker.nodeFieldInformation = {
    -- la progression à laquelle réinitialiser le joueur en cas de mort
    onDeath = { fieldType = "integer" }
}

-- Valeurs par défaut pour chaque node (array -> defaults pour chaque node)
progressTracker.nodeDefaults = {
    0  -- valeur par défaut pour onDeath
}

return progressTracker