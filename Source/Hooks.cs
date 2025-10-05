/*
ce fichier est destiner à cotenir les modification sur le personnage est tout interaction avec celeste
comme les input et observations

*/


using Monocle;
using Celeste;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Threading;


namespace Celeste.Mod.PPOCeleste
{
    public static class Hooks
    {
        private static On.Celeste.Player.hook_Update updateHook;//permet plus facilement la l'ajout à la fonction #risque de changer
        private static Player lastPlayer;//pour garder en mémoire l'état du joueur à l'instant de l'observation
        private static float clock = 0f;// compteur pour connaitre le temps passer
        private const float SendInterval = 1f / 20f; // 20 Hz



        public static void Load()//au lancement du mod
        {
            // ajoute à la fonction de mise a jour du personage effectuer a chaque frame(60 par seconde)différente fonctionalité
            updateHook = (orig, self) =>
            {
                
                orig(self);//fonction originel de On.Celeste.Player.hook_Update soit player.update()

                // Timer pour 20Hz
                clock += Engine.RawDeltaTime;// mets à jour le temps passer
                if (clock >= SendInterval)//compare pour vérifier que ça fait 1/20 seconde
                {
                    lastPlayer = self;//stoque l'état du joueur a cette frame 
                    clock = 0f;//réinitialise la clock  
                    var level = self.Scene as Level;// stoque le niveau actuel pour une utilisation claire
                    var obs = GetObservation(level);//récupère les variable/input pour l'entrainement
                    PPOCelesteModule.Instance.SendObsToPPO(obs);//envois les input a PPOTorsh

                    PPOCelesteModule.Instance.GetActionFromPPO(); // peut être 
                    ApplyActions(self);           // important

                    // On push dans la queue (non bloquant)

                }
            };
            On.Celeste.Player.Update += updateHook;// change la fonction du player par notre nouvelle fonction 
        }


        public static void Unload()//pour la desactivation du mod
        {
            if (updateHook != null)
            {
                On.Celeste.Player.Update -= updateHook;//retire notre fonction du jeu
            }

        }

        // Fonction pour récupérer les données utiles pour PPO
        public static Dictionary<string, object> GetObservation(Level level = null)
        {
            var obs = new Dictionary<string, object>();//creation de notre 
            if (lastPlayer != null && level != null)//répétitif avec la logique de update mais au cas ou
            {

                // Ennemis proches (limité à 10 pour éviter surcharge) #TODO mettre la limite sur Torch
                var enemies = new List<float>();

                int count = 0;//compte le nombre d'ennemie
                foreach (var entity in level.Tracker.GetEntities<Seeker>())
                {
                    if (count >= 10) break;//limite à 10 ennemie en coupant la boucle
                    enemies.Add(entity.Position.X);
                    enemies.Add(entity.Position.Y);
                    enemies.Add(entity.Width);
                    enemies.Add(entity.Height);
                    count++;
                }
                
                //construction des 
                obs["x"] = lastPlayer.Position.X;
                obs["y"] = lastPlayer.Position.Y;
                obs["vx"] = lastPlayer.Speed.X;
                obs["vy"] = lastPlayer.Speed.Y;
                obs["grounded"] = lastPlayer.OnGround();
                obs["dashes_left"] = lastPlayer.Dashes;
                obs["wallcheck"] = GetWallCheck(lastPlayer);
                obs["grab"] = lastPlayer.StateMachine.State == Player.StClimb; 
                obs["progress"] = GetProgress(level, lastPlayer);//variable d'avancement à optimiser #va impérativement changer avec Loën
                obs["enemies"] = enemies;
                // Matrice 15*15 centrée sur le joueur (0: vide, 1: solide, 2(^)-3(>)-4(v)-5(<): spikes)
                obs["grid"] = GetGrid(level, lastPlayer, 15);
            }
            return obs;

        }

        private static bool GetWallCheck(Player player)
        {
            // True si Madeline touche un mur à gauche ou à droite
            return player.CollideCheck<Solid>(player.Position + new Vector2(-1, 0))
                || player.CollideCheck<Solid>(player.Position + new Vector2(1, 0));
        }

        private static float GetProgress(Level level, Player player)
        {
            // Exemple simple : progression horizontale dans la room #DOIT CHANGER
            float minX = level.Bounds.Left;
            float maxX = level.Bounds.Right;
            float px = Calc.Clamp(player.Position.X, minX, maxX);
            return (px - minX) / (maxX - minX);
        }

        //fonction pour optenir une grille de "la vision" de l'agent# à optimiser
        private static List<int> GetGrid(Level level, Player player, int gridSize = 15)
        {
            var grid = new List<int>();

            int tileSize = 8; // taille d'une tuile en pixels
            int half = gridSize / 2; // half pour centrer

            // Position du joueur en coordonnées de tile
            int playerTileX = (int)(player.Position.X / tileSize);
            int playerTileY = (int)(player.Position.Y / tileSize);

            for (int dy = -half; dy <= half; dy++) // de moins a plus pour permetre d'avoir les casse autour de l'agent
            {
                for (int dx = -half; dx <= half; dx++) // de même
                {
                    int tileX = playerTileX + dx;// avoir la possition de la tile
                    int tileY = playerTileY + dy;
                    Vector2 tilePos = new(tileX * tileSize, tileY * tileSize);//vecteur de la localisation de la case

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

                    // Vérifier les spikes (il font moins que une demis case alors il falait chercher autour pour voir le spike)
                    foreach (var p in samplePoints)
                    {
                        foreach (Spikes spike in level.Tracker.GetEntities<Spikes>())//vérifie pour chaque spike si il est sur cette case j'ai pas encore plus simple #trouver plus simple
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

                    // Vérifier solide uniquement si pas de spike
                    if (val == 0 && level.CollideCheck<Solid>(tilePos))
                    {
                        val = 1;
                    }
                    grid.Add(val);
                }
            }

            return grid;
        }

        //actione en fonction des output du PPO
        public static void ApplyActions(Player player)
        {
            var actions = PPOCelesteModule.Instance.GetActionFromPPO();

            if (actions.TryGetValue("left", out bool left) && left)
            {
                player.Speed.X -= 1; // ou ajuster player.Speed.X
            }
            if (actions.TryGetValue("right", out bool right) && right)
            {
                player.Speed.X += 1;
            }
            if (actions.TryGetValue("up", out bool up) && up)
            {
                player.Speed.Y -= 1; // ou ajuster player.Speed.X
            }
            if (actions.TryGetValue("down", out bool down) && down)
            {
                player.Speed.Y += 1;
            }

            if (actions.TryGetValue("jump", out bool jump) && jump)
            {
                player.Jump();
            }
            if (actions.TryGetValue("dash", out bool dash) && dash && player.Dashes > 0)
            {
                player.DashBegin();
            }
            if (actions.TryGetValue("grab", out bool grab) && grab)
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