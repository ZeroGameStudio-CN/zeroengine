# Optional Quantum frontend

G4 compatibility candidate. Quantum Console is a separately installed vendor
SDK; this package contains neither its code nor its assets. A project supplies
its QC prefab and an explicitly verified compatibility profile. Unknown profiles
fall back to the independent native frontend.

The bridge uses public response/lifecycle APIs and owns a cloned QC instance.
Raw input goes to the same ZE session; QC does not parse or execute that input.
SDK 2.6.7's response route has a native proof, including quoted Chinese input,
rearming and one native execution per submission. Complete frontend/AOT and
experience acceptance remains in G4/G6.

## Explicit setup

Pin the Console, UI and Quantum packages to the same tested Git commit. The
Quantum package compiles without the vendor SDK; it has no vendor assembly
reference and does not register duplicate QC command handlers.

1. Supply the project's licensed QC prefab. Keep it outside the ZE repository.
2. In explicit Editor setup, call
   `QuantumProfileVerifier.VerifyPrefab(prefab)` and save the returned owned
   `QuantumCompatibilityProfile` with `AssetDatabase.CreateAsset`. This validates
   the inspected 2.6.7 source fingerprint and binds the current assembly identity.
   Changed SDK source/assembly requires explicit verification again.
3. Add `ConsoleHost`, `NativeConsole`, `QuantumConsoleFrontend` and
   `ConsoleFrontendSelector` to the project's Console owner. Call
   `quantum.Configure(prefab, profile)` and `selector.ConfigureQuantum(quantum)`.
4. Supply the project's input/navigation lease through
   `selector.AcquireInputIsolation`. The selector closes the previous frontend
   before opening the next. Call `Select(Native)` to keep the independent UI.

For Chinese or other scripts outside the vendor prefab's font coverage, provide
the project's TMP font through `quantum.ConfigureFont(fontAsset)` or Text Font
in the Inspector. It is assigned only to cloned widgets; source prefabs and fonts
are not rewritten. The bridge's default prompt is ASCII so opening it does not
create a missing-glyph warning loop on an unconfigured vendor font.

The adapter clones the prefab and key configuration. It owns submission,
IME-safe keyboard handling, native completion/history, cancellation and input
restoration. It never leaves a borrowed QC instance in response mode. A profile
or opening failure selects Native with one specific reason. Already started
commands are never replayed during a frontend change.

An Editor profile is accepted only in the Editor. For a Windows Player, compile
scripts with the final target/backend/development settings first, then call
`QuantumProfileVerifier.VerifyPlayerAssembly(prefab, compiledQcDll,
RuntimePlatform.WindowsPlayer, backend)` and save the returned owned asset before
serializing the build scene. The verifier reads the compiled DLL's public PE
metadata through Unity's bundled Cecil, without loading the vendor DLL into the
Editor domain. It binds assembly name, exact module ID, DLL SHA-256, Unity version,
platform and `mono`/`il2cpp` backend. A changed identity still selects Native.
Keep the compilation inputs unchanged between this step and BuildPlayer; a
Player build/run must establish compatibility for the resulting artifact.
The supplied `link.xml` preserves public vendor types used by this bridge and
tolerates a missing SDK. The SDK-absent Mono and IL2CPP Players passed managed
build/run checks at e47ce24, including Native fallback. SDK-present QC Player
compatibility of this new build profile, rendered input/IME acceptance and full project migration remain
separate acceptance items.
