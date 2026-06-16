using System;
using System.IO;
using UnityEngine;

#if NEWTONSOFT_JSON
using Newtonsoft.Json;
#endif

namespace ZeroEngine.ModSystem
{
    public static class ModManifestReader
    {
        public static bool TryRead(string modRootPath, out ModManifest manifest, out ModLoadIssue issue, string manifestFileName = "manifest.json")
        {
            manifest = null;

            if (string.IsNullOrWhiteSpace(modRootPath))
            {
                issue = new ModLoadIssue(ModIssueSeverity.Error, string.Empty, string.Empty, "Mod root path must not be empty.");
                return false;
            }

            string safeManifestFileName = string.IsNullOrWhiteSpace(manifestFileName) ? "manifest.json" : manifestFileName;
            if (!ModPathResolver.TryResolveRelativePath(modRootPath, safeManifestFileName, out string manifestPath, out string pathError))
            {
                issue = new ModLoadIssue(ModIssueSeverity.Error, string.Empty, modRootPath, pathError);
                return false;
            }

            if (!File.Exists(manifestPath))
            {
                issue = new ModLoadIssue(ModIssueSeverity.Error, string.Empty, modRootPath, $"Missing manifest.json in {modRootPath}.");
                return false;
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
#if NEWTONSOFT_JSON
                manifest = JsonConvert.DeserializeObject<ModManifest>(json);
#else
                manifest = JsonUtility.FromJson<ModManifest>(json);
#endif
                if (manifest == null)
                {
                    issue = new ModLoadIssue(ModIssueSeverity.Error, string.Empty, manifestPath, "Failed to parse mod manifest.");
                    return false;
                }

                manifest.RootPath = modRootPath;
                if (!manifest.IsValid(out string validationError))
                {
                    issue = new ModLoadIssue(ModIssueSeverity.Error, manifest.Id, manifestPath, validationError);
                    return false;
                }

                issue = null;
                return true;
            }
            catch (Exception ex)
            {
                issue = new ModLoadIssue(ModIssueSeverity.Error, string.Empty, manifestPath, ex.Message);
                return false;
            }
        }
    }
}
