# EV Charge & Swap Management System — Product Requirements

Last updated: 2026-11-15

This document is the product source of truth. The most important operating rule is: **operators at a location confirm events that happen in the physical world; scheduled jobs only process time-based events**.

The product uses an **ASP.NET Core REST API modular monolith with an Angular client**. Development begins with nine core business entities and adds supporting tables when their feature is implemented.

## 1. Goal and scope

The system allows customers to find locations on a map, reserve a car charging bay or hold a motorcycle battery, get directions, use a service, and pay. Operators confirm arrival and record service execution. Administrators manage the network and review reports.

A location can offer either or both services:

- **Car charging:** each charging bay is one parking space paired with one charger. A location with six chargers manages six corresponding parking spaces.
- **Motorcycle battery swap:** each swap receives one old battery and gives one ready battery. Version one supports one battery system and does not offer motorcycle charging at the location.

Version one does not integrate with real chargers or battery cabinets. **Operators are the source of operational truth** for customer arrival, charging start/end, meter readings, vehicle departure, battery receipt/delivery, and equipment faults. The system must not present these values as automatically reported by hardware.

## 2. Roles

Customer registration collects full name, email, Vietnamese mobile number, and password. Email and phone are verified separately; verifying either channel permits login through that channel. Email or phone plus password both resolve to the same account. Operator/Admin accounts remain provisioned by administrators or bootstrap. Access JWTs are short lived; a refresh-token family has a fixed maximum lifetime of 30 days from login, unaffected by rotation. Development uses a local verification-code inbox; real email/SMS delivery requires provider configuration before production registration is enabled.

| Role | Primary permissions |
| --- | --- |
| **Customer** | Find locations, reserve/cancel, view history and invoices, and pay |
| **Operator** | Confirm customers, operate charging/swaps, and report incidents at assigned locations |
| **Admin** | Manage locations, resources, staff, pricing, and reports |

Unauthenticated users may view public information. The backend must enforce both role and location assignment on every relevant API. Hiding an Angular button is not authorization.

## 3. Functional requirements

### 3.1. Locations, maps, and search

- Admin can create, edit, and hide a location with its name, address, coordinates, offered services, and service hours for each service.
- Angular displays locations on a map. A location offering both services still has one marker.
- Customers can search by name/address and radius; filter by service, price, available charging bays, or ready batteries.
- Location details show available resource counts, pricing, and when operators last updated the physical data.
- A **Directions** action opens Google Maps for the location coordinates.
- A service can only be reserved during its configured service hours.

### 3.2. Car charging bays

- Admin creates each `ChargingBay` with a bay code, power rating, and location. Version one uses one connector standard across the network.
- Each bay tracks two independent states:
  - Equipment: `ACTIVE`, `FAULTED`, `MAINTENANCE`.
  - Parking occupancy: `VACANT`, `OCCUPIED`.
- A bay is reservable only when equipment is `ACTIVE`, parking is `VACANT`, and no active reservation holds it.
- Faulted or maintained bays reject new reservations.

### 3.3. Motorcycle batteries

- Each battery has a unique code/serial and belongs to a location while in inventory.
- Battery states are `READY`, `WITH_CUSTOMER`, `INSPECTION`, `CHARGING`, `FAULTED`.
- A returned battery moves to `INSPECTION`; an operator inspects and records charging before it can return to `READY`.
- A held `READY` battery cannot be reserved by another customer or issued in another swap.

### 3.4. Reservations

Version one supports **reserve to arrive now**, not future time-slot booking.

- A Customer holds one specific charging bay or one specific battery for 15 minutes.
- A Customer can have at most one active reservation.
- Concurrent requests cannot both reserve the same bay or battery.
- A Customer can cancel before check-in.
- A scheduled job expires `HELD` reservations older than 15 minutes. Repeated or delayed execution must not expire a checked-in reservation.
- Every reservation has a single-use reservation code/QR for operator lookup.

### 3.5. Check-in and car charging

- The Operator verifies the reservation code and changes the reservation to `CHECKED_IN`. **Check-in is not charging start.**
- When the vehicle is parked and charging begins, the Operator creates a `ChargingSession` with start time and starting kWh reading.
- At charging completion, the Operator records end time and ending kWh. Ending kWh cannot be below starting kWh.
- **Charging completion does not mean vehicle departure.** The bay remains `OCCUPIED` and unavailable until the Operator confirms departure and changes it to `VACANT`.
- If the Customer arrives but chooses not to charge, the Operator closes the reservation with reason `DID_NOT_CHARGE`. No charging session or electricity invoice is created; the bay only reopens after vehicle departure.

### 3.6. Motorcycle battery swaps

- The Operator verifies the reservation and confirms arrival.
- The Operator records the returned battery code, initial inspection result, and issued battery code.
- A completed swap receives exactly one old battery and issues exactly one ready battery.
- Only a `READY` battery at the correct location and not held by another reservation can be issued.
- The swap records the responsible Operator and timestamps.

### 3.7. Incidents

- Operators can report a charging bay or battery fault with description and timestamp.
- If a resource fails after reservation but before use, the system finds an alternative of the same service at the same location; the Operator confirms reassignment. If none exists, the reservation is cancelled with an incident reason and the Customer is notified.
- `IncidentReport` links the resource, related usage when applicable, reporter, and resolution.
- Usage history supports investigation. The system must **not automatically conclude customer liability or create compensation charges**.

### 3.8. Pricing, invoices, and payments

- Charging price is `(ending kWh − starting kWh) × unit price per kWh`.
- Battery swap price is per battery/swap.
- Snapshot the applied unit price on the session, swap, or invoice. Later tariff changes must not alter historical invoices.
- Create an invoice after service completion and show its calculation to the Customer.
- Use a sandbox or mock payment provider with a webhook flow.
- The backend verifies webhook authenticity, invoice, and amount. Duplicate or concurrent valid webhooks record payment exactly once. Failed payments can be retried.

### 3.9. Notifications and reports

- In-app notifications cover reservation creation, approaching expiry, incident cancellation, invoice creation, and successful payment.
- Admin reports include charging-session count, total kWh, battery-swap count, paid revenue, charging-bay utilization, and incidents by location and time range.

## 4. State rules

Do not use one `status` column to describe every dimension. The backend combines source states into a customer-facing label:

| Scenario | Source data | Customer label |
| --- | --- | --- |
| Healthy equipment, no vehicle, no hold | Bay `ACTIVE` + `VACANT`, no active reservation | Available |
| Healthy equipment, no vehicle, held by An | Bay `ACTIVE` + `VACANT`, reservation `HELD` | Reserved |
| An arrived and vehicle is parked | Bay `OCCUPIED`, reservation `CHECKED_IN` | In use |
| Charger fault | Bay `FAULTED` | Temporarily unavailable |
| Eligible unheld battery | Battery `READY`, no active reservation | Battery available |

`ChargingSession` indicates whether electrical charging is occurring. Reservation states are `HELD`, `CHECKED_IN`, `COMPLETED`, `CANCELLED`, `EXPIRED`; reasons such as no charging or incident cancellation are stored separately.

## 5. Data model and ERD development

Begin with nine core business entities:

`User`, `Location`, `ChargingBay`, `Battery`, `Reservation`, `ChargingSession`, `BatterySwap`, `Invoice`, `Payment`.

Add supporting entities with their feature:

- `OperatorAssignment` for operator-location assignments.
- `ServiceHours` for per-service/per-day hours.
- `Tariff` for managed pricing.
- `IncidentReport` for incident handling.
- `AuditLog` for operation history.
- `Notification` for in-app notifications.
- `PaymentWebhookEvent` for durable webhook idempotency.

Draft an ERD before each workflow. Develop C# entities and schema with **EF Core Code First Migrations**. Review every migration, especially foreign keys, unique constraints, and indexes.

Infrastructure entities such as `RefreshToken`, Outbox, or Inbox support the system but do not replace the nine core business entities.

## 6. Technical and testing requirements

The backend is an ASP.NET Core Web API organized by business feature in one modular monolith. Angular provides Customer, Operator, and Admin interfaces. The API provides OpenAPI, validation, consistent errors, and pagination. The repository provides Docker Compose, sample data, README instructions, demo accounts, and ERDs.

Minimum tests:

1. Two customers reserve the same bay or battery concurrently; only one succeeds.
2. A reservation expires; the expiry job runs again; a checked-in reservation is never expired.
3. An Operator assigned to location A cannot operate at location B.
4. A checked-in Customer does not charge; no charging session or electricity invoice is created.
5. Charging completes but the vehicle has not left; another Customer still cannot reserve the bay.
6. Ending kWh below starting kWh is rejected.
7. A returned battery does not automatically become `READY`.
8. A resource fails after reservation; valid reassignment occurs or cancellation stores a reason.
9. Tariff changes do not alter the amount of a completed usage.
10. Invalid webhooks are rejected; duplicate valid webhooks record payment once.

## 7. Out of scope for version one

- Future time-slot booking.
- Multiple charging connector standards.
- Multiple battery systems.
- Peak/off-peak pricing.
- Overtime parking fees.
- Automatic customer liability or compensation charges.
- Real charger/battery-cabinet integration.
- Unattended operation.

Version one focuses on a correct and testable flow:

`find location → reserve → operator confirms → charge/swap → invoice → payment`.
