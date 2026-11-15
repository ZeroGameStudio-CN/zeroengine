# ZeroEngine Interaction Multiplayer

Optional, SDK-neutral bindings for `ZeroEngine.Interaction.Core.Authority` and the
existing `ZeroEngine.Multiplayer.Replication` entity/generation identities.
The interaction package remains usable without this module or a networking SDK.

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
POB's existing network dialogue owns its final FishNet/protocol and business wiring.

Import **Interaction Authority Playground** for an isolated offline/host/client
consumer. Its loopback-only TCP transport exists in the sample, not this runtime.
The sample's pure shared model also runs as two independent .NET processes through
`Tools~/AuthorityDemo`; see the sample instructions for the verified commands.
