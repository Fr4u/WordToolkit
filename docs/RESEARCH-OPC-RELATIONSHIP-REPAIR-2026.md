# Guarded OPC relationship repair research and contract

## Why the model is a graph, not a ZIP cleanup heuristic

ECMA-376 Part 2 defines Open Packaging Conventions (OPC). Microsoft's OPC overview
describes a package as a directed graph whose relationship source is either the package
or a part and whose target is internal or external. A relationship set belongs to one
source. The .NET packaging contract also makes the dangerous boundary explicit: deleting
a relationship does not delete its target part. WordToolkit preserves that boundary.

Primary references:

- [ECMA-376, Office Open XML File Formats](https://ecma-international.org/publications-and-standards/standards/ecma-376/)
- [Microsoft OPC fundamentals](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/opc/open-packaging-conventions-overview)
- [Microsoft relationships overview](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/opc/relationships-overview)
- [PackagePart.CreateRelationship](https://learn.microsoft.com/en-us/dotnet/api/system.io.packaging.packagepart.createrelationship?view=windowsdesktop-10.0)
- [Open XML SDK IRelationshipCollection.Create](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.irelationshipcollection.create?view=openxml-3.0.1)
- [Microsoft OPC packaging errors](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/opc/packaging-errors)

## Implemented typed usage model

`WordRelationshipUsageGraphBuilder` reads the existing OPC relationship model and parses
each XML owner at most once under hard relationship, part, byte and reference-detail
limits. It scans every element and every attribute, including all branches retained under
Markup Compatibility. It does not select a Word view. Exact relationship-ID attribute
matches are counted; bounded source evidence is optional.

Every relationship receives one explicit state:

- package relationship;
- missing, binary or unparseable owner;
- duplicate ID for one source;
- referenced by markup;
- implicit by a closed standard relationship-type allowlist;
- unreferenced explicit standard relationship;
- unknown unreferenced relationship.

Only the unreferenced-explicit state can become a markup relationship-removal candidate.
Package-root, duplicate, implicit, unknown, binary-owner, missing-owner and unparseable-
owner relationships are blocked. Unknown does not mean dead. It means the engine lacks
enough vocabulary to prove the opposite.

An orphan relationship part is modeled separately: its `.rels` entry exists while its
owning source part does not. This is not the same thing as an internal relationship whose
target is missing.

## Mutation contract

One `WordRelationshipRepairPlan` accepts a bounded ordered batch of two typed commands:

1. remove one fingerprinted, proven-unreferenced explicit relationship;
2. remove one fingerprinted orphan relationship part.

The planner applies commands against successive in-memory snapshots, so two removals from
the same `.rels` entry remain preconditioned on the exact result of the earlier command.
Relationship elements are removed through byte-range patches from the lossless XML parser.
Orphan `.rels` entries are deleted through the entry-level package transaction core.
Target parts are never deleted, and no command is allowed to add an entry.

Before publication the exact candidate is reopened and must prove all of the following:

- the semantic Word projection is unchanged;
- every unplanned ZIP entry retains its exact content hash;
- exactly the planned relationship values disappeared;
- no new OPC error/fatal diagnostic exists;
- no new `OPC040` unreachable part exists;
- every intended relationship or orphan entry is absent;
- applying the generated inverse restores the exact baseline package fingerprint.

The direct Engine, strict `relationship-repair-package` CLI and lazy MCP inspection/plan/
apply actions share the same request parser, planner and atomic writer. Apply reconstructs
the reviewed `wrrplan_` identity from the current bytes, blocks signed packages, requires
baseline/candidate Microsoft Open XML SDK validation with no new errors and requires a
separate Boolean authorization when any removed relationship is external. A sibling
backup is retained by default.

## Privacy and token contract

Inspection returns only stable IDs, fingerprints, source/relationship-part URIs, the
bounded relationship type name, target mode, optional internal resolved part URI, counts
and status. External target values and raw XML are never returned. The compact default
returns only removal candidates plus orphan relationship parts; all other classifications
require `include_all=true`, and source attribute evidence requires `include_details=true`.

## Deliberate boundaries

This slice does not infer that an unreachable target is disposable, delete target parts,
repair markup references, synthesize missing relationships, rewrite IDs, normalize a
relationship part, validate signatures cryptographically or optimize ZIP compression.
Those operations require different typed commands and different preservation proofs.
