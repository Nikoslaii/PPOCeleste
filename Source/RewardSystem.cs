using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.PPOCeleste {

    public static class RewardSystem {

        // previous values to compute reward deltas

        public static float ComputeReward(Dictionary<string, object> obs) {

            float reward = 0f;

            // --- On récupère les valeurs utiles ---
            float vx = (float)obs["vx"];
            float vy = (float)obs["vy"];
            float progress = (float)obs["progress"];
            float grounded = (float)obs["grounded"];
            float wall = (float)obs["wallcheck"];
            float ducking = (float)obs["ducking"];
            float stamina = (float)obs["stamina"];

            Vector2 movement = new Vector2(vx, vy);

            // --------- 1. REWARD DE PROGRESSION ---------
            // Dot product : si le joueur avance VERS l’objectif → reward positif
            reward += progress * 3f;  // reward brut de progression
            

            // --------- 2. VIVRE = POSITIF ---------
            reward += 0.05f;  // incite l'agent à ne pas mourir

            // --------- 3. DÉPLACEMENT ACTIF ---------
            reward += movement.Length() * progress *9f;

            // --------- 4. REWARD POUR ÉTAT AU SOL ---------
            if (grounded==1)
                reward += 0.15f;

            // --------- 5. PENALITÉ WALLHUG ---------
            if (wall==1 && grounded==0)
                reward -= 0.05f;

            // --------- 6. PENALITÉ POUR STAGNATION ---------
            if (movement.Length() < 0.10f)
                reward -= 0.10f;

            if (ducking==1)
                reward -= 0.25f;

            if (stamina == 0f)
                reward -= 0.05f;

            return reward;
        }

        public static float DeathPenalty() => -15f;

        public static float LevelCompleteReward() => 25f;
    }
}
