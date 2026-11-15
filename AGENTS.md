# Repository Working Agreement

Before planning or changing code in this repository, read these documents completely:

1. `docs/requirements.md` — product scope, actors, workflows, state rules, and minimum tests.
2. `docs/architecture-roadmap.md` — architecture decisions, infrastructure boundaries, and delivery order.
3. `README.md` — current implementation status and local operating instructions.

Treat those documents as the repository source of truth. When an approved product or architecture decision changes, update the relevant document in the same commit as the code change.

Core invariants:

- Operators confirm physical events. Jobs only process time-based events and never infer physical state.
- PostgreSQL is the source of truth for transactional data.
- Booking conflicts must be protected by database constraints and transactions, not cache checks alone.
- Redis, RabbitMQ, and Elasticsearch are introduced only for the use cases and in the order described in the architecture roadmap.
- Public API and database changes require tests and migration review.
