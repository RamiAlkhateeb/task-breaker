using System.Security.Cryptography;
using System.Text;

namespace IdeaSplit.Shared.Models;

/// <summary>A task that comes back on chosen days of the week, e.g. "Gym" on Mon, Wed and Fri.</summary>
public class Routine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? ProjectName { get; set; }
    /// <summary>Days it repeats on: bit <c>(int)DayOfWeek</c> set (Sunday = bit 0).</summary>
    public int Days { get; set; }
    /// <summary>The last date tasks were added for, so a task deleted from a day isn't added again.</summary>
    public DateTime GeneratedThrough { get; set; }

    public DateTime UpdatedAt { get; set; }
    public bool IsDirty { get; set; } = true;

    public const int EveryDay = 0b111_1111;

    public bool RepeatsOn(DateTime date) => (Days & (1 << (int)date.DayOfWeek)) != 0;

    /// <summary>
    /// The id of this routine's task on <paramref name="date"/>. Every device works out the same id, so two
    /// devices adding the same day's task produce one task, not two.
    /// </summary>
    public Guid TaskIdFor(DateTime date)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Id:N}/{date:yyyy-MM-dd}"));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5-style
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return new Guid(bytes);
    }
}
