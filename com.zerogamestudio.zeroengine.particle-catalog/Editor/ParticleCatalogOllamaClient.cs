using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.ParticleCatalog
{
    public static class ParticleCatalogOllamaClient
    {
        private const string Endpoint = "http://127.0.0.1:11434/api/generate";
        private const string TagsEndpoint = "http://127.0.0.1:11434/api/tags";

        public static async Task<ParticleAiClassification> Classify(byte[] png, ParticleCatalogEntry entry, string model)
        {
            GenerateRequest request = new GenerateRequest
            {
                model = model,
                stream = false,
                images = new[] { Convert.ToBase64String(png) },
                prompt = "你是游戏特效资产分类器。根据预览图、路径和技术指标分类。只能输出一个 JSON 对象，不要 markdown。" +
                         "字段必须是 purposes,elements,shapes,motions,colors,timings,styles,performance 的字符串数组，summaryZh 为简短中文，summaryEn 为简短英文，confidence 为 0-1。" +
                         $"允许值：{ParticleCatalogTaxonomy.DescribeAllowedValues()}。" +
                         $"路径={entry.path}; 粒子系统={entry.particleSystemCount}; 渲染器={entry.rendererCount}; maxParticles={entry.maxParticles}; duration={entry.maxDuration}; looping={entry.looping}; trails={entry.hasTrails}; collision={entry.usesCollision}; lights={entry.usesLights}。"
            };

            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            using (StringContent content = new StringContent(JsonUtility.ToJson(request), Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await client.PostAsync(Endpoint, content))
            {
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama request failed: HTTP {(int)response.StatusCode}.");
                GenerateResponse generated = JsonUtility.FromJson<GenerateResponse>(body);
                if (generated == null || !string.IsNullOrWhiteSpace(generated.error)) throw new InvalidOperationException("Ollama returned an error.");
                ParticleAiClassification result = JsonUtility.FromJson<ParticleAiClassification>(StripCodeFence(generated.response));
                if (result == null || string.IsNullOrWhiteSpace(result.summaryZh) || string.IsNullOrWhiteSpace(result.summaryEn))
                    throw new InvalidOperationException("Ollama classification is missing bilingual summaries.");
                result.modelDigest = await GetModelDigest(client, model);
                return result;
            }
        }

        private static async Task<string> GetModelDigest(HttpClient client, string model)
        {
            using (HttpResponseMessage response = await client.GetAsync(TagsEndpoint))
            {
                if (!response.IsSuccessStatusCode) return null;
                TagsResponse tags = JsonUtility.FromJson<TagsResponse>(await response.Content.ReadAsStringAsync());
                if (tags?.models == null) return null;
                foreach (OllamaModel item in tags.models)
                {
                    if (string.Equals(item.name, model, StringComparison.OrdinalIgnoreCase) || string.Equals(item.model, model, StringComparison.OrdinalIgnoreCase)) return item.digest;
                }
                return null;
            }
        }

        private static string StripCodeFence(string value)
        {
            string result = (value ?? string.Empty).Trim();
            if (!result.StartsWith("```", StringComparison.Ordinal)) return result;
            int firstLine = result.IndexOf('\n');
            int lastFence = result.LastIndexOf("```", StringComparison.Ordinal);
            return firstLine >= 0 && lastFence > firstLine ? result.Substring(firstLine + 1, lastFence - firstLine - 1).Trim() : result;
        }

        [Serializable] private sealed class GenerateRequest { public string model; public string prompt; public bool stream; public string[] images; public string keep_alive = "30m"; }
        [Serializable] private sealed class GenerateResponse { public string response; public string error; }
        [Serializable] private sealed class TagsResponse { public OllamaModel[] models; }
        [Serializable] private sealed class OllamaModel { public string name; public string model; public string digest; }
    }
}
