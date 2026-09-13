using Azure;
using Azure.Data.Tables;

namespace ChargingFunctions.Auth;

public class OwnershipService
{
    private readonly TableClient _table;

    public OwnershipService(Tables tables) => _table = tables.Ownership;

    public async Task<bool> OwnsAsync(string userId, string deviceId)
    {
        try
        {
            await _table.GetEntityAsync<OwnershipEntity>(userId, deviceId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListDevicesAsync(string userId)
    {
        var devices = new List<string>();

        await foreach (var row in _table.QueryAsync<OwnershipEntity>(
            e => e.PartitionKey == userId))
        {
            devices.Add(row.RowKey);
        }

        return devices;
    }

    public async Task<bool> IsClaimedAsync(string deviceId)
    {
        await foreach (var _ in _table.QueryAsync<OwnershipEntity>(
            e => e.RowKey == deviceId, maxPerPage: 1))
        {
            return true;
        }

        return false;
    }

    public Task ClaimAsync(string userId, string deviceId) =>
        _table.AddEntityAsync(new OwnershipEntity
        {
            PartitionKey = userId,
            RowKey = deviceId,
            ClaimedAt = DateTimeOffset.UtcNow
        });
}