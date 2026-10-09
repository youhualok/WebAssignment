using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebAssignment.Data;

#nullable disable

namespace WebAssignment.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260908010000_RepairMissingColumns")]
    public sealed class RepairMissingColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Users', 'PhoneNumber') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD PhoneNumber nvarchar(20) NULL;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.BookingDetails', 'ShowtimeId') IS NULL
BEGIN
    ALTER TABLE dbo.BookingDetails ADD ShowtimeId int NULL;
END;");

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_BookingDetails_ShowtimeId_SeatId'
      AND object_id = OBJECT_ID('dbo.BookingDetails'))
BEGIN
    CREATE UNIQUE INDEX IX_BookingDetails_ShowtimeId_SeatId
    ON dbo.BookingDetails (ShowtimeId, SeatId)
    WHERE ShowtimeId IS NOT NULL;
END;");

            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_BookingDetails_Showtimes_ShowtimeId'
      AND parent_object_id = OBJECT_ID('dbo.BookingDetails'))
BEGIN
    ALTER TABLE dbo.BookingDetails WITH CHECK
    ADD CONSTRAINT FK_BookingDetails_Showtimes_ShowtimeId
    FOREIGN KEY (ShowtimeId) REFERENCES dbo.Showtimes (ShowtimeId);
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = 'FK_BookingDetails_Showtimes_ShowtimeId'
      AND parent_object_id = OBJECT_ID('dbo.BookingDetails'))
BEGIN
    ALTER TABLE dbo.BookingDetails
    DROP CONSTRAINT FK_BookingDetails_Showtimes_ShowtimeId;
END;");

            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_BookingDetails_ShowtimeId_SeatId'
      AND object_id = OBJECT_ID('dbo.BookingDetails'))
BEGIN
    DROP INDEX IX_BookingDetails_ShowtimeId_SeatId ON dbo.BookingDetails;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.BookingDetails', 'ShowtimeId') IS NOT NULL
BEGIN
    ALTER TABLE dbo.BookingDetails DROP COLUMN ShowtimeId;
END;");

            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Users', 'PhoneNumber') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Users DROP COLUMN PhoneNumber;
END;");
        }
    }
}
