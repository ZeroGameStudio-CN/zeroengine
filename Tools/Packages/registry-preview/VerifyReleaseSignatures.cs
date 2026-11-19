using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

public static class VerifyReleaseSignatures
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [Serializable] private sealed class PackageResult { public string name, version, sha256, signatureStatus, source; }
    [Serializable] private sealed class Evidence { public PackageResult[] packages; }

    private static object Field(object value, string name)
    {
        return value.GetType().GetField(name, Fields).GetValue(value);
    }

    public static void Verify()
    {
        var plan = Environment.GetEnvironmentVariable("ZE_RELEASE_VERIFICATION_PLAN");
        var proof = Environment.GetEnvironmentVariable("ZE_RELEASE_VERIFICATION_PROOF");
        var installed = PackageInfo.GetAllRegisteredPackages().ToDictionary(p => p.name);
        var rows = new List<PackageResult>();
        foreach (var line in File.ReadLines(plan))
        {
            var parts = line.Split('\t');
            var package = installed[parts[0]];
            var source = package.source.ToString();
            if (package.version != parts[1] || source != parts[2])
                throw new InvalidOperationException("Unexpected release package version or source: " + package.name);
            if (source == "Registry" && package.registry?.url.TrimEnd('/') != "https://packages-preview.zerogamestudio.com")
                throw new InvalidOperationException("A registry package resolved outside ZE Registry: " + package.name);
            var signature = typeof(PackageInfo).GetProperty("signature", Fields).GetValue(package);
            var status = Field(signature, "m_Status").ToString();
            var organization = Field(Field(signature, "m_Attestation"), "m_OwnerOrgId").ToString();
            if (status != "Valid" || organization != parts[3])
                throw new InvalidOperationException("Invalid package or signer: " + package.name);
            if (!File.ReadAllText(Path.Combine(package.resolvedPath, "package.json")).Contains(parts[4]))
                throw new InvalidOperationException("Package provenance changed: " + package.name);
            rows.Add(new PackageResult { name = package.name, version = package.version, source = source,
                signatureStatus = status, sha256 = parts[5] });
        }
        var skillSnapshot = Environment.GetEnvironmentVariable("ZE_RELEASE_SKILL_SNAPSHOT");
        if (!string.IsNullOrEmpty(skillSnapshot))
        {
            var type = Type.GetType("ZeroEngine.AiSupport.Editor.SkillPackageSnapshot, ZeroEngine.AiSupport.Editor", true);
            var value = type.GetMethod("CreateJson").Invoke(null, null);
            File.WriteAllText(skillSnapshot, (string)value);
        }
        File.WriteAllText(proof, JsonUtility.ToJson(new Evidence { packages = rows.ToArray() }, true));
    }
}
