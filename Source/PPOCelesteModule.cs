using AsmResolver.DotNet.Builder;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;


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

    public override void Load()
    {
        Hooks.Load();
    }

    public override void Initialize()
    {

    }

    public override void Unload()
    {
        Hooks.Unload();
    }

    public void SendObsToPPO(Dictionary<string, object> obs)
    {

    }

    
}

