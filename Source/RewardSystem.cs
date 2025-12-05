using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.PPOCeleste {

    public static class RewardSystem {

        // previous values to compute reward deltas
        private static Vector2 lastPos;
        private static bool isInitialized = false;

        public static float ComputeReward(Dictionary<string, object> obs, Player player) {

            float reward = 0f;

            // --- On récupère les valeurs utiles ---
            float x = (float)obs["x"];
            float y = (float)obs["y"];
            float vx = (float)obs["vx"];
            float vy = (float)obs["vy"];
            Vector2 progress = (Vector2)obs["progress"];  // direction normalisée
            int dashesLeft = (int)obs["dashes_left"];
            bool grounded = (bool)obs["grounded"];
            bool wall = (bool)obs["wallcheck"];

            // --- Première frame après spawn ---
            if (!isInitialized) {
                lastPos = new Vector2(x, y);
                isInitialized = true;
                return 0f;
            }

            Vector2 currentPos = new Vector2(x, y);
            Vector2 movement = currentPos - lastPos;

            // --------- 1. REWARD DE PROGRESSION ---------
            // Dot product : si le joueur avance VERS l’objectif → reward positif
            float directionalReward = Vector2.Dot(movement, progress);
            reward += directionalReward * 3f;   // facteur important → drive agent

            // --------- 2. VIVRE = POSITIF ---------
            reward += 0.05f;  // incite l'agent à ne pas mourir

            // --------- 3. DÉPLACEMENT ACTIF ---------
            reward += movement.Length() * 0.2f;

            // --------- 4. REWARD POUR ÉTAT AU SOL ---------
            if (grounded)
                reward += 0.03f;

            // --------- 5. PENALITÉ WALLHUG ---------
            if (wall)
                reward -= 0.05f;

            // --------- 6. PENALITÉ POUR STAGNATION ---------
            if (movement.Length() < 0.05f)
                reward -= 0.03f;

            // save last position
            lastPos = currentPos;

            return reward;
        }

        public static float DeathPenalty() => -15f;

        public static float LevelCompleteReward() => 25f;

        public static void Reset() {
            isInitialized = false;
        }
    }
}
