using Azure.Data.Tables;

namespace ChargingFunctions;

public class Tables
{
    public TableClient CarState { get; }
    public TableClient Users { get; }
    public TableClient Ownership { get; }

    public Tables(string connectionString)
    {
        var service = new TableServiceClient(connectionString);

        CarState = service.GetTableClient("CarState");
        Users = service.GetTableClient("Users");
        Ownership = service.GetTableClient("DeviceOwnership");

        CarState.CreateIfNotExists();
        Users.CreateIfNotExists();
        Ownership.CreateIfNotExists();
    }
}