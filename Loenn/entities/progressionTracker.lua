local progressTracker = {}

progressTracker.name = "CelesteCustom/progressionTracker"
progressTracker.depth = 0
progressTracker.nodeLimits = {1, -1} -- garde les node entre 1 et l'infini

progressTracker.fieldInformation = {
    flag = { fieldType = "string" }, -- le nom de notre entité
}

progressTracker.placements = {
    {
        name = "progressTracker", -- valeur par défaut
        data = {
            flag = "progress_stage",
        }
    }
}

progressTracker.nodeFieldInformation = { -- caractéristiques par node
    onDeath = { fieldType = "integer" }
}

-- Défauts pour les nodes : tableau d'entrées (ici une entrée par défaut appliquée à tous les nodes)
progressTracker.nodeDefaults = {
    { onDeath = 0 }
}

return progressTracker