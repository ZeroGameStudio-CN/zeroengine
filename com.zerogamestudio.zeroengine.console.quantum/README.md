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

The source profile currently has Editor evidence. Its assembly module identity
is deliberately checked at runtime: a different Player/AOT assembly needs matching
build evidence rather than being declared compatible from an Editor result.
The supplied `link.xml` preserves public vendor types used by this bridge and
tolerates a missing SDK. Player/IL2CPP and full project migration remain separate
acceptance items.
