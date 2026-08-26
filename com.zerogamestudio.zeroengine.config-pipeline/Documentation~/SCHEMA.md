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

## Typed presets and explicit empty values

Mark a top-level preset array with `x-zgs-preset-type`. A typed string selector
declares the same value together with `x-zgs-ref`; an empty selector means that
the optional slot is not selected, while a non-empty value is validated against
the target primary key.

For generation-time inheritance, the instance array declares
`x-zgs-preset-source:"#/properties/<preset-table>"` and
`x-zgs-preset-ref-field:"<selector>"`. The source and instance tables must be
arrays of objects with one string primary key, the selector type and target must
match, and a preset cannot inherit from or select another typed preset. Matching
scalar and object fields resolve in this order: schema default, one preset, then
an explicit instance value. Generation writes only the flattened result.

A shared child collection declares `x-zgs-override-mode-field` naming a sibling
string enum containing exactly `Inherit` and `Replace`. `Inherit` accepts no
instance child rows. `Replace` uses the complete instance list, and no rows means
an explicit clear. Append, union, and index merge are unsupported.

Blank Excel cells remain absent. A string cell containing `@empty` is an
explicit empty string. `@clear` produces null only for a scalar marked
`x-zgs-nullable:true`. A business literal beginning with `@` escapes the first
character as `@@`. Source-map format 2 records each final field as `Schema`,
`Preset`, or `Instance`, including its schema path, source JSON path, and source
cell when one exists.
