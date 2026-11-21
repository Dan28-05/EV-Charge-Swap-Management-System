# Incident report

Each report belongs to a station and links exactly one original charging bay or battery. It records the reporter, description and timestamp. The report can link the affected reservation and, when charging already started, its charging session.

| Relation | Cardinality |
| --- | --- |
| Location → IncidentReport | One to many |
| ChargingBay or Battery → IncidentReport | One to many for each original resource |
| Reservation → IncidentReport | One to many, optional on the report |
| ChargingSession → IncidentReport | One to many, optional on the report |
| Operator → IncidentReport | One reporter and an optional resolver per report |

Reports capture the result and optional replacement resource. The reservation owns the current assignment. PostgreSQL enforces one active hold per resource. Reporting a fault, resolving a report, and writing their corresponding Outbox events each use one transaction.
