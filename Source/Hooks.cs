/*
ce fichier est destiner à contenir les modification sur le personnage est tout interaction avec celeste
comme les input et observations

*/


using Microsoft.Xna.Framework;
using Monocle;
using System.Collections.Generic;
using static Celeste.Mod.PPOCeleste.PPOCelesteModule;


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
                    var level = self.Scene as Level;// stoque le niveau actuel
                    var obs = GetObservation(level);//récupère les variables/inputs pour l'entrainement
                    Instance.SendObsToPPO(obs,lastPlayer);//envois les inputs a PPOTorsh

                    Instance.GetActionFromPPO(); // récupère les actions a effectuer
                    ApplyActions(self);           // applique les actions reçues

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
            var obs = new Dictionary<string, object>();//creation de notre dictionnaire d'observation
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

                //construction des obs
                obs["x"] = lastPlayer.Position.X;
                obs["y"] = lastPlayer.Position.Y;
                obs["vx"] = lastPlayer.Speed.X; 
                obs["vy"] = lastPlayer.Speed.Y;
                obs["grounded"] = lastPlayer.OnGround();         // si Madeline est au sol
                obs["dashes_left"] = lastPlayer.Dashes;          // nombre de dash restant
                obs["wallcheck"] = GetWallCheck(lastPlayer);     // si Madeline touche un mur
                obs["grab"] = lastPlayer.StateMachine.State == Player.StClimb; // si Madeline est en train de grimper
                ProgressionTracker tracker = GetProgressTracker(level);

                if (tracker != null)
                    obs["progress"] = tracker.NextVector;
                else{
                    
                    obs["progress"] = Vector2.Zero;//progression dans la room par ProgressionTracker
                }
                obs["enemies"] = enemies;                     // liste des ennemies proches (coordonées et tailles)
                // Matrice 15*15 centrée sur le joueur (0: vide, 1: solide, 2(^)-3(>)-4(v)-5(<): piques)
                obs["grid"] = GetGrid(level, lastPlayer, 15);
            }
            return obs;

        }

        private static bool GetWallCheck(Player player)
        {
            // True si Madeline touche un mur à gauche ou à droite
            return player.CollideCheck<Solid>(player.Position + new Vector2(-1, 0)) // gauche
                || player.CollideCheck<Solid>(player.Position + new Vector2(1, 0)); // droite
        }


        
        private static ProgressionTracker GetProgressTracker(Level level) {
            ;

            if (level == null)
                return null;
            try{
            
                return level.Tracker.GetEntity<ProgressionTracker>();
            }
            catch{
                
            return null;
            }
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

            for (int dy = -half; dy <= half; dy++) // parcourir les lignes
            {
                for (int dx = -half; dx <= half; dx++) // parcourir les colonnes
                {
                    // avoir la possition de la tile 
                    int tileX = playerTileX + dx;
                    int tileY = playerTileY + dy;

                    Vector2 tilePos = new(tileX * tileSize, tileY * tileSize);//vecteur de la localisation de la case

                    // Échantillonnage : coins + centre
                    Vector2[] samplePoints = 
                    [
                        tilePos,
                        tilePos + new Vector2(tileSize-1, 0),
                        tilePos + new Vector2(0, tileSize-1),
                        tilePos + new Vector2(tileSize-1, tileSize-1),
                        tilePos + new Vector2(tileSize/2, tileSize/2)
                    ];

                    int val = 0; // valeur par défaut (0 = vide)

                    // Vérifier les spikes (il font moins que une demis case alors il falait chercher autour pour voir le spike)
                    foreach (var p in samplePoints)
                    {
                        // vérifie pour chaque spike si il est sur cette case j'ai pas encore plus simple #trouver plus simple
                        foreach (Spikes spike in level.Tracker.GetEntities<Spikes>()) 
                        {
                            if (spike.CollidePoint(p)) // si le point est dans le spike
                            {
                                switch (spike.Direction) // direction du spike
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
                        val = 1; // solide
                    }
                    grid.Add(val); 
                }
            }

            return grid;
        }

        //actione en fonction des output du PPO
        public static void ApplyActions(Player player) 
        {
            var actions = Instance.GetActionFromPPO();

            if (actions.TryGetValue("left", out bool left) && left)
            {
                player.Speed.X -= 1; // déplace Madeline vers la gauche
            }
            if (actions.TryGetValue("right", out bool right) && right)
            {
                player.Speed.X += 1; // déplace Madeline vers la droite
            }
            if (actions.TryGetValue("up", out bool up) && up)
            {
                player.Speed.Y -= 1; // déplace(direction) Madeline vers le haut
            }
            if (actions.TryGetValue("down", out bool down) && down)
            {
                player.Speed.Y += 1; // déplace(direction) Madeline vers le bas
            }

            if (actions.TryGetValue("jump", out bool jump) && jump && player.OnGround())
            {
                player.Jump(); // fait sauter Madeline
            }
            if (actions.TryGetValue("dash", out bool dash) && dash && player.Dashes > 0)
            {
                player.DashBegin(); // fait dasher Madeline
            }
            if (actions.TryGetValue("grab", out bool grab) && grab)
            {
                if (player.CollideCheck<Solid>(player.Position + Vector2.UnitX) || // droite
                    player.CollideCheck<Solid>(player.Position - Vector2.UnitX))   // gauche
                {
                    player.StateMachine.State = Player.StClimb; // passe en état de grimpe
                }
            }
        }
    }
}