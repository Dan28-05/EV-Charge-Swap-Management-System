using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVChargeSwap.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenReservationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "IdempotencyKey",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_reservations_customer_request",
                table: "reservations",
                columns: new[] { "CustomerId", "IdempotencyKey" },
                unique: true,
                filter: " \"IdempotencyKey\" IS NOT NULL ");

            // Legacy cancellations and expirations did not record closing metadata.
            migrationBuilder.Sql("""
                ALTER TABLE reservations ADD CONSTRAINT ck_reservations_lifecycle CHECK (
                    (("CheckedInAtUtc" IS NULL) = ("CheckedInById" IS NULL))
                    AND (("VehicleArrivedAtUtc" IS NULL) = ("VehicleArrivedById" IS NULL))
                    AND (("VehicleDepartedAtUtc" IS NULL) = ("VehicleDepartedById" IS NULL))
                    AND (("NoChargeConfirmedAtUtc" IS NULL) = ("NoChargeConfirmedById" IS NULL))
                    AND ("VehicleArrivedAtUtc" IS NULL OR ("ChargingBayId" IS NOT NULL AND "CheckedInAtUtc" IS NOT NULL))
                    AND ("VehicleDepartedAtUtc" IS NULL OR ("VehicleArrivedAtUtc" IS NOT NULL AND "Status" = 'Completed'))
                    AND ("NoChargeConfirmedAtUtc" IS NULL OR "VehicleArrivedAtUtc" IS NOT NULL)
                    AND CASE "Status"
                        WHEN 'Held' THEN "CheckedInAtUtc" IS NULL AND "ClosedAtUtc" IS NULL AND "ClosedById" IS NULL
                        WHEN 'CheckedIn' THEN "CheckedInAtUtc" IS NOT NULL AND "ClosedAtUtc" IS NULL AND "ClosedById" IS NULL
                        WHEN 'Completed' THEN "CheckedInAtUtc" IS NOT NULL AND "ClosedAtUtc" IS NOT NULL AND "ClosedById" IS NOT NULL
                        WHEN 'Cancelled' THEN "ClosedAtUtc" IS NOT NULL AND "ClosedById" IS NOT NULL
                        WHEN 'Expired' THEN "CheckedInAtUtc" IS NULL AND "ClosedAtUtc" IS NOT NULL AND "ClosedById" IS NULL
                        ELSE FALSE
                    END
                ) NOT VALID;
                """);

            // A bay has a permanent station. A battery can move after its active hold closes.
            migrationBuilder.Sql("""
                ALTER TABLE charging_bays ADD CONSTRAINT uq_charging_bays_station UNIQUE ("Id", "LocationId");
                ALTER TABLE reservations ADD CONSTRAINT fk_reservations_bay_station
                    FOREIGN KEY ("ChargingBayId", "LocationId") REFERENCES charging_bays ("Id", "LocationId");

                LOCK TABLE batteries IN SHARE ROW EXCLUSIVE MODE;
                DO $check_battery_station$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM reservations r JOIN batteries b ON b."Id" = r."BatteryId"
                        WHERE r."Status" IN ('Held', 'CheckedIn') AND b."LocationId" IS DISTINCT FROM r."LocationId"
                    ) THEN
                        RAISE EXCEPTION 'An active reservation refers to a battery at a different station.';
                    END IF;
                END;
                $check_battery_station$;

                CREATE FUNCTION enforce_reservation_battery_station() RETURNS trigger LANGUAGE plpgsql AS $battery_hold$
                BEGIN
                    IF NEW."BatteryId" IS NOT NULL AND NEW."Status" IN ('Held', 'CheckedIn') THEN
                        PERFORM 1 FROM batteries
                        WHERE "Id" = NEW."BatteryId" AND "LocationId" = NEW."LocationId" FOR SHARE;
                        IF NOT FOUND THEN
                            RAISE EXCEPTION 'The reserved battery must belong to the reservation station.'
                                USING ERRCODE = '23514', CONSTRAINT = 'ck_reservations_battery_station';
                        END IF;
                    END IF;
                    RETURN NEW;
                END;
                $battery_hold$;

                CREATE TRIGGER reservation_battery_station
                    BEFORE INSERT OR UPDATE OF "BatteryId", "LocationId", "Status" ON reservations
                    FOR EACH ROW EXECUTE FUNCTION enforce_reservation_battery_station();

                CREATE FUNCTION enforce_held_battery_station() RETURNS trigger LANGUAGE plpgsql AS $battery_move$
                BEGIN
                    IF NEW."LocationId" IS DISTINCT FROM OLD."LocationId" AND EXISTS (
                        SELECT 1 FROM reservations WHERE "BatteryId" = OLD."Id"
                            AND "Status" IN ('Held', 'CheckedIn')
                            AND "LocationId" IS DISTINCT FROM NEW."LocationId"
                    ) THEN
                        RAISE EXCEPTION 'A battery with an active reservation cannot move to another station.'
                            USING ERRCODE = '23514', CONSTRAINT = 'ck_reservations_battery_station';
                    END IF;
                    RETURN NEW;
                END;
                $battery_move$;

                CREATE TRIGGER held_battery_station
                    BEFORE UPDATE OF "LocationId" ON batteries
                    FOR EACH ROW EXECUTE FUNCTION enforce_held_battery_station();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER held_battery_station ON batteries;
                DROP FUNCTION enforce_held_battery_station();
                DROP TRIGGER reservation_battery_station ON reservations;
                DROP FUNCTION enforce_reservation_battery_station();
                ALTER TABLE reservations DROP CONSTRAINT fk_reservations_bay_station;
                ALTER TABLE charging_bays DROP CONSTRAINT uq_charging_bays_station;
                """);

            migrationBuilder.DropIndex(
                name: "ux_reservations_customer_request",
                table: "reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reservations_lifecycle",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "reservations");
        }
    }
}
