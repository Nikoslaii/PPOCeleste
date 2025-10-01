/*
fichier contenant la logique du PPO


*/

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

        public void start()
        {
            
        }
        
        public void stop()
        {

        }  





        public void ReceiveObs(Dictionary<string, object> obs)
        {


        }








        public class ActionReceiver
        {




            public static bool GetKey(string key)//#récupere la logique des fichier python
            {
                return latestActions.ContainsKey(key) && latestActions[key];
            }
        } 
    }
}

