using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WattsOn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "actors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    cvr = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    is_own = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "brs_processes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    process_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    current_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metering_point_gsrn = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    effective_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    counterpart_gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    process_data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brs_processes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    business_process = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sender_gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    receiver_gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    raw_payload = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_processed = table.Column<bool>(type: "boolean", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    processing_attempts = table.Column<int>(type: "integer", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cpr = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    cvr = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    street_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    building_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    floor = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    suite = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    post_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    city_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    municipality_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "metering_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    gsrn = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    art = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    settlement_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resolution = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    connection_state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    street_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    building_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    floor = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    suite = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    post_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    city_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    municipality_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    grid_area = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    grid_company_gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    has_active_supply = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metering_points", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    business_process = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sender_gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    receiver_gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_sent = table.Column<bool>(type: "boolean", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response = table.Column<string>(type: "jsonb", nullable: true),
                    send_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    send_attempts = table.Column<int>(type: "integer", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    charge_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    owner_gln = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    vat_exempt = table.Column<bool>(type: "boolean", nullable: false),
                    price_resolution = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "process_state_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    to_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    transitioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_state_transitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_process_state_transitions_brs_processes_process_id",
                        column: x => x.process_id,
                        principalTable: "brs_processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fakturaer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    invoice_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invoice_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sub_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sub_total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    is_sent = table.Column<bool>(type: "boolean", nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    original_faktura_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fakturaer", x => x.id);
                    table.ForeignKey(
                        name: "FK_fakturaer_fakturaer_original_faktura_id",
                        column: x => x.original_faktura_id,
                        principalTable: "fakturaer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fakturaer_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    metering_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    supply_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ended_by_process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplies", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplies_actors_actor_id",
                        column: x => x.actor_id,
                        principalTable: "actors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplies_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplies_metering_points_metering_point_id",
                        column: x => x.metering_point_id,
                        principalTable: "metering_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "time_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    metering_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_latest = table.Column<bool>(type: "boolean", nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_series", x => x.id);
                    table.ForeignKey(
                        name: "FK_time_series_metering_points_metering_point_id",
                        column: x => x.metering_point_id,
                        principalTable: "metering_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pris_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pris_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pris_points", x => x.id);
                    table.ForeignKey(
                        name: "FK_pris_points_prices_pris_id",
                        column: x => x.pris_id,
                        principalTable: "prices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    metering_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pris_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    link_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_price_links_metering_points_metering_point_id",
                        column: x => x.metering_point_id,
                        principalTable: "metering_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_links_prices_pris_id",
                        column: x => x.pris_id,
                        principalTable: "prices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "faktura_linjer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    faktura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    quantity_unit = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true, defaultValue: "kWh"),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    settlement_linje_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faktura_linjer", x => x.id);
                    table.ForeignKey(
                        name: "FK_faktura_linjer_fakturaer_faktura_id",
                        column: x => x.faktura_id,
                        principalTable: "fakturaer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    metering_point_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supply_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settlement_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    time_series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_series_version = table.Column<int>(type: "integer", nullable: false),
                    total_energy_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    total_energy_unit = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false, defaultValue: "kWh"),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    is_correction = table.Column<bool>(type: "boolean", nullable: false),
                    previous_settlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlements", x => x.id);
                    table.ForeignKey(
                        name: "FK_settlements_supplies_supply_id",
                        column: x => x.supply_id,
                        principalTable: "supplies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_settlements_metering_points_metering_point_id",
                        column: x => x.metering_point_id,
                        principalTable: "metering_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_settlements_time_series_time_series_id",
                        column: x => x.time_series_id,
                        principalTable: "time_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_series_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quantity_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_unit = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false, defaultValue: "kWh"),
                    quality = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observations", x => x.id);
                    table.ForeignKey(
                        name: "FK_observations_time_series_time_series_id",
                        column: x => x.time_series_id,
                        principalTable: "time_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "settlement_linjer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pris_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity_kwh = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    quantity_unit = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false, defaultValue: "kWh"),
                    unit_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "DKK"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settlement_linjer", x => x.id);
                    table.ForeignKey(
                        name: "FK_settlement_linjer_settlements_settlement_id",
                        column: x => x.settlement_id,
                        principalTable: "settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "faktura_settlements",
                columns: table => new
                {
                    SettlementsId = table.Column<Guid>(type: "uuid", nullable: false),
                    FakturaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_faktura_settlements", x => new { x.SettlementsId, x.FakturaId });
                    table.ForeignKey(
                        name: "FK_faktura_settlements_settlements_SettlementsId",
                        column: x => x.SettlementsId,
                        principalTable: "settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_faktura_settlements_fakturaer_FakturaId",
                        column: x => x.FakturaId,
                        principalTable: "fakturaer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_settlement_linjer_settlement_id",
                table: "settlement_linjer",
                column: "settlement_id");

            migrationBuilder.CreateIndex(
                name: "IX_settlements_supply_id",
                table: "settlements",
                column: "supply_id");

            migrationBuilder.CreateIndex(
                name: "IX_settlements_metering_point_id",
                table: "settlements",
                column: "metering_point_id");

            migrationBuilder.CreateIndex(
                name: "IX_settlements_time_series_id",
                table: "settlements",
                column: "time_series_id");

            migrationBuilder.CreateIndex(
                name: "IX_actors_gln",
                table: "actors",
                column: "gln",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brs_processes_status",
                table: "brs_processes",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_brs_processes_transaction_id",
                table: "brs_processes",
                column: "transaction_id");

            migrationBuilder.CreateIndex(
                name: "IX_faktura_settlements_FakturaId",
                table: "faktura_settlements",
                column: "FakturaId");

            migrationBuilder.CreateIndex(
                name: "IX_faktura_linjer_faktura_id",
                table: "faktura_linjer",
                column: "faktura_id");

            migrationBuilder.CreateIndex(
                name: "IX_fakturaer_invoice_number",
                table: "fakturaer",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fakturaer_customer_id",
                table: "fakturaer",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_fakturaer_original_faktura_id",
                table: "fakturaer",
                column: "original_faktura_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_is_processed",
                table: "inbox_messages",
                column: "is_processed");

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_message_id",
                table: "inbox_messages",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplies_actor_id",
                table: "supplies",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplies_customer_id",
                table: "supplies",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplies_metering_point_id",
                table: "supplies",
                column: "metering_point_id");

            migrationBuilder.CreateIndex(
                name: "IX_metering_points_gsrn",
                table: "metering_points",
                column: "gsrn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_observations_time_series_id_timestamp",
                table: "observations",
                columns: new[] { "time_series_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_is_sent",
                table: "outbox_messages",
                column: "is_sent");

            migrationBuilder.CreateIndex(
                name: "IX_pris_points_pris_id_timestamp",
                table: "pris_points",
                columns: new[] { "pris_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_prices_charge_id",
                table: "prices",
                column: "charge_id");

            migrationBuilder.CreateIndex(
                name: "IX_price_links_metering_point_id",
                table: "price_links",
                column: "metering_point_id");

            migrationBuilder.CreateIndex(
                name: "IX_price_links_pris_id",
                table: "price_links",
                column: "pris_id");

            migrationBuilder.CreateIndex(
                name: "IX_process_state_transitions_process_id",
                table: "process_state_transitions",
                column: "process_id");

            migrationBuilder.CreateIndex(
                name: "IX_time_series_metering_point_id_is_latest",
                table: "time_series",
                columns: new[] { "metering_point_id", "is_latest" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settlement_linjer");

            migrationBuilder.DropTable(
                name: "faktura_settlements");

            migrationBuilder.DropTable(
                name: "faktura_linjer");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "observations");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "pris_points");

            migrationBuilder.DropTable(
                name: "price_links");

            migrationBuilder.DropTable(
                name: "process_state_transitions");

            migrationBuilder.DropTable(
                name: "settlements");

            migrationBuilder.DropTable(
                name: "fakturaer");

            migrationBuilder.DropTable(
                name: "prices");

            migrationBuilder.DropTable(
                name: "brs_processes");

            migrationBuilder.DropTable(
                name: "supplies");

            migrationBuilder.DropTable(
                name: "time_series");

            migrationBuilder.DropTable(
                name: "actors");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "metering_points");
        }
    }
}
