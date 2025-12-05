using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.PPOCeleste
{
    /// <summary>
    /// Compact, pure-C# PPO agent (multi-binary action head).
    /// - No native dependencies
    /// - On-policy PPO with GAE
    /// - Simple MLP backbone + Bernoulli policy head + value head
    /// 
    /// Public API (for integration with Hooks/PPOCelesteModule):
    /// - PPOAgent(int obsSize, int[] hiddenSizes)
    /// - void ReceiveObs(Dictionary<string, object> obs)
    /// - Dictionary<string,bool> GetActionFromPPO(bool deterministic=false)
    /// - void StoreReward(float r)
    /// - void EndEpisode(float finalReward=0f)
    /// - void UpdatePolicy()
    /// - void SaveWeights(string path), LoadWeights(string path)
    /// 
    /// Observation mapping defaults to the same layout used previously in Hooks:
    /// [x,y,vx,vy,grounded,dashes_left,wallcheck,grab, progress.x, progress.y, enemies(10*4), grid(15*15)]
    /// (Total default size = 275). Update ObsToVector if you change observation contents.
    /// </summary>
    public class PPOAgent
    {
        // Configuration
        public int ObservationSize { get; private set; }
        public int ActionCount { get; } = 7; // left,right,up,down,jump,dash,grab
        public int[] HiddenSizes { get; private set; }

        // Hyperparameters (tweakable)
        public float Gamma = 0.99f;
        public float Lambda = 0.95f;
        public float ClipEps = 0.2f;
        public float ValueCoef = 0.5f;
        public float EntropyCoef = 0.01f;
        public float LearningRate = 1e-3f;
        public int TrainEpochs = 4;
        public int MinibatchSize = 64;

        // Network parameters (shared MLP)
        private List<float[]> W; // flattened weight matrices row-major per hidden layer
        private List<float[]> B; // biases per hidden layer

        // Heads
        private float[] policyW; // ActionCount x lastHidden
        private float[] policyB; // ActionCount
        private float[] valueW;  // lastHidden
        private float valueB;

        // Adam optimizer state
        private List<float[]> mW, vW, mB, vB;
        private float[] mPolicyW, vPolicyW, mPolicyB, vPolicyB;
        private float[] mValueW, vValueW;
        private float mValueB, vValueB;
        private float beta1 = 0.9f, beta2 = 0.999f, eps = 1e-8f;
        private int adamT = 0;

        private Random rng;

        // Buffers for on-policy training
        private List<float[]> obsBuf = new();
        private List<float[]> actBuf = new();
        private List<float[]> logpBuf = new();
        private List<float> rewBuf = new();
        private List<float> doneBuf = new();
        private List<float> valBuf = new();

        // Current observation & last forward
        private float[] lastObsVec = null;
        private float[] lastProbs = null;
        private float[] lastSample = null;
        private float lastValue = 0f;

        public PPOAgent(int obsSize = 275, int[] hiddenSizes = null, int seed = 0)
        {
            ObservationSize = obsSize;
            HiddenSizes = hiddenSizes ?? new int[] { 128, 64 };
            rng = new Random(seed);
            InitNetwork();
        }

        private void InitNetwork()
        {
            W = new List<float[]>();
            B = new List<float[]>();
            mW = new List<float[]>();
            vW = new List<float[]>();
            mB = new List<float[]>();
            vB = new List<float[]>();

            int prev = ObservationSize;
            foreach (int hs in HiddenSizes)
            {
                var w = new float[hs * prev];
                var b = new float[hs];
                RandInit(w, (float)Math.Sqrt(2.0 / Math.Max(1, prev)));
                RandInit(b, 0f);
                W.Add(w); B.Add(b);
                mW.Add(new float[w.Length]); vW.Add(new float[w.Length]);
                mB.Add(new float[b.Length]); vB.Add(new float[b.Length]);
                prev = hs;
            }

            int lastHidden = prev;
            policyW = new float[ActionCount * lastHidden]; policyB = new float[ActionCount];
            valueW = new float[lastHidden]; valueB = 0f;
            RandInit(policyW, (float)Math.Sqrt(2.0 / Math.Max(1, lastHidden))); RandInit(policyB, 0f);
            RandInit(valueW, (float)Math.Sqrt(2.0 / Math.Max(1, lastHidden))); valueB = 0f;

            mPolicyW = new float[policyW.Length]; vPolicyW = new float[policyW.Length];
            mPolicyB = new float[policyB.Length]; vPolicyB = new float[policyB.Length];
            mValueW = new float[valueW.Length]; vValueW = new float[valueW.Length];
            mValueB = 0f; vValueB = 0f;
        }

        private void RandInit(float[] arr, float scale)
        {
            for (int i = 0; i < arr.Length; i++) arr[i] = (float)Gaussian() * scale;
        }
        private double Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble(); double u2 = 1.0 - rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        // ---------- Forward pass helpers ----------
        private float[] ForwardBody(float[] obs)
        {
            float[] x = obs;
            for (int l = 0; l < W.Count; l++)
            {
                int outDim = B[l].Length; int inDim = x.Length;
                float[] y = new float[outDim];
                var w = W[l]; var b = B[l];
                for (int i = 0; i < outDim; i++)
                {
                    float s = b[i]; int baseIdx = i * inDim;
                    for (int j = 0; j < inDim; j++) s += w[baseIdx + j] * x[j];
                    y[i] = s > 0 ? s : 0f; // ReLU
                }
                x = y;
            }
            return x;
        }

        private float[] PolicyFromHidden(float[] hid)
        {
            int h = hid.Length; float[] probs = new float[ActionCount];
            for (int a = 0; a < ActionCount; a++)
            {
                int baseIdx = a * h; float s = policyB[a];
                for (int j = 0; j < h; j++) s += policyW[baseIdx + j] * hid[j];
                probs[a] = Sigmoid(s);
            }
            return probs;
        }
        private float ValueFromHidden(float[] hid)
        {
            float s = valueB; for (int j = 0; j < hid.Length; j++) s += valueW[j] * hid[j]; return s;
        }

        private static float Sigmoid(float x) => x >= 0 ? 1f / (1f + (float)Math.Exp(-x)) : (float)Math.Exp(x) / (1f + (float)Math.Exp(x));

        // Sample bernoulli per-action
        private float[] SampleBernoulli(float[] probs)
        {
            var acts = new float[probs.Length];
            for (int i = 0; i < probs.Length; i++) acts[i] = rng.NextDouble() < probs[i] ? 1f : 0f;
            return acts;
        }

        private float[] LogProbBernoulli(float[] probs, float[] acts)
        {
            var logs = new float[probs.Length];
            for (int i = 0; i < probs.Length; i++) { float p = Math.Clamp(probs[i], 1e-6f, 1f - 1e-6f); logs[i] = acts[i] > 0.5f ? (float)Math.Log(p) : (float)Math.Log(1f - p); }
            return logs;
        }

        // ---------- Observation conversion (default) ----------
        public void ReceiveObs(Dictionary<string, object> obs)
        {
            lastObsVec = ObsToVector(obs);
            var hid = ForwardBody(lastObsVec);
            lastProbs = PolicyFromHidden(hid);
            lastValue = ValueFromHidden(hid);
        }

        public float[] ObsToVector(Dictionary<string, object> obs)
        {
            var v = new List<float>();
            v.Add(GetF(obs, "x")); v.Add(GetF(obs, "y")); v.Add(GetF(obs, "vx")); v.Add(GetF(obs, "vy"));
            v.Add(GetB(obs, "grounded") ? 1f : 0f); v.Add(GetI(obs, "dashes_left"));
            v.Add(GetB(obs, "wallcheck") ? 1f : 0f); v.Add(GetB(obs, "grab") ? 1f : 0f);

            if (obs.TryGetValue("progress", out object prog) && prog != null)
            {
                if (prog is Vector2 mv) { v.Add(mv.X); v.Add(mv.Y); }
                else if (prog is System.Numerics.Vector2 nv) { v.Add(nv.X); v.Add(nv.Y); }
                else { v.Add(0f); v.Add(0f); }
            }
            else { v.Add(0f); v.Add(0f); }

            if (obs.TryGetValue("enemies", out object en) && en is List<float> el)
            {
                int maxE = 10; int take = Math.Min(maxE, el.Count / 4);
                for (int i = 0; i < take; i++) { v.Add(el[i * 4 + 0]); v.Add(el[i * 4 + 1]); v.Add(el[i * 4 + 2]); v.Add(el[i * 4 + 3]); }
                for (int i = take; i < maxE; i++) { v.AddRange(new float[4]); }
            }
            else { for (int i = 0; i < 40; i++) v.Add(0f); }

            if (obs.TryGetValue("grid", out object g) && g is List<int> grid)
            {
                foreach (var ii in grid) v.Add(ii);
            }
            else { for (int i = 0; i < 225; i++) v.Add(0f); }

            // Ensure size matches or pad/truncate
            if (v.Count == ObservationSize) return v.ToArray();
            var arr = new float[ObservationSize];
            for (int i = 0; i < ObservationSize; i++) arr[i] = i < v.Count ? v[i] : 0f;
            return arr;
        }

        private static float GetF(Dictionary<string, object> d, string k)
        {
            if (d.TryGetValue(k, out object o) && o != null) { if (o is float f) return f; if (o is double db) return (float)db; if (o is int iv) return iv; }
            return 0f;
        }
        private static int GetI(Dictionary<string, object> d, string k) { if (d.TryGetValue(k, out object o) && o != null) { if (o is int i) return i; if (o is float f) return (int)f; } return 0; }
        private static bool GetB(Dictionary<string, object> d, string k) { if (d.TryGetValue(k, out object o) && o is bool b) return b; return false; }

        // ---------- Inference API ----------
        public Dictionary<string, bool> GetActionFromPPO(bool deterministic = false)
        {
            if (lastObsVec == null) return NoAction();
            var hid = ForwardBody(lastObsVec);
            var probs = PolicyFromHidden(hid);
            var value = ValueFromHidden(hid);

            float[] sample = deterministic ? probs.Select(p => p > 0.5f ? 1f : 0f).ToArray() : SampleBernoulli(probs);
            var logp = LogProbBernoulli(probs, sample);

            // store for training
            obsBuf.Add((float[])lastObsVec.Clone());
            actBuf.Add((float[])sample.Clone());
            logpBuf.Add((float[])logp.Clone());
            valBuf.Add(value);
            // reward/done added externally via StoreReward/EndEpisode

            lastProbs = probs; lastSample = sample; lastValue = value;
            return SampleToDict(sample);
        }

        private Dictionary<string, bool> NoAction() => new Dictionary<string, bool> { { "left", false }, { "right", false }, { "up", false }, { "down", false }, { "jump", false }, { "dash", false }, { "grab", false } };
        private Dictionary<string, bool> SampleToDict(float[] s) => new Dictionary<string, bool> { { "left", s[0] > 0.5f }, { "right", s[1] > 0.5f }, { "up", s[2] > 0.5f }, { "down", s[3] > 0.5f }, { "jump", s[4] > 0.5f }, { "dash", s[5] > 0.5f }, { "grab", s[6] > 0.5f } };

        // ---------- Environment hooks ----------
        public void StoreReward(float r) { rewBuf.Add(r); doneBuf.Add(0f); }
        public void EndEpisode(float finalReward = 0f)
        {
            if (rewBuf.Count < obsBuf.Count) { rewBuf.Add(finalReward); doneBuf.Add(1f); }
            else if (doneBuf.Count > 0) doneBuf[doneBuf.Count - 1] = 1f;
        }

        // ---------- Training (PPO with GAE) ----------
        public void UpdatePolicy()
        {
            int N = obsBuf.Count;
            if (N == 0) return;

            // compute advantages & returns (GAE)
            float[] returns = new float[N];
            float[] adv = new float[N];
            float nextVal = 0f; float gae = 0f;
            for (int t = N - 1; t >= 0; t--)
            {
                float mask = 1f - doneBuf[t];
                float delta = rewBuf[t] + Gamma * nextVal * mask - valBuf[t];
                gae = delta + Gamma * Lambda * gae * mask;
                adv[t] = gae;
                returns[t] = adv[t] + valBuf[t];
                nextVal = valBuf[t];
            }

            // training loop
            var idxs = Enumerable.Range(0, N).ToArray();
            int mb = Math.Min(MinibatchSize, N);
            for (int epoch = 0; epoch < TrainEpochs; epoch++)
            {
                Shuffle(idxs);
                for (int start = 0; start < N; start += mb)
                {
                    int end = Math.Min(N, start + mb);
                    // accumulate grads
                    var gW = W.Select(w => new float[w.Length]).ToList();
                    var gB = B.Select(b => new float[b.Length]).ToList();
                    var gPolicyW = new float[policyW.Length]; var gPolicyB = new float[policyB.Length];
                    var gValueW = new float[valueW.Length]; var gValueB = 0f;

                    for (int ii = start; ii < end; ii++)
                    {
                        int i = idxs[ii];
                        float[] o = obsBuf[i]; float[] a = actBuf[i]; float[] oldLogp = logpBuf[i];
                        float A = adv[i]; float R = returns[i];

                        // forward with activations
                        var acts = new List<float[]>(); acts.Add(o);
                        float[] x = o;
                        for (int l = 0; l < W.Count; l++)
                        {
                            int outDim = B[l].Length; float[] y = new float[outDim]; int inDim = x.Length; var w = W[l]; var b = B[l];
                            for (int u = 0; u < outDim; u++) { float s = b[u]; int baseIdx = u * inDim; for (int jj = 0; jj < inDim; jj++) s += w[baseIdx + jj] * x[jj]; y[u] = s > 0 ? s : 0f; }
                            x = y; acts.Add(x);
                        }
                        float[] hidden = acts.Last();
                        // compute probs & value
                        float[] probs = new float[ActionCount]; for (int aIdx = 0; aIdx < ActionCount; aIdx++) { int baseIdx = aIdx * hidden.Length; float s = policyB[aIdx]; for (int j = 0; j < hidden.Length; j++) s += policyW[baseIdx + j] * hidden[j]; probs[aIdx] = Sigmoid(s); }
                        float curV = valueB; for (int j = 0; j < hidden.Length; j++) curV += valueW[j] * hidden[j];

                        float[] curLogp = LogProbBernoulli(probs, a);
                        float[] ratios = new float[ActionCount]; for (int k = 0; k < ActionCount; k++) ratios[k] = (float)Math.Exp(curLogp[k] - oldLogp[k]);

                        // surrogate loss and gradients (simplified): compute dL/dlogit per action
                        float[] dLogit = new float[ActionCount];
                        for (int k = 0; k < ActionCount; k++)
                        {
                            float r = ratios[k]; float clipped = Math.Clamp(r, 1f - ClipEps, 1f + ClipEps);
                            float surr1 = r * A; float surr2 = clipped * A; float chosen = Math.Abs(surr1) < Math.Abs(surr2) ? surr1 : surr2; // approximate
                            // derivative approx: use unclipped gradient when inside clip, else 0
                            if (Math.Abs(r - 1f) <= ClipEps) dLogit[k] = -A * r * (a[k] - probs[k]); else dLogit[k] = 0f;
                        }

                        // accumulate policy head gradients
                        for (int aIdx = 0; aIdx < ActionCount; aIdx++)
                        {
                            int baseIdx = aIdx * hidden.Length; for (int j = 0; j < hidden.Length; j++) gPolicyW[baseIdx + j] += dLogit[aIdx] * hidden[j];
                            gPolicyB[aIdx] += dLogit[aIdx];
                        }

                        // value gradient
                        float dV = 2f * (curV - R) * ValueCoef; // derivative of value loss
                        for (int j = 0; j < hidden.Length; j++) gValueW[j] += dV * hidden[j];
                        gValueB += dV;

                        // hidden gradient from heads
                        var dHidden = new float[hidden.Length];
                        for (int aIdx = 0; aIdx < ActionCount; aIdx++) { int baseIdx = aIdx * hidden.Length; for (int j = 0; j < hidden.Length; j++) dHidden[j] += dLogit[aIdx] * policyW[baseIdx + j]; }
                        for (int j = 0; j < hidden.Length; j++) dHidden[j] += dV * valueW[j];

                        // backprop through body (ReLU)
                        for (int layer = W.Count - 1; layer >= 0; layer--)
                        {
                            float[] inp = acts[layer]; float[] outAct = acts[layer + 1]; int outDim = outAct.Length; int inDim = inp.Length; var w = W[layer];
                            for (int outi = 0; outi < outDim; outi++)
                            {
                                float gradOut = dHidden[outi]; if (outAct[outi] <= 0f) gradOut = 0f;
                                int baseIdx = outi * inDim;
                                for (int j = 0; j < inDim; j++) gW[layer][baseIdx + j] += gradOut * inp[j];
                                gB[layer][outi] += gradOut;
                                for (int j = 0; j < inDim; j++) dHidden[j] += gradOut * w[baseIdx + j];
                            }
                        }
                    }

                    // apply grads with Adam
                    float scale = 1f / (end - start);
                    for (int l = 0; l < W.Count; l++) ApplyAdam(W[l], gW[l], mW[l], vW[l], scale);
                    for (int l = 0; l < B.Count; l++) ApplyAdam(B[l], gB[l], mB[l], vB[l], scale);
                    ApplyAdam(policyW, gPolicyW, mPolicyW, vPolicyW, scale); ApplyAdam(policyB, gPolicyB, mPolicyB, vPolicyB, scale);
                    ApplyAdam(valueW, gValueW, mValueW, vValueW, scale); mValueB = beta1 * mValueB + (1 - beta1) * (gValueB * scale); vValueB = beta2 * vValueB + (1 - beta2) * (gValueB * gValueB * scale * scale);
                    // valueB update
                    adamT++; float lrT = LearningRate * (float)Math.Sqrt(1 - Math.Pow(beta2, adamT)) / (1 - (float)Math.Pow(beta1, adamT)); valueB -= lrT * (mValueB / ((float)Math.Sqrt(vValueB) + eps));
                }
            }

            // clear buffers
            obsBuf.Clear(); actBuf.Clear(); logpBuf.Clear(); rewBuf.Clear(); doneBuf.Clear(); valBuf.Clear();
        }

        private void ApplyAdam(float[] param, float[] grad, float[] m, float[] v, float scale)
        {
            adamT++; float lrT = LearningRate * (float)Math.Sqrt(1 - Math.Pow(beta2, adamT)) / (1 - (float)Math.Pow(beta1, adamT));
            for (int i = 0; i < param.Length; i++)
            {
                float g = grad[i] * scale;
                m[i] = beta1 * m[i] + (1 - beta1) * g;
                v[i] = beta2 * v[i] + (1 - beta2) * g * g;
                param[i] -= lrT * (m[i] / ((float)Math.Sqrt(v[i]) + eps));
            }
        }

        private static void Shuffle(int[] arr) { var r = new Random(); for (int i = arr.Length - 1; i > 0; i--) { int j = r.Next(i + 1); int t = arr[i]; arr[i] = arr[j]; arr[j] = t; } }

        // ---------- Persistence ----------
        public void SaveWeights(string path)
        {
            var obj = new Serializable
            {
                ObsSize = ObservationSize,
                Hidden = HiddenSizes,
                Weights = W.ToArray(), Biases = B.ToArray(), PolicyW = policyW, PolicyB = policyB, ValueW = valueW, ValueB = valueB
            };
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(obj, opts));
        }
        public void LoadWeights(string path)
        {
            var txt = File.ReadAllText(path);
            var obj = JsonSerializer.Deserialize<Serializable>(txt);
            if (obj == null) throw new Exception("Invalid weights file");
            if (obj.Hidden.Length != HiddenSizes.Length) throw new Exception("Architecture mismatch");
            W = obj.Weights.ToList(); B = obj.Biases.ToList(); policyW = obj.PolicyW; policyB = obj.PolicyB; valueW = obj.ValueW; valueB = obj.ValueB;
            // reinit Adam buffers
            mW = W.Select(w => new float[w.Length]).ToList(); vW = W.Select(w => new float[w.Length]).ToList();
            mB = B.Select(b => new float[b.Length]).ToList(); vB = B.Select(b => new float[b.Length]).ToList();
            mPolicyW = new float[policyW.Length]; vPolicyW = new float[policyW.Length]; mPolicyB = new float[policyB.Length]; vPolicyB = new float[policyB.Length];
            mValueW = new float[valueW.Length]; vValueW = new float[valueW.Length]; mValueB = 0f; vValueB = 0f;
        }

        private class Serializable
        {
            public int ObsSize { get; set; }
            public int[] Hidden { get; set; }
            public float[][] Weights { get; set; }
            public float[][] Biases { get; set; }
            public float[] PolicyW { get; set; }
            public float[] PolicyB { get; set; }
            public float[] ValueW { get; set; }
            public float ValueB { get; set; }
        }
    }
}
