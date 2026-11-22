# EV Charge & Swap Management System — Product Requirements

Last updated: 2026-11-21

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
| **Operator** | At assigned locations, edit per-service hours, update charging bay and battery states, confirm customers, operate charging/swaps, report incidents, and manage charging tariffs when tariff management is available |
| **Admin** | Manage location records and resource inventory, set initial service hours, assign Operators, manage pricing, and review reports |

Unauthenticated users may view public information. The backend must enforce both role and location assignment on every relevant API. Hiding an Angular button is not authorization.

Customers can read their own profile and update their full name. Email and phone remain login identifiers and cannot be changed through the basic profile form; changing either requires a separate verification workflow. An authenticated customer can change a password by entering the current password. A customer who forgot the password can request a six-digit, five-minute, single-use reset code through a previously verified email or phone number, then set a new password. The request response does not disclose whether the account exists. Changing or resetting a password invalidates existing access and refresh tokens; the customer signs in again.

## 3. Functional requirements

### 3.1. Locations, maps, and search

- Admin can create, edit, and hide location records with name, address, coordinates, and offered services. Admin sets the initial per-service schedule. Operators can edit service hours only at assigned locations.
- Angular displays locations on a map. A location offering both services still has one marker.
- Customers can search by name/address and radius; filter by service, price, available charging bays, or ready batteries.
- The station finder searches station names and addresses across the entire network; the customer interface does not expose a radius selector.
- The station finder suggests matches while typing and accepts Vietnamese terms with or without diacritics. Search results only expose publicly visible stations, with availability from current transactional data.
- Location details show available resource counts, pricing, and when operators last updated the physical data.
- A **Directions** action opens Google Maps for the location coordinates.
- Public location responses include a manually maintained operating status: `Unknown`, `Open`, `Closed`, or `Maintenance`. `Unknown` is used for existing locations until an Admin confirms their state. This status is independent of public visibility (`isActive`), service hours, and individual equipment availability. The UI must not infer 24/7 opening, charger power/connector, or swap duration from the offered-service flags.
- The current demo locations have a 24/7 service schedule. `Closed` and `Maintenance` are temporary operational exceptions to that schedule. A location may report a supported charging power of 30, 60, 150 or 250 kW when car charging is offered, and a 1.5 kWh LFP battery specification when battery swap is offered. When battery swap is offered, locations also report available battery pack counts (`available_batteries`, between 0 and 20 packs) reflecting ready batteries available for swap. When car charging is offered, locations report available charging bay counts (`available_charging_bays`, between 0 and 10 bays) reflecting currently vacant/ready charging bays.
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
- Clients can send a UUID in `Idempotency-Key` when creating a reservation. Repeating a successful request for the same customer, location and service returns the original reservation in its current state. Reusing the key for a different location or service returns a conflict.
- A Customer can cancel before check-in.
- A scheduled job expires `HELD` reservations older than 15 minutes. Repeated or delayed execution must not expire a checked-in reservation.
- Every reservation has a single-use reservation code/QR for operator lookup.

### 3.5. Check-in and car charging

- The Operator verifies the reservation code and changes the reservation to `CHECKED_IN`. **Check-in is not charging start.**
- The Operator separately confirms that the car occupies the bay before a charging session can start. Retrying this confirmation for the same checked-in visit while the car is still parked preserves the original time and Operator.
- When the vehicle is parked and charging begins, the Operator creates a `ChargingSession` with start time and starting kWh reading.
- At charging completion, the Operator records end time and ending kWh. Ending kWh cannot be below starting kWh.
- **Charging completion does not mean vehicle departure.** The bay remains `OCCUPIED` and unavailable until the Operator confirms departure and changes it to `VACANT`.
- If the Customer arrives but chooses not to charge, the Operator marks `DID_NOT_CHARGE` and then confirms vehicle departure. No charging session or electricity invoice is created; the reservation closes and the bay reopens only on departure. If the customer checks in but never parks, the Operator may close the unused reservation directly while the bay is vacant.

### 3.6. Motorcycle battery swaps

- The Operator verifies the reservation and confirms arrival.
- The Operator records the returned battery code, initial inspection result, and issued battery code.
- A completed swap receives exactly one old battery and issues exactly one ready battery.
- Only a `READY` battery at the correct location and not held by another reservation can be issued.
- The swap records the responsible Operator and timestamps.
- The Operator may receive a serial that is new to the system and register it in `INSPECTION` during the handover. An existing serial can be returned only when recorded in the current customer's custody. The issued battery must be the currently reserved battery.

### 3.7. Incidents

- Operators can report a charging bay or battery fault with description and timestamp.
- New reservations exclude faulted resources. Changes to service hours apply to new reservations and do not revoke checked-in visits.
- Public available-resource counts come from individual bays, batteries, and active reservations in PostgreSQL.
- When an Operator reports a fault before the reserved resource is in use, the system immediately reassigns the visit to an available resource for the same service at the same station. The reservation view updates to show the replacement without creating a customer inbox notification.
- If no replacement exists, the system cancels the visit with the incident reason and notifies the Customer. A report for an occupied bay or started session leaves the visit in the operational workflow.
- `IncidentReport` links the resource, related usage when applicable, reporter, and resolution.
- Reassignment preserves the original reservation code, status and expiry. A charging bay with a parked vehicle or a started charging session is handled through the existing charging workflow. The Operator can close its incident report independently.
- Usage history supports investigation. The system must **not automatically conclude customer liability or create compensation charges**.

### 3.8. Pricing, invoices, and payments

- A charging invoice contains only the cost of electricity, calculated as `(ending kWh − starting kWh) × unit price per kWh`.
- When tariff management is available, Operators can record effective-dated charging tariffs for their assigned location. Every change records the Operator and timestamp.
- Battery swap price is per battery/swap.
- Snapshot the applied unit price on the session, swap, or invoice. Later tariff changes must not alter historical invoices.
- Create one invoice for each completed charge or swap. Show the amount and calculation to the Customer and Operator in the completion flow using the saved invoice.
- Offer cash at the counter, Mock Dynamic VietQR in Development, and pay later with Operator approval. These choices settle the same invoice. Payment and vehicle departure are independent actions.
- For cash, an Operator currently assigned to the invoice's station confirms receipt of the full amount. Record the authenticated actor as `CollectedByOperatorId`, along with the amount, method and receipt timestamp.
- For QR, display the amount due and a unique payment reference in a code labeled **Simulation**. Use test-only recipient data and the QR references in the [architecture roadmap](architecture-roadmap.md#phase-6--billing-and-observability-completion). Scanning the code does not settle the invoice.
- The mock provider requires a configured signing secret. Verify the callback signature, payment attempt, invoice and amount before settlement. An authenticated callback for a failed, underpaid or overpaid attempt leaves the invoice payable and reports the reason. A failed attempt can be retried. Duplicate callbacks and concurrent cash/QR confirmations can produce at most one successful payment for an invoice.
- An assigned Operator can grant pay later on an unpaid invoice. The due time is seven days after approval, stored in UTC and displayed in Vietnam local time. Repeating the approval preserves the original deadline. Customers can reopen the invoice from history and pay after the deadline.
- Any pending invoice blocks new bookings for that customer across the network, including invoices with no due date and pay-later invoices still within their term. The seven-day deadline records when payment is due. Existing bookings remain valid. Settling all pending invoices removes the payment block immediately. An idempotent replay of an existing booking still returns that booking.
- Customer history shows owned invoices, amount, due time when applicable, status, and successful payment method and time. Operator history shows the same payment state for invoices at the assigned station and identifies the cash collector when applicable. Both screens refresh from the API on a private realtime signal.

### 3.9. Notifications and reports

- In-app notifications cover reservation creation, approaching expiry, incident cancellation, invoice creation, and successful payment.
- Operators have a dashboard inbox with an unread count. An assigned Operator receives a notification for a new reservation, an incident affecting a reservation, or a successful QR payment at the station. Opening an item marks it read and links to the relevant visit or invoice.
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
- Each Operator has at most one active assignment at a time. A location may have multiple Operators. Transfers end the previous assignment and retain its history.
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
11. Service completion returns the invoice despite RabbitMQ downtime; a retry returns the same invoice and amount.
12. An Operator from another station cannot collect cash, approve a deferral, or read the invoice. Cash collection records the authenticated Operator. Concurrent cash and QR confirmations settle the invoice once.
13. Mock QR decoding returns the exact invoice amount and payment reference. An underpayment or overpayment leaves it unpaid and retryable.
14. New bookings are rejected while an invoice is pending, before deferral, during the payment term, and at or after the due date. Settling all pending invoices removes the payment block. Existing bookings and their idempotent replays are unaffected.
15. Both authorized views refresh after settlement and converge after reconnect. Other customers and stations receive no invoice refresh signal.

## 7. Out of scope for version one

- Future time-slot booking.
- Multiple charging connector standards.
- Multiple battery systems.
- Peak/off-peak pricing.
- Overtime parking fees.
- Automatic customer liability or compensation charges.
- Real charger/battery-cabinet integration.
- Unattended operation.
- Live QR settlement, sandbox provider integration, partial payments, overpayment reconciliation, debt reminders, and shift cash reconciliation.

Version one focuses on a correct and testable flow:

`find location → reserve → operator confirms → charge/swap → invoice → payment`.
