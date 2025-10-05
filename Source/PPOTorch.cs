/*
Fichier contenant la logique PPO réécrite de zéro pour la gestion de l'agent PPO avec TorchSharp.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;
using static TorchSharp.torch.optim;

namespace Celeste.Mod.PPOCeleste
{
    public class PPOTorch
    {
        private ActorCritic policy;
        private Optimizer optimizer;

        private List<float[]> obsBuffer = new List<float[]>();
        private List<long> actionBuffer = new List<long>();
        private List<float> logProbBuffer = new List<float>();
        private List<float> valueBuffer = new List<float>();
        private List<float> rewardBuffer = new List<float>();
        private List<byte> doneBuffer = new List<byte>();

        private float previousProgress = 0f;

        private const int obsDim = 9 + 40 + 225; // x,y,vx,vy,grounded,dashes,wallcheck,grab,progress + enemies (10*4) + grid (15*15)
        private const int actDim = 7; // left, right, up, down, jump, dash, grab

        private Dictionary<int, string> actionMap = new Dictionary<int, string>
        {
            {0, "left"}, {1, "right"}, {2, "up"}, {3, "down"}, {4, "jump"}, {5, "dash"}, {6, "grab"}
        };

        public void Start()
        {
            policy = new ActorCritic(obsDim, actDim);
            optimizer = Adam(policy.parameters(), lr: 3e-4f);
            LogMessage("PPO agent started.");
        }

        private void LogMessage(string message)
        {
            try
            {
                string logPath = "ppo_log.txt";
                using (StreamWriter sw = new StreamWriter(logPath, true))
                {
                    sw.WriteLine($"{DateTime.Now}: {message}");
                }
            }
            catch (Exception)
            {
                // Ignore logging errors
            }
        }

        public void Stop()
        {
            // Optionally save model here
        }

        public void ReceiveObs(Dictionary<string, object> obs)
        {
            var obsList = new List<float>
            {
                (float)obs["x"],
                (float)obs["y"],
                (float)obs["vx"],
                (float)obs["vy"],
                (bool)obs["grounded"] ? 1f : 0f,
                (float)obs["dashes_left"],
                (bool)obs["wallcheck"] ? 1f : 0f,
                (bool)obs["grab"] ? 1f : 0f,
                (float)obs["progress"]
            };

            var enemies = (List<float>)obs["enemies"];
            obsList.AddRange(enemies);
            while (obsList.Count < 9 + 40) obsList.Add(0f);

            var grid = (List<int>)obs["grid"];
            obsList.AddRange(grid.Select(i => (float)i));

            var obsArray = obsList.ToArray();
            var obsTensor = tensor(obsArray);

            var (action, logProb, value) = PPO.SelectAction(policy, obsTensor);

            float currentProgress = (float)obs["progress"];
            float reward = currentProgress - previousProgress;
            previousProgress = currentProgress;

            obsBuffer.Add(obsArray);
            actionBuffer.Add(action);
            logProbBuffer.Add(logProb);
            valueBuffer.Add(value);
            rewardBuffer.Add(reward);
            doneBuffer.Add(0);

            ActionReceiver.latestActions.Clear();
            string actName = actionMap[(int)action];
            ActionReceiver.latestActions[actName] = true;
            foreach (var k in actionMap.Values)
            {
                if (!ActionReceiver.latestActions.ContainsKey(k))
                    ActionReceiver.latestActions[k] = false;
            }

            if (obsBuffer.Count >= 2048)
            {
                UpdatePPO();
            }
        }

        private void UpdatePPO()
        {
            LogMessage("Starting PPO update...");
            var lastObs = tensor(obsBuffer.Last());
            var forwardResult = policy.ForwardPass(lastObs.unsqueeze(0));
            var lastValue = forwardResult.value;
            float lastVal = lastValue.item<float>();

            var (returns, advantages) = PPO.ComputeGAE(
                rewardBuffer.ToArray(),
                valueBuffer.ToArray(),
                doneBuffer.ToArray(),
                lastVal
            );

            var obsTensors = obsBuffer.Select(o => tensor(o)).ToArray();
            var obsBatch = stack(obsTensors);
            var actionsBatch = tensor(actionBuffer.ToArray());
            var oldLogProbsBatch = tensor(logProbBuffer.ToArray());
            var returnsBatch = tensor(returns);
            var advantagesBatch = tensor(advantages);

            PPO.PPOUpdate(policy, optimizer, obsBatch, actionsBatch, oldLogProbsBatch, returnsBatch, advantagesBatch);

            LogMessage("PPO update completed.");

            obsBuffer.Clear();
            actionBuffer.Clear();
            logProbBuffer.Clear();
            valueBuffer.Clear();
            rewardBuffer.Clear();
            doneBuffer.Clear();
        }

        public Dictionary<string, bool> GetActionFromPPO()
        {
            return ActionReceiver.latestActions;
        }
    }

    public class ActorCritic : Module
    {
        private Sequential shared;
        private Sequential policy;
        private Sequential value;

        public ActorCritic(int obsDim, int actDim, int hidden = 64) : base("ActorCritic")
        {
            shared = Sequential(
                Linear(obsDim, hidden),
                ReLU()
            );
            policy = Sequential(
                Linear(hidden, hidden),
                ReLU(),
                Linear(hidden, actDim)
            );
            value = Sequential(
                Linear(hidden, hidden),
                ReLU(),
                Linear(hidden, 1)
            );

            RegisterComponents();
        }

        public (Tensor logits, Tensor value) ForwardPass(Tensor x)
        {
            var h = shared.forward(x);
            var logits = policy.forward(h);
            var v = value.forward(h).squeeze(-1);
            return (logits, v);
        }
    }

    public static class PPO
    {
        public static (float[] returns, float[] advantages) ComputeGAE(
            float[] rewards, float[] values, byte[] dones, float lastValue,
            float gamma = 0.99f, float lam = 0.95f)
        {
            int T = rewards.Length;
            var returns = new float[T];
            var adv = new float[T];
            float gae = 0f;
            var vals = new float[T + 1];
            Array.Copy(values, vals, T);
            vals[T] = lastValue;
            for (int t = T - 1; t >= 0; t--)
            {
                float nonTerminal = dones[t] == 1 ? 0f : 1f;
                float delta = rewards[t] + gamma * vals[t + 1] * nonTerminal - vals[t];
                gae = delta + gamma * lam * nonTerminal * gae;
                returns[t] = gae + vals[t];
                adv[t] = returns[t] - vals[t];
            }
            var advTensor = tensor(adv);
            var mean = advTensor.mean().item<float>();
            var std = advTensor.std().item<float>();
            for (int i = 0; i < T; i++)
                adv[i] = (adv[i] - mean) / (std + 1e-8f);

            return (returns, adv);
        }

        public static void PPOUpdate(
            ActorCritic policyNet,
            Optimizer optimizer,
            Tensor obsTensor,
            Tensor actionsTensor,
            Tensor oldLogProbsTensor,
            Tensor returnsTensor,
            Tensor advantagesTensor,
            float clipEps = 0.2f,
            float valueCoef = 0.5f,
            float entropyCoef = 0.01f,
            int epochs = 4,
            int batchSize = 64)
        {
            var N = (int)obsTensor.shape[0];
            var idxs = Enumerable.Range(0, N).ToArray();
            var rng = new Random();

            for (int epoch = 0; epoch < epochs; epoch++)
            {
                idxs = idxs.OrderBy(_ => rng.Next()).ToArray();
                for (int start = 0; start < N; start += batchSize)
                {
                    int end = Math.Min(start + batchSize, N);
                    var batchIdx = idxs.Skip(start).Take(end - start).ToArray();

                    var bObs = obsTensor.index_select(0, tensor(batchIdx, dtype: ScalarType.Int64));
                    var bActions = actionsTensor.index_select(0, tensor(batchIdx, dtype: ScalarType.Int64));
                    var bOldLog = oldLogProbsTensor.index_select(0, tensor(batchIdx, dtype: ScalarType.Int64));
                    var bReturns = returnsTensor.index_select(0, tensor(batchIdx, dtype: ScalarType.Int64));
                    var bAdv = advantagesTensor.index_select(0, tensor(batchIdx, dtype: ScalarType.Int64));

                    var forwardResult = policyNet.ForwardPass(bObs);
                    var logits = forwardResult.logits;
                    var values = forwardResult.value;

                    var probs = logits.softmax(-1);
                    var logSoftmax = logits.log_softmax(-1);
                    var newLogProbs = logSoftmax.gather(1, bActions.unsqueeze(1)).squeeze(1);

                    var entropy = -(probs * logSoftmax).sum(-1).mean();

                    var ratio = (newLogProbs - bOldLog).exp();

                    var surr1 = ratio * bAdv;
                    var surr2 = ratio.clamp(1.0f - clipEps, 1.0f + clipEps) * bAdv;
                    var policyLoss = -torch.min(surr1, surr2).mean();

                    var valueLoss = (bReturns - values).pow(2).mean();

                    var loss = policyLoss + valueCoef * valueLoss - entropyCoef * entropy;

                    optimizer.zero_grad();
                    loss.backward();
                    optimizer.step();
                }
            }
        }

        public static (long action, float logProb, float value) SelectAction(ActorCritic net, Tensor obs)
        {
            if (obs.dim() == 1) obs = obs.unsqueeze(0);
            var forwardResult = net.ForwardPass(obs);
            var logits = forwardResult.logits;
            var value = forwardResult.value;
            var probs = logits.softmax(-1);
            var actionTensor = probs.multinomial(1).squeeze(1);
            var logSoftmax = logits.log_softmax(-1);
            var logProb = logSoftmax.gather(1, actionTensor.unsqueeze(1)).squeeze(1);

            return (actionTensor[0].ToInt64(), logProb[0].item<float>(), value[0].item<float>());
        }
    }

    public class ActionReceiver
    {
        public static Dictionary<string, bool> latestActions = new Dictionary<string, bool>();

        public static bool GetKey(string key)
        {
            return latestActions.ContainsKey(key) && latestActions[key];
        }
    }
}
