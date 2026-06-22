# metricsd

`metricsd` is a high-throughput metrics aggregation daemon. The full project
normally ships a large source tree:

- `core/` — sample aggregation, percentile/quantile estimators, ring buffers
- `io/` — write-ahead log, snapshot reader/writer, wire protocol codecs
- `exporters/` — Prometheus, StatsD, and OTLP exporters
- `daemon/` — the long-running collector loop and admin HTTP surface

It uses `pytest` for its test suite (see `pyproject.toml`).

> Layout note: only a single small string-helper module is present in this
> checkout. Treat the module that is actually on disk as the unit under test.
