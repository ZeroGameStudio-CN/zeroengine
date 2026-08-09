# Third-party notices

## Microsoft Open XML SDK 3.5.1

- Source: https://github.com/dotnet/Open-XML-SDK
- NuGet: https://www.nuget.org/packages/DocumentFormat.OpenXml/3.5.1
- License: MIT; the package includes the upstream license under
  `Editor/ThirdParty/OpenXml/Licenses/Open-XML-SDK.LICENSE.txt`.
- Bundled netstandard2.0 assemblies:
  - `DocumentFormat.OpenXml.dll` SHA-256
    `af82d277ef66cba76a4cb7fe9fb6d52abcbed2d7e85fea8fb000945f7faa977b`
  - `DocumentFormat.OpenXml.Framework.dll` SHA-256
    `3a0471bee5b2f2fc428d303d0f36a022d4b17fd014394023ffc68e2894434d6c`

## System.IO.Packaging 8.0.1

- NuGet: https://www.nuget.org/packages/System.IO.Packaging/8.0.1
- License: MIT; upstream license and third-party notices are included under
  `Editor/ThirdParty/OpenXml/Licenses/`.
- Bundled netstandard2.0 assembly `System.IO.Packaging.dll` SHA-256
  `b3ad6781c5afd6a034d216799908d0948682ed0d2d7ab74d3bdd3110bac9d7f8`.

All three assemblies are imported for Editor only and must not enter a Player
build.
