using Monocle;
using Celeste;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Threading;
using TorchSharp;
using static TorchSharp.torch.nn;

namespace Celeste.Mod.PPOCeleste
{
    public class PPOTorch
    {



        public class ActionReceiver
        {




            public static bool GetKey(string key)
            {
                return latestActions.ContainsKey(key) && latestActions[key];
            } 
        } 
    }
}

