# Fishing core extraction

## Goal and boundary

Move our reusable fish locomotion, neighbourhood steering, bait inspection/contact and continuous struggle state into `ZeroEngine.Fishing`. POB owns single-key approach/stance, food catalog/variants/rewards, sprites, animation, rod/line rendering, UI, save and room lifetime. Existing two-rod QTE behavior remains a host policy; the package has no QTE or POB dependency.

## Reference evidence and limits

Cast n Chill 1.3.1(174), Unity 6000.3.26f1, IL2CPP metadata v39. Metadata declarations confirm separate fish idle/chase/fight/flee profiles, sight/hearing, lure preferences, nibble forces, stamina and line load. Native whole-image and focused-PE Ghidra attempts exceeded their bounded import/analysis deadlines; no control-flow equivalence is claimed. Research inputs, PE/token address mappings and original failures remain in the calling task's external evidence directory, outside this source package.

## Observable behavior

- Reservation alone does not move a fish toward an airborne lure.
- Independent reaction timing and acceleration/braking preserve a fish's motion when it joins inspection.
- Nibble readiness requires actual mouth/hook contact. Slow non-winners cannot block indefinitely; unreachable hooks cancel.
- Long mouth offsets cannot cause repeated pivot/heading flips.
- Warning precedes struggle. Load depends on fish/tackle strength; tiring and released recovery remain continuous.
- Invalid/frozen delta does not advance; reset cancels state, identity and force.

## Delivery

Portable unit evidence, Unity compilation/package evidence, POB real-flow evidence and human feel acceptance are separate. Source publication requires explicit upstream commit/push authority; consumer manifests return to pinned Git URLs before source handoff. POB retains its pre-extraction implementation in Plastic cs:18581 for rollback, rather than running duplicate motion or outcome systems. Do not change food/weapon shared owners or add new QTE controls during extraction.


## POB adapter

`FishingFishMotion` converts Unity vectors and serialized `FishingAmbientSettings` into the pure motion core. `FishingPondSchool` owns renderer/food identities, supplies stable snapshots to `SchoolSteering`, and begins the perception/inspection/contact gate only after the host reports lure landing. The host releases all companions on catch, cancellation and pool return. Existing `FishingConfig` remains the tuning entry.

`FishingRodChallenge` forwards the external gameplay delta to continuous resistance, while `ZeroEngine.Qte` alone decides success/failure. Resistance affects the visible pull and rod load; it never spawns rewards, consumes items or mutates saves. Existing food identity and single-fish delivery remain in POB. Real-flow recording and exact package/source fingerprints are retained in the consumer task evidence separately from portable tests.
