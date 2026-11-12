# Optional Quantum frontend

G4 compatibility candidate. Quantum Console is a separately installed vendor
SDK; this package contains neither its code nor its assets. A project supplies
its QC prefab and an explicitly verified compatibility profile. Unknown profiles
fall back to the independent native frontend.

The bridge uses public response/lifecycle APIs and owns a cloned QC instance.
Raw input goes to the same ZE session; QC does not parse or execute that input.
SDK 2.6.7's response route has a native proof, including quoted Chinese input,
rearming and one native execution per submission. Platform support is bounded
below; project migration and full experience acceptance remain separate.

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

An Editor profile is accepted only in the Editor. A Windows Player profile must
bind the **final linked** QC assembly: Unity's linker can change its module ID
after script precompilation. The verified build workflow is:

1. Compile with the final target/backend/development settings and create an
   owned provisional profile with `QuantumProfileVerifier.VerifyPlayerAssembly`.
2. Build a probe Player, then read its final linked `QFSW.QC.dll`. The tested
   Unity 6000.6.4f1 Windows pipeline retains it in the Bee `ManagedStripped`
   output; for Mono, require byte equality with the DLL shipped in the Player.
3. Call `QuantumProfileVerifier.VerifyPlayerAssembly(prefab, linkedQcDll,
   RuntimePlatform.WindowsPlayer, backend)`, reacquire the asset references after
   `BuildPlayer`, and update the same owned profile asset before saving.
4. Build again with unchanged compilation inputs. Require the assembly name,
   module ID and DLL SHA-256 to remain identical to the stored linked profile,
   then run the resulting Player to check its runtime identity and frontend.

The verifier reads public PE metadata through Unity's bundled Cecil without
loading the vendor DLL into the Editor domain. Profiles also bind the Unity
version, platform and `mono`/`il2cpp` backend. Any identity mismatch selects Native.
The supplied `link.xml` preserves the public vendor types and tolerates a missing
SDK.

For SDK 2.6.7 on Unity **6000.6.4f1 / Windows**, the managed Player checks establish
QC support for **Mono Development** with this two-pass workflow. The same
**IL2CPP Development** runtime reports an all-zero `ModuleVersionId`, so QC is
**unsupported** there and the strict identity guard selects Native. A successful
Native fallback is independent evidence and never counts as QC compatibility.
Other Unity versions, SDK revisions, targets and backends require their own
verified profile and Player evidence. The independent Native frontend supports
the tested SDK-absent Mono/IL2CPP configurations; release builds keep the host
disabled by default. Controlled composition input checks use the active
EventSystem input override; OS input-method acceptance remains unverified.
