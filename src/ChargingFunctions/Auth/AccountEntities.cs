using Azure;
using Azure.Data.Tables;

namespace ChargingFunctions.Auth;

public class UserEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "user";

    public string RowKey { get; set; } = default!;

    public string PasswordHash { get; set; } = default!;
    public string PasswordSalt { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

public class OwnershipEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // userId

    public string RowKey { get; set; } = default!; // deviceId

    public DateTimeOffset ClaimedAt { get; set; }

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}