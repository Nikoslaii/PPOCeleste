using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
    


    public static class TorchLoader {

        // Importation de l'API Windows pour charger manuellement une DLL
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        // Liste des DLLs natives requises par TorchSharp (dans l'ordre de dépendance si possible)
        private static readonly string[] NativeLibs = {
            "c10.dll",
            "libtorch_cuda.dll", // Version GPU
            "libtorch.dll",
            "TorchSharp.dll"    // Parfois nécessaire de précharger le wrapper natif
        };
    

        public static void LoadNativeLibs(EverestModuleMetadata meta) {
            // 1. Définir le dossier de destination dans le Cache d'Everest
            string cachePath = Path.Combine(Everest.Loader.PathCache, meta.Name, "native-libs");
            if (!Directory.Exists(cachePath)) Directory.CreateDirectory(cachePath);

            Logger.Log(LogLevel.Info, "PPOCeleste", $"Extraction des libs TorchSharp vers : {cachePath}");

            foreach (var libName in NativeLibs) {
                string destPath = Path.Combine(cachePath, libName);
                
                // 2. Extraction : On récupère le fichier depuis le Mod (Zip ou Dossier)
                // Le chemin interne doit correspondre à ton dossier créé à l'étape 2 (lib-native)
                string internalPath = Path.Combine("lib-native", libName).Replace("\\", "/");

                if (Everest.Content.TryGet(internalPath, out var asset)) {
                    // On n'écrase le fichier que s'il est plus récent ou absent
                    if (!File.Exists(destPath)) {
                        using (var stream = asset.Stream) 
                        using (var fileStream = File.Create(destPath)) {
                            stream.CopyTo(fileStream);
                        }
                    }
                
                    // 3. Chargement explicite
                    IntPtr handle = LoadLibrary(destPath);
                    if (handle == IntPtr.Zero) {
                        int errorCode = Marshal.GetLastWin32Error();
                        Logger.Log(LogLevel.Error, "PPOCeleste", $"Échec du chargement de {libName}. Code erreur Windows: {errorCode}");
                    } else {
                        Logger.Log(LogLevel.Verbose, "PPOCeleste", $"Chargé avec succès : {libName}");
                    }
                } else {
                    Logger.Log(LogLevel.Warn, "PPOCeleste", $"Impossible de trouver {internalPath} dans le mod !");
                }
            }
        }
    }





    [CustomEntity("CelesteCustom/progressionTracker")]
    public class ProgressionTracker : Entity {
        private string flag;
        private bool ordered;
        private List<Vector2> points = new List<Vector2>();
        public Vector2 NextVector { get; private set; } = Vector2.Zero; // vecteur vers le prochains points

        public ProgressionTracker(EntityData data, Vector2 offset) : base(data.Position + offset) {
            flag = data.Attr("flag", "progress_stage");
            ordered = data.Bool("ordered", true);

            Tag |= Tags.Global | Tags.Persistent; // permet de garder chargé l'entité dans tout le niveau

            // Récupère les positions et attributs des nodes
            for (int i = 0; i < data.Nodes.Length; i++)
            {
                Vector2 pos = data.Nodes[i] + offset;
                points.Add(pos);
            }
        }

        public override void Update() {
            base.Update();
            Level level = SceneAs<Level>();
            Player player = Scene.Tracker.GetEntity<Player>();
            if (player == null)
                return;

            int progress = level.Session.GetCounter(flag);

            if (ordered) {
                if (progress < points.Count) {
                    Vector2 nextPoint = points[progress];
                    float distance = Vector2.Distance(player.Center, nextPoint);

                    NextVector = nextPoint - player.Center;

                    if (distance < 16f) {
                        level.Session.SetCounter(flag, progress + 1);
                        if (progress + 1 == points.Count)
                            level.Session.SetFlag(flag + "_done", true);
                    }
                } else {
                    NextVector = Vector2.Zero;
                }
            } else {
                Vector2? closest = null;
                float bestDist = float.MaxValue;

                for (int i = 0; i < points.Count; i++) {
                    string nodeFlag = $"{flag}_{i}";
                    if (!level.Session.GetFlag(nodeFlag)) {
                        float d = Vector2.Distance(player.Center, points[i]);
                        if (d < bestDist) {
                            bestDist = d;
                            closest = points[i];
                        }

                        if (d < 16f) {
                            level.Session.SetFlag(nodeFlag, true);
                            int visited = CountVisited(level);
                            level.Session.SetCounter(flag, visited);
                            if (visited == points.Count)
                                level.Session.SetFlag(flag + "_done", true);
                        }
                    }
                }

                if (closest.HasValue)
                    NextVector = closest.Value - player.Center;
                else
                    NextVector = Vector2.Zero;
            }
        }

        private int CountVisited(Level level) {
            int count = 0;
            for (int i = 0; i < points.Count; i++)
                if (level.Session.GetFlag($"{flag}_{i}"))
                    count++;
            return count;
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
        try {
            // On passe les métadonnées pour savoir où extraire
            TorchLoader.LoadNativeLibs(Metadata);
        } catch (Exception e) {
            Logger.Log(LogLevel.Error, "PPOCeleste", "Erreur lors de l'init de Torch : " + e.ToString());
        }



        Hooks.Load();

    }

    public override void Initialize()//lancé après le chargement 
    {
        ppo = new PPOTorch();

        ppo.Start();
    }

    public override void Unload()//lancé au déchargement du mod
    {
        Hooks.Unload();
        ppo?.Stop();
    }

    public void SendObsToPPO(Dictionary<string, object> obs)//liens entre hooks et ppo
    {
        ppo.ReceiveObs(obs);
    }

    public Dictionary<string, bool> GetActionFromPPO()
    {
        return ppo.GetActionFromPPO();
    }
}

