# ZeroEngine Interaction Multiplayer

Optional, SDK-neutral bindings for `ZeroEngine.Interaction.Core.Authority` and the
existing `ZeroEngine.Multiplayer.Replication` entity/generation identities.
The interaction package remains usable without this module or a networking SDK.

Version 0.1.1 takes the interaction 0.3 execution port. Trusted server-side content
can retain `IInteractionExecution`, observe its terminal lifetime and complete via
the existing guarded request; no new RPC or wire format is added. An execution
handle is local to its owning authority and must not be serialized to a client.

Create one authority per session epoch. Bind each authenticated connection to its
server-owned `InteractionReplicationPeer`; never resolve that receiver from an
actor ID supplied in the command. Register targets using the same canonical ZE
`NetworkEntityKey` identity. The server supplies positions, conditions and clocks.

Offline and remote inputs call the same receiver. Press/release/cancel use the
existing interaction agent, including server-timed hold/repeat. Confirmation is
an explicitly registered server-side callback. Admission receipts do not grant
rewards; only the authority's guarded business callback can commit them.

The transport owns authentication, ordered/reliable command delivery, disconnection
and snapshot recovery. Bindings provide sequence/replay admission and outcome
projection; they do not replace FishNet or introduce a second game transport.
POB integration supplies its existing FishNet/protocol and game-specific providers.

## Package ownership

| Layer | Responsibility |
| --- | --- |
| Feature core, such as Interaction | Offline/server rules, gestures, conditions and guarded business execution |
| Shared Multiplayer foundation | Entity/generation identity and common replication contracts |
| Optional feature adapter, such as Interaction.Multiplayer | Mapping feature commands/results to shared identities and authenticated actor bindings |
| Consumer transport and gameplay | FishNet/session delivery, reliable ordering, domain snapshots, wallets, rewards and presentation |

A feature that runs only on the authority can use its ordinary core and replicate
its existing domain state. Create an optional adapter when a reusable feature
actually needs client commands or replicated results. A small mapping can remain
an optional assembly in an existing adapter package; it does not require another
UPM package solely because the feature also runs in multiplayer.

## Independent example

Import **Interaction Authority Playground** for an isolated offline/host/client
consumer. Its loopback-only TCP transport exists in the sample, not this runtime.
The sample's pure shared model also runs as two independent .NET processes through
`Tools~/AuthorityDemo`; see the sample instructions for the verified commands.
