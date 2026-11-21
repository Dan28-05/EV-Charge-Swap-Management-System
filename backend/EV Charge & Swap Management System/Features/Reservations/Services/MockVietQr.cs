using System.Globalization;
using System.Text;
using EVChargeSwap.Features.Reservations.Entities;

namespace EVChargeSwap.Features.Reservations.Services;

internal static class MockVietQr
{
    public static string Encode(Payment attempt)
    {
        // Reserved demo data. The recipient is intentionally not routable.
        var recipient = Field("00", "000000") + Field("01", "EVGOTEST");
        var merchantAccount = Field("00", "A000000727") + Field("01", recipient) + Field("02", "QRIBFTTA");
        var amount = attempt.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        var reference = Field("05", attempt.Id.ToString("N")[..24].ToUpperInvariant());
        var payload = Field("00", "01") + Field("01", "12") + Field("38", merchantAccount) +
            Field("52", "0000") + Field("53", "704") + Field("54", amount) + Field("58", "VN") +
            Field("59", "EVGO DEMO") + Field("60", "HANOI") + Field("62", reference) + "6304";

        return payload + Checksum(payload).ToString("X4", CultureInfo.InvariantCulture);
    }

    private static string Field(string tag, string value) =>
        tag + value.Length.ToString("D2", CultureInfo.InvariantCulture) + value;

    private static ushort Checksum(string value)
    {
        ushort crc = 0xFFFF;
        foreach (var character in Encoding.ASCII.GetBytes(value))
        {
            crc ^= (ushort)(character << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc << 1) ^ ((crc & 0x8000) == 0 ? 0 : 0x1021));
        }

        return crc;
    }
}
