using System;
using System.Collections.Generic;
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
    

    [CustomEntity("CelesteCustom/progressionTracker")]
    public class ProgressionTracker : Entity {
        private string flag;
        private bool ordered;
        private List<Vector2> points = new List<Vector2>();
        private int currentIndex = 0;

        public ProgressionTracker(EntityData data, Vector2 offset) : base(data.Position + offset) {
            flag = data.Attr("flag", "progress_stage");
            ordered = data.Bool("ordered", true);

            foreach (Vector2 node in data.NodesWithPosition(offset))
                points.Add(node);
        }

        public override void Update() {
            base.Update();
            Level level = SceneAs<Level>();
            Player player = Scene.Tracker.GetEntity<Player>();
            if (player == null)
                return;

            // Vérifie progression actuelle
            int progress = level.Session.GetCounter(flag);

            if (ordered) {
                // Mode "ordre strict"
                if (progress < points.Count) {
                    if (Vector2.Distance(player.Center, points[progress]) < 16f) {
                        level.Session.SetCounter(flag, progress + 1);
                        if (progress + 1 == points.Count)
                            level.Session.SetFlag(flag + "_done", true);
                    }
                }
            } else {
                // Mode "désordre"
                for (int i = 0; i < points.Count; i++) {
                    if (Vector2.Distance(player.Center, points[i]) < 16f && progress < points.Count) {
                        string nodeFlag = $"{flag}_{i}";
                        if (!level.Session.GetFlag(nodeFlag)) {
                            level.Session.SetFlag(nodeFlag, true);
                            progress++;
                            level.Session.SetCounter(flag, progress);
                        }
                    }
                }
                // Vérifie si tous visités
                bool allVisited = true;
                for (int i = 0; i < points.Count; i++)
                    if (!level.Session.GetFlag($"{flag}_{i}")) allVisited = false;

                if (allVisited)
                    level.Session.SetFlag(flag + "_done", true);
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
        


    private PPOTorch ppo;

    public override void Load()//lancé au chargement du mod 
    {
        
        //Hooks.Load();

    }

    public override void Initialize()//lancé après le chargement 
    {
        //ppo = new PPOTorch();

        //ppo.Start();
    }

    public override void Unload()//lancé au déchargement du mod
    {
        //Hooks?.Unload();
        ppo?.Stop();
    }

    public void SendObsToPPO(Dictionary<string, object> obs)//liens entre hooks et ppo
    {
        //ppo.ReceiveObs(obs);
    }

    public Dictionary<string, bool> GetActionFromPPO()
    {
        return ppo.GetActionFromPPO();
    }
}

