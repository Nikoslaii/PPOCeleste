using System;
using System.Collections.Generic;
using System.Data;
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

    private int stepCounter = 0;
    private const int STEPS_PER_UPDATE = 2048; // PPO standard
    private const int SAVE_INTERVAL = 10;      // save every 10 updates
    private int updateCounter = 0;


    [CustomEntity("CelesteCustom/progressionTracker")]
    public class ProgressionTracker : Entity {
        private string flag;
        private List<Vector2> points = [];
        private List<int> nodeOnDeathValues = [];  // Ajouter ce champ à la classe
        public Vector2 NextVector { get; private set; } = Vector2.Zero; // vecteur vers le prochains points

        public ProgressionTracker(EntityData data, Vector2 offset) : base(data.Position + offset) {
            flag = data.Attr("flag", "progress_stage");

            Tag |= Tags.Global | Tags.Persistent; // permet de garder chargé l'entité dans tout le niveau

            // Récupère les positions et les propriétés onDeath depuis le dictionnaire Values
            for (int i = 0; i < data.Nodes.Length; i++)
            {
                Vector2 pos = data.Nodes[i] + offset;
                points.Add(pos);
                
                // Loenn exporte les propriétés des nodes sous la forme "nodeX_propertyName"
                // Pour le node 0 et la propriété onDeath : "node0_onDeath", etc.
                string onDeathKey = $"node{i}_onDeath";
                int onDeathProgress = data.Int(onDeathKey, 0);
                nodeOnDeathValues.Add(onDeathProgress);
                
                Logger.Log(LogLevel.Verbose, "ProgressionTracker", $"Node {i}: position={pos}, onDeath={onDeathProgress}");
            }
        }

        public override void Update() {
            base.Update();

            Level level = SceneAs<Level>();
            Player player = Scene.Tracker.GetEntity<Player>();
            

            
            if (player == null)
                return;

            int progress = level.Session.GetCounter(flag);
    
            if (progress < points.Count) {
                Vector2 nextPoint = points[progress];
                float distance = Vector2.Distance(player.Center, nextPoint);

                NextVector = (nextPoint - player.Center).SafeNormalize();

                if (distance < 16f) {
                    level.Session.SetCounter(flag, progress + 1);

                    
                    Instance.ppo.EndEpisode(RewardSystem.LevelCompleteReward());
                    RewardSystem.Reset();

                    if (progress + 1 == points.Count)
                        level.Session.SetFlag(flag + "_done", true);
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


    public override void Load()//lancé au chargement du mod 
    {
        Hooks.Load();

        

    }

    private static On.Celeste.Player.hook_Die OnDeathHook;

    public override void Initialize()//lancé après le chargement 
    {
        ppo = new PPOAgent(obsSize: 275, hiddenSizes: [128,64]);
        ppo.LoadWeights("path/to/weights.json"); // optional

        
        OnDeathHook = (orig, self, direction, evenIfInvincible, registerDeathInStats) =>
        {
            var result = orig(self, direction, evenIfInvincible, registerDeathInStats);
            
            Instance.ppo.EndEpisode(RewardSystem.DeathPenalty());
            RewardSystem.Reset();

            return result;
        };
        
       On.Celeste.Player.Die += OnDeathHook;


    }

    public override void Unload()//lancé au déchargement du mod
    {
        Hooks.Unload();
        On.Celeste.Player.Die -= OnDeathHook;
    }

    public void SendObsToPPO(Dictionary<string, object> obs, Player player)//liens entre hooks et ppo
    {
        ppo.ReceiveObs(obs);
        float reward = RewardSystem.ComputeReward(obs, player);
        ppo.StoreReward(reward);

    }

    public Dictionary<string, bool> GetActionFromPPO()
    {
        
        return ppo.GetActionFromPPO();

    }
}
