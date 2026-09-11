This fixture supplies a prevalidated package revision and a confirmed Grid-only input manifest for
the coordinator-to-top-level-worker capability evaluation. The package revision owns the 46 package
rows and contains only package-wide documentation evidence; it does not retain Chart source,
documentation, or evidence. The separate writable Grid worker owns only the 64 component rows.

worker-prompt.txt is the immutable bounded prompt for the top-level worker. The coordinator copies
it into the per-unit directory without reconstruction so the single worker launch receives the
same verified assignment in every arm.

grid-input.confirmed.json SHA-256:
8b8c2ef26d79774defad6eae6e22884b6e9391dedb0cf7d96868ebcf9f79adce

package-revisions/0001/package.validation.json SHA-256:
ae820b82b0ac8ff69a04a5a78fd65e92386246d72ddcf304db1e88d390054edc

worker-prompt.txt SHA-256:
b400a7949d3eef323e7ffe58606bbf9c131f558cddda04dbd3441c7c8c6ed7eb
