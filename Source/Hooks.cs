using Monocle;
using Celeste;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Threading;


namespace Celeste.Mod.PPOCeleste
{
    public static class Hooks
    {
        private static On.Celeste.Player.hook_Update updateHook;
        private static Player lastPlayer;
        private static Dictionary<string, object> latestObs = null;
        private static object lockObs = new object();
        private static float clock = 0f;
        private const float SendInterval = 1f / 20f; // 20 Hz



        public static void Load()
        {
            updateHook = (orig, self) =>
            {
                lastPlayer = self;
                orig(self);

                // Timer pour 20Hz
                clock += Engine.RawDeltaTime;
                if (clock >= SendInterval)
                {
                    clock = 0f;
                    var level = self.Scene as Level;
                    var obs = GetObservation(level);
                    PPOCelesteModule.Instance.SendObsToPPO(obs);

                    PPOCelesteModule.Instance.GetActionFromPPO();
                    ApplyActions(self);           // important

                    // On push dans la queue (non bloquant)
                    
                }
            };
            On.Celeste.Player.Update += updateHook;
        }


        public static void Unload()
        {
            if (updateHook != null)
            {
                On.Celeste.Player.Update -= updateHook;
            }    
            
        }

        // Fonction pour récupérer les données utiles pour PPO
        public static Dictionary<string, object> GetObservation(Level level = null)
        {
            var obs = new Dictionary<string, object>();
            if (lastPlayer != null)
            {

                // Ennemis proches (limité à 10 pour éviter surcharge)
                var enemies = new List<float>();
                if (level != null)
                {
                    int count = 0;
                    foreach (var entity in level.Tracker.GetEntities<Seeker>())
                    {
                        if (count >= 10) break;
                        enemies.Add(entity.Position.X);
                        enemies.Add(entity.Position.Y);
                        enemies.Add(entity.Width);
                        enemies.Add(entity.Height);
                        count++;
                    }
                }

                obs["x"] = lastPlayer.Position.X;
                obs["y"] = lastPlayer.Position.Y;
                obs["vx"] = lastPlayer.Speed.X;
                obs["vy"] = lastPlayer.Speed.Y;
                obs["grounded"] = lastPlayer.OnGround();
                obs["dashes_left"] = lastPlayer.Dashes;
                obs["wallcheck"] = GetWallCheck(lastPlayer);
                obs["grab"] = lastPlayer.StateMachine.State == Player.StClimb;
                obs["progress"] = GetProgress(level, lastPlayer);
                obs["enemies"] = enemies;
                // Matrice 15*15 centrée sur le joueur (0: vide, 1: solide, 2-3-4-5: spikes)
                obs["grid"] = GetGrid(level, lastPlayer, 15);
            }
            return obs;

        }

        private static bool GetWallCheck(Player player)
        {
            // True si Madeline touche un mur à gauche OU à droite
            return player.CollideCheck<Solid>(player.Position + new Vector2(-1, 0))
                || player.CollideCheck<Solid>(player.Position + new Vector2(1, 0));
        }

        private static float GetProgress(Level level, Player player)
        {
            // Exemple simple : progression horizontale dans la room DOIT CHANGER
            if (level == null) return 0f;
            float minX = level.Bounds.Left;
            float maxX = level.Bounds.Right;
            float px = Calc.Clamp(player.Position.X, minX, maxX);
            return (px - minX) / (maxX - minX);
        }

        private static List<int> GetGrid(Level level, Player player, int gridSize = 15)
        {
            var grid = new List<int>();
            if (level == null) return grid;

            int tileSize = 8; // taille d'une tuile en pixels
            int half = gridSize / 2; // half for centering

            // Position du joueur en coordonnées de tile
            int playerTileX = (int)(player.Position.X / tileSize);
            int playerTileY = (int)(player.Position.Y / tileSize);

            for (int dy = -half; dy <= half; dy++) // from -half to half inclusive for gridSize cells
            {
                for (int dx = -half; dx <= half; dx++)
                {
                    int tileX = playerTileX + dx;
                    int tileY = playerTileY + dy;
                    Vector2 tilePos = new(tileX * tileSize, tileY * tileSize);

                    // Échantillonnage : coins + centre
                    Vector2[] samplePoints =
                    {
                        tilePos,
                        tilePos + new Vector2(tileSize-1, 0),
                        tilePos + new Vector2(0, tileSize-1),
                        tilePos + new Vector2(tileSize-1, tileSize-1),
                        tilePos + new Vector2(tileSize/2, tileSize/2)
                    };

                    int val = 0;

                    // Vérifier les spikes
                    foreach (var p in samplePoints)
                    {
                        foreach (Spikes spike in level.Tracker.GetEntities<Spikes>())
                        {
                            if (spike.CollidePoint(p))
                            {
                                switch (spike.Direction)
                                {
                                    case Spikes.Directions.Up: val = 2; break;
                                    case Spikes.Directions.Right: val = 3; break;
                                    case Spikes.Directions.Down: val = 4; break;
                                    case Spikes.Directions.Left: val = 5; break;
                                }
                                break;
                            }
                        }
                        if (val != 0) break; // stop si spike détecté
                    }

                    // Vérifier solid uniquement si pas de spike
                    if (val == 0 && level.CollideCheck<Solid>(tilePos))
                    {
                        val = 1;
                    }
                    grid.Add(val);
                }
            }

            return grid;
        }

        public static void ApplyActions(Player player)
        {
            if (PPOTorch.ActionReceiver.GetKey("left"))
            {
                player.Speed.X -= 1; // ou ajuster player.Speed.X
            }
            if (PPOTorch.ActionReceiver.GetKey("right"))
            {
                player.Speed.X += 1;
            }
            if (PPOTorch.ActionReceiver.GetKey("up"))
            {
                player.Speed.Y -= 1; // ou ajuster player.Speed.X
            }
            if (PPOTorch.ActionReceiver.GetKey("down"))
            {
                player.Speed.Y += 1;
            }

            if (PPOTorch.ActionReceiver.GetKey("jump"))
            {
                player.Jump();
            }
            if (PPOTorch.ActionReceiver.GetKey("dash") && player.Dashes > 0)
            {
                player.DashBegin();
            }
            if (PPOTorch.ActionReceiver.GetKey("grab"))
            {
                if (player.CollideCheck<Solid>(player.Position + Vector2.UnitX) || // right
                    player.CollideCheck<Solid>(player.Position - Vector2.UnitX))   // left
                {
                    player.StateMachine.State = Player.StClimb; // sets climbing state
                }
            }
        }
    }
}