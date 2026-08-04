using System.Data;
using Dapper;

namespace Payments.Infrastructure;

/// <summary>
/// Npgsql trả `DateTime` cho cột `timestamptz`, nhưng DTO record dùng `DateTimeOffset` →
/// Dapper không match được constructor. Type handler dưới đây bắc cầu (giống D-Pro).
/// Gọi 1 lần lúc khởi động.
/// </summary>
public static class PersistenceConventions
{
    private static bool _applied;

    public static void Apply()
    {
        if (_applied) return;
        _applied = true;

        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetHandler());
        DefaultTypeMap.MatchNamesWithUnderscores = true;   // from_account → FromAccount
    }

    private sealed class DateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new DataException($"Không chuyển được {value?.GetType()} sang DateTimeOffset"),
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
            => parameter.Value = value.UtcDateTime;
    }

    private sealed class NullableDateTimeOffsetHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        public override DateTimeOffset? Parse(object value) => value switch
        {
            null or DBNull => null,
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new DataException($"Không chuyển được {value.GetType()} sang DateTimeOffset?"),
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
            => parameter.Value = value?.UtcDateTime ?? (object)DBNull.Value;
    }
}
