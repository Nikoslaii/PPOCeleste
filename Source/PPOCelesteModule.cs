using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Celeste;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.PPOCeleste;

public class PPOCelesteModule : EverestModule
{
    public static PPOCelesteModule Instance { get; private set; }

    public override Type SettingsType => typeof(PPOCelesteModuleSettings);
    public static PPOCelesteModuleSettings Settings => (PPOCelesteModuleSettings)Instance._Settings;

    public override Type SessionType => typeof(PPOCelesteModuleSession);
    public static PPOCelesteModuleSession Session => (PPOCelesteModuleSession)Instance._Session;

    public override Type SaveDataType => typeof(PPOCelesteModuleSaveData);
    public static PPOCelesteModuleSaveData SaveData => (PPOCelesteModuleSaveData)Instance._SaveData;

    public PPOCelesteModule()
    {
        Instance = this;
#if DEBUG
        Logger.SetLogLevel(nameof(PPOCelesteModule), LogLevel.Verbose);
#else
        Logger.SetLogLevel(nameof(PPOCelesteModule), LogLevel.Info);
#endif
    }

    private PPOAgent ppo;

    // Episode management (public for access from Hooks)
    public float episodeTimer = 0f;
    public int episodeCount = 0;
    public float totalReward = 0f;
    public bool isResetting = false; // Prevent double EndEpisode from death hook

    public const int TRAIN_EVERY_N_EPISODES = 10; // Train after every 10 episodes

    // Public access to PPO agent
    public PPOAgent PPO => ppo;
    [CustomEntity("CelesteCustom/progressionTracker")]
    public class ProgressionTracker : Entity {
        private string flag;
        private List<Vector2> points = [];
        public Vector2 NextVector { get; private set; } = Vector2.Zero; // vecteur vers le prochains points

        public ProgressionTracker(EntityData data, Vector2 offset) : base(data.Position + offset) {
            flag = data.Attr("flag", "progress_stage");

            Tag |= Tags.Global | Tags.Persistent; // permet de garder chargé l'entité dans tout le niveau

            // Récupère les positions depuis le dictionnaire Values
            for (int i = 0; i < data.Nodes.Length; i++)
            {
                Vector2 pos = data.Nodes[i] + offset;
                points.Add(pos);
            }
        }

        public override void Update() {
            base.Update();

            Level level = SceneAs<Level>();
            if (level == null) return;

            Player player = Scene.Tracker.GetEntity<Player>();
            

            
            if (player == null)
                return;

            int progress = level.Session.GetCounter(flag);
    
            if (progress < points.Count) { // il reste des points à atteindre
                Vector2 nextPoint = points[progress]; // prochain point à atteindre
                float distance = Vector2.Distance(player.Center, nextPoint);

                NextVector = nextPoint - player.Center;

                if (distance < 16f) { // si le joueur est proche du point
                    level.Session.SetCounter(flag, progress + 1); // incrémente le progrès

                    // Donne la récompense à l'agent PPO (AddReward, not EndEpisode)
                    Instance.PPO.AddReward(RewardSystem.LevelCompleteReward());
                    
                    // Reset timer to give more time after reaching checkpoint
                    Instance.episodeTimer = 0f;

                    if (progress + 1 >= points.Count) {
                        level.Session.SetFlag(flag + "_done", true);
                        // End episode when all checkpoints completed
                        Instance.PPO.EndEpisode(0f);
                        Instance.ResetEpisode(player, true);
                    }
                }
            } else {
                NextVector = Vector2.Zero;
            }
        }

        public override void Render() {
            base.Render();
            #if DEBUG

            // Couleur principale des nodes
            Color nodeColor = Color.Lime;
            Color textColor = Color.White;

            // Dessine les points et leur index
            for (int i = 0; i < points.Count; i++) {
                Vector2 p = points[i];

                // Cercle du node
                Draw.Circle(p, 8f, nodeColor, 4);

                // Texte au centre : l'index
                ActiveFont.DrawOutline(
                    i.ToString(),                    // Texte = index
                    p,                              // Position = node
                    new Vector2(0.5f, 0.5f),        // Centré sur le point
                    Vector2.One * 0.5f,             // Taille du texte (facultatif)
                    textColor,                      // Couleur principale
                    2f,                             // Épaisseur du contour
                    Color.Black                     // Couleur du contour
                );
            }

            // Dessine le vecteur vers le prochain node
            if (NextVector != Vector2.Zero) {
                Player player = Scene.Tracker.GetEntity<Player>();
                if (player != null) {
                    Vector2 from = player.Center;
                    Vector2 to = from + NextVector;
                    Draw.Line(from, to, Color.Yellow);
                    Draw.Circle(to, 4f, Color.Yellow, 3);
                }
            }
        
            #endif
        }
    }



    public void ResetEpisode(Player player, bool completed = false) {
        isResetting = true; // Set flag to prevent death hook recursion
        episodeCount++;
        
        // Log episode statistics
        Logger.Log("PPO", $"Episode {episodeCount} ended - Time: {episodeTimer:F2}s, Reward: {totalReward:F2}, Status: {(completed ? "COMPLETED" : "TIMEOUT/DEATH")}");
        
        // Trigger training every N episodes
        if (episodeCount % TRAIN_EVERY_N_EPISODES == 0) {
            Logger.Log("PPO", $"Training after {episodeCount} episodes...");
            ppo.UpdatePolicy();
            
            // Save updated weights
            string weightsPath = Path.Combine(Everest.PathGame, "Mods", "PPOCeleste", "ppo_weights.json");
            ppo.SaveWeights(weightsPath);
            Logger.Log("PPO", "Weights saved.");
        }
        
        // Reset episode state
        episodeTimer = 0f;
        totalReward = 0f;
        
        // Reset level if player is alive
        if (player != null && !player.Dead) {
            var level = player.Scene as Level;
            if (level != null) {
                // Reset to beginning of level
                level.Session.RespawnPoint = level.Session.LevelData.Spawns[0];
                player.Die(Vector2.Zero);
            }
        }
        
        isResetting = false; // Clear flag after reset complete
    }

    public static ProgressionTracker GetTracker(Scene Scene) {
    return Scene.Tracker.GetEntity<ProgressionTracker>();
    }


    public static class DrawUtils {
        public static void Circle(Vector2 center, float radius, Color color, int segments = 12) {
            Vector2 last = center + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++) {
                float angle = MathHelper.TwoPi * (i / (float)segments);
                Vector2 next = center + new Vector2(
                    (float)Math.Cos(angle) * radius,
                    (float)Math.Sin(angle) * radius
                );
                Draw.Line(last, next, color);
                last = next;
            }
        }
    }

    private static void DrawActionCircles(Player player, Dictionary<string, bool> actions) 
    {
        Vector2 start = player.Position + new Vector2(-10 * actions.Count / 2f, -25f);

        int i = 0;
        foreach (var kv in actions) {
            bool active = kv.Value;
            Vector2 pos = start + new Vector2(i * 12, 0f);

            Color c = active ? Color.LimeGreen : Color.Red;
            DrawUtils.Circle(pos, 4f, c);

            i++;
        }
    }




    public override void Load()//lancé au chargement du mod 
    {
        Hooks.Load();

    }

    private static On.Celeste.Player.hook_Die OnDeathHook;

    public override void Initialize()//lancé après le chargement 
    {
        ppo = new PPOAgent(obsSize: 277, hiddenSizes: [128,64]);

        string weightsPath = Path.Combine(Everest.PathGame, "Mods", "PPOCeleste", "ppo_weights.json");

        if(File.Exists(weightsPath))
        {
            ppo.LoadWeights(weightsPath);
        }
        else
        {
            Logger.Log("PPO", "No weights found, starting fresh.");
            ppo.SaveWeights(weightsPath);
        }



        
        OnDeathHook = (orig, self, direction, evenIfInvincible, registerDeathInStats) =>
        {
            var result = orig(self, direction, evenIfInvincible, registerDeathInStats);
            
            if (!Instance.isResetting) {
                Instance.ppo.EndEpisode(RewardSystem.DeathPenalty());
            }

            return result;
        };        
        
        On.Celeste.Player.Die += OnDeathHook;


        On.Celeste.Player.Render += static (orig, self) => { 
             
            orig(self);

            // Use CACHED actions from Hooks to avoid re-sampling (and modifying buffers) during Render
            var actions = Hooks.LastActions;
            if (actions == null)
                return;

            DrawActionCircles(self, actions); // Dessine les cercles d'actions
        };

    }

    public override void Unload()//lancé au déchargement du mod
    {
        Hooks.Unload();
        On.Celeste.Player.Die -= OnDeathHook;
        string weightsPath = Path.Combine(Everest.PathGame, "Mods", "PPOCeleste", "ppo_weights.json");
        ppo.SaveWeights(weightsPath); 
        ppo = null;
    }

    public void SendObsToPPO(Dictionary<string, object> obs)//liens entre hooks et ppo
    {
        ppo.ReceiveObs(obs);
        float reward = RewardSystem.ComputeReward(obs);
        ppo.StoreReward(reward);

    }

    public Dictionary<string, bool> GetActionFromPPO()
    {
        
        return ppo.GetActionFromPPO(); // 

    }
}
