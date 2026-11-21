# Swap, billing and payment

- Each reservation has at most one BatterySwap and one Invoice. A swap refers to exactly two distinct batteries, one Operator and the reservation. Battery custody is tracked by an optional Customer reference on Battery.
- Location has many effective-dated tariffs per service. ChargingSession stores the rate prevailing at start. BatterySwap stores the rate prevailing at handover.
- Invoice has many payment attempts. PaymentWebhookEvent references one attempt and has a unique provider event ID. PostgreSQL permits only one pending and one successful attempt per invoice.
