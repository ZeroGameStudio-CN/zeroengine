# Schema contract

The authority is a JSON Schema Draft 2020-12 document using the package's strict
subset. Unknown keywords fail closed. The root is an object with `$id`, positive
`x-zgs-schema-version`, and `additionalProperties:false`.

Supported types are object, array, string, integer, number and boolean. Supported
constraints are required, default, enum, numeric bounds, pattern, item bounds,
uniqueItems and local non-recursive `$defs/$ref`.

`x-zgs-sheet` maps an array of objects to a worksheet. Each table has one or
more top-level string `x-zgs-primary-key` fields. When several are declared,
their schema-property order defines an ordered composite identity; uniqueness
and deterministic import ordering use the complete tuple, never an invented
single `id`. A child array additionally declares a synthetic `x-zgs-parent-key`
column and an integer `x-zgs-order-field`; the parent key does not enter runtime
JSON. Child-sheet joins and `x-zgs-ref` targets currently require a single-field
primary key. Composite parents and references to an individual composite
component fail closed. Numeric representation is fixed with
`x-zgs-number-type`.
References, assets, localization, scope and author-only fields use the remaining
documented `x-zgs-*` annotations. Schema changes increment the version; semantic
or destructive changes require an explicit migration or a new config set.
An unspecified localization or stable-identity string must remain absent; the
empty string is invalid, so do not declare `default:""` for those fields.
