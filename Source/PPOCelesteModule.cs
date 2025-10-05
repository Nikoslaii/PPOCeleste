using AsmResolver.DotNet.Builder;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;


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

    private PPOTorch ppo;

    public override void Load()//lancé au chargement du mod 
    {
        ppo = new PPOTorch();
        Hooks.Load();
        ppo.Start();
    }

    public override void Initialize()//lancé après le chargement 
    {

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

